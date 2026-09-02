# -*- coding: utf-8 -*-
"""
flight_server.py
----------------
FastAPI application for the OTP Flight Finder.

All flight searches are enforced to originate from OTP
(Henri Coanda International Airport, Bucharest).
Baneasa (BBU) is never referenced in any route or generated URL.
"""

from __future__ import annotations

from pathlib import Path

import uvicorn
from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import FileResponse, JSONResponse
from fastapi.staticfiles import StaticFiles
from pydantic import BaseModel, Field

from flight_engine.destinations import DESTINATIONS
from flight_engine.search import search_flights

# ---------------------------------------------------------------------------
# App setup
# ---------------------------------------------------------------------------

app = FastAPI(
    title="OTP Flight Finder",
    description="Find cheap flights from Henri Coanda (OTP) international airport.",
    version="1.0.0",
)

app.add_middleware(
    CORSMiddleware,
    allow_origins=[
        "http://localhost",
        "http://localhost:8000",
        "http://127.0.0.1",
        "http://127.0.0.1:8000",
    ],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

_STATIC_DIR = Path(__file__).parent / "static"

# Mount the static folder (CSS, JS, images, etc.)
if _STATIC_DIR.exists():
    app.mount("/static", StaticFiles(directory=str(_STATIC_DIR)), name="static")

# ---------------------------------------------------------------------------
# Pydantic models
# ---------------------------------------------------------------------------


class SearchRequest(BaseModel):
    """Body schema for POST /api/search."""

    origin: str = Field(default="OTP", description="Always overridden to OTP.")
    destination: str = Field(..., description="IATA code of the destination airport.")
    dateOut: str = Field(..., description="Departure date (YYYY-MM-DD).")
    dateIn: str | None = Field(default=None, description="Return date (YYYY-MM-DD).")
    adults: int = Field(default=1, ge=1, le=9, description="Number of adult passengers.")


# ---------------------------------------------------------------------------
# Routes
# ---------------------------------------------------------------------------


@app.get("/", summary="Serve the frontend SPA")
async def serve_index() -> FileResponse:
    """Return the main HTML page from the static/ directory."""
    index_path = _STATIC_DIR / "index.html"
    if index_path.exists():
        return FileResponse(str(index_path), media_type="text/html")
    # Graceful fallback if static folder is not yet built
    return FileResponse(str(index_path))


@app.get("/api/destinations", summary="List all destinations served from OTP")
async def get_destinations() -> JSONResponse:
    """Return the complete list of OTP destinations with airline info."""
    return JSONResponse(content=DESTINATIONS)


@app.post("/api/search", summary="Search for flight deals from OTP")
async def post_search(request: SearchRequest) -> JSONResponse:
    """Search for simulated flight deals.

    The *origin* field in the request body is always overridden to 'OTP'
    regardless of what the client sends, enforcing OTP-only departures.
    """
    # Enforce OTP — ignore whatever origin the client supplied
    enforced_origin = "OTP"

    results = search_flights(
        origin=enforced_origin,
        destination=request.destination,
        date_out=request.dateOut,
        date_in=request.dateIn,
        adults=request.adults,
    )

    return JSONResponse(
        content={
            "results": results,
            "count": len(results),
            "query": {
                "origin": enforced_origin,
                "destination": request.destination.upper(),
                "dateOut": request.dateOut,
                "dateIn": request.dateIn,
                "adults": request.adults,
            },
        }
    )


# ---------------------------------------------------------------------------
# Entry point
# ---------------------------------------------------------------------------

if __name__ == "__main__":
    uvicorn.run(app, host="127.0.0.1", port=8000)