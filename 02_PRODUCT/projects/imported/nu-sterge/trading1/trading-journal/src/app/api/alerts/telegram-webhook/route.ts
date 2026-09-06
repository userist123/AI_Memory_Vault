import { NextRequest, NextResponse } from 'next/server';
import {
  answerTelegramCallback,
  editTelegramMessage,
  sendTelegramMessage,
} from '@/lib/notifications/telegram';
import { getAlert, updateAlertStatus, updateNotificationPrefs, findUserByTelegramChatId } from '@/lib/db/alerts';
import { executeSignal } from '@/lib/signals/execution';
import { getUserById } from '@/lib/db/users';
import { verifyJWT } from '@/lib/auth/jwt';

export const runtime = 'nodejs';

/**
 * Telegram webhook receiver.
 *
 * Handles:
 * - /start <linkToken>  - link user account to this Telegram chat
 * - Callback: exec:<signalId>  - execute signal
 * - Callback: skip:<signalId>  - skip signal
 */
export async function POST(req: NextRequest) {
  try {
    const update = await req.json();
    // Optional: verify Telegram secret header if set
    const secret = process.env.TELEGRAM_WEBHOOK_SECRET;
    if (secret) {
      const provided = req.headers.get('x-telegram-bot-api-secret-token');
      if (provided !== secret) {
        return NextResponse.json({ error: 'unauthorized' }, { status: 401 });
      }
    }

    // Handle /start command for account linking
    if (update.message?.text?.startsWith('/start')) {
      const chatId = update.message.chat.id;
      const parts = update.message.text.split(' ');
      const linkToken = parts[1];

      if (!linkToken) {
        await sendTelegramMessage({
          chatId,
          text: `👋 Salut! Pentru a-ți conecta contul, deschide <a href="${process.env.NEXT_PUBLIC_APP_URL}/settings/notifications">Setări → Notificări</a> în app și click 'Conectează Telegram'.`,
        });
        return NextResponse.json({ ok: true });
      }

      // Link token is a short-lived JWT with userId
      const payload = await verifyJWT(linkToken);
      if (!payload?.sub) {
        await sendTelegramMessage({
          chatId,
          text: '❌ Token expirat sau invalid. Generează unul nou în app.',
        });
        return NextResponse.json({ ok: true });
      }

      const user = await getUserById(payload.sub);
      if (!user) {
        await sendTelegramMessage({ chatId, text: '❌ Utilizator invalid.' });
        return NextResponse.json({ ok: true });
      }

      await updateNotificationPrefs(payload.sub, {
        telegram: {
          enabled: true,
          chatId: String(chatId),
          linkedAt: new Date(),
        },
      });

      await sendTelegramMessage({
        chatId,
        text: `✅ Contul <b>${user.email}</b> e conectat!\n\nAcum vei primi semnale de trading aici. Poți executa sau skippa direct din chat.`,
      });
      return NextResponse.json({ ok: true });
    }

    // Handle inline button callbacks
    if (update.callback_query) {
      const cb = update.callback_query;
      const chatId = cb.message.chat.id;
      const messageId = cb.message.message_id;
      const data: string = cb.data;

      const userId = await findUserByTelegramChatId(String(chatId));
      if (!userId) {
        await answerTelegramCallback(cb.id, 'Cont neconectat', true);
        return NextResponse.json({ ok: true });
      }

      if (data.startsWith('exec:')) {
        const signalId = data.slice(5);
        const alert = await getAlert(signalId);
        if (!alert || alert.userId !== userId) {
          await answerTelegramCallback(cb.id, '❌ Semnal indisponibil', true);
          return NextResponse.json({ ok: true });
        }
        if (alert.status !== 'pending') {
          await answerTelegramCallback(cb.id, `Semnal deja ${alert.status}`, true);
          return NextResponse.json({ ok: true });
        }

        await answerTelegramCallback(cb.id, '⏳ Se execută...');

        const result = await executeSignal({
          userId,
          signalId,
          signal: alert.signal,
          brokerId: 'binance',
          testnet: true, // Safe default for Telegram execution
          reason: 'Executat din Telegram',
        });

        if (!result.success) {
          await editTelegramMessage({
            chatId,
            messageId,
            text: `❌ <b>${alert.signal.symbol}</b> - eșuat\n\n${result.error}`,
          });
        } else {
          await editTelegramMessage({
            chatId,
            messageId,
            text: `✅ <b>${alert.signal.symbol}</b> - executat\n\nOrder ID: <code>${result.order?.brokerOrderId}</code>\nQty: ${result.order?.filledQuantity}\nAvg price: ${result.order?.avgFillPrice?.toFixed(4)}`,
          });
        }
        return NextResponse.json({ ok: true });
      }

      if (data.startsWith('skip:')) {
        const signalId = data.slice(5);
        await updateAlertStatus(signalId, 'skipped', { skipReason: 'Skipped via Telegram' });
        await answerTelegramCallback(cb.id, '⏭ Skippat');
        await editTelegramMessage({
          chatId,
          messageId,
          text: cb.message.text + '\n\n⏭ <i>Skippat</i>',
        });
        return NextResponse.json({ ok: true });
      }
    }

    return NextResponse.json({ ok: true });
  } catch (err: unknown) {
    const e = err as { message?: string };
    console.error('[Telegram webhook] Error:', e);
    return NextResponse.json({ ok: true }); // Always 200 to Telegram
  }
}
