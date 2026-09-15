"""
analyze_exploratory_hypotheses.py — Explorare Declarată (Generare de Ipoteze)

Investighează anomalii structurale potențiale fără a pretinde validare confirmativă:
1. Sezonalitate orară intraday (24 de ore pe EURUSD, USDJPY, XAUUSD).
2. Efecte de sesiune (London open vs NY open).
3. Condiționare pe regimuri de volatilitate (High Vol vs Low Vol).
4. Contorizare globală a testelor și rata fals-pozitivă așteptată.

Generează:
- 07_EVALUATION/metatrader/research/tables/table_11_exploratory_hypotheses.csv
"""

import csv
from datetime import datetime
import os
import sys
import numpy as np
from scipy import stats

BASE_DIR = os.path.join("07_EVALUATION", "metatrader", "research")
sys.path.insert(0, os.path.abspath("."))

TABLES_DIR = os.path.join(BASE_DIR, "tables")
CORPUS_H1 = os.path.join(BASE_DIR, "corpus", "H1")
CORPUS_D1 = os.path.join(BASE_DIR, "corpus", "D1")


def load_h1(symbol: str):
    csv_path = os.path.join(CORPUS_H1, f"{symbol}_H1.csv")
    timestamps, opens, highs, lows, closes = [], [], [], [], []
    with open(csv_path, "r", encoding="utf-8") as f:
        reader = csv.DictReader(f)
        for r in reader:
            dt = datetime.strptime(r["time"], "%Y-%m-%d %H:%M:%S")
            timestamps.append(dt)
            opens.append(float(r["open"]))
            highs.append(float(r["high"]))
            lows.append(float(r["low"]))
            closes.append(float(r["close"]))
    return (
        timestamps,
        np.array(opens, dtype=np.float64),
        np.array(highs, dtype=np.float64),
        np.array(lows, dtype=np.float64),
        np.array(closes, dtype=np.float64),
    )


