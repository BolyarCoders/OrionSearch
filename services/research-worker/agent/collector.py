import json
import re
from datetime import datetime, timedelta, timezone
from pathlib import Path
from urllib.parse import urlencode

import feedparser
import requests

BASE_DIR = Path(__file__).resolve().parent
CONFIG = json.loads((BASE_DIR / "config.json").read_text(encoding="utf-8"))
OUTPUT_DIR = BASE_DIR / "output"


def get_rss(url, source):
    response = requests.get(url, timeout=20, headers={"User-Agent": "OrionSearch/0.1"})
    response.raise_for_status()
    feed = feedparser.parse(response.content)
    articles = []

    for entry in feed.entries[: CONFIG["max_per_source"]]:
        date = entry.get("published_parsed") or entry.get("updated_parsed")
        published_at = (
            datetime(*date[:6], tzinfo=timezone.utc).isoformat() if date else None
        )

        articles.append(
            {
                "title": re.sub(r"<[^>]+>", "", entry.get("title", "")).strip(),
                "url": entry.get("link", ""),
                "source": source,
                "published_at": published_at,
                "publication_date_kind": "published" if entry.get("published_parsed") else "updated",
                "summary": re.sub(r"<[^>]+>", "", entry.get("summary", "")).strip()[
                    :700
                ],
            }
        )
        publisher = entry.get("source") or {}
        if publisher.get("href") and publisher.get("title"):
            articles[-1]["publisher_url"] = publisher["href"]
            articles[-1]["publisher_name"] = publisher["title"]

    return articles


def search_google_news():
    articles = []

    for query in CONFIG["queries"]:
        params = {
            "q": f'{query} when:{CONFIG["days_back"]}d',
            "hl": "en-US",
            "gl": "US",
            "ceid": "US:en",
        }
        url = "https://news.google.com/rss/search?" + urlencode(params)
        articles.extend(get_rss(url, "Google News"))

    return articles


def search_arxiv():
    return get_rss("https://rss.arxiv.org/rss/cs.AI+cs.LG+cs.CL", "arXiv")


def search_hacker_news():
    articles = []
    since = int(
        (datetime.now(timezone.utc) - timedelta(days=CONFIG["days_back"])).timestamp()
    )

    for query in CONFIG["queries"]:
        params = {
            "query": query,
            "tags": "story",
            "hitsPerPage": CONFIG["max_per_source"],
            "numericFilters": f"created_at_i>{since}",
        }
        response = requests.get(
            "https://hn.algolia.com/api/v1/search_by_date", params=params, timeout=20
        )
        response.raise_for_status()

        for item in response.json().get("hits", []):
            articles.append(
                {
                    "title": item.get("title") or "",
                    "url": item.get("url")
                    or f'https://news.ycombinator.com/item?id={item["objectID"]}',
                    "source": "Hacker News",
                    "published_at": item.get("created_at"),
                    "publication_date_kind": "discovery" if item.get("url") else "published",
                    "summary": "",
                }
            )

    return articles


def search_hugging_face():
    return get_rss("https://huggingface.co/blog/feed.xml", "Hugging Face")


def process_articles(articles):
    now = datetime.now(timezone.utc)
    cutoff = now - timedelta(days=CONFIG["days_back"])
    unique = {}

    for article in articles:
        title = article.get("title")
        url = article.get("url")
        published_at = article.get("published_at")
        if not title or not url or not published_at:
            continue

        try:
            date = datetime.fromisoformat(published_at.replace("Z", "+00:00"))
            if date.tzinfo is None:
                date = date.replace(tzinfo=timezone.utc)
        except (AttributeError, TypeError, ValueError):
            continue

        if date < cutoff or date > now + timedelta(days=1):
            continue

        key = re.sub(r"\W+", "", title.lower())
        if key in unique:
            continue

        normalized = dict(article)
        text = f'{title} {article.get("summary", "")}'.lower()
        normalized["trends"] = [
            name
            for name, keywords in CONFIG["trend_categories"].items()
            if any(keyword.lower() in text for keyword in keywords)
        ]
        normalized["published_at"] = date.isoformat()
        unique[key] = normalized

    return sorted(
        unique.values(), key=lambda article: article["published_at"], reverse=True
    )


def save_results(articles, errors):
    OUTPUT_DIR.mkdir(exist_ok=True)
    timestamp = datetime.now(timezone.utc).strftime("%Y%m%d_%H%M%S")
    json_path = OUTPUT_DIR / f"research_{timestamp}.json"
    md_path = OUTPUT_DIR / f"research_{timestamp}.md"

    trends = {}
    for article in articles:
        for trend in article["trends"]:
            trends[trend] = trends.get(trend, 0) + 1

    result = {
        "topic": CONFIG["topic"],
        "generated_at": datetime.now(timezone.utc).isoformat(),
        "total_results": len(articles),
        "trends": trends,
        "articles": articles[: CONFIG["top_results"]],
        "errors": errors,
    }
    json_path.write_text(
        json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8"
    )

    lines = [
        f'# OrionSearch: {CONFIG["topic"]}',
        "",
        f"Found {len(articles)} articles.",
        "",
        "## Keyword signals",
        "",
    ]
    for name, count in sorted(trends.items(), key=lambda item: item[1], reverse=True):
        lines.append(f"- {name}: {count} articles")

    lines.extend(["", "## Articles", ""])
    for article in result["articles"]:
        lines.extend(
            [
                f'### {article["title"]}',
                f'- Source: {article["source"]}',
                f'- Date: {article["published_at"]}',
                f'- Link: {article["url"]}',
                f'- Tags: {", ".join(article["trends"]) or "none"}',
                "",
            ]
        )

    if errors:
        lines.extend(["## Errors", ""])
        for source, error in errors.items():
            lines.append(f"- {source}: {error}")

    md_path.write_text("\n".join(lines), encoding="utf-8")
    print(f"Saved: {json_path}\nSaved: {md_path}")


def main():
    sources = {
        "google_news": search_google_news,
        "arxiv": search_arxiv,
        "hacker_news": search_hacker_news,
        "hugging_face": search_hugging_face,
    }
    articles = []
    errors = {}

    for name, search in sources.items():
        if not CONFIG["sources"].get(name):
            continue

        try:
            results = search()
            articles.extend(results)
            print(f"{name}: {len(results)} articles")
        except (requests.RequestException, ValueError) as error:
            errors[name] = str(error)
            print(f"{name}: {error}")

    articles = process_articles(articles)
    print(f"Unique recent articles: {len(articles)}")
    save_results(articles, errors)


if __name__ == "__main__":
    main()
