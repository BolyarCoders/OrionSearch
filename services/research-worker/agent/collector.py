import html
import logging
import os
import re
from concurrent.futures import ThreadPoolExecutor, as_completed
from datetime import datetime, timedelta, timezone
from time import perf_counter
from urllib.parse import urlencode

import feedparser
import requests

logger = logging.getLogger("orionsearch.collector")

SOURCES = ("google_news", "hacker_news", "arxiv", "hugging_face")
TIMEOUT = (3, 8)
MAX_WORKERS = 6


def get_feed_limit():
    raw = os.getenv("RESEARCH_FEED_LIMIT", "12")

    try:
        limit = int(raw)
    except (TypeError, ValueError) as exc:
        raise ValueError(
            "RESEARCH_FEED_LIMIT must be an integer from 1 to 100"
        ) from exc

    if not 1 <= limit <= 100:
        raise ValueError("RESEARCH_FEED_LIMIT must be an integer from 1 to 100")

    return limit


def get_selected_sources():
    raw = os.getenv("RESEARCH_SOURCES")

    if raw is None:
        return ["google_news", "hacker_news"]

    sources = [source.strip() for source in raw.split(",")]
    unknown = set(sources) - set(SOURCES)

    if not sources or any(not source for source in sources) or unknown:
        raise ValueError(
            f"Invalid RESEARCH_SOURCES={raw!r}. Allowed: {', '.join(SOURCES)}"
        )

    return list(dict.fromkeys(sources))


def validate_limits(days, max_articles=None):
    if type(days) is not int or not 1 <= days <= 90:
        raise ValueError("days must be between 1 and 90")

    if max_articles is not None:
        if type(max_articles) is not int or not 1 <= max_articles <= 50:
            raise ValueError("max_articles must be between 1 and 50")


def clean_text(value):
    return html.unescape(re.sub(r"<[^>]+>", "", value or "")).strip()


def get_rss(url, source, limit):
    response = requests.get(
        url, timeout=TIMEOUT, headers={"User-Agent": "OrionSearch/1.0"}
    )
    response.raise_for_status()

    articles = []

    for entry in feedparser.parse(response.content).entries[:limit]:
        date = entry.get("published_parsed") or entry.get("updated_parsed")
        published = (
            datetime(*date[:6], tzinfo=timezone.utc).isoformat() if date else None
        )

        articles.append(
            {
                "title": clean_text(entry.get("title")),
                "url": entry.get("link", ""),
                "source": source,
                "published_at": published,
                "summary": clean_text(entry.get("summary"))[:500],
            }
        )

    return articles


def search_google_news(query, days, limit):
    params = {"q": f"{query} when:{days}d", "hl": "en-US", "gl": "US", "ceid": "US:en"}

    url = "https://news.google.com/rss/search?" + urlencode(params)
    return get_rss(url, "Google News", limit)


def search_hacker_news(query, days, limit):
    since = int((datetime.now(timezone.utc) - timedelta(days=days)).timestamp())

    params = {
        "query": query,
        "tags": "story",
        "hitsPerPage": limit,
        "numericFilters": f"created_at_i>{since}",
    }

    response = requests.get(
        "https://hn.algolia.com/api/v1/search_by_date",
        params=params,
        timeout=TIMEOUT,
    )
    response.raise_for_status()

    articles = []

    for item in response.json().get("hits", []):
        article_id = item.get("objectID", "")

        articles.append(
            {
                "title": item.get("title") or "",
                "url": item.get("url")
                or f"https://news.ycombinator.com/item?id={article_id}",
                "source": "Hacker News",
                "published_at": item.get("created_at"),
                "summary": "",
            }
        )

    return articles


def search_arxiv(limit):
    return get_rss("https://rss.arxiv.org/rss/cs.AI+cs.LG+cs.CL", "arXiv", limit)


def search_hugging_face(limit):
    return get_rss("https://huggingface.co/blog/feed.xml", "Hugging Face", limit)


def process_articles(articles, days, max_articles=None):
    validate_limits(days, max_articles)

    now = datetime.now(timezone.utc)
    cutoff = now - timedelta(days=days)
    unique = {}

    for article in articles:
        title = article.get("title", "")
        url = article.get("url", "")
        published = article.get("published_at")

        if not title or not url or not published:
            continue

        try:
            date = datetime.fromisoformat(str(published).replace("Z", "+00:00"))

            if date.tzinfo is None:
                date = date.replace(tzinfo=timezone.utc)

            date = date.astimezone(timezone.utc)

        except (TypeError, ValueError):
            continue

        if not cutoff <= date <= now:
            continue

        key = re.sub(r"\W+", "", title.casefold())

        if not key:
            continue

        normalized = dict(article)
        normalized["published_at"] = date.isoformat()
        normalized["trends"] = []

        previous = unique.get(key)

        if previous is None or (normalized["published_at"], normalized["url"]) > (
            previous["published_at"],
            previous["url"],
        ):
            unique[key] = normalized

    result = sorted(
        unique.values(),
        key=lambda article: (article["published_at"], article["url"]),
        reverse=True,
    )

    return result[:max_articles] if max_articles is not None else result


def collect(topic, days, queries, selected_sources, max_articles=10):
    validate_limits(days, max_articles)
    feed_limit = get_feed_limit()

    if not isinstance(selected_sources, (list, tuple, set)):
        raise ValueError("selected_sources must be a list of source names")

    invalid_sources = set(selected_sources) - set(SOURCES)

    if invalid_sources or not selected_sources:
        raise ValueError(f"Invalid selected_sources: {sorted(invalid_sources)}")

    query_list = list(
        dict.fromkeys(
            query.strip()
            for query in queries
            if isinstance(query, str) and query.strip()
        )
    )[:2] or [topic]

    tasks = []

    for source in dict.fromkeys(selected_sources):
        if source == "google_news":
            tasks.extend(
                (f"google_news:{query}", search_google_news, (query, days, feed_limit))
                for query in query_list
            )

        elif source == "hacker_news":
            tasks.extend(
                (f"hacker_news:{query}", search_hacker_news, (query, days, feed_limit))
                for query in query_list
            )

        elif source == "arxiv":
            tasks.append(("arxiv", search_arxiv, (feed_limit,)))

        elif source == "hugging_face":
            tasks.append(("hugging_face", search_hugging_face, (feed_limit,)))

    start = perf_counter()
    raw_articles = []
    errors = {}

    with ThreadPoolExecutor(max_workers=min(MAX_WORKERS, len(tasks))) as executor:
        pending = {
            executor.submit(function, *args): task_name
            for task_name, function, args in tasks
        }

        for future in as_completed(pending):
            task_name = pending[future]

            try:
                results = future.result()
                raw_articles.extend(results)
                logger.info("%s: %d articles", task_name, len(results))

            except Exception as exc:
                errors[task_name] = str(exc)
                logger.warning("%s failed: %s", task_name, exc)

    all_articles = process_articles(raw_articles, days)
    total_articles = len(all_articles)
    selected_articles = all_articles[:max_articles]

    logger.info(
        "Collection completed in %.2fs; %d unique articles, %d returned",
        perf_counter() - start,
        total_articles,
        len(selected_articles),
    )

    return selected_articles, errors, total_articles
