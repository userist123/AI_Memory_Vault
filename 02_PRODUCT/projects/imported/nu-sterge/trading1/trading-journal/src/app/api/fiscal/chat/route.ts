import { NextRequest, NextResponse } from 'next/server';
import { z } from 'zod';
import { getCurrentUser } from '@/lib/auth/session';
import { callLLM } from '@/lib/ai/llm';
import { getTradesByUser } from '@/lib/db/mongo';
import { computeFiscalReport } from '@/lib/fiscal/calculator';
import { consumeQuota, quotaExceededResponse } from '@/lib/billing/quota';

export const runtime = 'nodejs';
export const maxDuration = 30;

const ChatRequestSchema = z.object({
  message: z.string().min(1).max(2000),
  // Optional chat history for context
  history: z.array(z.object({
    role: z.enum(['user', 'assistant']),
    content: z.string(),
  })).max(10).default([]),
  year: z.number().min(2020).max(2030).optional(),
});

const SYSTEM_PROMPT_RO = `Ești expert contabil specializat în impozitare trading România. Răspunzi în română, direct și concis.

REGULI ABSOLUTE:
1. Te bazezi EXCLUSIV pe datele reale ale utilizatorului (furnizate în context) și pe legislația română 2025-2026.
2. NU inventezi cifre. Dacă nu ai date, spui "nu știu" sau "am nevoie de mai multe informații".
3. Mereu menționezi că recomanzi confirmare cu un expert contabil pentru decizii fiscale importante.
4. Folosești cifre EXACTE din datele utilizatorului.
5. Citezi legea când e relevant (ex: "conform art. 116 Cod Fiscal").

LEGISLAȚIE CHEIE 2025-2026:
- Crypto 2025: impozit 10% pe câștig net (preț vânzare - preț achiziție - costuri directe)
- Crypto 2026: impozit 16% (crescut de la 10%, conform Legea 239/2025)
- Acțiuni/ETF prin broker nerezident (IBKR, T212): 10% pe câștig net
- Acțiuni prin broker rezident RO (Tradeville, BT Capital): reținut la sursă (informativ)
- Scutire: câștig <200 RON/tranzacție ȘI total anual <600 RON
- Pierderile la crypto NU se deduc din câștiguri (regulă strictă)
- CASS 10% pe praguri: 6/12/24 salarii minime (24.300 / 48.600 / 97.200 RON pentru 2026)
- Declarația Unică (D212) se depune până la 25 mai
- Bonificație 3% dacă depui + plătești până la 15 aprilie
- Curs BNR la DATA TRANZACȚIEI (nu la data declarării)
- Salariu minim 2026: 4.050 lei H1, 4.325 lei H2
- Din 2026 se aplică DAC8 - platformele crypto raportează automat la ANAF

STIL:
- Conversațional, direct, fără formule de politețe excesive
- Răspunsuri SCURTE (max 3-4 paragrafe pentru întrebări simple)
- Cu exemple numerice concrete când user-ul are date
- Recomanzi acțiuni specifice, nu generalități`;

const SYSTEM_PROMPT_EN = `You are a Romanian tax expert specialized in trading taxation. Respond in English, direct and concise.

ABSOLUTE RULES:
1. Base answers EXCLUSIVELY on user's actual data (provided in context) and Romanian 2025-2026 legislation.
2. NEVER invent numbers. If missing data, say "I don't know" or ask for more info.
3. Always recommend confirmation with a certified accountant for important fiscal decisions.

KEY LEGISLATION 2025-2026:
- Crypto 2025: 10% tax on net gain
- Crypto 2026: 16% (increased per Law 239/2025)
- Stocks via non-resident broker: 10% on net gain
- Stocks via RO resident broker: withheld at source (informational)
- Exemption: gain <200 RON/trade AND annual total <600 RON
- Crypto losses NOT deductible from gains
- CASS 10% at 6/12/24 minimum wages thresholds
- D212 deadline: May 25
- 3% bonus if paid by April 15
- BNR rate at TRADE DATE

Keep responses SHORT (3-4 paragraphs max for simple questions), conversational, with concrete numerical examples.`;

