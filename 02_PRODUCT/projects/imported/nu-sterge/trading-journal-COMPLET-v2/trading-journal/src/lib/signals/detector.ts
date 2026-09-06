import type { Candle } from './indicators';
import {
  rsi,
  atr,
  ema,
  sma,
  volumeMA,
  findSwingHighs,
  findSwingLows,
} from './indicators';

export type SignalType =
  | 'breakout_up'
  | 'breakout_down'
  | 'pullback_long'
  | 'pullback_short'
  | 'rsi_oversold_bounce'
  | 'rsi_overbought_reject'
  | 'rsi_divergence_bullish'
  | 'rsi_divergence_bearish'
  | 'ema_cross_bullish'
  | 'ema_cross_bearish';

export interface Signal {
  type: SignalType;
  symbol: string;
  timeframe: string;
  timestamp: number;
  direction: 'long' | 'short';
  strength: number; // 0-100
  entry: number;
  stopLoss: number;
  takeProfit: number;
  riskRewardRatio: number;
  reason: string;
  // Context for user to make decision
  context: {
    currentPrice: number;
    rsi?: number;
    atr?: number;
    volumeRatio?: number; // current vol / avg vol
    patternDetails?: Record<string, unknown>;
  };
}

export interface SignalConfig {
  // Thresholds
  minStrength: number; // default 60
  minRiskReward: number; // default 1.5

  // Indicator params
  rsiPeriod: number;
  rsiOversold: number;
  rsiOverbought: number;
  emaFast: number;
  emaSlow: number;
  atrPeriod: number;
  atrMultiplier: number; // for SL/TP calc

  // Which signals to look for
  enabledSignals: SignalType[];
}

export const DEFAULT_SIGNAL_CONFIG: SignalConfig = {
  minStrength: 60,
  minRiskReward: 1.5,
  rsiPeriod: 14,
  rsiOversold: 30,
  rsiOverbought: 70,
  emaFast: 20,
  emaSlow: 50,
  atrPeriod: 14,
  atrMultiplier: 2,
  enabledSignals: [
    'breakout_up',
    'breakout_down',
    'pullback_long',
    'pullback_short',
    'rsi_oversold_bounce',
    'rsi_overbought_reject',
  ],
};

/**
 * Analyze candles and return list of active signals.
 * Runs ALL enabled detectors and returns the strongest.
 */
