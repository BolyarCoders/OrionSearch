"""Bounded public retrieval and versioned private uploads; never use feed summaries."""

import hashlib
import http.client
import ipaddress
import multiprocessing
import os
import re
import socket
import ssl
import time
import unicodedata
import uuid
from datetime import datetime, timezone
from urllib.parse import quote, urljoin, urlsplit

from api.contract import ContractError, canonical_url, validate_result
from api.discovery import matches_domain

MAX_BYTES = 2097152
MAX_TEXT = 16000
MAX_SOURCES = 10


class CaptureRejected(ValueError):
    pass


def public_address(host, port):
    addresses = socket.getaddrinfo(host, port, type=socket.SOCK_STREAM)
    parsed = [ipaddress.ip_address(item[4][0]) for item in addresses]
    if not parsed or any(not address.is_global or address.is_multicast or address.is_reserved
                         or isinstance(address, ipaddress.IPv6Address) and
                         (address.ipv4_mapped is not None or address.sixtofour is not None or address.teredo is not None
                          or address in ipaddress.ip_network("64:ff9b::/96") or address in ipaddress.ip_network("64:ff9b:1::/48"))
                         for address in parsed):
        raise CaptureRejected("Non-public source")
    return addresses[0][4][0]


def connection_for(host, port, address, secure, timeout):
    # Resolve once, validate all answers, and pin the selected IP. TLS still
    # authenticates the original hostname. No proxy environment is consulted.
    connection = http.client.HTTPConnection(host, port, timeout=timeout)

    def connect():
        sock = socket.create_connection((address, port), timeout=timeout)
        if secure:
            try:
                sock = ssl.create_default_context().wrap_socket(sock, server_hostname=host)
            except BaseException:
                sock.close()
                raise
        connection.sock = sock

    connection.connect = connect
    return connection


def fetch_public(url, excluded, deadline, *, resolver=public_address, connector=connection_for):
    for redirect in range(4):
        url = canonical_url(url)
        parts = urlsplit(url)
        host = parts.hostname
        port = parts.port or (443 if parts.scheme == "https" else 80)
        if port != (443 if parts.scheme == "https" else 80) or matches_domain(host, excluded):
            raise CaptureRejected("Source is not permitted")
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            raise CaptureRejected("Capture time budget exhausted")
        address = resolver(host, port)
        connection = connector(host, port, address, parts.scheme == "https", min(10, remaining))
        try:
            target = parts.path + ("?" + parts.query if parts.query else "")
            connection.request("GET", target, headers={"User-Agent": "OrionSearch/1.0 (research capture)",
                "Accept": "text/html,text/plain", "Accept-Encoding": "identity"})
            response = connection.getresponse()
            if response.status in (301, 302, 303, 307, 308):
                location = response.getheader("Location")
                if redirect == 3 or not location:
                    raise CaptureRejected("Redirect limit exceeded")
                next_url = canonical_url(urljoin(url, location))
                if parts.scheme == "https" and urlsplit(next_url).scheme != "https":
                    raise CaptureRejected("Insecure redirect")
                url = next_url
                continue
            media_type = (response.getheader("Content-Type") or "").split(";")[0].strip().lower()
            if response.status != 200 or media_type not in ("text/html", "text/plain"):
                raise CaptureRejected("Unsupported capture response")
            if (response.getheader("Content-Encoding") or "identity").lower() != "identity":
                raise CaptureRejected("Compressed capture is not supported")
            length = response.getheader("Content-Length")
            if length is not None and (not length.isdecimal() or not 0 < int(length) <= MAX_BYTES):
                raise CaptureRejected("Invalid capture size")
            data = bytearray()
            while True:
                remaining = deadline - time.monotonic()
                if remaining <= 0:
                    raise CaptureRejected("Capture time budget exhausted")
                if connection.sock is not None:
                    connection.sock.settimeout(min(10, remaining))
                block = response.read1(min(16384, MAX_BYTES + 1 - len(data)))
                if not block:
                    break
                data.extend(block)
                if len(data) > MAX_BYTES:
                    raise CaptureRejected("Capture is too large")
            if not data or length is not None and len(data) != int(length):
                raise CaptureRejected("Incomplete capture")
            return bytes(data), media_type, url
        finally:
            connection.close()
    raise CaptureRejected("Redirect limit exceeded")


def extract_text(data, media_type):
    if media_type == "text/plain":
        text = data.decode("utf-8", errors="strict")
    else:
        from bs4 import BeautifulSoup
        soup = BeautifulSoup(data, "html.parser")
        for element in soup(["script", "style", "noscript", "template", "nav", "header", "footer", "form"]):
            element.decompose()
        main = soup.find("article") or soup.find("main") or soup.body
        if main is None:
            raise CaptureRejected("No source body")
        text = main.get_text(separator="\n", strip=True)
    # Same identity normalization as ContentFingerprint: NFC, LF and Unicode trim.
    text = unicodedata.normalize("NFC", text)
    text = re.sub(r"\r\n|[\r\n\f\u0085\u2028\u2029]", "\n", text)
    text = text.strip(" \t\n\r\v\f\u0085\u00a0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200a\u2028\u2029\u202f\u205f\u3000")
    if not text or len(text) > MAX_TEXT or "\x00" in text:
        raise CaptureRejected("Unsupported extracted text size")
    return text


