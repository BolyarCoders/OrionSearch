from api.source_metadata import enrich_source_metadata, enrich_sources


def test_reads_explicit_publication_metadata_from_source_page():
    source = {"canonicalUrl": "https://example.com/story"}
    html = b'''<html lang="en-US"><head>
        <meta property="og:site_name" content="Example News">
        <meta property="article:published_time" content="2026-10-30T12:30:00+02:00">
        <meta property="article:modified_time" content="2026-11-01T10:00:00Z">
        </head></html>'''
    enrich_source_metadata(source, html, "https://example.com/story")
    assert source["publishedAt"] == "2026-10-30T10:30:00Z"
    assert source["publisher"] == {"name": "Example News", "domain": "example.com"}
    assert source["language"] == "en"


def test_never_invents_publication_date_from_update_or_naive_time():
    source = {"canonicalUrl": "https://example.com/story"}
    html = b'''<html><head><meta property="article:modified_time" content="2026-10-30T10:00:00Z">
        <meta property="article:published_time" content="2026-10-30T10:00:00"></head></html>'''
    enrich_source_metadata(source, html, "https://example.com/story")
    assert "publishedAt" not in source
    assert "publisher" not in source
    assert "language" not in source


def test_json_ld_date_published_and_date_only():
    source = {"canonicalUrl": "https://example.com/story"}
    html = b'''<html><head><script type="application/ld+json">{"@type":"NewsArticle",
        "datePublished":"2025-02-03","dateModified":"2025-03-01"}</script></head></html>'''
    enrich_source_metadata(source, html, "https://example.com/story")
    assert source["publishedAt"] == "2025-02-03T00:00:00Z"


def test_preserves_existing_metadata():
    source = {"canonicalUrl": "https://example.com/story", "publishedAt": "2025-01-01T00:00:00Z", "language": "fr"}
    enrich_source_metadata(source, b'<html lang="en"><meta property="article:published_time" content="2025-02-03T10:00:00Z"></html>', "https://example.com/story")
    assert source["publishedAt"] == "2025-01-01T00:00:00Z"
    assert source["language"] == "fr"


def test_unavailable_page_does_not_fail_research_or_bypass_exclusions():
    sources = [{"canonicalUrl": "https://example.com/story"}, {"canonicalUrl": "https://blocked.example/story"}]
    seen = []

    def fetch(url, excluded, deadline):
        seen.append((url, excluded))
        if "blocked.example" in url:
            raise ValueError("blocked")
        return b'<html lang="en"><meta property="article:published_time" content="2025-02-03T10:00:00Z"></html>', "text/html", url

    enrich_sources(sources, ["blocked.example"], fetch=fetch)
    assert sources[0]["publishedAt"] == "2025-02-03T10:00:00Z"
    assert "publishedAt" not in sources[1]
    assert all(excluded == ["blocked.example"] for _, excluded in seen)
