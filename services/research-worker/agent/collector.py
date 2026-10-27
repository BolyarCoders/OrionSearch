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
MAX_WORKERS = 4
FEED_LIMIT = max(1, int(os.getenv("RESEARCH_FEED_LIMIT", "12")))


def clean_text(value):
    return html.unescape(re.sub(r"<[^>]+>", "", value or "")).strip()


def get_rss(url, source, limit=FEED_LIMIT):
    response = requests.get(
        url, timeout=TIMEOUT, headers={"User-Agent": "OrionSearch/1.0"}
    )
    response.raise_for_status()

    feed = feedparser.parse(response.content)
    articles = []

    for entry in feed.entries[:limit]:
        date = entry.get("published_parsed") or entry.get("updated_parsed")
        published_at = (
            datetime(*date[:6], tzinfo=timezone.utc).isoformat() if date else None
        )

        articles.append(
            {
                "title": clean_text(entry.get("title")),
                "url": entry.get("link", ""),
                "source": source,
                "published_at": published_at,
                "summary": clean_text(entry.get("summary"))[:500],
            }
        )

    return articles


def search_google_news(query, days, limit=FEED_LIMIT):
    params = {"q": f"{query} when:{days}d", "hl": "en-US", "gl": "US", "ceid": "US:en"}
    url = "https://news.google.com/rss/search?" + urlencode(params)
    return get_rss(url, "Google News", limit)


def search_hacker_news(query, days, limit=FEED_LIMIT):
    since = int((datetime.now(timezone.utc) - timedelta(days=days)).timestamp())

    params = {
        "query": query,
        "tags": "story",
        "hitsPerPage": limit,
        "numericFilters": f"created_at_i>{since}",
    }

    response = requests.get(
        "https://hn.algolia.com/api/v1/search_by_date", params=params, timeout=TIMEOUT
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


def search_arxiv(limit=FEED_LIMIT):
    url = "https://rss.arxiv.org/rss/cs.AI+cs.LG+cs.CL"
    return get_rss(url, "arXiv", limit)


<<<<<<< Updated upstream
def process_articles(articles):
    cutoff = datetime.now(timezone.utc) - timedelta(days=CONFIG["days_back"])
    unique = {}

    for article in articles:
        if not article["title"] or not article["url"] or not article["published_at"]:
=======
def search_hugging_face(limit=FEED_LIMIT):
    url = "https://huggingface.co/blog/feed.xml"
    return get_rss(url, "Hugging Face", limit)


def process_articles(articles, days, max_articles):
    now = datetime.now(timezone.utc)
    cutoff = now - timedelta(days=days)
    unique = {}

    for article in articles:
        title = article.get("title")
        url = article.get("url")
        published_at = article.get("published_at")

        if not title or not url or not published_at:
>>>>>>> Stashed changes
            continue

        try:
            date = datetime.fromisoformat(
                article["published_at"].replace("Z", "+00:00")
            )
            if date.tzinfo is None:
                date = date.replace(tzinfo=timezone.utc)
        except ValueError:
            continue

<<<<<<< Updated upstream
        if date < cutoff or date > datetime.now(timezone.utc) + timedelta(days=1):
            continue

        key = re.sub(r"\W+", "", article["title"].lower())
        if key in unique:
            continue

        text = f'{article["title"]} {article["summary"]}'.lower()
        article["trends"] = [
            name
            for name, keywords in CONFIG["trend_categories"].items()
            if any(keyword.lower() in text for keyword in keywords)
        ]
        article["published_at"] = date.isoformat()
        unique[key] = article
=======
        if not cutoff <= date <= now + timedelta(days=1):
            continue

        key = re.sub(r"\W+", "", title.lower())

        if not key or key in unique:
            continue

        normalized = dict(article)
        normalized["published_at"] = date.isoformat()
        normalized["trends"] = []
        unique[key] = normalized
>>>>>>> Stashed changes

    sorted_articles = sorted(
        unique.values(), key=lambda article: article["published_at"], reverse=True
    )

    return sorted_articles[:max_articles]


def collect(topic, days, queries, selected_sources, max_articles=10):
    start = perf_counter()
    tasks = []

    enabled = set(selected_sources) & set(SOURCES)
    unique_queries = list(dict.fromkeys(q.strip() for q in queries if q.strip()))[:2]
    unique_queries = unique_queries or [topic]

    if "google_news" in enabled:
        for query in unique_queries:
            tasks.append(("google_news", search_google_news, (query, days)))

    if "hacker_news" in enabled:
        for query in unique_queries:
            tasks.append(("hacker_news", search_hacker_news, (query, days)))

    if "arxiv" in enabled:
        tasks.append(("arxiv", search_arxiv, ()))

    if "hugging_face" in enabled:
        tasks.append(("hugging_face", search_hugging_face, ()))

    articles = []
    errors = {}

    if not tasks:
        return [], {"collector": "No enabled sources"}

    with ThreadPoolExecutor(max_workers=min(MAX_WORKERS, len(tasks))) as executor:
        futures = {
            executor.submit(function, *args): name for name, function, args in tasks
        }

        for future in as_completed(futures):
            name = futures[future]

            try:
                results = future.result()
                articles.extend(results)
                logger.info("%s returned %d articles", name, len(results))
            except Exception as error:
                errors[name] = str(error)
                logger.warning("%s failed: %s", name, error)

    articles = process_articles(articles, days, max_articles)

    logger.info(
        "Collection finished in %.2fs; returned %d articles",
        perf_counter() - start,
        len(articles),
    )

    return articles, errors