def _fetch_child(pipe, url, excluded, deadline):
    try:
        pipe.send(fetch_public(url, excluded, deadline))
    except Exception:
        pipe.send(None)
    finally:
        pipe.close()


def fetch_isolated(url, excluded, deadline):
    # DNS resolution and network reads can block outside Python's clock checks.
    # A disposable process provides a hard per-source boundary, including DNS.
    context = multiprocessing.get_context("spawn")
    reader, writer = context.Pipe(duplex=False)
    process = context.Process(target=_fetch_child, args=(writer, url, excluded, deadline), daemon=True)
    try:
        process.start()
        writer.close()
        if not reader.poll(max(0, min(15, deadline - time.monotonic()))):
            raise CaptureRejected("Source retrieval timed out")
        result = reader.recv()
        if result is None:
            raise CaptureRejected("Source retrieval failed")
        return result
    except EOFError:
        raise CaptureRejected("Source retrieval failed") from None
    finally:
        reader.close()
        writer.close()
        if process.pid is not None:
            process.join(timeout=0.1)
            if process.is_alive():
                process.terminate()
                process.join(timeout=2)
            if process.is_alive():
                process.kill()
                process.join(timeout=2)
            process.close()


class CaptureUploader:
    def __init__(self):
        import boto3
        from botocore.config import Config
        self.bucket = os.environ["ORION_CAPTURE_BUCKET"]
        self.prefix = os.getenv("ORION_CAPTURE_PREFIX", "captures")
        self.write_mode = os.getenv("ORION_CAPTURE_WRITE_MODE", "conditional")
        if self.write_mode not in ("conditional", "backblaze"):
            raise RuntimeError("Invalid capture write mode")
        self.uploaded = {}
        endpoint = os.environ["ORION_CAPTURE_ENDPOINT"]
        parts = urlsplit(endpoint)
        loopback = parts.hostname in ("localhost", "127.0.0.1", "::1")
        if (parts.scheme != "https" and not (parts.scheme == "http" and loopback and os.getenv("ORION_CAPTURE_ALLOW_LOOPBACK_HTTP") == "true")
                or parts.username is not None or parts.path not in ("", "/") or parts.query or parts.fragment):
            raise RuntimeError("Unsafe capture store endpoint")
        if not self.bucket or not self.prefix or any(not (c.isascii() and (c.isalnum() or c in "_-")) for c in self.prefix):
            raise RuntimeError("Invalid capture store configuration")
        self.client = boto3.client("s3", endpoint_url=endpoint, region_name=os.getenv("ORION_CAPTURE_REGION", "us-east-1"),
            aws_access_key_id=os.environ["ORION_CAPTURE_ACCESS_KEY"], aws_secret_access_key=os.environ["ORION_CAPTURE_SECRET_KEY"],
            config=Config(connect_timeout=5, read_timeout=10, retries={"total_max_attempts": 1}, s3={"addressing_style": "path"}))

    def upload(self, run_id, nonce, data, media_type):
        digest = hashlib.sha256(data).hexdigest()
        key = f"{self.prefix}/{uuid.UUID(run_id)}/{nonce}/{digest}"
        if key in self.uploaded:
            return dict(self.uploaded[key])
        arguments = {"Bucket": self.bucket, "Key": key, "Body": data, "ContentType": media_type}
        if self.write_mode == "conditional":
            arguments["IfNoneMatch"] = "*"
        response = self.client.put_object(**arguments)
        version = response.get("VersionId")
        if not version or version == "null":
            raise RuntimeError("Capture bucket versioning is required")
        raw = {"storageUri": f"s3://{self.bucket}/{key}?versionId={quote(version, safe='')}", "sha256": digest}
        self.uploaded[key] = raw
        return dict(raw)

    def close(self):
        self.client.close()


def enrich_captures(result, request, uploader, *, fetch=fetch_isolated):
    deadline = time.monotonic() + 60
    nonce = uuid.uuid4().hex
    total = 0
    for source in result["sources"][:MAX_SOURCES]:
        if time.monotonic() >= deadline:
            break
        try:
            data, media_type, final_url = fetch(source["canonicalUrl"], request["settings"]["excludedDomains"], deadline)
            total += len(data)
            if total > 16777216:
                break
            text = extract_text(data, media_type)
        except (CaptureRejected, ValueError, UnicodeError, OSError, http.client.HTTPException):
            # A rejected article remains an honest discovery. Storage failures
            # below are not swallowed into successful capture/discovery output.
            continue
        raw = uploader.upload(request["researchRunId"], nonce, data, media_type)
        source["capture"] = {"fetchedAt": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
            "extractedText": text, "raw": raw, "chunks": [
                {"index": index, "text": text[start:start + 2000], "startOffset": start, "endOffset": min(start + 2000, len(text))}
                for index, start in enumerate(range(0, len(text), 2000)) if text[start:start + 2000].strip()],
            "metadata": {"extractor": "visible_html_nfc_lf_v1" if media_type == "text/html" else "utf8_nfc_lf_v1", "final_url": final_url}}
    validate_result(result, request)
    return result