export function detectSignals(
  symbol: string,
  timeframe: string,
  candles: Candle[],
  config: SignalConfig = DEFAULT_SIGNAL_CONFIG
): Signal[] {
  if (candles.length < 100) return []; // Need enough data

  const closes = candles.map((c) => c.close);
  const highs = candles.map((c) => c.high);
  const lows = candles.map((c) => c.low);

  const rsiValues = rsi(closes, config.rsiPeriod);
  const emaFast = ema(closes, config.emaFast);
  const emaSlow = ema(closes, config.emaSlow);
  const atrValues = atr(candles, config.atrPeriod);
  const volMA = volumeMA(candles, 20);

  const i = candles.length - 1; // current candle
  const prev = i - 1;
  const signals: Signal[] = [];

  const currentPrice = closes[i];
  const currentATR = atrValues[i];
  const currentRSI = rsiValues[i];
  const currentVol = candles[i].volume;
  const avgVol = volMA[i];
  const volRatio = avgVol > 0 ? currentVol / avgVol : 1;

  // ===== Breakout detection =====
  if (config.enabledSignals.includes('breakout_up')) {
    const swingHighs = findSwingHighs(candles.slice(0, -1), 5);
    if (swingHighs.length > 0) {
      const lastSwingHigh = swingHighs[swingHighs.length - 1];
      const resistance = candles[lastSwingHigh].high;
      if (
        closes[i] > resistance &&
        closes[prev] <= resistance &&
        volRatio > 1.3
      ) {
        const sl = resistance - currentATR;
        const tp = currentPrice + (currentPrice - sl) * 2;
        signals.push({
          type: 'breakout_up',
          symbol,
          timeframe,
          timestamp: candles[i].time,
          direction: 'long',
          strength: Math.min(100, 50 + volRatio * 20),
          entry: currentPrice,
          stopLoss: sl,
          takeProfit: tp,
          riskRewardRatio: (tp - currentPrice) / (currentPrice - sl),
          reason: `Breakout peste rezistența ${resistance.toFixed(4)} cu volum ${volRatio.toFixed(1)}× media`,
          context: { currentPrice, rsi: currentRSI, atr: currentATR, volumeRatio: volRatio, patternDetails: { resistance } },
        });
      }
    }
  }

  if (config.enabledSignals.includes('breakout_down')) {
    const swingLows = findSwingLows(candles.slice(0, -1), 5);
    if (swingLows.length > 0) {
      const lastSwingLow = swingLows[swingLows.length - 1];
      const support = candles[lastSwingLow].low;
      if (
        closes[i] < support &&
        closes[prev] >= support &&
        volRatio > 1.3
      ) {
        const sl = support + currentATR;
        const tp = currentPrice - (sl - currentPrice) * 2;
        signals.push({
          type: 'breakout_down',
          symbol,
          timeframe,
          timestamp: candles[i].time,
          direction: 'short',
          strength: Math.min(100, 50 + volRatio * 20),
          entry: currentPrice,
          stopLoss: sl,
          takeProfit: tp,
          riskRewardRatio: (currentPrice - tp) / (sl - currentPrice),
          reason: `Breakdown sub suportul ${support.toFixed(4)} cu volum ${volRatio.toFixed(1)}× media`,
          context: { currentPrice, rsi: currentRSI, atr: currentATR, volumeRatio: volRatio, patternDetails: { support } },
        });
      }
    }
  }

  // ===== Pullback to EMA =====
  if (config.enabledSignals.includes('pullback_long')) {
    const uptrend = emaFast[i] > emaSlow[i] && emaFast[i - 20] < emaFast[i];
    const pulledBackToEma = lows[i] <= emaFast[i] * 1.005 && closes[i] > emaFast[i];
    if (uptrend && pulledBackToEma && currentRSI < 50 && currentRSI > 30) {
      const sl = lows[i] - currentATR * 0.5;
      const tp = currentPrice + (currentPrice - sl) * 2;
      signals.push({
        type: 'pullback_long',
        symbol,
        timeframe,
        timestamp: candles[i].time,
        direction: 'long',
        strength: 65,
        entry: currentPrice,
        stopLoss: sl,
        takeProfit: tp,
        riskRewardRatio: (tp - currentPrice) / (currentPrice - sl),
        reason: `Pullback la EMA${config.emaFast} în uptrend, RSI ${currentRSI.toFixed(0)}`,
        context: { currentPrice, rsi: currentRSI, atr: currentATR, volumeRatio: volRatio },
      });
    }
  }

  if (config.enabledSignals.includes('pullback_short')) {
    const downtrend = emaFast[i] < emaSlow[i] && emaFast[i - 20] > emaFast[i];
    const pulledBackToEma = highs[i] >= emaFast[i] * 0.995 && closes[i] < emaFast[i];
    if (downtrend && pulledBackToEma && currentRSI > 50 && currentRSI < 70) {
      const sl = highs[i] + currentATR * 0.5;
      const tp = currentPrice - (sl - currentPrice) * 2;
      signals.push({
        type: 'pullback_short',
        symbol,
        timeframe,
        timestamp: candles[i].time,
        direction: 'short',
        strength: 65,
        entry: currentPrice,
        stopLoss: sl,
        takeProfit: tp,
        riskRewardRatio: (currentPrice - tp) / (sl - currentPrice),
        reason: `Pullback la EMA${config.emaFast} în downtrend, RSI ${currentRSI.toFixed(0)}`,
        context: { currentPrice, rsi: currentRSI, atr: currentATR, volumeRatio: volRatio },
      });
    }
  }

  // ===== RSI extremes =====
  if (config.enabledSignals.includes('rsi_oversold_bounce')) {
    if (rsiValues[prev] < config.rsiOversold && currentRSI > config.rsiOversold && closes[i] > closes[prev]) {
      const sl = lows[i] - currentATR * 0.5;
      const tp = currentPrice + (currentPrice - sl) * 2;
      signals.push({
        type: 'rsi_oversold_bounce',
        symbol,
        timeframe,
        timestamp: candles[i].time,
        direction: 'long',
        strength: 60,
        entry: currentPrice,
        stopLoss: sl,
        takeProfit: tp,
        riskRewardRatio: (tp - currentPrice) / (currentPrice - sl),
        reason: `Bounce după RSI oversold (${rsiValues[prev].toFixed(0)} → ${currentRSI.toFixed(0)})`,
        context: { currentPrice, rsi: currentRSI, atr: currentATR, volumeRatio: volRatio },
      });
    }
  }

  if (config.enabledSignals.includes('rsi_overbought_reject')) {
    if (rsiValues[prev] > config.rsiOverbought && currentRSI < config.rsiOverbought && closes[i] < closes[prev]) {
      const sl = highs[i] + currentATR * 0.5;
      const tp = currentPrice - (sl - currentPrice) * 2;
      signals.push({
        type: 'rsi_overbought_reject',
        symbol,
        timeframe,
        timestamp: candles[i].time,
        direction: 'short',
        strength: 60,
        entry: currentPrice,
        stopLoss: sl,
        takeProfit: tp,
        riskRewardRatio: (currentPrice - tp) / (sl - currentPrice),
        reason: `Reject după RSI overbought (${rsiValues[prev].toFixed(0)} → ${currentRSI.toFixed(0)})`,
        context: { currentPrice, rsi: currentRSI, atr: currentATR, volumeRatio: volRatio },
      });
    }
  }

  // ===== EMA crossover =====
  if (config.enabledSignals.includes('ema_cross_bullish')) {
    if (emaFast[prev] <= emaSlow[prev] && emaFast[i] > emaSlow[i]) {
      const sl = lows[i] - currentATR;
      const tp = currentPrice + (currentPrice - sl) * 2;
      signals.push({
        type: 'ema_cross_bullish',
        symbol,
        timeframe,
        timestamp: candles[i].time,
        direction: 'long',
        strength: 55,
        entry: currentPrice,
        stopLoss: sl,
        takeProfit: tp,
        riskRewardRatio: (tp - currentPrice) / (currentPrice - sl),
        reason: `EMA${config.emaFast} a tăiat peste EMA${config.emaSlow} (golden cross)`,
        context: { currentPrice, rsi: currentRSI, atr: currentATR, volumeRatio: volRatio },
      });
    }
  }

  if (config.enabledSignals.includes('ema_cross_bearish')) {
    if (emaFast[prev] >= emaSlow[prev] && emaFast[i] < emaSlow[i]) {
      const sl = highs[i] + currentATR;
      const tp = currentPrice - (sl - currentPrice) * 2;
      signals.push({
        type: 'ema_cross_bearish',
        symbol,
        timeframe,
        timestamp: candles[i].time,
        direction: 'short',
        strength: 55,
        entry: currentPrice,
        stopLoss: sl,
        takeProfit: tp,
        riskRewardRatio: (currentPrice - tp) / (sl - currentPrice),
        reason: `EMA${config.emaFast} a tăiat sub EMA${config.emaSlow} (death cross)`,
        context: { currentPrice, rsi: currentRSI, atr: currentATR, volumeRatio: volRatio },
      });
    }
  }

  // Filter by min strength and RR
  return signals.filter(
    (s) => s.strength >= config.minStrength && s.riskRewardRatio >= config.minRiskReward
  );
}

/**
 * Calculate position size based on risk management.
 * Given: account size, risk % per trade, entry, stop loss
 * Returns: quantity to trade
 */
export function calculatePositionSize(
  accountBalance: number,
  riskPercent: number, // e.g. 1 for 1%
  entry: number,
  stopLoss: number
): {
  quantity: number;
  riskAmount: number;
  maxLoss: number;
} {
  const riskAmount = accountBalance * (riskPercent / 100);
  const priceDiff = Math.abs(entry - stopLoss);
  const quantity = priceDiff > 0 ? riskAmount / priceDiff : 0;
  const maxLoss = quantity * priceDiff;
  return { quantity, riskAmount, maxLoss };
}
