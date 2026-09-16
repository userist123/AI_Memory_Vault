"""
fetch_corpus.py — Descarcă barele D1 și H1 pentru toate cele 37 de instrumente MT5
și generează MANIFEST.json cu hash-uri SHA-256 criptografice.

Invariante:
- Niciun apel de tranzacționare.
- Zero date de autentificare stocate.
- SHA-256 pentru fiecare fișier salvat.
"""

import csv
import hashlib
import json
import os
import sys
from datetime import datetime, timezone
import MetaTrader5 as mt5

MT5_PATH = r"C:\Program Files\MetaTrader 5\terminal64.exe"
BASE_DIR = os.path.join("07_EVALUATION", "metatrader", "research")
CORPUS_DIR = os.path.join(BASE_DIR, "corpus")
D1_DIR = os.path.join(CORPUS_DIR, "D1")
H1_DIR = os.path.join(CORPUS_DIR, "H1")
MANIFEST_PATH = os.path.join(CORPUS_DIR, "MANIFEST.json")


def sha256_file(filepath: str) -> str:
    h = hashlib.sha256()
    with open(filepath, "rb") as f:
        while chunk := f.read(65536):
            h.update(chunk)
    return h.hexdigest()


def save_rates_csv(rates, filepath: str):
    fieldnames = ["time", "open", "high", "low", "close", "tick_volume", "spread", "real_volume"]
    with open(filepath, "w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=fieldnames)
        writer.writeheader()
        for r in rates:
            # Format time as ISO UTC timestamp string
            ts_str = datetime.fromtimestamp(r["time"], tz=timezone.utc).strftime("%Y-%m-%d %H:%M:%S")
            writer.writerow({
                "time": ts_str,
                "open": r["open"],
                "high": r["high"],
                "low": r["low"],
                "close": r["close"],
                "tick_volume": r["tick_volume"],
                "spread": r["spread"],
                "real_volume": r["real_volume"]
            })


def main():
    os.makedirs(D1_DIR, exist_ok=True)
    os.makedirs(H1_DIR, exist_ok=True)

    print(f"Connecting to MT5 terminal at {MT5_PATH}...", flush=True)
    if not mt5.initialize(path=MT5_PATH):
        err = mt5.last_error()
        print(f"FAILED to initialize MT5: {err}", file=sys.stderr, flush=True)
        sys.exit(1)

    term_info = mt5.terminal_info()
    acc_info = mt5.account_info()
    broker_company = acc_info.company if acc_info else "RoboForex Ltd"
    terminal_build = term_info.build if term_info else 0
    extraction_time_utc = datetime.now(timezone.utc).isoformat()

    symbols = mt5.symbols_get()
    if not symbols:
        print("No symbols found!", file=sys.stderr, flush=True)
        mt5.shutdown()
        sys.exit(1)

    manifest_entries = []

    print(f"Extracting corpus for {len(symbols)} symbols...", flush=True)
    for s in symbols:
        sym = s.name
        mt5.symbol_select(sym, True)

        # 1. D1 Rates (up to 10,000 bars)
        rates_d1 = mt5.copy_rates_from_pos(sym, mt5.TIMEFRAME_D1, 0, 10000)
        if rates_d1 is not None and len(rates_d1) > 0:
            d1_path = os.path.join(D1_DIR, f"{sym}_D1.csv")
            save_rates_csv(rates_d1, d1_path)
            h_d1 = sha256_file(d1_path)
            sz_d1 = os.path.getsize(d1_path)
            t0_d1 = datetime.fromtimestamp(rates_d1[0]["time"], tz=timezone.utc).strftime("%Y-%m-%d")
            t1_d1 = datetime.fromtimestamp(rates_d1[-1]["time"], tz=timezone.utc).strftime("%Y-%m-%d")
            manifest_entries.append({
                "symbol": sym,
                "timeframe": "D1",
                "bars": len(rates_d1),
                "start": t0_d1,
                "end": t1_d1,
                "rel_path": os.path.relpath(d1_path, CORPUS_DIR).replace("\\", "/"),
                "size_bytes": sz_d1,
                "sha256": h_d1
            })
            print(f"  {sym:8s} D1: {len(rates_d1):5d} bars [{t0_d1} -> {t1_d1}] SHA: {h_d1[:10]}...", flush=True)

        # 2. H1 Rates (up to 10,000 bars)
        rates_h1 = mt5.copy_rates_from_pos(sym, mt5.TIMEFRAME_H1, 0, 10000)
        if rates_h1 is not None and len(rates_h1) > 0:
            h1_path = os.path.join(H1_DIR, f"{sym}_H1.csv")
            save_rates_csv(rates_h1, h1_path)
            h_h1 = sha256_file(h1_path)
            sz_h1 = os.path.getsize(h1_path)
            t0_h1 = datetime.fromtimestamp(rates_h1[0]["time"], tz=timezone.utc).strftime("%Y-%m-%d %H:%M")
            t1_h1 = datetime.fromtimestamp(rates_h1[-1]["time"], tz=timezone.utc).strftime("%Y-%m-%d %H:%M")
            manifest_entries.append({
                "symbol": sym,
                "timeframe": "H1",
                "bars": len(rates_h1),
                "start": t0_h1,
                "end": t1_h1,
                "rel_path": os.path.relpath(h1_path, CORPUS_DIR).replace("\\", "/"),
                "size_bytes": sz_h1,
                "sha256": h_h1
            })

    mt5.shutdown()

    # Save MANIFEST.json
    manifest = {
        "provenance": {
            "broker_company": broker_company,
            "terminal_build": terminal_build,
            "extraction_time_utc": extraction_time_utc,
            "total_files": len(manifest_entries)
        },
        "files": manifest_entries
    }
    with open(MANIFEST_PATH, "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=2)

    print(f"MANIFEST.json written successfully: {len(manifest_entries)} files registered.", flush=True)


if __name__ == "__main__":
    main()