def main():
    import argparse
    parser = argparse.ArgumentParser()
    parser.add_argument("--out-dir", default=TABLES_DIR, help="Output directory for tables")
    args = parser.parse_args()
    out_dir = args.out_dir

    records = []
    test_counter = 0

    # 1. Sezonalitate orară pe EURUSD, USDJPY, XAUUSD
    target_symbols = ["EURUSD", "USDJPY", "XAUUSD"]

    for sym in target_symbols:
        ts, opens, highs, lows, closes = load_h1(sym)
        # randament orar
        rets = (closes - opens) / opens
        hours = np.array([t.hour for t in ts])

        # Corecție Bonferroni pe cele 24 de ore
        bonf_alpha = 0.05 / 24.0

        for h in range(24):
            test_counter += 1
            mask_h = (hours == h)
            rets_h = rets[mask_h]
            n_h = len(rets_h)
            if n_h > 30:
                mean_h = float(np.mean(rets_h))
                std_h = float(np.std(rets_h, ddof=1))
                t_stat = mean_h / (std_h / np.sqrt(n_h))
                p_val = float(2.0 * (1.0 - stats.norm.cdf(abs(t_stat))))
            else:
                mean_h, std_h, t_stat, p_val = 0.0, 0.0, 0.0, 1.0

            survives_bonf = bool(p_val < bonf_alpha)

            records.append({
                "test_id": f"EXP_H_{sym}_H{h:02d}",
                "category": "Intraday_Hourly_Seasonality",
                "symbol": sym,
                "description": f"Randament mediu la ora {h:02d}:00 UTC",
                "sample_size": n_h,
                "metric_value": round(mean_h * 10000.0, 2),  # în puncte de bază (bps)
                "test_statistic": round(t_stat, 4),
                "raw_p_value": round(p_val, 5),
                "bonferroni_adjusted_alpha": round(bonf_alpha, 5),
                "significant_after_correction": survives_bonf,
                "epistemic_status": "HYPOTHESIS_ONLY",
            })

    # 2. Efect de sesiune: London Open (08:00-10:00 UTC) vs NY Open (13:00-15:00 UTC)
    for sym in target_symbols:
        ts, opens, highs, lows, closes = load_h1(sym)
        rets = np.abs((closes - opens) / opens)  # volatilitate absolută
        hours = np.array([t.hour for t in ts])

        london_mask = (hours >= 8) & (hours < 10)
        ny_mask = (hours >= 13) & (hours < 15)

        london_vol = rets[london_mask]
        ny_vol = rets[ny_mask]

        test_counter += 1
        t_stat, p_val = stats.ttest_ind(london_vol, ny_vol, equal_var=False)

        records.append({
            "test_id": f"EXP_SES_{sym}_LDN_VS_NY",
            "category": "Session_Open_Volatility",
            "symbol": sym,
            "description": f"Diferenta volatilitate London Open vs NY Open",
            "sample_size": len(london_vol) + len(ny_vol),
            "metric_value": round((float(np.mean(london_vol)) - float(np.mean(ny_vol))) * 10000.0, 2),
            "test_statistic": round(float(t_stat), 4),
            "raw_p_value": round(float(p_val), 5),
            "bonferroni_adjusted_alpha": 0.05,
            "significant_after_correction": bool(p_val < 0.05),
            "epistemic_status": "HYPOTHESIS_ONLY",
        })

    # 3. Condiționare pe regim de volatilitate (ATR14 > mediană vs <= mediană)
    # Pe D1: S1_MOM_60 pe EURUSD, USDJPY, XAUUSD
    for sym in target_symbols:
        d1_path = os.path.join(CORPUS_D1, f"{sym}_D1.csv")
        opens_d1, highs_d1, lows_d1, closes_d1 = [], [], [], []
        with open(d1_path, "r", encoding="utf-8") as f:
            reader = csv.DictReader(f)
            for r in reader:
                opens_d1.append(float(r["open"]))
                highs_d1.append(float(r["high"]))
                lows_d1.append(float(r["low"]))
                closes_d1.append(float(r["close"]))

        c_arr = np.array(closes_d1)
        h_arr = np.array(highs_d1)
        l_arr = np.array(lows_d1)

        # Calcul ATR14
        tr = np.maximum(h_arr[1:] - l_arr[1:], np.maximum(np.abs(h_arr[1:] - c_arr[:-1]), np.abs(l_arr[1:] - c_arr[:-1])))
        atr14 = np.zeros(len(tr))
        atr14[13] = np.mean(tr[:14])
        for i in range(14, len(tr)):
            atr14[i] = (atr14[i - 1] * 13.0 + tr[i]) / 14.0

        med_atr = np.median(atr14[14:])
        # Momentum 60 returns aligned strictly by index t
        N = len(c_arr)
        t_indices = np.arange(61, N)
        rets_d1 = (c_arr[t_indices] - c_arr[t_indices - 1]) / c_arr[t_indices - 1]
        sigs_d1 = np.sign(c_arr[t_indices - 1] - c_arr[t_indices - 61])
        strat_ret = sigs_d1 * rets_d1

        atr_vals = atr14[t_indices - 1]
        regime_high = atr_vals > med_atr
        ret_hi = strat_ret[regime_high]
        ret_lo = strat_ret[~regime_high]

        test_counter += 1
        t_stat, p_val = stats.ttest_ind(ret_hi, ret_lo, equal_var=False)

        records.append({
            "test_id": f"EXP_REG_{sym}_MOM60_VOL",
            "category": "Volatility_Regime_Conditioning",
            "symbol": sym,
            "description": f"Performanta MOM60 in High Vol vs Low Vol",
            "sample_size": len(strat_ret),
            "metric_value": round((float(np.mean(ret_hi)) - float(np.mean(ret_lo))) * 10000.0, 2),
            "test_statistic": round(float(t_stat), 4),
            "raw_p_value": round(float(p_val), 5),
            "bonferroni_adjusted_alpha": 0.05,
            "significant_after_correction": bool(p_val < 0.05),
            "epistemic_status": "HYPOTHESIS_ONLY",
        })

    # Contorizare globală a programului de cercetare
    v1_tests = 246
    v2_owner_bot_tests = 7
    total_tests_cumulative = v1_tests + v2_owner_bot_tests + test_counter
    expected_false_positives = total_tests_cumulative * 0.05

    print(f"\nTotal teste exploratorii rulate in Partea E: {test_counter}")
    print(f"Total cumulativ teste pe repository: {total_tests_cumulative} (V1: {v1_tests}, Bot: {v2_owner_bot_tests}, Exp: {test_counter})")
    print(f"Numar asteptat de fals-pozitive la alpha=0.05: {expected_false_positives:.2f}")

    # Salvare CSV
    out_csv = os.path.join(out_dir, "table_11_exploratory_hypotheses.csv")
    fieldnames = list(records[0].keys())
    with open(out_csv, "w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=fieldnames)
        writer.writeheader()
        for r in records:
            writer.writerow(r)

    print(f"Salvat cu succes in {out_csv} ({len(records)} ipoteze documentate)")


if __name__ == "__main__":
    main()
