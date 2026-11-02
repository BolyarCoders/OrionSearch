"""Extract declared metadata without treating discovery time as publication time."""

import json
import re
import time
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timezone
from urllib.parse import urlsplit

from bs4 import BeautifulSoup

from api.captures import fetch_isolated
from api.contract import valid_domain

MAX_SOURCES = 20
METADATA_BUDGET_SECONDS = 30


def _date(value):
    if not isinstance(value, str):
        return None
    value = value.strip()
    try:
        if re.fullmatch(r"\d{4}-\d{2}-\d{2}", value):
            parsed = datetime.fromisoformat(value).replace(tzinfo=timezone.utc)
        else:
            parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
            if parsed.tzinfo is None or parsed.utcoffset() is None:
                return None
            parsed = parsed.astimezone(timezone.utc)
        if parsed > datetime.now(timezone.utc):
            return None
        return parsed.isoformat().replace("+00:00", "Z")
    except (ValueError, OverflowError):
        return None


def _json_ld_dates(value):
    if isinstance(value, list):
        for item in value:
            yield from _json_ld_dates(item)
    elif isinstance(value, dict):
        kind = value.get("@type", [])
        types = [kind] if isinstance(kind, str) else kind if isinstance(kind, list) else []
        if any(t in ("Article", "NewsArticle", "BlogPosting", "Report", "ScholarlyArticle") for t in types):
            yield value.get("datePublished")
        graph = value.get("@graph")
        if isinstance(graph, (list, dict)):
            yield from _json_ld_dates(graph)


def enrich_source_metadata(source, data, final_url):
    soup = BeautifulSoup(data, "html.parser")
    if "publishedAt" not in source:
        candidates = []
        for meta in soup.find_all("meta"):
            name = str(meta.get("property") or meta.get("name") or "").lower()
            if name in ("article:published_time", "datepublished", "citation_publication_date", "pubdate"):
                candidates.append(meta.get("content"))
        for script in soup.find_all("script", type="application/ld+json"):
            try:
                candidates.extend(_json_ld_dates(json.loads(script.string or "")))
            except (ValueError, TypeError, RecursionError):
                continue
        for value in candidates:
            published = _date(value)
            if published:
                source["publishedAt"] = published
                break
    if "language" not in source and soup.html:
        value = str(soup.html.get("lang") or "").split("-", 1)[0].lower()
        if value == "en":
            source["language"] = value
    if "publisher" not in source:
        tag = soup.find("meta", attrs={"property": "og:site_name"})
        name = str(tag.get("content") or "").strip() if tag else ""
        host = urlsplit(final_url).hostname or ""
        if name and len(name) <= 100 and valid_domain(host):
            source["publisher"] = {"name": name, "domain": host}


def enrich_sources(sources, excluded, *, fetch=fetch_isolated):
    deadline = time.monotonic() + METADATA_BUDGET_SECONDS

    def enrich(source):
        if time.monotonic() >= deadline:
            return
        try:
            data, media_type, final_url = fetch(source["canonicalUrl"], excluded, deadline)
            if media_type == "text/html":
                enrich_source_metadata(source, data, final_url)
        except Exception:
            # Metadata is optional. Unavailable pages remain honest discoveries.
            return

    with ThreadPoolExecutor(max_workers=3) as pool:
        list(pool.map(enrich, sources[:MAX_SOURCES]))
