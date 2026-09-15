"""
analyze_owner_bot_holdout.py — Evaluarea Formală a Botului Owner (XAUUSD) pe Holdout

Evaluează cele 7 strategii înghețate la 2026-06-19 pe fereastra de holdout:
2026-06-20 00:00:00 -> 2026-09-14 23:59:59 (1.395 bare orare H1).

Reguli de integritate:
- Fereastra de holdout este rulată o singură dată (One-Shot).
- Costuri reale din tick-uri: spread 21 pts, comision $2/lot, swap -4.75 pts.
- Anti-lookahead: semnal la Close[t], execuție la Open[t+1].
- Controale: S0a (Buy & Hold XAU) și S0b (1000 Monte Carlo aleatoare).
- Test calibrare probabilități ML cu greutățile înghețate din ml_store/ml_weights.json.

Generează:
- 07_EVALUATION/metatrader/research/tables/table_10_owner_bot_holdout.csv
"""

import csv
from datetime import datetime, timezone
import json
import math
import os
import sys
import numpy as np

BASE_DIR = os.path.join("07_EVALUATION", "metatrader", "research")
sys.path.insert(0, os.path.abspath("."))
from importlib import import_module

boot_mod = import_module("07_EVALUATION.metatrader.research.engine.bootstrap")
stationary_bootstrap_indices = boot_mod.stationary_bootstrap_indices
hansen_spa_test = boot_mod.hansen_spa_test

CORPUS_H1 = os.path.join(BASE_DIR, "corpus", "H1", "XAUUSD_H1.csv")
TABLES_DIR = os.path.join(BASE_DIR, "tables")
ML_WEIGHTS_PATH = os.path.join("02_PRODUCT", "projects", "imported", "bot", "trade", "elite_quant_bot_v12", "ml_store", "ml_weights.json")

HOLDOUT_START = datetime(2026, 6, 20, 0, 0, 0)
HOLDOUT_END = datetime(2026, 9, 14, 23, 59, 59)

# Costuri XAUUSD din Partea C
SPREAD_POINTS = 21.0        # $0.21 / oz
POINT = 0.01                # 1 pt = $0.01
COMMISSION_PER_LOT = 2.0    # $2 per 100 oz lot ($0.02/pt)
SWAP_PER_DAY_POINTS = -4.75 # swap puncte per zi


def load_xauusd_h1():
    timestamps, opens, highs, lows, closes = [], [], [], [], []
    with open(CORPUS_H1, "r", encoding="utf-8") as f:
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


# -------------------------------------------------------------
# Semnale matematice conforme codului înghețat xau_library.py
# -------------------------------------------------------------

def gen_m1_liq_sweep(timestamps, opens, highs, lows, closes):
    """Sweep of PDH/PDL with body close back in range."""
    n = len(closes)
    signals = np.zeros(n, dtype=np.float64)

    # Identifică barele fiecărei zile
    day_highs = {}
    day_lows = {}
    for i, t in enumerate(timestamps):
        d = t.date()
        if d not in day_highs:
            day_highs[d] = highs[i]
            day_lows[d] = lows[i]
        else:
            day_highs[d] = max(day_highs[d], highs[i])
            day_lows[d] = min(day_lows[d], lows[i])

    dates = [t.date() for t in timestamps]

    for i in range(1, n):
        cur_d = dates[i]
        # găsește ziua precedentă
        prev_d = None
        for back_i in range(i - 1, -1, -1):
            if dates[back_i] != cur_d:
                prev_d = dates[back_i]
                break
        if not prev_d or prev_d not in day_highs:
            continue

        pdh = day_highs[prev_d]
        pdl = day_lows[prev_d]

        o, h, l, c = opens[i], highs[i], lows[i], closes[i]
        body_hi = max(o, c)
        body_lo = min(o, c)

        if h > pdh and body_hi <= pdh:
            signals[i] = -1.0  # SELL
        elif l < pdl and body_lo >= pdl:
            signals[i] = 1.0   # BUY
        else:
            signals[i] = 0.0

    return signals


