"""The shared JSON Schemas own the wire shape; checks here cover semantic invariants."""

import json
import unicodedata
import idna
from datetime import date, datetime
from functools import lru_cache
from pathlib import Path
from urllib.parse import urlsplit

from jsonschema import Draft202012Validator, FormatChecker

VERSION = "orion.research.v1"
PROVISIONAL_VERSION = "orion.research.provisional.v1"
MAX_REQUEST_BYTES = 262144
MAX_RESPONSE_BYTES = 2097152
CONTRACT_ROOT = Path(__file__).resolve().parents[3] / "packages/contracts/research"


class ContractError(ValueError):
    pass


@lru_cache
def validator(kind, provisional=False):
    directory = "provisional-v1" if provisional else "v1"
    schema = json.loads((CONTRACT_ROOT / directory / f"{kind}.schema.json").read_text(encoding="utf-8"))
    Draft202012Validator.check_schema(schema)
    return Draft202012Validator(schema, format_checker=FormatChecker())


def validate_request(payload, allow_provisional=False):
    if not isinstance(payload, dict):
        raise ContractError("Invalid request")
    version = payload.get("contractVersion")
    if version != VERSION and not (allow_provisional and version == PROVISIONAL_VERSION):
        raise ContractError("Unsupported contract")
    if not validator("request", version == PROVISIONAL_VERSION).is_valid(payload):
        raise ContractError("Invalid request")
    pending = [(payload, 0)]
    while pending:
        value, depth = pending.pop()
        if depth > 64:
            raise ContractError("Request is too deeply nested")
        if isinstance(value, dict):
            pending.extend((item, depth + 1) for item in value.values())
        elif isinstance(value, list):
            pending.extend((item, depth + 1) for item in value)
    if not payload["title"].strip() or not payload["objective"].strip():
        raise ContractError("Empty research topic")
    settings = payload["settings"]
    if len(json.dumps(settings["config"], ensure_ascii=False).encode("utf-8")) > 16384:
        raise ContractError("Research settings are too large")
    start, end = settings["dateFrom"], settings["dateTo"]
    if start and end and date.fromisoformat(start) > date.fromisoformat(end):
        raise ContractError("Invalid date window")
    updated = datetime.fromisoformat(settings["updatedAt"].replace("Z", "+00:00"))
    if updated.tzinfo is None or updated.utcoffset().total_seconds() != 0:
        raise ContractError("Settings time must be UTC")
    for field in ("preferredDomains", "excludedDomains"):
        values = settings[field]
        if len(values) != len(set(values)) or any(not valid_domain(value) for value in values):
            raise ContractError("Invalid domain settings")
    if set(settings["preferredDomains"]) & set(settings["excludedDomains"]):
        raise ContractError("Overlapping domain settings")
    languages = settings["languages"]
    if len(languages) != len(set(languages)) or any(len(value) != 2 or not value.isascii() or not value.isalpha() for value in languages):
        raise ContractError("Invalid language settings")
    return payload


def valid_domain(value):
    try:
        normalized = idna.encode(value, uts46=True).decode("ascii").lower()
        return normalized == value and all(
            0 < len(label) <= 63 and label[0].isalnum() and label[-1].isalnum()
            and all(character.isalnum() or character == "-" for character in label)
            for label in normalized.split(".")
        )
    except (UnicodeError, IndexError):
        return False


def canonical_url(value):
    if not isinstance(value, str) or len(value) > 4096 or "\\" in value or any(unicodedata.category(c) == "Cc" or c.isspace() for c in value):
        raise ValueError("Invalid source URL")
    parts = urlsplit(value)
    if parts.scheme.lower() not in ("https", "http") or not parts.hostname or parts.username is not None:
        raise ValueError("Invalid source URL")
    for index, character in enumerate(value):
        if character == "%" and (len(value[index + 1:index + 3]) != 2 or any(c not in "0123456789abcdefABCDEF" for c in value[index + 1:index + 3])):
            raise ValueError("Invalid source URL escape")
    host = parts.hostname.lower()
    if ":" not in host:
        host = idna.encode(host, uts46=True).decode("ascii").lower()
    if ":" in host:
        host = f"[{host}]"
    port = parts.port
    authority = host if port is None or port == (443 if parts.scheme.lower() == "https" else 80) else f"{host}:{port}"
    suffix = value.split("://", 1)[1]
    boundary = min((suffix.find(c) for c in "/?#" if c in suffix), default=len(suffix))
    suffix = suffix[boundary:].split("#", 1)[0]
    if not suffix.startswith("/"):
        suffix = "/" + suffix
    return f"{parts.scheme.lower()}://{authority}{suffix}"


def validate_result(result, request):
    if not isinstance(result, dict):
        raise ContractError("Invalid result")
    if result.get("contractVersion") != request["contractVersion"] or result.get("researchRunId") != request["researchRunId"]:
        raise ContractError("Result correlation failed")
    if not validator("result", request["contractVersion"] == PROVISIONAL_VERSION).is_valid(result):
        raise ContractError("Invalid result")
    # The P2 producer returns discoveries only. Capture/evidence semantics remain
    # enforced by .NET for full v1 fixtures and by the later capture implementation.
    encoded = json.dumps(result, ensure_ascii=False, allow_nan=False, separators=(",", ":")).encode("utf-8")
    if len(encoded) > MAX_RESPONSE_BYTES:
        raise ContractError("Result too large")
    return encoded
