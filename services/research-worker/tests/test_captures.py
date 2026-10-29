import copy
import hashlib
import json
import socket
import time
import uuid
from datetime import datetime, timezone
from types import SimpleNamespace

import pytest

from api.captures import (CaptureRejected, CaptureUploader, enrich_captures,
    extract_text, fetch_public, fetch_isolated, public_address, MAX_BYTES)
from api.contract import CONTRACT_ROOT, validate_result


def test_shared_capture_fixture_is_generated_by_the_actual_adapter(monkeypatch):
    from api import captures
    class Clock:
        @staticmethod
        def now(tz): return datetime(2026, 10, 29, 0, 0, 0, tzinfo=timezone.utc)
    monkeypatch.setattr(captures, "datetime", Clock)
    monkeypatch.setattr(captures.uuid, "uuid4", lambda: uuid.UUID("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"))
    request, result = request_result()
    raw = (CONTRACT_ROOT / "v1/capture-source.html").read_bytes()
    uploader = object.__new__(CaptureUploader)
    uploader.bucket, uploader.prefix, uploader.uploaded = "test-captures", "captures", {}
    uploader.client = SimpleNamespace(put_object=lambda **k: {"VersionId": "fixture-version"})
    actual = enrich_captures(result, request, uploader, fetch=lambda *a: (raw, "text/html", "https://example.com/source"))
    assert actual == json.loads((CONTRACT_ROOT / "v1/capture-result.json").read_text(encoding="utf-8"))


def test_public_resolution_rejects_mixed_private_and_public_answers(monkeypatch):
    monkeypatch.setattr(socket, "getaddrinfo", lambda *a, **k: [
        (2, 1, 6, "", ("93.184.216.34", 443)), (2, 1, 6, "", ("127.0.0.1", 443))])
    with pytest.raises(CaptureRejected): public_address("example.com", 443)


@pytest.mark.parametrize("address", ["127.0.0.1", "10.0.0.1", "169.254.169.254", "::1", "::ffff:127.0.0.1", "100.64.0.1", "192.0.2.1",
    "224.0.0.1", "ff02::1", "64:ff9b::a00:1", "2002:0a00:0001::1", "::ffff:8.8.8.8"])
def test_nonpublic_addresses_cannot_be_fetched(monkeypatch, address):
    monkeypatch.setattr(socket, "getaddrinfo", lambda *a, **k: [(2, 1, 6, "", (address, 443))])
    with pytest.raises(CaptureRejected): public_address("example.com", 443)


class Response:
    def __init__(self, body=b"actual article", status=200, **headers):
        self.body, self.status = body, status
        self.headers = {"Content-Type": "text/plain", **headers}
    def getheader(self, name): return self.headers.get(name)
    def read1(self, size):
        block, self.body = self.body[:size], self.body[size:]
        return block


def transport(responses, calls):
    def connector(host, port, ip, secure, timeout):
        calls.append((host, ip, secure))
        return SimpleNamespace(sock=None, request=lambda *a, **k: None,
            getresponse=lambda: responses.pop(0), close=lambda: calls.append("closed"))
    return connector


@pytest.mark.parametrize("response", [Response(status=403), Response(**{"Content-Type": "application/pdf"}),
    Response(**{"Content-Encoding": "gzip"}), Response(**{"Content-Length": str(MAX_BYTES + 1)}),
    Response(**{"Content-Length": "100"}), Response(body=b"x" * (MAX_BYTES + 1))])
def test_invalid_or_oversized_responses_are_closed_and_rejected(response):
    calls = []
    with pytest.raises(CaptureRejected):
        fetch_public("https://example.com/article", [], time.monotonic() + 10,
            resolver=lambda *a: "93.184.216.34", connector=transport([response], calls))
    assert calls[-1] == "closed"


def test_every_redirect_is_resolved_and_pinned_again():
    calls = []
    def resolve(host, port):
        if host == "internal.example": raise CaptureRejected("private")
        return "93.184.216.34"
    with pytest.raises(CaptureRejected):
        fetch_public("https://example.com/article", [], time.monotonic() + 10, resolver=resolve,
            connector=transport([Response(status=302, Location="https://internal.example/secret")], calls))
    assert calls == [("example.com", "93.184.216.34", True), "closed"]


