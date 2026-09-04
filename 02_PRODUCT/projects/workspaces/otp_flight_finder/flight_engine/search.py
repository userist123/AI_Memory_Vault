# -*- coding: utf-8 -*-
"""
search.py
---------
Simulated flight search engine for OTP (Henri Coanda International Airport).

Results are deterministically generated from a seed derived from
(destination, date_out) so the same query always returns identical deals.
"""

from __future__ import annotations

import random

from flight_engine.deep_links import build_deep_link
from flight_engine.destinations import DESTINATIONS

# ---------------------------------------------------------------------------
# Internal helpers
# ---------------------------------------------------------------------------

_DEST_MAP: dict[str, dict] = {d["iata"]: d for d in DESTINATIONS}

# Plausible price ranges per airline (min, max) in EUR
_PRICE_RANGES: dict[str, tuple[float, float]] = {
    "Ryanair": (19.0, 149.0),
    "Wizz Air": (22.0, 159.0),
    "Blue Air": (35.0, 199.0),
    "TAROM": (69.0, 299.0),
}


def _seed_from_query(destination: str, date_out: str) -> int:
    """Return a deterministic integer seed from destination + date."""
    combined = f"{destination.upper()}:{date_out}"
    return int.from_bytes(combined.encode("utf-8"), byteorder="big") % (2**31)


def _generate_deals_for_airline(
    rng: random.Random,
    airline: str,
    destination: str,
    date_out: str,
    date_in: str | None,
) -> list[dict]:
    """Generate 1-4 simulated deals for a single airline."""
    price_min, price_max = _PRICE_RANGES.get(airline, (29.0, 299.0))
    num_deals = rng.randint(1, 4)
    deals: list[dict] = []

    for _ in range(num_deals):
        price = round(rng.uniform(price_min, price_max), 2)
        is_return = date_in is not None
        link = build_deep_link(
            airline=airline,
            origin_iata="OTP",
            destination_iata=destination,
            depart_date=date_out,
            return_date=date_in,
        )
        deals.append(
            {
                "airline": airline,
                "origin": "OTP",
                "destination": destination.upper(),
                "date_out": date_out,
                "date_in": date_in,
                "price_eur": price,
                "currency": "EUR",
                "deep_link": link,
                "is_return": is_return,
                "badge": f"{airline} \u00b7 OTP\u2192{destination.upper()}",
            }
        )
    return deals


# ---------------------------------------------------------------------------
# Public API
# ---------------------------------------------------------------------------


def search_flights(
    origin: str,
    destination: str,
    date_out: str,
    date_in: str | None = None,
    adults: int = 1,
) -> list[dict]:
    """Return a list of simulated flight deals from OTP to *destination*.

    Parameters
    ----------
    origin:
        Ignored / overridden to 'OTP' internally.
    destination:
        IATA code of the destination airport.
    date_out:
        Departure date in YYYY-MM-DD format.
    date_in:
        Optional return date in YYYY-MM-DD format.
    adults:
        Number of adult passengers (stored in query context but does not
        affect the simulated price generation in this stub).

    Returns
    -------
    list[dict]
        Possibly-empty list of deal dicts.  Empty when the destination is
        not found in the DESTINATIONS registry.
    """
    dest_upper = destination.upper()
    dest_info = _DEST_MAP.get(dest_upper)
    if dest_info is None:
        return []

    seed = _seed_from_query(dest_upper, date_out)
    rng = random.Random(seed)

    results: list[dict] = []
    for airline in dest_info["airlines"]:
        results.extend(
            _generate_deals_for_airline(rng, airline, dest_upper, date_out, date_in)
        )

    # Sort cheapest first for a nice default order
    results.sort(key=lambda d: d["price_eur"])
    return results