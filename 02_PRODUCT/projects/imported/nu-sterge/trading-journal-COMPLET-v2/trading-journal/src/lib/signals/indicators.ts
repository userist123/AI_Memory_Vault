/**
 * Technical indicators - pure functions, no dependencies.
 * Used by signal engine to detect trading setups.
 */

export interface Candle {
  time: number; // unix ms
  open: number;
  high: number;
  low: number;
  close: number;
  volume: number;
}

// ===== Moving averages =====

export function sma(values: number[], period: number): number[] {
  const result: number[] = [];
  for (let i = 0; i < values.length; i++) {
    if (i < period - 1) {
      result.push(NaN);
      continue;
    }
    let sum = 0;
    for (let j = i - period + 1; j <= i; j++) sum += values[j];
    result.push(sum / period);
  }
  return result;
}

export function ema(values: number[], period: number): number[] {
  const result: number[] = [];
  const multiplier = 2 / (period + 1);
  for (let i = 0; i < values.length; i++) {
    if (i === 0) {
      result.push(values[0]);
      continue;
    }
    result.push((values[i] - result[i - 1]) * multiplier + result[i - 1]);
  }
  return result;
}

// ===== RSI (Relative Strength Index) =====

export function rsi(values: number[], period = 14): number[] {
  const result: number[] = new Array(values.length).fill(NaN);
  if (values.length < period + 1) return result;

  let gains = 0;
  let losses = 0;
  for (let i = 1; i <= period; i++) {
    const diff = values[i] - values[i - 1];
    if (diff > 0) gains += diff;
    else losses -= diff;
  }

  let avgGain = gains / period;
  let avgLoss = losses / period;

  result[period] = avgLoss === 0 ? 100 : 100 - 100 / (1 + avgGain / avgLoss);

  for (let i = period + 1; i < values.length; i++) {
    const diff = values[i] - values[i - 1];
    const gain = diff > 0 ? diff : 0;
    const loss = diff < 0 ? -diff : 0;
    avgGain = (avgGain * (period - 1) + gain) / period;
    avgLoss = (avgLoss * (period - 1) + loss) / period;
    result[i] = avgLoss === 0 ? 100 : 100 - 100 / (1 + avgGain / avgLoss);
  }

  return result;
}

// ===== MACD =====

export function macd(
  values: number[],
  fast = 12,
  slow = 26,
  signal = 9
): { macd: number[]; signal: number[]; histogram: number[] } {
  const emaFast = ema(values, fast);
  const emaSlow = ema(values, slow);
  const macdLine = values.map((_, i) => emaFast[i] - emaSlow[i]);
  const signalLine = ema(macdLine.slice(slow - 1), signal);
  const paddedSignal = new Array(slow - 1).fill(NaN).concat(signalLine);
  const histogram = macdLine.map((m, i) => m - paddedSignal[i]);
  return { macd: macdLine, signal: paddedSignal, histogram };
}

// ===== ATR (Average True Range) =====

export function atr(candles: Candle[], period = 14): number[] {
  const trs: number[] = [];
  for (let i = 0; i < candles.length; i++) {
    if (i === 0) {
      trs.push(candles[i].high - candles[i].low);
      continue;
    }
    const h = candles[i].high;
    const l = candles[i].low;
    const pc = candles[i - 1].close;
    trs.push(Math.max(h - l, Math.abs(h - pc), Math.abs(l - pc)));
  }

  // Wilder's smoothing
  const result: number[] = new Array(candles.length).fill(NaN);
  if (trs.length < period) return result;

  let sum = 0;
  for (let i = 0; i < period; i++) sum += trs[i];
  result[period - 1] = sum / period;

  for (let i = period; i < trs.length; i++) {
    result[i] = (result[i - 1] * (period - 1) + trs[i]) / period;
  }
  return result;
}

// ===== Bollinger Bands =====

export function bollinger(
  values: number[],
  period = 20,
  stdDev = 2
): { upper: number[]; middle: number[]; lower: number[] } {
  const middle = sma(values, period);
  const upper: number[] = [];
  const lower: number[] = [];

  for (let i = 0; i < values.length; i++) {
    if (i < period - 1) {
      upper.push(NaN);
      lower.push(NaN);
      continue;
    }
    let sum = 0;
    for (let j = i - period + 1; j <= i; j++) {
      sum += (values[j] - middle[i]) ** 2;
    }
    const std = Math.sqrt(sum / period);
    upper.push(middle[i] + std * stdDev);
    lower.push(middle[i] - std * stdDev);
  }

  return { upper, middle, lower };
}

// ===== Swing highs/lows =====

export function findSwingHighs(candles: Candle[], lookback = 5): number[] {
  const highs: number[] = [];
  for (let i = lookback; i < candles.length - lookback; i++) {
    let isSwingHigh = true;
    for (let j = i - lookback; j <= i + lookback; j++) {
      if (j !== i && candles[j].high >= candles[i].high) {
        isSwingHigh = false;
        break;
      }
    }
    if (isSwingHigh) highs.push(i);
  }
  return highs;
}

export function findSwingLows(candles: Candle[], lookback = 5): number[] {
  const lows: number[] = [];
  for (let i = lookback; i < candles.length - lookback; i++) {
    let isSwingLow = true;
    for (let j = i - lookback; j <= i + lookback; j++) {
      if (j !== i && candles[j].low <= candles[i].low) {
        isSwingLow = false;
        break;
      }
    }
    if (isSwingLow) lows.push(i);
  }
  return lows;
}

// ===== Volume analysis =====

export function volumeMA(candles: Candle[], period = 20): number[] {
  return sma(candles.map((c) => c.volume), period);
}

export function isHighVolume(candle: Candle, avgVolume: number, multiplier = 1.5): boolean {
  return candle.volume > avgVolume * multiplier;
}
