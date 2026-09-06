/**
 * Email notifications via Resend.
 * Free tier: 3000 emails/month, 100/day.
 * https://resend.com/
 */

const RESEND_API_URL = 'https://api.resend.com/emails';

export interface EmailParams {
  to: string;
  subject: string;
  html: string;
  text?: string;
  from?: string; // default "Trading Journal <alerts@yourdomain.com>"
}

export async function sendEmail(params: EmailParams): Promise<boolean> {
  const apiKey = process.env.RESEND_API_KEY;
  if (!apiKey) {
    console.warn('[Email] RESEND_API_KEY not set, skipping');
    return false;
  }

  const fromAddress = params.from || process.env.RESEND_FROM_ADDRESS || 'onboarding@resend.dev';

  try {
    const res = await fetch(RESEND_API_URL, {
      method: 'POST',
      headers: {
        Authorization: `Bearer ${apiKey}`,
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({
        from: fromAddress,
        to: params.to,
        subject: params.subject,
        html: params.html,
        text: params.text,
      }),
    });

    if (!res.ok) {
      console.error('[Email] Resend error:', res.status, await res.text());
      return false;
    }
    return true;
  } catch (err) {
    console.error('[Email] Send failed:', err);
    return false;
  }
}

/**
 * Build signal alert email HTML.
 */
export function buildSignalAlertEmail(signal: {
  symbol: string;
  direction: 'long' | 'short';
  entry: number;
  stopLoss: number;
  takeProfit: number;
  riskRewardRatio: number;
  reason: string;
  strength: number;
  appUrl: string;
}): { subject: string; html: string; text: string } {
  const emoji = signal.direction === 'long' ? '🟢' : '🔴';
  const direction = signal.direction === 'long' ? 'LONG' : 'SHORT';

  const subject = `${emoji} Setup ${direction} pe ${signal.symbol}`;

  const html = `
    <!DOCTYPE html>
    <html>
    <head><meta charset="utf-8"></head>
    <body style="font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif; max-width: 600px; margin: 0 auto; padding: 20px; background: #0a0a0a; color: #fafafa;">
      <div style="background: #18181b; border-radius: 12px; padding: 24px; border: 1px solid #27272a;">
        <h1 style="margin: 0 0 8px 0; font-size: 24px;">${emoji} Setup ${direction} detectat</h1>
        <p style="color: #a1a1aa; margin: 0 0 20px 0;">Force: <strong style="color: ${signal.strength >= 70 ? '#22c55e' : '#eab308'}">${signal.strength}/100</strong></p>

        <div style="background: #27272a; border-radius: 8px; padding: 16px; margin-bottom: 16px;">
          <h2 style="margin: 0 0 8px 0; font-size: 32px; color: ${signal.direction === 'long' ? '#22c55e' : '#ef4444'}">${signal.symbol}</h2>
          <p style="margin: 0; color: #d4d4d8;">${signal.reason}</p>
        </div>

        <table style="width: 100%; border-collapse: collapse;">
          <tr>
            <td style="padding: 8px 0; color: #a1a1aa;">Entry</td>
            <td style="padding: 8px 0; text-align: right; font-family: monospace; font-weight: 600;">${signal.entry.toFixed(4)}</td>
          </tr>
          <tr>
            <td style="padding: 8px 0; color: #ef4444;">Stop Loss</td>
            <td style="padding: 8px 0; text-align: right; font-family: monospace; color: #ef4444;">${signal.stopLoss.toFixed(4)}</td>
          </tr>
          <tr>
            <td style="padding: 8px 0; color: #22c55e;">Take Profit</td>
            <td style="padding: 8px 0; text-align: right; font-family: monospace; color: #22c55e;">${signal.takeProfit.toFixed(4)}</td>
          </tr>
          <tr>
            <td style="padding: 8px 0; color: #a1a1aa;">Risk/Reward</td>
            <td style="padding: 8px 0; text-align: right; font-weight: 600;">1 : ${signal.riskRewardRatio.toFixed(2)}</td>
          </tr>
        </table>

        <a href="${signal.appUrl}/signals"
           style="display: block; background: #f97316; color: white; text-decoration: none; text-align: center; padding: 14px; border-radius: 8px; margin-top: 20px; font-weight: 600;">
          Deschide în app
        </a>

        <p style="color: #71717a; font-size: 12px; margin-top: 20px; text-align: center;">
          Acest semnal este generat automat pe baza indicatorilor tehnici.
          NU este sfat financiar. Decizia finală îți aparține.
        </p>
      </div>
    </body>
    </html>
  `;

  const text = `${emoji} Setup ${direction} pe ${signal.symbol}

${signal.reason}

Entry: ${signal.entry.toFixed(4)}
Stop Loss: ${signal.stopLoss.toFixed(4)}
Take Profit: ${signal.takeProfit.toFixed(4)}
Risk/Reward: 1:${signal.riskRewardRatio.toFixed(2)}
Force: ${signal.strength}/100

Deschide în app: ${signal.appUrl}/signals

Nu este sfat financiar.`;

  return { subject, html, text };
}
