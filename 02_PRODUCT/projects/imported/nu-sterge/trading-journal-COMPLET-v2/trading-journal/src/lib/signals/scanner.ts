import type { Signal } from './detector';
import { detectSignals } from './detector';
import { fetchBinanceCandles, getTopBinancePairs, type Timeframe } from '@/lib/market/binance-data';
import { saveAlert, getNotificationPrefs, type StoredAlert } from '@/lib/db/alerts';
import { sendEmail, buildSignalAlertEmail } from '@/lib/notifications/email';
import {
  sendTelegramMessage,
  buildSignalTelegramMessage,
  buildSignalInlineKeyboard,
} from '@/lib/notifications/telegram';
import { getUserById } from '@/lib/db/users';

/**
 * Scan a list of symbols for signals on given timeframes.
 * Used by:
 * - Cron job (periodic)
 * - Manual scan (user clicks "Scan now")
 */
export async function scanSymbols(
  symbols: string[],
  timeframes: Timeframe[]
): Promise<Signal[]> {
  const allSignals: Signal[] = [];

  // Parallel fetch + detect (with concurrency limit to not hit rate limits)
  const batchSize = 5;
  for (let i = 0; i < symbols.length; i += batchSize) {
    const batch = symbols.slice(i, i + batchSize);
    const promises = batch.flatMap((symbol) =>
      timeframes.map(async (tf) => {
        try {
          const candles = await fetchBinanceCandles(symbol, tf, 200);
          return detectSignals(symbol, tf, candles);
        } catch (err) {
          console.warn(`[Scan] ${symbol} ${tf} failed:`, err);
          return [];
        }
      })
    );
    const results = await Promise.all(promises);
    for (const signals of results) {
      allSignals.push(...signals);
    }
  }

  return allSignals;
}

/**
 * For a list of fresh signals, find which users care + notify them.
 */
export async function dispatchSignalsToUsers(signals: Signal[], userIds: string[]): Promise<{
  alertsCreated: number;
  emailsSent: number;
  telegramsSent: number;
}> {
  let alertsCreated = 0;
  let emailsSent = 0;
  let telegramsSent = 0;

  const appUrl = process.env.NEXT_PUBLIC_APP_URL || 'http://localhost:3000';

  for (const userId of userIds) {
    const [user, prefs] = await Promise.all([
      getUserById(userId),
      getNotificationPrefs(userId),
    ]);
    if (!user) continue;

    // Filter signals by user preferences
    const relevant = signals.filter((s) => {
      if (s.strength < prefs.filters.minStrength) return false;
      if (s.riskRewardRatio < prefs.filters.minRiskReward) return false;
      if (prefs.filters.symbols?.length && !prefs.filters.symbols.includes(s.symbol)) return false;
      if (prefs.filters.signalTypes?.length && !prefs.filters.signalTypes.includes(s.type)) return false;
      return true;
    });

    for (const signal of relevant) {
      const signalId = `${userId}_${signal.symbol}_${signal.timeframe}_${signal.type}_${signal.timestamp}`;

      const deliveredVia: StoredAlert['deliveredVia'] = [];

      // In-app (just store)
      if (prefs.inApp.enabled) deliveredVia.push('in_app');

      // Email
      if (prefs.email.enabled) {
        const emailAddr = prefs.email.address || user.email;
        if (emailAddr) {
          const { subject, html, text } = buildSignalAlertEmail({
            symbol: signal.symbol,
            direction: signal.direction,
            entry: signal.entry,
            stopLoss: signal.stopLoss,
            takeProfit: signal.takeProfit,
            riskRewardRatio: signal.riskRewardRatio,
            reason: signal.reason,
            strength: signal.strength,
            appUrl,
          });
          const sent = await sendEmail({ to: emailAddr, subject, html, text });
          if (sent) {
            deliveredVia.push('email');
            emailsSent++;
          }
        }
      }

      // Telegram
      if (prefs.telegram.enabled && prefs.telegram.chatId) {
        const text = buildSignalTelegramMessage({
          signalId,
          symbol: signal.symbol,
          direction: signal.direction,
          entry: signal.entry,
          stopLoss: signal.stopLoss,
          takeProfit: signal.takeProfit,
          riskRewardRatio: signal.riskRewardRatio,
          reason: signal.reason,
          strength: signal.strength,
          appUrl,
        });
        const keyboard = buildSignalInlineKeyboard(signalId, appUrl);
        const result = await sendTelegramMessage({
          chatId: prefs.telegram.chatId,
          text: text as string,
          inlineKeyboard: keyboard,
        });
        if (result.ok) {
          deliveredVia.push('telegram');
          telegramsSent++;
        }
      }

      // Persist alert
      const expiresAt = new Date(Date.now() + 24 * 60 * 60 * 1000);
      try {
        await saveAlert({
          userId,
          signalId,
          signal,
          createdAt: new Date(),
          expiresAt,
          status: 'pending',
          deliveredVia,
        });
        alertsCreated++;
      } catch (err) {
        // Duplicate signalId (already alerted) - skip
        console.debug('[Dispatch] Duplicate signal', signalId);
      }
    }
  }

  return { alertsCreated, emailsSent, telegramsSent };
}
