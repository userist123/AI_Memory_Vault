"""Offline analysis script for B2 Decimation Test.

Evaluates raw responses in fixtures/decimation_test_20markets_all.json vs
fixtures/decimation_test_20markets_dense.json.
Runs 100% offline without network.
"""
from __future__ import annotations

import csv
import json
import pathlib
from datetime import datetime, timezone


def analyze_decimation():
    base_dir = pathlib.Path(__file__).resolve().parent
    fixtures_dir = base_dir / "fixtures"
    tables_dir = base_dir / "tables"
    tables_dir.mkdir(parents=True, exist_ok=True)

    file_all = fixtures_dir / "decimation_test_20markets_all.json"
    file_dense = fixtures_dir / "decimation_test_20markets_dense.json"
    file_prov = fixtures_dir / "decimation_test_20markets.provenance.json"

    if not file_all.exists() or not file_dense.exists():
        raise FileNotFoundError("Decimation raw fixtures missing. Run fetch_decimation_test.py first.")

    with open(file_all, "r", encoding="utf-8") as f:
        data_all = json.load(f)["history_by_token"]
    with open(file_dense, "r", encoding="utf-8") as f:
        data_dense = json.load(f)["history_by_token"]
    with open(file_prov, "r", encoding="utf-8") as f:
        prov = json.load(f)

    tokens = list(data_all.keys())
    rows = []
    total_pts_all = 0
    total_pts_dense = 0
    ratios = []
    lead_time_lost_seconds = []
    sweep_points_count = 0

    for token in tokens:
        pts_all = data_all.get(token, [])
        pts_dense = data_dense.get(token, [])

        cnt_all = len(pts_all)
        cnt_dense = len(pts_dense)
        total_pts_all += cnt_all
        total_pts_dense += cnt_dense

        ratio = (cnt_dense / cnt_all) if cnt_all > 0 else 1.0
        ratios.append(ratio)

        t_first_all = pts_all[0]["t"] if pts_all else None
        t_first_dense = pts_dense[0]["t"] if pts_dense else None

        t_diff = 0
        if t_first_all is not None and t_first_dense is not None:
            t_diff = max(0, t_first_all - t_first_dense)
            lead_time_lost_seconds.append(t_diff)

        # Count post-close settlement sweep points (p in {0.0005, 0.9995})
        sweeps = [p for p in pts_dense if p["p"] in (0.0005, 0.9995, 0.0001, 0.9999)]
        sweep_points_count += len(sweeps)

        m_id = prov.get("queries", {}).get(token, {}).get("market_id", "unknown")

        rows.append({
            "token_id": token[:16] + "...",
            "market_id": m_id,
            "points_all": cnt_all,
            "points_dense": cnt_dense,
            "decimation_ratio": round(ratio, 2),
            "first_dense_iso": datetime.fromtimestamp(t_first_dense, tz=timezone.utc).isoformat() if t_first_dense else "N/A",
            "first_all_iso": datetime.fromtimestamp(t_first_all, tz=timezone.utc).isoformat() if t_first_all else "N/A",
            "start_delay_minutes": round(t_diff / 60.0, 1),
            "settlement_sweeps": len(sweeps),
        })

    # Save summary CSV
    csv_path = tables_dir / "decimation_summary.csv"
    with open(csv_path, "w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=list(rows[0].keys()))
        writer.writeheader()
        writer.writerows(rows)

    avg_ratio = (total_pts_dense / total_pts_all) if total_pts_all > 0 else 0.0
    avg_lost_mins = (sum(lead_time_lost_seconds) / len(lead_time_lost_seconds) / 60.0) if lead_time_lost_seconds else 0.0

    summary = {
        "tokens_evaluated": len(tokens),
        "total_points_interval_all": total_pts_all,
        "total_points_dense": total_pts_dense,
        "aggregate_decimation_ratio": round(avg_ratio, 2),
        "mean_entry_clipping_minutes": round(avg_lost_mins, 1),
        "total_settlement_sweep_points": sweep_points_count,
        "decimation_confirmed": bool(avg_ratio >= 3.0),
    }

    summary_json = tables_dir / "decimation_summary.json"
    with open(summary_json, "w", encoding="utf-8") as f:
        json.dump(summary, f, indent=2)

    print("\n=== B2 DECIMATION TEST OFFLINE RESULTS ===")
    print(f"Tokens evaluated: {summary['tokens_evaluated']}")
    print(f"Total points (interval=all): {summary['total_points_interval_all']}")
    print(f"Total points (dense tape):   {summary['total_points_dense']}")
    print(f"Aggregate Decimation Ratio:  {summary['aggregate_decimation_ratio']}x")
    print(f"Mean Entry Clipping:         {summary['mean_entry_clipping_minutes']} minutes")
    print(f"Total Settlement Sweeps:     {summary['total_settlement_sweep_points']}")
    print(f"Decimation Confirmed:        {summary['decimation_confirmed']}")
    print(f"CSV exported to: {csv_path}")

    return summary


if __name__ == "__main__":
    analyze_decimation()
