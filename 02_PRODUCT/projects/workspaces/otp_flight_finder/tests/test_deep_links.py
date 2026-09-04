# -*- coding: utf-8 -*-
"""
tests/test_deep_links.py
------------------------
Unit-tests for flight_engine.deep_links.build_deep_link.

Key invariants verified here:
- All links originate from OTP only (never BBU / Baneasa).
- Ryanair links contain both origin= and originIata= (and destination variants).
- Passing a non-OTP origin raises AssertionError.
"""

from __future__ import annotations

import pytest

from flight_engine.deep_links import build_deep_link

# ---------------------------------------------------------------------------
# Ryanair one-way
# ---------------------------------------------------------------------------


def test_ryanair_one_way() -> None:
    url = build_deep_link("Ryanair", "OTP", "STN", "2026-10-01")
    assert "origin=OTP" in url, "origin= param missing"
    assert "originIata=OTP" in url, "originIata= param missing"
    assert "destination=STN" in url, "destination= param missing"
    assert "destinationIata=STN" in url, "destinationIata= param missing"
    assert "isReturn=false" in url


def test_ryanair_round_trip() -> None:
    url = build_deep_link("Ryanair", "OTP", "STN", "2026-10-01", "2026-10-08")
    assert "origin=OTP" in url
    assert "originIata=OTP" in url
    assert "destination=STN" in url
    assert "destinationIata=STN" in url
    assert "isReturn=true" in url
    assert "dateIn=" in url
    assert "2026-10-08" in url


# ---------------------------------------------------------------------------
# BBU / Baneasa must never appear
# ---------------------------------------------------------------------------


def test_ryanair_no_bbu() -> None:
    url = build_deep_link("Ryanair", "OTP", "STN", "2026-10-01")
    assert "BBU" not in url
    assert "baneasa" not in url.lower()


# ---------------------------------------------------------------------------
# Wizz Air
# ---------------------------------------------------------------------------


def test_wizzair_url() -> None:
    url = build_deep_link("Wizz Air", "OTP", "BCN", "2026-11-15")
    assert url.startswith("https://wizzair.com"), f"Unexpected base URL: {url}"
    assert "OTP" in url
    assert "BCN" in url


def test_wizzair_round_trip_contains_return_date() -> None:
    url = build_deep_link("Wizz Air", "OTP", "BCN", "2026-11-15", "2026-11-22")
    assert "2026-11-22" in url


def test_wizzair_one_way_null_placeholder() -> None:
    url = build_deep_link("Wizz Air", "OTP", "BCN", "2026-11-15")
    assert "null" in url


# ---------------------------------------------------------------------------
# AssertionError when origin is not OTP
# ---------------------------------------------------------------------------


def test_origin_always_otp() -> None:
    """Passing BBU as origin must raise AssertionError."""
    with pytest.raises(AssertionError):
        build_deep_link("Ryanair", "BBU", "STN", "2026-10-01")


def test_origin_non_otp_raises_for_all_airlines() -> None:
    for airline in ("Ryanair", "Wizz Air", "Blue Air", "TAROM"):
        with pytest.raises(AssertionError):
            build_deep_link(airline, "BBU", "STN", "2026-10-01")


# ---------------------------------------------------------------------------
# All supported airlines produce OTP links with no BBU
# ---------------------------------------------------------------------------


def test_all_airlines() -> None:
    airlines = ["Ryanair", "Wizz Air", "Blue Air", "TAROM"]
    for airline in airlines:
        url = build_deep_link(airline, "OTP", "MAD", "2026-12-10")
        assert "OTP" in url, f"OTP missing in {airline} link: {url}"
        assert "BBU" not in url, f"BBU found in {airline} link: {url}"
        assert "baneasa" not in url.lower(), f"baneasa found in {airline} link: {url}"


# ---------------------------------------------------------------------------
# Blue Air
# ---------------------------------------------------------------------------


def test_blue_air_url_structure() -> None:
    url = build_deep_link("Blue Air", "OTP", "ATH", "2026-09-20")
    assert "blueairweb.com" in url
    assert "from=OTP" in url
    assert "to=ATH" in url


# ---------------------------------------------------------------------------
# TAROM
# ---------------------------------------------------------------------------


def test_tarom_url_structure() -> None:
    url = build_deep_link("TAROM", "OTP", "FCO", "2026-09-25")
    assert "tarom.ro" in url
    assert "from=OTP" in url
    assert "to=FCO" in url


# ---------------------------------------------------------------------------
# Fallback (unknown airline)
# ---------------------------------------------------------------------------


def test_fallback_unknown_airline() -> None:
    url = build_deep_link("Unknown Airline", "OTP", "LHR", "2027-01-01")
    assert "google.com/travel/flights" in url
    assert "OTP" in url
    assert "LHR" in url