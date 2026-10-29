import hashlib
from datetime import date, datetime, timedelta, timezone
from urllib.parse import urlsplit

from api.contract import ContractError, canonical_url


def execution_window(request, today=None):
    today = today or datetime.now(timezone.utc).date()
    settings = request["settings"]
    end = date.fromisoformat(settings["dateTo"]) if settings["dateTo"] else today
    end = min(end, today)
    start = date.fromisoformat(settings["dateFrom"]) if settings["dateFrom"] else end - timedelta(days=6)
    days = (today - start).days + 1
    if start > end or not 1 <= days <= 90:
        raise ContractError("The discovery adapters support date windows within the last 90 days")
    maximum = settings["config"].get("maxArticles", 20)
    if type(maximum) is not int or not 1 <= maximum <= 50:
        raise ContractError("maxArticles must be an integer from 1 to 50")
    return days, start, end, maximum


def matches_domain(host, domains):
    return any(host == domain or host.endswith("." + domain) for domain in domains)


def build_result(request, queries, providers, articles, today=None):
    _, start, end, maximum = execution_window(request, today)
    settings = request["settings"]
    sources = {}
    for article in articles:
        try:
            url = canonical_url(article.get("url"))
        except (ValueError, UnicodeError):
            continue
        host = urlsplit(url).hostname
        publisher = None
        try:
            publisher_host = urlsplit(canonical_url(article.get("publisher_url"))).hostname
            publisher_name = article.get("publisher_name")
            if isinstance(publisher_name, str) and publisher_name.strip():
                publisher = {"domain": publisher_host, "name": publisher_name.strip()[:100]}
        except (ValueError, UnicodeError):
            pass
        filter_host = publisher["domain"] if publisher else host
        if host == "news.google.com" and publisher is None and (settings["preferredDomains"] or settings["excludedDomains"]):
            continue
        if matches_domain(host, settings["excludedDomains"]) or matches_domain(filter_host, settings["excludedDomains"]):
            continue
        language = article.get("language")
        language = language.lower() if isinstance(language, str) and len(language) == 2 and language.isascii() and language.isalpha() else None
        if settings["languages"] and language not in [value.lower() for value in settings["languages"]]:
            continue
        published = None
        value = article.get("published_at") if article.get("publication_date_kind") not in ("updated", "discovery") else None
        if value:
            try:
                parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
                if parsed.tzinfo is not None:
                    published = parsed.astimezone(timezone.utc)
            except (ValueError, AttributeError, TypeError):
                pass
        if published is not None and not start <= published.date() <= end:
            continue
        if published is None and (settings["dateFrom"] or settings["dateTo"]):
            continue
        if url in sources:
            continue
        source = {"key": "source_" + hashlib.sha256(url.encode("utf-8")).hexdigest(), "canonicalUrl": url}
        title = article.get("title")
        if isinstance(title, str) and title.strip():
            source["title"] = title.strip()[:1000]
        if published is not None:
            source["publishedAt"] = published.isoformat().replace("+00:00", "Z")
        if language:
            source["language"] = language
        if publisher:
            source["publisher"] = publisher
        sources[url] = source
    ordered = sorted(sources.values(), key=lambda source: (
        not matches_domain(source.get("publisher", {}).get("domain", urlsplit(source["canonicalUrl"]).hostname), settings["preferredDomains"]), source["canonicalUrl"]))[:maximum]
    executed = []
    for provider in providers:
        actual_queries = queries if provider in ("google_news", "hacker_news") else ["cs.AI+cs.LG+cs.CL" if provider == "arxiv" else "hugging_face_blog_feed"]
        for query in actual_queries:
            if not isinstance(query, str) or not query.strip() or len(query) > 1000:
                raise ContractError("Invalid planned query")
            item = {"query": query.strip(), "provider": provider}
            if item not in executed:
                executed.append(item)
    return {"contractVersion": request["contractVersion"], "researchRunId": request["researchRunId"],
            "queries": executed, "sources": ordered, "findings": []}
