import hashlib
from datetime import datetime, timedelta, timezone
from urllib.parse import urlsplit

from api.contract import ContractError, canonical_url


def execution_window(request, today=None):
    """Respect the user's inclusive publication window.

    The collector's 'days' is lookback from today, even when the user
    requests a historical interval, so older valid results are not
    discarded before the final date filter.
    """
    today = today or datetime.now(timezone.utc).date()
    settings = request["settings"]
    from datetime import date
    try:
        explicit_to = date.fromisoformat(settings["dateTo"]) if settings["dateTo"] else None
        explicit_from = date.fromisoformat(settings["dateFrom"]) if settings["dateFrom"] else None
    except (ValueError, TypeError) as exc:
        raise ContractError("Invalid research date") from exc

    end = min(explicit_to or today, today)
    start = explicit_from or (end - timedelta(days=89))
    if start > end:
        raise ContractError("Research period is in the future or reversed")
    days = (today - start).days + 1
    if not 1 <= days <= 3660:
        raise ContractError("Research date range exceeds the 10-year search horizon")

    maximum = settings["config"].get("maxArticles", 20)
    if type(maximum) is not int or not 1 <= maximum <= 50:
        raise ContractError("maxArticles must be an integer from 1 to 50")
    return days, start, end, maximum

def published_within_window(source, start, end):
    """Unknown or updated-only dates cannot prove inclusion in a period."""
    stamp = source.get("publishedAt")
    if not isinstance(stamp, str):
        return False
    try:
        published = datetime.fromisoformat(stamp.replace("Z", "+00:00"))
        if published.tzinfo is None:
            return False
    except (ValueError, TypeError, OverflowError):
        return False
    return start <= published.astimezone(timezone.utc).date() <= end


def matches_domain(host, domains):
    return any(host == domain or host.endswith("." + domain) for domain in domains)


def eligible_source(article, settings):
    try:
        url = canonical_url(article.get("url"))
    except (ValueError, UnicodeError):
        return None
    host = urlsplit(url).hostname
    publisher = None
    publisher_host = None
    try:
        publisher_host = urlsplit(canonical_url(article.get("publisher_url"))).hostname
        publisher_name = article.get("publisher_name")
        if isinstance(publisher_name, str) and publisher_name.strip():
            publisher = {"domain": publisher_host, "name": publisher_name.strip()[:100]}
    except (ValueError, UnicodeError):
        pass
    if host == "news.google.com" and publisher is None and (settings["preferredDomains"] or settings["excludedDomains"]):
        return None
    if (matches_domain(host, settings["excludedDomains"]) or
            publisher_host is not None and matches_domain(publisher_host, settings["excludedDomains"])):
        return None
    return url, publisher


def build_result(request, queries, providers, articles, today=None, selected_articles=None, candidate_limit=None):
    _, _, _, maximum = execution_window(request, today)
    if candidate_limit is not None:
        maximum = max(maximum, candidate_limit)
    settings = request["settings"]
    sources = {}
    accepted_articles = {}
    for article in articles:
        eligible = eligible_source(article, settings)
        if eligible is None:
            continue
        url, publisher = eligible
        language = article.get("language")
        language = language.lower() if isinstance(language, str) and len(language) == 2 and language.isascii() and language.isalpha() else None
        published = None
        value = article.get("published_at") if article.get("publication_date_kind") not in ("updated", "discovery") else None
        if value:
            try:
                parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
                if parsed.tzinfo is not None:
                    published = parsed.astimezone(timezone.utc)
            except (ValueError, AttributeError, TypeError):
                pass
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
        accepted_articles[url] = article
    # Python's stable sort preserves collector relevance, while honoring
    # explicitly preferred publishers. Alphabetical URL sorting is not ranking.
    ordered = sorted(
        sources.values(),
        key=lambda source: not matches_domain(
            source.get("publisher", {}).get(
                "domain", urlsplit(source["canonicalUrl"]).hostname
            ),
            settings["preferredDomains"],
        ),
    )[:maximum]
    if selected_articles is not None:
        selected_articles.extend(accepted_articles[source["canonicalUrl"]] for source in ordered)
    executed = []
    for provider in providers:
        actual_queries = queries if provider in ("web_search", "google_news", "hacker_news") else ["cs.AI+cs.LG+cs.CL" if provider == "arxiv" else "hugging_face_blog_feed"]
        for query in actual_queries:
            if not isinstance(query, str) or not query.strip() or len(query) > 1000:
                raise ContractError("Invalid planned query")
            item = {"query": query.strip(), "provider": provider}
            if item not in executed:
                executed.append(item)
    return {"contractVersion": request["contractVersion"], "researchRunId": request["researchRunId"],
            "queries": executed, "sources": ordered, "findings": []}