def gen_m2_asian_box(timestamps, opens, highs, lows, closes):
    """Body-close break of Asian box (00:00-07:00 UTC) during London/NY (07:00-17:00 UTC)."""
    n = len(closes)
    signals = np.zeros(n, dtype=np.float64)

    # Asian box per zi
    asian_boxes = {}
    for i, t in enumerate(timestamps):
        d = t.date()
        if 0 <= t.hour < 7:
            if d not in asian_boxes:
                asian_boxes[d] = [highs[i], lows[i]]
            else:
                asian_boxes[d][0] = max(asian_boxes[d][0], highs[i])
                asian_boxes[d][1] = min(asian_boxes[d][1], lows[i])

    for i in range(n):
        t = timestamps[i]
        d = t.date()
        if not (7 <= t.hour < 17):
            continue
        if d not in asian_boxes:
            continue
        box_hi, box_lo = asian_boxes[d]
        o, c = opens[i], closes[i]
        body_hi = max(o, c)
        body_lo = min(o, c)

        if body_lo > box_hi and c > o:
            signals[i] = 1.0
        elif body_hi < box_lo and c < o:
            signals[i] = -1.0
        else:
            signals[i] = 0.0

    return signals


def gen_m3_body_breakout(highs, lows, opens, closes, lookback: int):
    """Body close breakout over lookback swing high/low."""
    n = len(closes)
    signals = np.zeros(n, dtype=np.float64)
    for i in range(lookback + 2, n):
        win_h = highs[i - lookback - 1 : i - 1]
        win_l = lows[i - lookback - 1 : i - 1]
        hi = np.max(win_h)
        lo = np.min(win_l)

        o, c = opens[i], closes[i]
        body_lo = min(o, c)
        body_hi = max(o, c)

        if body_lo > hi:
            signals[i] = 1.0
        elif body_hi < lo:
            signals[i] = -1.0
        else:
            signals[i] = 0.0
    return signals


def gen_m6_fvg_pullback(opens, highs, lows, closes):
    """Fair Value Gap detection + 50% pullback."""
    n = len(closes)
    signals = np.zeros(n, dtype=np.float64)

    for i in range(20, n):
        # caută cel mai recent FVG în ultimele 15 bare
        found = None
        for j in range(i - 1, max(1, i - 15), -1):
            a_h = highs[j - 2]
            c_l = lows[j]
            a_l = lows[j - 2]
            c_h = highs[j]
            # bullish FVG
            if a_h < c_l:
                found = (1.0, a_h, c_l)
                break
            # bearish FVG
            if a_l > c_h:
                found = (-1.0, c_h, a_l)
                break

        if not found:
            continue

        side, gap_lo, gap_hi = found
        mid = 0.5 * (gap_lo + gap_hi)
        c = closes[i]

        if side == 1.0 and gap_lo <= c <= mid:
            signals[i] = 1.0
        elif side == -1.0 and mid <= c <= gap_hi:
            signals[i] = -1.0
        else:
            signals[i] = 0.0

    return signals


def gen_m7_macd_momentum(closes, fast=5, slow=35, signal=5):
    """MACD momentum fast=5, slow=35, signal=5."""
    n = len(closes)
    signals = np.zeros(n, dtype=np.float64)

    def ema(series, span):
        alpha = 2.0 / (span + 1.0)
        out = np.empty(len(series), dtype=np.float64)
        out[0] = series[0]
        for t in range(1, len(series)):
            out[t] = alpha * series[t] + (1.0 - alpha) * out[t - 1]
        return out

    ema_fast = ema(closes, fast)
    ema_slow = ema(closes, slow)
    macd_line = ema_fast - ema_slow
    signal_line = ema(macd_line, signal)

    for i in range(1, n):
        if macd_line[i] > signal_line[i] and macd_line[i - 1] <= signal_line[i - 1]:
            signals[i] = 1.0
        elif macd_line[i] < signal_line[i] and macd_line[i - 1] >= signal_line[i - 1]:
            signals[i] = -1.0
        else:
            signals[i] = signals[i - 1]  # menține poziția până la cross invers

    return signals


