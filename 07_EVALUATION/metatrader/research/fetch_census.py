"""
fetch_census.py — Extrae recensământul complet al instrumentelor MT5 și proprietățile lor.

Invariante respectate:
- Zero date de autentificare stocate (fără cont, parolă sau nume de server).
- Niciun apel de tranzacționare (fără order_send / order_check).
- Date pasive obținute strict prin symbols_get, symbol_info, copy_rates_from_pos.
"""

import csv
import json
import os
import sys
from datetime import datetime, timezone
import MetaTrader5 as mt5

MT5_PATH = r"C:\Program Files\MetaTrader 5\terminal64.exe"
OUTPUT_DIR = os.path.join("07_EVALUATION", "metatrader", "research", "tables")
CENSUS_CSV = os.path.join(OUTPUT_DIR, "table_1_census.csv")
CENSUS_JSON = os.path.join(OUTPUT_DIR, "table_1_census.json")

FX_MAJORS = {"EURUSD", "GBPUSD", "USDJPY", "USDCHF", "USDCAD", "AUDUSD", "NZDUSD"}


def classify_symbol(name: str, path: str) -> str:
    path_lower = path.lower()
    if "forex" in path_lower or "fx" in path_lower:
        if name in FX_MAJORS:
            return "FX_MAJOR"
        return "FX_CROSS"
    elif "metal" in path_lower:
        return "METALS"
    elif "crypto" in path_lower:
        return "CRYPTO"
    elif "stock" in path_lower or "share" in path_lower:
        return "EQUITIES"
    elif "index" in path_lower or "indices" in path_lower:
        return "INDICES"
    elif "commodity" in path_lower or "oil" in path_lower:
        return "COMMODITIES"
    return "OTHER"


def main():
    os.makedirs(OUTPUT_DIR, exist_ok=True)
    print(f"Connecting to MT5 terminal at {MT5_PATH}...", flush=True)
    if not mt5.initialize(path=MT5_PATH):
        err = mt5.last_error()
        print(f"FAILED to initialize MT5: {err}", file=sys.stderr, flush=True)
        sys.exit(1)

    term_info = mt5.terminal_info()
    acc_info = mt5.account_info()

    # Provenance data — strictly scrubbed of accounts, servers, and private info
    broker_company = acc_info.company if acc_info else "Unknown"
    account_currency = acc_info.currency if acc_info else "USD"
    terminal_build = term_info.build if term_info else 0
    extraction_time_utc = datetime.now(timezone.utc).isoformat()

    print(f"Connected to broker: {broker_company}, build: {terminal_build}", flush=True)

    all_symbols = mt5.symbols_get()
    if not all_symbols:
        print("No symbols retrieved from MT5!", file=sys.stderr, flush=True)
        mt5.shutdown()
        sys.exit(1)

    print(f"Found {len(all_symbols)} symbols. Gathering details...", flush=True)
    records = []

    for s in all_symbols:
        name = s.name
        mt5.symbol_select(name, True)
        info = mt5.symbol_info(name)
        if info is None:
            continue

        category = classify_symbol(name, s.path)

        # D1 Depth
        rates_d1 = mt5.copy_rates_from_pos(name, mt5.TIMEFRAME_D1, 0, 10000)
        n_d1 = len(rates_d1) if rates_d1 is not None else 0
        d1_start = (
            datetime.fromtimestamp(rates_d1[0]["time"], tz=timezone.utc).strftime("%Y-%m-%d")
            if n_d1 > 0
            else "N/A"
        )
        d1_end = (
            datetime.fromtimestamp(rates_d1[-1]["time"], tz=timezone.utc).strftime("%Y-%m-%d")
            if n_d1 > 0
            else "N/A"
        )

        # H1 Depth
        rates_h1 = mt5.copy_rates_from_pos(name, mt5.TIMEFRAME_H1, 0, 10000)
        n_h1 = len(rates_h1) if rates_h1 is not None else 0
        h1_start = (
            datetime.fromtimestamp(rates_h1[0]["time"], tz=timezone.utc).strftime("%Y-%m-%d %H:%M")
            if n_h1 > 0
            else "N/A"
        )
        h1_end = (
            datetime.fromtimestamp(rates_h1[-1]["time"], tz=timezone.utc).strftime("%Y-%m-%d %H:%M")
            if n_h1 > 0
            else "N/A"
        )

        rec = {
            "symbol": name,
            "path": s.path,
            "category": category,
            "trade_mode": info.trade_mode,
            "digits": info.digits,
            "point": info.point,
            "contract_size": info.trade_contract_size,
            "tick_size": info.trade_tick_size,
            "tick_value": info.trade_tick_value,
            "spread_points": info.spread,
            "swap_long": info.swap_long,
            "swap_short": info.swap_short,
            "swap_mode": info.swap_mode,
            "swap_rollover3days": info.swap_rollover3days,
            "d1_bars": n_d1,
            "d1_start": d1_start,
            "d1_end": d1_end,
            "h1_bars": n_h1,
            "h1_start": h1_start,
            "h1_end": h1_end,
        }
        records.append(rec)
        print(f"  {name:8s} | {category:10s} | D1: {n_d1:5d} ({d1_start} -> {d1_end})", flush=True)

    mt5.shutdown()

    # Save JSON
    metadata = {
        "broker_company": broker_company,
        "account_currency": account_currency,
        "terminal_build": terminal_build,
        "extraction_time_utc": extraction_time_utc,
        "total_symbols": len(records),
        "symbols": records,
    }
    with open(CENSUS_JSON, "w", encoding="utf-8") as f:
        json.dump(metadata, f, indent=2)

    # Save CSV
    fieldnames = [
        "symbol",
        "category",
        "path",
        "trade_mode",
        "digits",
        "point",
        "contract_size",
        "tick_size",
        "tick_value",
        "spread_points",
        "swap_long",
        "swap_short",
        "swap_mode",
        "swap_rollover3days",
        "d1_bars",
        "d1_start",
        "d1_end",
        "h1_bars",
        "h1_start",
        "h1_end",
    ]
    with open(CENSUS_CSV, "w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=fieldnames)
        writer.writeheader()
        for r in records:
            writer.writerow(r)

    print(f"Successfully generated census table: {CENSUS_CSV} ({len(records)} instruments)", flush=True)


if __name__ == "__main__":
    main()