export async function POST(req: NextRequest) {
  try {
    const user = await getCurrentUser();
    if (!user) return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });

    // Use tradeReview quota for fiscal chat (soft limit)
    const quota = await consumeQuota(user._id!, 'tradeReview');
    if (!quota.allowed) {
      const resp = quotaExceededResponse(quota, user.language);
      return NextResponse.json(resp.body, { status: resp.status });
    }

    const body = await req.json();
    const parsed = ChatRequestSchema.safeParse(body);
    if (!parsed.success) {
      return NextResponse.json({ error: 'Invalid', details: parsed.error.errors }, { status: 400 });
    }

    const { message, history, year } = parsed.data;
    const fiscalYear = year || new Date().getFullYear() - 1;

    // Build context from user's actual data
    const yearStart = new Date(`${fiscalYear}-01-01T00:00:00Z`);
    const trades = await getTradesByUser(user._id!, { limit: 10000, since: yearStart });

    let fiscalContext = '';
    if (trades.length > 0) {
      const report = await computeFiscalReport(user._id!, trades, fiscalYear);
      fiscalContext = `
DATELE UTILIZATORULUI PENTRU ANUL ${fiscalYear}:
- Total tranzacții închise: ${report.categories.crypto.tradeCount + report.categories.stocks_eu.tradeCount + report.categories.forex.tradeCount} (crypto: ${report.categories.crypto.tradeCount}, acțiuni UE: ${report.categories.stocks_eu.tradeCount}, forex: ${report.categories.forex.tradeCount})
- Câștiguri crypto: ${report.categories.crypto.grossGainsRon.toFixed(2)} RON
- Pierderi crypto: ${report.categories.crypto.grossLossesRon.toFixed(2)} RON
- Câștiguri acțiuni UE: ${report.categories.stocks_eu.grossGainsRon.toFixed(2)} RON
- Pierderi acțiuni UE: ${report.categories.stocks_eu.grossLossesRon.toFixed(2)} RON
- Venit declarabil total: ${report.netDeclarableIncomeRon.toFixed(2)} RON
- Impozit crypto datorat: ${report.cryptoTaxDue.toFixed(2)} RON
- Impozit câștiguri capital: ${report.capitalGainsTaxDue.toFixed(2)} RON
- CASS datorat (prag ${report.cassThresholdReached} salarii): ${report.cassDue.toFixed(2)} RON
- TOTAL DE PLATĂ: ${report.totalDueStandard.toFixed(2)} RON
- Plan fiscal: Pro (dacă include modul fiscal complet)`;
    } else {
      fiscalContext = `\nUTILIZATORUL NU ARE TRANZACȚII ÎN ${fiscalYear}.`;
    }

    const systemPrompt = (user.language === 'en' ? SYSTEM_PROMPT_EN : SYSTEM_PROMPT_RO) + fiscalContext;

    // Include history
    const historyText = history.map((m) => `${m.role === 'user' ? 'User' : 'Assistant'}: ${m.content}`).join('\n\n');
    const userPrompt = (historyText ? historyText + '\n\n' : '') + `User: ${message}\n\nAssistant:`;

    const llmResponse = await callLLM({
      systemPrompt,
      userPrompt,
      jsonMode: false,
      maxTokens: 600,
      temperature: 0.3,
    });

    return NextResponse.json({
      reply: llmResponse.content,
      provider: llmResponse.provider,
      model: llmResponse.model,
    });
  } catch (err: unknown) {
    const e = err as { message?: string };
    console.error('[Fiscal chat] Error:', e);
    return NextResponse.json({ error: 'Chat failed', details: e.message }, { status: 500 });
  }
}