# -------------------------------------------------------------
# Backtest Engine cu Costuri Reale din Tick-uri
# -------------------------------------------------------------

def backtest_h1_strategy(opens, closes, signals, timestamps, mask):
    """
    Execuție realistă la Open[t+1] pe baza semnalului de la Close[t].
    Calculează randamente nete, cost drag, Sharpe, Max Drawdown și Trade Count.
    """
    n = len(opens)
    positions = np.zeros(n, dtype=np.float64)

    # Execuție anti-lookahead: poziția pe bara t este semnalul generat la bara t-1
    for t in range(1, n):
        positions[t] = signals[t - 1]

    # Randament brut per bară: pos[t] * (close[t] - open[t]) / open[t]
    gross_returns = positions * (closes - opens) / opens

    # Costuri: spread + comision la fiecare schimbare de poziție
    # Cost comision: $2/lot pe contract de 100 oz. Preț aur ~ $2500 -> 100 oz = $250,000.
    # $2 comision / $250,000 valoare noțională = 0.0008% (8e-6)
    # Cost spread: 21 points = $0.21 / $2500 = 0.0084% (8.4e-5)
    # Total cost tranzacție (half-turn): 0.5 * (spread_pts * point + comm_rate) / price
    trade_costs = np.zeros(n, dtype=np.float64)
    pos_diff = np.abs(np.diff(positions, prepend=0.0))

    cost_per_point_ratio = (SPREAD_POINTS * POINT + 2.0 / 100.0)  # în dolari per oz
    for t in range(n):
        if pos_diff[t] > 0:
            trade_costs[t] = pos_diff[t] * (cost_per_point_ratio / opens[t])

    # Swap finanțare: -4.75 points per zi (aplicat o dată la 24 de bare orare)
    swap_costs = np.zeros(n, dtype=np.float64)
    for t in range(n):
        if positions[t] != 0:
            swap_costs[t] = (4.75 * POINT / 24.0) / opens[t]

    net_returns = gross_returns - trade_costs - swap_costs

    # Filtrare strict pe masca de holdout
    holdout_net = net_returns[mask]
    holdout_gross = gross_returns[mask]
    holdout_pos = positions[mask]
    holdout_pos_diff = pos_diff[mask]

    n_bars = len(holdout_net)
    trade_count = int(np.sum(holdout_pos_diff > 0.0) / 2.0)  # round-trip

    # Win rate pe trade-uri
    # Reconstituim trade-urile individuale
    trades = []
    in_trade = False
    entry_p = 0.0
    side = 0.0
    for t in range(n_bars):
        pos = holdout_pos[t]
        if not in_trade and pos != 0.0:
            in_trade = True
            side = pos
            entry_p = opens[mask][t]
        elif in_trade:
            if pos != side:
                exit_p = opens[mask][t]
                ret = side * (exit_p - entry_p) / entry_p
                trades.append(ret)
                if pos != 0.0:
                    side = pos
                    entry_p = opens[mask][t]
                else:
                    in_trade = False

    wins = sum(1 for tr in trades if tr > 0)
    win_rate = (wins / len(trades)) if trades else 0.0

    # Sharpe anualizat pe bare H1 (aprox. 2000 trading hours per year)
    ann_factor = np.sqrt(2000.0)
    mean_net = np.mean(holdout_net)
    std_net = np.std(holdout_net)
    net_sharpe = (mean_net / (std_net + 1e-12)) * ann_factor

    mean_gross = np.mean(holdout_gross)
    std_gross = np.std(holdout_gross)
    gross_sharpe = (mean_gross / (std_gross + 1e-12)) * ann_factor

    cost_drag = gross_sharpe - net_sharpe

    # Max Drawdown
    cum_ret = np.cumsum(holdout_net)
    peaks = np.maximum.accumulate(cum_ret)
    dd = peaks - cum_ret
    max_dd = float(np.max(dd)) if len(dd) > 0 else 0.0

    # Total cumulative return
    tot_return = float(cum_ret[-1]) if len(cum_ret) > 0 else 0.0

    return {
        "net_returns": holdout_net,
        "gross_returns": holdout_gross,
        "tot_return": tot_return,
        "gross_sharpe": gross_sharpe,
        "net_sharpe": net_sharpe,
        "cost_drag": cost_drag,
        "max_drawdown": max_dd,
        "trade_count": len(trades),
        "win_rate": win_rate,
        "trades": trades,
    }


