# -*- coding: utf-8 -*-
"""
destinations.py
---------------
Static registry of destinations served from OTP (Henri Coanda International Airport).
Each entry maps an IATA code to its city/country metadata and the airlines
that operate OTP->destination routes.

Airlines pool: Ryanair, Wizz Air, Blue Air, TAROM
"""

DESTINATIONS: list[dict] = [
    {
        "iata": "STN",
        "city": "Londra (Stansted)",
        "country": "Regatul Unit",
        "airlines": ["Ryanair", "Wizz Air"],
    },
    {
        "iata": "LTN",
        "city": "Londra (Luton)",
        "country": "Regatul Unit",
        "airlines": ["Wizz Air"],
    },
    {
        "iata": "BCN",
        "city": "Barcelona",
        "country": "Spania",
        "airlines": ["Ryanair", "Wizz Air", "Blue Air"],
    },
    {
        "iata": "MXP",
        "city": "Milano (Malpensa)",
        "country": "Italia",
        "airlines": ["Wizz Air", "Blue Air"],
    },
    {
        "iata": "CIA",
        "city": "Roma (Ciampino)",
        "country": "Italia",
        "airlines": ["Ryanair", "Wizz Air"],
    },
    {
        "iata": "BRU",
        "city": "Bruxelles",
        "country": "Belgia",
        "airlines": ["Ryanair", "Blue Air"],
    },
    {
        "iata": "MAD",
        "city": "Madrid",
        "country": "Spania",
        "airlines": ["Ryanair", "TAROM"],
    },
    {
        "iata": "VIE",
        "city": "Viena",
        "country": "Austria",
        "airlines": ["Wizz Air", "Blue Air", "TAROM"],
    },
    {
        "iata": "PRG",
        "city": "Praga",
        "country": "Cehia",
        "airlines": ["Wizz Air", "Ryanair"],
    },
    {
        "iata": "WAW",
        "city": "Varsovia",
        "country": "Polonia",
        "airlines": ["Wizz Air", "TAROM"],
    },
    {
        "iata": "BUD",
        "city": "Budapesta",
        "country": "Ungaria",
        "airlines": ["Wizz Air", "Ryanair"],
    },
    {
        "iata": "ATH",
        "city": "Atena",
        "country": "Grecia",
        "airlines": ["Blue Air", "TAROM"],
    },
    {
        "iata": "LIS",
        "city": "Lisabona",
        "country": "Portugalia",
        "airlines": ["Ryanair", "Wizz Air"],
    },
    {
        "iata": "DUB",
        "city": "Dublin",
        "country": "Irlanda",
        "airlines": ["Ryanair"],
    },
    {
        "iata": "ORY",
        "city": "Paris (Orly)",
        "country": "Franta",
        "airlines": ["Wizz Air", "Blue Air"],
    },
    {
        "iata": "MRS",
        "city": "Marsilia",
        "country": "Franta",
        "airlines": ["Ryanair"],
    },
    {
        "iata": "NAP",
        "city": "Napoli",
        "country": "Italia",
        "airlines": ["Wizz Air", "Ryanair"],
    },
    {
        "iata": "PMO",
        "city": "Palermo",
        "country": "Italia",
        "airlines": ["Ryanair"],
    },
    {
        "iata": "CTA",
        "city": "Catania",
        "country": "Italia",
        "airlines": ["Ryanair", "Wizz Air"],
    },
    {
        "iata": "CFU",
        "city": "Corfu",
        "country": "Grecia",
        "airlines": ["Blue Air", "Wizz Air"],
    },
    {
        "iata": "RHO",
        "city": "Rodos",
        "country": "Grecia",
        "airlines": ["Blue Air", "Ryanair"],
    },
    {
        "iata": "SKG",
        "city": "Salonic",
        "country": "Grecia",
        "airlines": ["Blue Air", "Wizz Air"],
    },
    {
        "iata": "HER",
        "city": "Heraklion (Creta)",
        "country": "Grecia",
        "airlines": ["Blue Air", "Ryanair"],
    },
    {
        "iata": "AGP",
        "city": "Malaga",
        "country": "Spania",
        "airlines": ["Ryanair", "Wizz Air"],
    },
    {
        "iata": "ALC",
        "city": "Alicante",
        "country": "Spania",
        "airlines": ["Ryanair", "Wizz Air"],
    },
    {
        "iata": "AMS",
        "city": "Amsterdam",
        "country": "Olanda",
        "airlines": ["Blue Air", "TAROM"],
    },
    {
        "iata": "FCO",
        "city": "Roma (Fiumicino)",
        "country": "Italia",
        "airlines": ["TAROM", "Blue Air"],
    },
    {
        "iata": "CDG",
        "city": "Paris (Charles de Gaulle)",
        "country": "Franta",
        "airlines": ["TAROM"],
    },
    {
        "iata": "FRA",
        "city": "Frankfurt",
        "country": "Germania",
        "airlines": ["TAROM", "Blue Air"],
    },
    {
        "iata": "MUC",
        "city": "Munchen",
        "country": "Germania",
        "airlines": ["Wizz Air", "Blue Air"],
    },
]