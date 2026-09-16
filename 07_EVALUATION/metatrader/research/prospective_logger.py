"""
prospective_logger.py — Testul Prospectiv (Logger Pasiv Read-Only cu Lanț SHA-256)

Contract de Integritate:
- Script STRICT de citire (Zero apeluri order_send / order_check / order_calc_margin).
- Monitorizează pasiv cotațiile și semnalele ipotetice pentru strategiile preînregistrate pe date viitoare.
- Jurnalizare tamper-evident: fiecare intrare conține hash-ul SHA-256 al intrării precedente.
- Orizont preînregistrat: 8 săptămâni (2026-09-15 -> 2026-11-10).

Utilizare:
  python prospective_logger.py --run-once     # Înregistrează o stare curentă
  python prospective_logger.py --verify       # Verifică integritatea lanțului de hash-uri
"""

import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
import sys
import numpy as np

try:
    import MetaTrader5 as mt5
except ImportError:
    mt5 = None

BASE_DIR = os.path.join("07_EVALUATION", "metatrader", "research")
LOG_FILE = os.path.join(BASE_DIR, "prospective_log.jsonl")

# Parametri de evaluare prospectivă preînregistrați
EVALUATION_START_UTC = "2026-09-15T00:00:00Z"
EVALUATION_END_UTC = "2026-11-10T23:59:59Z"
SUCCESS_CRITERION = "SR_net >= 0.50 cu p_bootstrap < 0.05 pe cele 8 saptamani prospective"


def compute_entry_hash(entry_dict: dict, prev_hash: str) -> str:
    """Calculează hash SHA-256 tamper-evident peste conținutul intrării și hash-ul precedent."""
    canonical_str = (
        f"{entry_dict['index']}|"
        f"{entry_dict['timestamp_utc']}|"
        f"{entry_dict['symbol']}|"
        f"{entry_dict['bid']:.5f}|"
        f"{entry_dict['ask']:.5f}|"
        f"{entry_dict['spread_pts']:.1f}|"
        f"{entry_dict['signals']}|"
        f"{prev_hash}"
    )
    return hashlib.sha256(canonical_str.encode("utf-8")).hexdigest()