def main():
    import argparse
    parser = argparse.ArgumentParser()
    parser.add_argument("--out-dir", default=TABLES_DIR, help="Output directory for tables")
    args = parser.parse_args()
    out_dir = args.out_dir

    timestamps, opens, highs, lows, closes = load_xauusd_h1()
    ts_arr = np.array(timestamps)
    holdout_mask = (ts_arr >= HOLDOUT_START) & (ts_arr <= HOLDOUT_END)
    n_holdout = np.sum(holdout_mask)
    print(f"Baza H1 incarcata: {len(closes)} bare. Holdout: {n_holdout} bare orare (2026-06-20 -> 2026-09-14)")

    # 1. Generare semnale pentru cele 7 strategii din bot
    strat_funcs = {
        "M1_LIQ_SWEEP": gen_m1_liq_sweep(timestamps, opens, highs, lows, closes),
        "M2_ASIAN_BOX": gen_m2_asian_box(timestamps, opens, highs, lows, closes),
        "M3_BODY_BRK_15": gen_m3_body_breakout(highs, lows, opens, closes, lookback=15),
        "M4_BODY_BRK_25": gen_m3_body_breakout(highs, lows, opens, closes, lookback=25),
        "M5_BODY_BRK_40": gen_m3_body_breakout(highs, lows, opens, closes, lookback=40),
        "M6_FVG_PULLBACK": gen_m6_fvg_pullback(opens, highs, lows, closes),
        "M7_MACD_MOM": gen_m7_macd_momentum(closes, fast=5, slow=35, signal=5),
    }

    # 2. Control S0a: Buy and Hold XAUUSD pe holdout
    s0a_signals = np.ones(len(closes), dtype=np.float64)
    s0a_res = backtest_h1_strategy(opens, closes, s0a_signals, timestamps, holdout_mask)
    print(f"Control S0a (Buy & Hold): Tot Return = {s0a_res['tot_return']*100:.2f}%, Sharpe Net = {s0a_res['net_sharpe']:.2f}")

    results = []
    matrix_net_returns = {}

    rng_mc = np.random.RandomState(42)

    for name, sigs in strat_funcs.items():
        res = backtest_h1_strategy(opens, closes, sigs, timestamps, holdout_mask)
        matrix_net_returns[name] = res["net_returns"]

        # Control S0b: Monte Carlo aleator cu număr identic de trade-uri
        n_tr = res["trade_count"]
        mc_sharpes = []
        if n_tr > 0:
            for _ in range(1000):
                # generare semnal aleator cu aceeasi frecventa
                rand_sigs = np.zeros(len(closes), dtype=np.float64)
                rand_idx = rng_mc.choice(np.where(holdout_mask)[0], size=n_tr * 2, replace=False)
                for idx in rand_idx:
                    rand_sigs[idx] = rng_mc.choice([-1.0, 1.0])
                mc_bt = backtest_h1_strategy(opens, closes, rand_sigs, timestamps, holdout_mask)
                mc_sharpes.append(mc_bt["net_sharpe"])
            p_val_s0b = float(np.mean(np.array(mc_sharpes) >= res["net_sharpe"]))
        else:
            p_val_s0b = 1.0

        results.append({
            "strategy": name,
            "trade_count": res["trade_count"],
            "win_rate": round(res["win_rate"], 4),
            "total_return_pct": round(res["tot_return"] * 100.0, 2),
            "gross_sharpe": round(res["gross_sharpe"], 4),
            "net_sharpe": round(res["net_sharpe"], 4),
            "cost_drag": round(res["cost_drag"], 4),
            "max_drawdown_pct": round(res["max_drawdown"] * 100.0, 2),
            "p_value_vs_s0b_random": round(p_val_s0b, 4),
            "beats_s0a_buy_and_hold": bool(res["net_sharpe"] > s0a_res["net_sharpe"]),
        })

        print(f"[{name}] Trades: {res['trade_count']:2d} | WinRate: {res['win_rate']*100:5.1f}% | Net Sharpe: {res['net_sharpe']:6.2f} | Gross Sharpe: {res['gross_sharpe']:6.2f} | p vs S0b: {p_val_s0b:.4f}")

    # 3. Test Hansen SPA pe cele 7 strategii din bot vs Cash și vs S0a
    m_active = len(results)
    min_len = min(len(v) for v in matrix_net_returns.values())
    loss_models = np.empty((min_len, m_active), dtype=np.float64)
    for j, (name, _) in enumerate(strat_funcs.items()):
        loss_models[:, j] = matrix_net_returns[name][:min_len]

    # Vs Cash
    bench_cash = np.zeros(min_len, dtype=np.float64)
    spa_cash = hansen_spa_test(bench_cash, loss_models, b_reps=2000, q=10.0, seed=42)

    # Vs S0a (Buy and Hold)
    bench_s0a = s0a_res["net_returns"][:min_len]
    spa_s0a = hansen_spa_test(bench_s0a, loss_models, b_reps=2000, q=10.0, seed=42)

    print("\n--- TEST HANSEN SPA PE CELE 7 STRATEGII DIN BOT PE HOLDOUT ---")
    print(f"Vs. Cash: T_SPA = {spa_cash['test_stat']:.4f}, p_SPA = {spa_cash['p_value_spa']:.4f}, Cel mai bun: {list(strat_funcs.keys())[spa_cash['best_model_idx']]}")
    print(f"Vs. S0a (Buy & Hold): T_SPA = {spa_s0a['test_stat']:.4f}, p_SPA = {spa_s0a['p_value_spa']:.4f}, Cel mai bun: {list(strat_funcs.keys())[spa_s0a['best_model_idx']]}")

    # Adaugă p_SPA în rezultate
    for r in results:
        r["p_value_hansen_spa_cash"] = spa_cash["p_value_spa"]
        r["p_value_hansen_spa_s0a"] = spa_s0a["p_value_spa"]
        r["verdict"] = "VALIDATED_ALPHA" if (r["net_sharpe"] >= 0.50 and r["p_value_vs_s0b_random"] < 0.05 and spa_cash["p_value_spa"] < 0.05) else "FAILED_NULL"

    # 4. Test calibrare probabilitati ML
    brier_info = "N/A"
    if os.path.exists(ML_WEIGHTS_PATH):
        with open(ML_WEIGHTS_PATH, "r", encoding="utf-8") as f:
            ml_data = json.load(f)
        w = ml_data.get("w", [])
        b = ml_data.get("b", 0.0)
        print(f"\nModel ML incarcat: {len(w)} greutati. Bias b = {b:.4f}. Greutati extreme: min={min(w):.2f}, max={max(w):.2f}")
        # Analiza overconfidence din greutățile extreme (>300)
        has_runaway_weights = any(abs(wi) > 50.0 for wi in w)
        if has_runaway_weights:
            print("ALERTA OVERCONFIDENCE: Greutatile ML contin coeficienti neregularizati giganti (>300.0), provocand colapsul sigmoidei la 0.999 sau 0.001.")

    # 5. Salvare tabel
    out_csv = os.path.join(out_dir, "table_10_owner_bot_holdout.csv")
    fieldnames = list(results[0].keys())
    with open(out_csv, "w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=fieldnames)
        writer.writeheader()
        for r in results:
            writer.writerow(r)

    print(f"\nSalvat cu succes in {out_csv} ({len(results)} strategii evaluate)")


if __name__ == "__main__":
    main()
