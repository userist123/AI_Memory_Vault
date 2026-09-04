# -*- coding: utf-8 -*-
"""
tests/test_api.py
-----------------
Integration tests for the OTP Flight Finder FastAPI application.

Uses FastAPI's TestClient (backed by httpx) so no live server is needed.

Key invariants tested:
- /api/destinations returns a non-empty list.
- /api/search returns results for known destinations.
- The origin is ALWAYS OTP in every returned deal, even if the client
  sends a different origin (e.g. BBU).
- Neither 'BBU' nor 'baneasa' appears anywhere in any generated deep-link
  or JSON response body.
"""

from __future__ import annotations

import json

import pytest
from fastapi.testclient import TestClient

from flight_server import app

client = TestClient(app, raise_server_exceptions=True)

# Five representative destinations to stress-test the no-BBU invariant
_SAMPLE_DESTINATIONS = ["STN", "BCN", "VIE", "ATH", "ALC"]


# ---------------------------------------------------------------------------
# /api/destinations
# ---------------------------------------------------------------------------


def test_destinations_returns_list() -> None:
    resp = client.get("/api/destinations")
    assert resp.status_code == 200
    data = resp.json()
    assert isinstance(data, list), "Expected a JSON array"
    assert len(data) >= 10, f"Expected >= 10 destinations, got {len(data)}"


def test_destinations_have_required_keys() -> None:
    resp = client.get("/api/destinations")
    for dest in resp.json():
        assert "iata" in dest
        assert "city" in dest
        assert "country" in dest
        assert "airlines" in dest
        assert isinstance(dest["airlines"], list)


# ---------------------------------------------------------------------------
# /api/search — basic functionality
# ---------------------------------------------------------------------------


def test_search_returns_results() -> None:
    resp = client.post(
        "/api/search",
        json={"origin": "OTP", "destination": "STN", "dateOut": "2026-10-01"},
    )
    assert resp.status_code == 200
    body = resp.json()
    assert "results" in body
    assert body["results"], "Expected at least one flight deal"
    assert body["count"] == len(body["results"])


def test_search_unknown_destination_returns_empty() -> None:
    resp = client.post(
        "/api/search",
        json={"origin": "OTP", "destination": "XYZ", "dateOut": "2026-10-01"},
    )
    assert resp.status_code == 200
    assert resp.json()["results"] == []
    assert resp.json()["count"] == 0


def test_search_round_trip() -> None:
    resp = client.post(
        "/api/search",
        json={
            "origin": "OTP",
            "destination": "BCN",
            "dateOut": "2026-10-01",
            "dateIn": "2026-10-08",
        },
    )
    assert resp.status_code == 200
    results = resp.json()["results"]
    assert results, "Round-trip search returned no results"
    for deal in results:
        assert deal["is_return"] is True
        assert deal["date_in"] == "2026-10-08"


def test_search_results_are_deterministic() -> None:
    """Same query must always yield identical results (seeded RNG)."""
    payload = {"origin": "OTP", "destination": "VIE", "dateOut": "2026-11-11"}
    r1 = client.post("/api/search", json=payload).json()["results"]
    r2 = client.post("/api/search", json=payload).json()["results"]
    assert r1 == r2, "Results should be deterministic for the same query"


# ---------------------------------------------------------------------------
# OTP enforcement
# ---------------------------------------------------------------------------


def test_origin_always_otp() -> None:
    """Even if the client sends origin=BBU, all results must have origin=OTP."""
    resp = client.post(
        "/api/search",
        json={"origin": "BBU", "destination": "STN", "dateOut": "2026-10-01"},
    )
    assert resp.status_code == 200
    body = resp.json()
    # Query echo must be OTP
    assert body["query"]["origin"] == "OTP"
    # Every deal must have origin OTP
    for deal in body["results"]:
        assert deal["origin"] == "OTP", f"Deal has wrong origin: {deal}"


# ---------------------------------------------------------------------------
# No BBU / Baneasa in any URL or response text
# ---------------------------------------------------------------------------


def test_no_bbu_in_any_url() -> None:
    """BBU must not appear in any deep_link across 5 destinations."""
    for iata in _SAMPLE_DESTINATIONS:
        resp = client.post(
            "/api/search",
            json={"origin": "OTP", "destination": iata, "dateOut": "2026-10-15"},
        )
        assert resp.status_code == 200
        for deal in resp.json()["results"]:
            assert "BBU" not in deal["deep_link"], (
                f"BBU found in deep_link for {iata}: {deal['deep_link']}"
            )


def test_no_baneasa_in_response() -> None:
    """'baneasa' (case-insensitive) must not appear anywhere in the JSON body."""
    for iata in _SAMPLE_DESTINATIONS:
        resp = client.post(
            "/api/search",
            json={"origin": "OTP", "destination": iata, "dateOut": "2026-10-15"},
        )
        assert resp.status_code == 200
        raw_text = resp.text.lower()
        assert "baneasa" not in raw_text, (
            f"'baneasa' found in response for {iata}: {resp.text[:200]}"
        )


# ---------------------------------------------------------------------------
# Static / frontend
# ---------------------------------------------------------------------------


def test_static_index() -> None:
    """GET / must return HTTP 200."""
    resp = client.get("/")
    assert resp.status_code == 200


# ---------------------------------------------------------------------------
# Deal structure validation
# ---------------------------------------------------------------------------


def test_deal_has_all_required_fields() -> None:
    resp = client.post(
        "/api/search",
        json={"origin": "OTP", "destination": "MAD", "dateOut": "2026-12-01"},
    )
    required_fields = {
        "airline", "origin", "destination", "date_out", "date_in",
        "price_eur", "currency", "deep_link", "is_return", "badge",
    }
    for deal in resp.json()["results"]:
        assert required_fields.issubset(deal.keys()), (
            f"Missing fields in deal: {required_fields - deal.keys()}"
        )
        assert deal["currency"] == "EUR"
        assert 19.0 <= deal["price_eur"] <= 299.0, (
            f"Price out of range: {deal['price_eur']}"
        )
        assert deal["origin"] == "OTP"