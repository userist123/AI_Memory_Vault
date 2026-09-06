"""CLI: print the latest N rows from the SQLite trade journal."""
from __future__ import annotations

import json
import sys

from core.journal import Journal


def main(n: int = 20) -> int:
    j = Journal()
    rows = j.query_recent_days(n)[:n]
    if not rows:
        print("(journal empty)")
        return 0
    for r in rows:
        print(json.dumps({k: r[k] for k in (
            "ticket", "opened_at", "closed_at", "strategy_id", "side",
            "entry_price", "exit_price", "sl_price", "tp_plan", "tp_hits",
            "initial_volume", "closed_volume", "exit_reason",
            "gross_pnl", "r_multiple", "ml_prob_win", "spread_entry",
        )}, default=str))
    return 0


if __name__ == "__main__":
    sys.exit(main(int(sys.argv[1]) if len(sys.argv) > 1 else 20))
