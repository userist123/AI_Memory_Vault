/**
 * Telegram Bot API integration.
 * No dependencies - pure fetch.
 *
 * Flow:
 * 1. User creates bot with @BotFather, gets token, sets TELEGRAM_BOT_TOKEN env
 * 2. User links Telegram chat to account (via /start with deep-link code)
 * 3. We send alerts with inline buttons: Execute, Skip, Details
 * 4. Button callbacks hit /api/alerts/telegram-callback
 */

const TELEGRAM_API = 'https://api.telegram.org/bot';

function getBotToken(): string {
  const token = process.env.TELEGRAM_BOT_TOKEN;
  if (!token) throw new Error('TELEGRAM_BOT_TOKEN not set');
  return token;
}

export interface TelegramMessage {
  chatId: string | number;
  text: string;
  parseMode?: 'Markdown' | 'HTML';
  inlineKeyboard?: Array<Array<{ text: string; callback_data?: string; url?: string }>>;
}

export async function sendTelegramMessage(msg: TelegramMessage): Promise<{ ok: boolean; messageId?: number }> {
  try {
    const token = getBotToken();
    const body: Record<string, unknown> = {
      chat_id: msg.chatId,
      text: msg.text,
      parse_mode: msg.parseMode || 'HTML',
      disable_web_page_preview: true,
    };
    if (msg.inlineKeyboard) {
      body.reply_markup = { inline_keyboard: msg.inlineKeyboard };
    }

    const res = await fetch(`${TELEGRAM_API}${token}/sendMessage`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    });

    const data = (await res.json()) as { ok: boolean; result?: { message_id: number }; description?: string };
    if (!data.ok) {
      console.error('[Telegram] Send failed:', data.description);
      return { ok: false };
    }
    return { ok: true, messageId: data.result?.message_id };
  } catch (err) {
    console.error('[Telegram] Error:', err);
    return { ok: false };
  }
}

/**
 * Answer a callback query (remove loading spinner on the button)
 */
export async function answerTelegramCallback(
  callbackQueryId: string,
  text?: string,
  showAlert = false
): Promise<boolean> {
  try {
    const token = getBotToken();
    const res = await fetch(`${TELEGRAM_API}${token}/answerCallbackQuery`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        callback_query_id: callbackQueryId,
        text,
        show_alert: showAlert,
      }),
    });
    const data = await res.json();
    return (data as { ok: boolean }).ok;
  } catch {
    return false;
  }
}

/**
 * Edit a message after action (e.g. "EXECUTED ✓" replacing buttons)
 */
export async function editTelegramMessage(params: {
  chatId: string | number;
  messageId: number;
  text: string;
  parseMode?: 'Markdown' | 'HTML';
}): Promise<boolean> {
  try {
    const token = getBotToken();
    const res = await fetch(`${TELEGRAM_API}${token}/editMessageText`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        chat_id: params.chatId,
        message_id: params.messageId,
        text: params.text,
        parse_mode: params.parseMode || 'HTML',
      }),
    });
    const data = await res.json();
    return (data as { ok: boolean }).ok;
  } catch {
    return false;
  }
}

/**
 * Build signal alert message for Telegram with Execute/Skip buttons.
 */
export function buildSignalTelegramMessage(signal: {
  signalId: string;
  symbol: string;
  direction: 'long' | 'short';
  entry: number;
  stopLoss: number;
  takeProfit: number;
  riskRewardRatio: number;
  reason: string;
  strength: number;
  appUrl: string;
}): TelegramMessage['text'] | TelegramMessage {
  const emoji = signal.direction === 'long' ? '🟢' : '🔴';
  const direction = signal.direction === 'long' ? 'LONG' : 'SHORT';

  const text = `
${emoji} <b>Setup ${direction} pe ${signal.symbol}</b>

<i>${signal.reason}</i>

<b>Entry:</b> <code>${signal.entry.toFixed(4)}</code>
<b>Stop:</b> <code>${signal.stopLoss.toFixed(4)}</code>
<b>Target:</b> <code>${signal.takeProfit.toFixed(4)}</code>
<b>R/R:</b> 1 : ${signal.riskRewardRatio.toFixed(2)}
<b>Force:</b> ${signal.strength}/100

<i>Nu este sfat financiar.</i>
`.trim();

  return text;
}

export function buildSignalInlineKeyboard(signalId: string, appUrl: string) {
  return [
    [
      { text: '✅ Execută', callback_data: `exec:${signalId}` },
      { text: '⏭ Skip', callback_data: `skip:${signalId}` },
    ],
    [
      { text: '📊 Detalii', url: `${appUrl}/signals?id=${signalId}` },
    ],
  ];
}

/**
 * Set webhook for bot (call once during setup).
 */
export async function setTelegramWebhook(webhookUrl: string): Promise<boolean> {
  try {
    const token = getBotToken();
    const res = await fetch(`${TELEGRAM_API}${token}/setWebhook`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        url: webhookUrl,
        allowed_updates: ['message', 'callback_query'],
      }),
    });
    const data = await res.json();
    return (data as { ok: boolean }).ok;
  } catch {
    return false;
  }
}