@pytest.mark.parametrize("target", ["http://example.com/plain", "https://excluded.example/path", "https://example.com:8443/path"])
def test_redirects_cannot_downgrade_or_bypass_domain_or_port_policy(target):
    calls = []
    with pytest.raises(CaptureRejected):
        fetch_public("https://example.com/article", ["excluded.example"], time.monotonic() + 10,
            resolver=lambda *a: "93.184.216.34", connector=transport([Response(status=302, Location=target)], calls))
    assert calls.count("closed") == 1


def test_visible_text_normalization_and_unicode_scalar_chunks():
    assert extract_text("\ufeff".encode(), "text/plain") == "\ufeff"
    raw = "<html><body><nav>ignore</nav><article>Café 😀<script>evil()</script><p>Text.</p></article></body></html>".encode()
    assert extract_text(raw, "text/html") == "Café 😀\nText."
    assert extract_text("  café\r\nx\u0085y\u2028z  ".encode(), "text/plain") == "café\nx\ny\nz"


class Uploader:
    def __init__(self): self.uploads = []
    def upload(self, run, nonce, data, media_type):
        self.uploads.append(data)
        digest = hashlib.sha256(data).hexdigest()
        return {"storageUri": f"s3://test-captures/captures/{run}/{nonce}/{digest}?versionId=fixture-version", "sha256": digest}


def request_result():
    request = json.loads((CONTRACT_ROOT / "v1/request.json").read_text())
    result = json.loads((CONTRACT_ROOT / "v1/discovery-result.json").read_text())
    return request, result


def test_capture_uses_actual_bytes_never_feed_summary_and_quotes_remain_empty():
    request, result = request_result()
    uploader = Uploader()
    data = ("A😀" * 2001 + " evidence.").encode()
    result = enrich_captures(result, request, uploader, fetch=lambda *a: (data, "text/plain", "https://example.com/source"))
    validate_result(result, request)
    capture = result["sources"][0]["capture"]
    assert capture["raw"]["sha256"] == hashlib.sha256(data).hexdigest()
    assert uploader.uploads == [data]
    assert result["findings"] == []
    for chunk in capture["chunks"]:
        assert capture["extractedText"][chunk["startOffset"]:chunk["endOffset"]] == chunk["text"]
    assert len(capture["chunks"]) == 3


def test_rejected_source_remains_discovery_and_storage_failure_is_not_hidden():
    request, result = request_result()
    original = copy.deepcopy(result)
    def reject(*a): raise CaptureRejected("private")
    assert enrich_captures(result, request, Uploader(), fetch=reject) == original
    uploader = Uploader()
    def fail(*a): raise RuntimeError("storage unavailable")
    uploader.upload = fail
    with pytest.raises(RuntimeError):
        enrich_captures(result, request, uploader, fetch=lambda *a: (b"real text", "text/plain", "https://example.com"))


def test_upload_requires_versioning_and_retains_exact_digest():
    uploader = object.__new__(CaptureUploader)
    uploader.bucket, uploader.prefix = "test-captures", "captures"
    uploader.uploaded = {}
    calls = []
    def put(**values):
        calls.append(values)
        return {"VersionId": "immutable/version+1"}
    uploader.client = SimpleNamespace(put_object=put)
    raw = uploader.upload("11111111-1111-1111-1111-111111111111", "a" * 32, b"raw", "text/plain")
    assert raw["storageUri"].endswith("?versionId=immutable%2Fversion%2B1")
    assert calls[0]["IfNoneMatch"] == "*"
    assert calls[0]["Body"] == b"raw"
    assert uploader.upload("11111111-1111-1111-1111-111111111111", "a" * 32, b"raw", "text/plain") == raw
    assert len(calls) == 1
    uploader.client = SimpleNamespace(put_object=lambda **k: {"VersionId": "null"})
    with pytest.raises(RuntimeError, match="versioning"): uploader.upload("11111111-1111-1111-1111-111111111111", "b" * 32, b"x", "text/plain")


def test_real_disposable_fetch_process_rejects_loopback_before_connecting():
    with pytest.raises(CaptureRejected):
        fetch_isolated("http://127.0.0.1/private", [], time.monotonic() + 10)
