"""
engine/strategies.py — Implementarea exactă a familiilor de strategii preînregistrate
(S1 Momentum, S2 Revenire la Medie, S3 Breakout, S4 Carry, S0 Controale).

Regulă strictă de cauzalitate:
Orice semnal la pasul t folosește strict informația până la t inclusiv.
Execuția poziției se face la t+1 (deschiderea barei t+1).
"""

from typing import List, Tuple
import numpy as np


def generate_s1_momentum(closes: np.ndarray, L: int) -> np.ndarray:
    """
    S1: Time-Series Momentum (TSMOM).
    Semnal: +1 dacă Close[t] > Close[t-L], -1 dacă Close[t] < Close[t-L], 0 altfel.
    """
    n = len(closes)
    signals = np.zeros(n, dtype=np.float64)
    if n <= L:
        return signals

    for t in range(L, n):
        ret = (closes[t] - closes[t - L]) / closes[t - L]
        if ret > 0:
            signals[t] = 1.0
        elif ret < 0:
            signals[t] = -1.0
        else:
            signals[t] = 0.0
    return signals


def generate_s2_mean_reversion(
    closes: np.ndarray, W: int = 20, Z_thresh: float = 2.0, K_max: int = 10
) -> np.ndarray:
    """
    S2: Mean Reversion (Bollinger Z-Score).
    Intrare:
      Z < -Z_thresh -> Long (+1) până la Z >= 0 sau K_max bare.
      Z > +Z_thresh -> Short (-1) până la Z <= 0 sau K_max bare.
    """
    n = len(closes)
    signals = np.zeros(n, dtype=np.float64)
    if n < W:
        return signals

    current_pos = 0.0
    holding_bars = 0

    for t in range(W - 1, n):
        window = closes[t - W + 1 : t + 1]
        mean_val = np.mean(window)
        std_val = np.std(window)
        z = (closes[t] - mean_val) / std_val if std_val > 1e-12 else 0.0

        if current_pos == 0.0:
            if z < -Z_thresh:
                current_pos = 1.0
                holding_bars = 1
            elif z > Z_thresh:
                current_pos = -1.0
                holding_bars = 1
        elif current_pos == 1.0:
            holding_bars += 1
            # Ieșire la revenire la medie sau timeout
            if z >= 0.0 or holding_bars >= K_max:
                current_pos = 0.0
                holding_bars = 0
        elif current_pos == -1.0:
            holding_bars += 1
            # Ieșire la revenire la medie sau timeout
            if z <= 0.0 or holding_bars >= K_max:
                current_pos = 0.0
                holding_bars = 0

        signals[t] = current_pos

    return signals


def generate_s3_breakout(highs: np.ndarray, lows: np.ndarray, closes: np.ndarray, L: int = 20) -> np.ndarray:
    """
    S3: Donchian Channel Breakout.
    Semnal:
      Close[t] > max(High[t-L .. t-1]) -> +1
      Close[t] < min(Low[t-L .. t-1]) -> -1
      altfel menține semnalul precedent.
    """
    n = len(closes)
    signals = np.zeros(n, dtype=np.float64)
    if n <= L:
        return signals

    current_pos = 0.0
    for t in range(L, n):
        max_high = np.max(highs[t - L : t])
        min_low = np.min(lows[t - L : t])

        if closes[t] > max_high:
            current_pos = 1.0
        elif closes[t] < min_low:
            current_pos = -1.0
        # altfel current_pos rămâne neschimbat

        signals[t] = current_pos

    return signals


def generate_s4_carry(n_bars: int, swap_long: float, swap_short: float) -> np.ndarray:
    """
    S4: FX Carry (Swap Differential).
    Semnal:
      +1 dacă swap_long > 0 și swap_short <= 0
      -1 dacă swap_short > 0 și swap_long <= 0
      0 dacă ambele sunt negative (marjă penalizatoare broker).
    """
    signals = np.zeros(n_bars, dtype=np.float64)
    if swap_long > 0 and swap_short <= 0:
        signals[:] = 1.0
    elif swap_short > 0 and swap_long <= 0:
        signals[:] = -1.0
    else:
        signals[:] = 0.0
    return signals


def generate_s0a_buy_and_hold(n_bars: int) -> np.ndarray:
    """
    S0a: Control pasiv Cumpără-și-Ține (100% Long permanent).
    """
    return np.ones(n_bars, dtype=np.float64)


def extract_trades_and_durations(signals: np.ndarray) -> Tuple[int, List[int], List[float]]:
    """
    Extrage tranzacțiile, duratele de deținere și direcțiile dintr-o serie de semnale.
    """
    durations = []
    directions = []
    n = len(signals)
    i = 0
    while i < n:
        if signals[i] != 0.0:
            direction = signals[i]
            dur = 0
            while i < n and signals[i] == direction:
                dur += 1
                i += 1
            durations.append(dur)
            directions.append(direction)
        else:
            i += 1
    return len(durations), durations, directions


def generate_s0b_random_matched(
    real_signals: np.ndarray, seed: int = 42
) -> np.ndarray:
    """
    S0b: Control cu intrări aleatoare potrivit pe expunere (Random-Entry Matched Control).
    Păstrează numărul de tranzacții și distribuția duratelor de deținere, dar alege
    momentele de intrare aleatoriu uniform în eșantion.
    """
    n = len(real_signals)
    n_trades, durations, directions = extract_trades_and_durations(real_signals)

    if n_trades == 0:
        return np.zeros(n, dtype=np.float64)

    rng = np.random.RandomState(seed)
    signals = np.zeros(n, dtype=np.float64)

    # Încercăm plasarea aleatoare a blocurilor de tranzacții fără suprapunere
    total_trade_bars = sum(durations)
    if total_trade_bars >= n:
        # Dacă strategia este investită 100% din timp, inversăm direcția aleatoriu
        return rng.choice([-1.0, 1.0], size=n)

    # Permutăm duratele și le plasăm la puncte de pornire aleatoare
    indices = rng.permutation(n_trades)
    occupied = np.zeros(n, dtype=bool)

    for idx in indices:
        dur = durations[idx]
        dir_val = directions[idx]
        max_start = n - dur
        if max_start <= 0:
            continue

        # Căutăm o fereastră liberă
        placed = False
        candidates = rng.permutation(max_start)
        for start in candidates:
            if not np.any(occupied[start : start + dur]):
                occupied[start : start + dur] = True
                signals[start : start + dur] = dir_val
                placed = True
                break

        if not placed:
            # Plasăm unde putem
            signals[candidates[0] : min(candidates[0] + dur, n)] = dir_val

    return signals
