# -*- coding: utf-8 -*-
"""
deep_links.py
-------------
Builds airline booking deep-links originating exclusively from OTP
(Henri Coanda International Airport, Bucharest).

IMPORTANT: This module NEVER references BBU (Baneasa) or any other
Bucharest airport. All links are hard-coded to depart from OTP.
"""

from __future__ import annotations


def build_deep_link(
    airline: str,
    origin_iata: str,
    destination_iata: str,
    depart_date: str,
    return_date: str | None = None,
) -> str:
    """Build a booking deep-link for *airline* from OTP to *destination_iata*.

    Parameters
    ----------
    airline:
        One of 'Ryanair', 'Wizz Air', 'Blue Air', 'TAROM'.
        Any unrecognised value falls back to a Google Flights search URL.
    origin_iata:
        MUST be 'OTP'.  An AssertionError is raised otherwise to prevent
        accidental links from Baneasa or other airports.
    destination_iata:
        IATA code of the destination airport.
    depart_date:
        ISO-8601 date string (YYYY-MM-DD).
    return_date:
        Optional ISO-8601 date string for the return leg.

    Returns
    -------
    str
        A fully-qualified HTTPS booking URL.
    """
    assert origin_iata == "OTP", (
        f"Origin must be OTP (Henri Coanda), got '{origin_iata}'. "
        "Links from BBU / Baneasa are not supported."
    )

    origin = origin_iata  # always 'OTP'
    dest = destination_iata.upper()
    is_return = return_date is not None

    if airline == "Ryanair":
        base = (
            f"https://www.ryanair.com/ro/ro/trip/flights/select"
            f"?origin={origin}&originIata={origin}"
            f"&destination={dest}&destinationIata={dest}"
            f"&dateOut={depart_date}"
        )
        if is_return:
            base += (
                f"&dateIn={return_date}"
                f"&isReturn=true"
                f"&tpStartDate={depart_date}"
                f"&tpEndDate={return_date}"
                f"&adults=1"
            )
        else:
            base += "&isReturn=false&adults=1"
        return base

    if airline == "Wizz Air":
        ret = return_date if return_date else "null"
        return (
            f"https://wizzair.com/ro-ro#/booking/select-flight"
            f"/{origin}/{dest}/{depart_date}/{ret}/1/0/0/null"
        )

    if airline == "Blue Air":
        url = (
            f"https://www.blueairweb.com/ro/ro/book"
            f"?from={origin}&to={dest}&departure={depart_date}"
        )
        return url

    if airline == "TAROM":
        return (
            f"https://www.tarom.ro/rezervare"
            f"?from={origin}&to={dest}&date={depart_date}"
        )

    # Fallback: Google Flights
    return (
        f"https://www.google.com/travel/flights"
        f"?q=Flights+from+{origin}+to+{dest}+on+{depart_date}"
    )