def get_last_chain_state():
    """Citește ultima intrare din log pentru a prelua indexul și prev_hash-ul."""
    if not os.path.exists(LOG_FILE) or os.path.getsize(LOG_FILE) == 0:
        # Genesis block
        return 0, "0" * 64

    last_line = ""
    with open(LOG_FILE, "r", encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if line:
                last_line = line

    if not last_line:
        return 0, "0" * 64

    entry = json.loads(last_line)
    return entry["index"] + 1, entry["entry_hash"]


def verify_chain():
    """Verifică integritatea întregului lanț de hash-uri din fișierul de log."""
    if not os.path.exists(LOG_FILE):
        print(f"Fișierul de log nu există: {LOG_FILE}")
        return True

    prev_hash = "0" * 64
    total_entries = 0

    with open(LOG_FILE, "r", encoding="utf-8") as f:
        for line_no, line in enumerate(f, 1):
            line = line.strip()
            if not line:
                continue
            entry = json.loads(line)
            idx = entry["index"]
            expected_prev = entry.get("prev_hash")
            claimed_hash = entry.get("entry_hash")

            if expected_prev != prev_hash:
                print(f"EROARE DE INTEGRITATE la linia {line_no} (index {idx}): prev_hash nepotrivit!")
                print(f"  Asteptat: {prev_hash}")
                print(f"  Gasit:    {expected_prev}")
                return False

            recomputed_hash = compute_entry_hash(entry, prev_hash)
            if recomputed_hash != claimed_hash:
                print(f"EROARE DE INTEGRITATE la linia {line_no} (index {idx}): hash corupt!")
                print(f"  Claimed:    {claimed_hash}")
                print(f"  Recomputed: {recomputed_hash}")
                return False

            prev_hash = claimed_hash
            total_entries += 1

    print(f"LANT SHA-256 VALID: {total_entries} intrari verificate cu succes. Nicio modificare detectata.")
    return True


def log_prospective_snapshot(symbols=("XAUUSD", "EURUSD", "USDJPY")):
    if mt5 is None:
        print("MetaTrader5 nu este instalat.")
        return False

    if not mt5.initialize():
        print(f"Eroare initializare MT5: {mt5.last_error()}")
        return False

    cur_idx, prev_hash = get_last_chain_state()
    now_utc = datetime.now(timezone.utc).isoformat()

    logged_any = False
    for sym in symbols:
        mt5.symbol_select(sym, True)
        tick = mt5.symbol_info_tick(sym)
        sym_info = mt5.symbol_info(sym)

        if not tick or not sym_info:
            print(f"Avertisment: Imposibil de citit tick pentru {sym}")
            continue

        point = sym_info.point
        bid = float(tick.bid)
        ask = float(tick.ask)
        spread_pts = (ask - bid) / point

        # Preluare ultimele 60 de bare H1 pentru generare semnale pasive
        rates = mt5.copy_rates_from_pos(sym, mt5.TIMEFRAME_H1, 0, 60)
        signals = {}

        if rates is not None and len(rates) >= 40:
            c = rates["close"]
            h = rates["high"]
            l = rates["low"]
            o = rates["open"]

            # 1. Breakout 20
            signals["BRK_20"] = 1 if c[-1] > np.max(h[-21:-1]) else (-1 if c[-1] < np.min(l[-21:-1]) else 0)

            # 2. MACD fast=5, slow=35, sig=5
            def ema(s, span):
                alpha = 2.0 / (span + 1.0)
                res = np.empty(len(s))
                res[0] = s[0]
                for i in range(1, len(s)):
                    res[i] = alpha * s[i] + (1.0 - alpha) * res[i-1]
                return res

            m_line = ema(c, 5) - ema(c, 35)
            s_line = ema(m_line, 5)
            signals["MACD_MOM"] = 1 if m_line[-1] > s_line[-1] else -1

            # 3. Buy & Hold control
            signals["S0a_BH"] = 1
        else:
            signals["STATUS"] = "INSUFFICIENT_BARS"

        entry_data = {
            "index": cur_idx,
            "timestamp_utc": now_utc,
            "symbol": sym,
            "bid": round(bid, 5),
            "ask": round(ask, 5),
            "spread_pts": round(spread_pts, 1),
            "signals": signals,
            "prev_hash": prev_hash,
        }

        entry_hash = compute_entry_hash(entry_data, prev_hash)
        entry_data["entry_hash"] = entry_hash

        with open(LOG_FILE, "a", encoding="utf-8") as f:
            f.write(json.dumps(entry_data) + "\n")

        print(f"[BLOC #{cur_idx:04d}] {now_utc} | {sym} | Bid: {bid:.2f} Ask: {ask:.2f} | Signals: {signals} | Hash: {entry_hash[:12]}...")

        prev_hash = entry_hash
        cur_idx += 1
        logged_any = True

    mt5.shutdown()
    return logged_any


def main():
    parser = argparse.ArgumentParser(description="Prospective Read-Only Logger")
    parser.add_argument("--run-once", action="store_true", help="Log a single current prospective snapshot")
    parser.add_argument("--verify", action="store_true", help="Verify the SHA-256 hash chain")
    args = parser.parse_args()

    if args.verify:
        ok = verify_chain()
        sys.exit(0 if ok else 1)
    elif args.run_once:
        ok = log_prospective_snapshot()
        sys.exit(0 if ok else 1)
    else:
        # Default: run once and verify
        print(f"Rulare snapshot prospectiv pe piata live (read-only)...")
        log_prospective_snapshot()
        print("\nVerificare integritate lant hash-uri:")
        verify_chain()


if __name__ == "__main__":
    main()
