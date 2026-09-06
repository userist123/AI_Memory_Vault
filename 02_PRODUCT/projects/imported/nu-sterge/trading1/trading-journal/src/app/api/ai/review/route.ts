import { NextRequest, NextResponse } from 'next/server';
import { callLLM } from '@/lib/ai/llm';
import { getTradeById, saveTradeReview, getTradeReview } from '@/lib/db/mongo';
import { getUserIdFromRequest } from '@/lib/auth/session';
import { consumeQuota, quotaExceededResponse } from '@/lib/billing/quota';
import { ReviewRequestSchema } from '@/types/ai-review';
import type { TradeReview } from '@/types/ai-review';

export const runtime = 'nodejs';
export const maxDuration = 30;

const SYSTEM_PROMPT_RO = `Ești un coach profesionist de trading care analizează tranzacții individuale pentru a ajuta traderii să devină mai buni.

ABORDARE:
- Ești direct, nu linguțești
- Identifici ce s-a făcut bine ȘI ce poate fi îmbunătățit
- Dai recomandări concrete, executabile
- Te bazezi EXCLUSIV pe datele furnizate, nu inventezi
- Răspunzi DOAR cu JSON valid

SCHEMA JSON:
{
  "grade": "A" | "B" | "C" | "D" | "F",
  "score": number 0-100,
  "strengths": string[] (în română),
  "weaknesses": string[] (în română),
  "entryQuality": "excellent" | "good" | "neutral" | "poor" | "bad",
  "exitQuality": "excellent" | "good" | "neutral" | "poor" | "bad",
  "riskManagement": "excellent" | "good" | "neutral" | "poor" | "bad",
  "flags": array din: "no_stop_loss", "oversized_position", "tight_stop", "wide_stop", "held_too_long", "cut_profits_early", "revenge_trade", "overtrading", "counter_trend", "chased_entry", "good_rr_ratio", "disciplined_exit", "strong_setup",
  "recommendations": string[] (în română, 2-4 items),
  "summary": string (2-3 propoziții în română)
}

EVALUARE:
- Grade A (90-100): Tranzacție exemplară cu plan clar, execuție disciplinată, risk management solid
- Grade B (75-89): Tranzacție solidă, mici aspecte de îmbunătățit
- Grade C (60-74): Tranzacție medie, câteva probleme vizibile
- Grade D (40-59): Probleme importante de disciplină sau risk management
- Grade F (0-39): Erori grave: fără SL, overtrading, revenge trade, risc excesiv`;

const SYSTEM_PROMPT_EN = `You are a professional trading coach analyzing individual trades to help traders improve.

APPROACH:
- Direct, no sugar-coating
- Identify both strengths AND weaknesses
- Give concrete, actionable recommendations
- Base conclusions EXCLUSIVELY on provided data, never invent
- Respond ONLY with valid JSON

JSON SCHEMA:
{
  "grade": "A" | "B" | "C" | "D" | "F",
  "score": number 0-100,
  "strengths": string[] (in English),
  "weaknesses": string[] (in English),
  "entryQuality": "excellent" | "good" | "neutral" | "poor" | "bad",
  "exitQuality": "excellent" | "good" | "neutral" | "poor" | "bad",
  "riskManagement": "excellent" | "good" | "neutral" | "poor" | "bad",
  "flags": array from: "no_stop_loss", "oversized_position", "tight_stop", "wide_stop", "held_too_long", "cut_profits_early", "revenge_trade", "overtrading", "counter_trend", "chased_entry", "good_rr_ratio", "disciplined_exit", "strong_setup",
  "recommendations": string[] (in English, 2-4 items),
  "summary": string (2-3 sentences in English)
}

GRADING:
- Grade A (90-100): Exemplary trade - clear plan, disciplined execution, solid risk management
- Grade B (75-89): Solid trade, minor improvements possible
- Grade C (60-74): Average trade, some visible issues
- Grade D (40-59): Important discipline or risk management problems
- Grade F (0-39): Serious errors: no SL, overtrading, revenge trade, excessive risk`;

export async function POST(req: NextRequest) {
  try {
    const userId = await getUserIdFromRequest(req);
    if (!userId) {
      return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });
    }

    const body = await req.json();
    const parsed = ReviewRequestSchema.safeParse(body);

    if (!parsed.success) {
      return NextResponse.json(
        { error: 'Invalid request', details: parsed.error.errors },
        { status: 400 }
      );
    }

    const { tradeId, language } = parsed.data;

    const cached = await getTradeReview(userId, tradeId);
    if (cached) {
      return NextResponse.json({ review: cached, cached: true });
    }

    // Only consume quota for fresh reviews (cached = free)
    const quota = await consumeQuota(userId, 'tradeReview');
    if (!quota.allowed) {
      const resp = quotaExceededResponse(quota, language);
      return NextResponse.json(resp.body, { status: resp.status });
    }

    const trade = await getTradeById(userId, tradeId);
    if (!trade) {
      return NextResponse.json({ error: 'Trade not found' }, { status: 404 });
    }

    const holdTimeMin =
      trade.entryTime && trade.exitTime
        ? (new Date(trade.exitTime).getTime() - new Date(trade.entryTime).getTime()) / 60000
        : null;

    const rr =
      trade.stopLoss && trade.takeProfit && trade.entryPrice
        ? Math.abs(trade.takeProfit - trade.entryPrice) / Math.abs(trade.entryPrice - trade.stopLoss)
        : null;

    const riskPct =
      trade.stopLoss && trade.entryPrice
        ? (Math.abs(trade.entryPrice - trade.stopLoss) / trade.entryPrice) * 100
        : null;

    const tradeData = {
      symbol: trade.symbol,
      assetClass: trade.assetClass,
      direction: trade.direction,
      status: trade.status,
      entryPrice: trade.entryPrice,
      exitPrice: trade.exitPrice,
      stopLoss: trade.stopLoss,
      takeProfit: trade.takeProfit,
      hasStopLoss: !!trade.stopLoss,
      hasTakeProfit: !!trade.takeProfit,
      quantity: trade.quantity,
      entryTime: trade.entryTime,
      exitTime: trade.exitTime,
      holdTimeMinutes: holdTimeMin,
      pnl: trade.pnl,
      pnlPercent: trade.pnlPercent,
      commission: trade.commission,
      swap: trade.swap,
      currency: trade.currency,
      riskRewardRatio: rr,
      riskPercent: riskPct,
      rMultiple: trade.rMultiple,
      strategy: trade.strategy,
      notes: trade.notes,
    };

    const systemPrompt = language === 'ro' ? SYSTEM_PROMPT_RO : SYSTEM_PROMPT_EN;
    const userPrompt =
      language === 'ro'
        ? `Analizează această tranzacție și returnează evaluarea structurată ca JSON:\n\n${JSON.stringify(tradeData, null, 2)}`
        : `Analyze this trade and return structured evaluation as JSON:\n\n${JSON.stringify(tradeData, null, 2)}`;

    const llmResponse = await callLLM({
      systemPrompt,
      userPrompt,
      jsonMode: true,
      maxTokens: 1000,
      temperature: 0.3,
    });

    let reviewData;
    try {
      reviewData = JSON.parse(llmResponse.content);
    } catch {
      return NextResponse.json(
        { error: 'AI returned invalid JSON', raw: llmResponse.content.slice(0, 500) },
        { status: 502 }
      );
    }

    const review: TradeReview = {
      tradeId,
      userId,
      language,
      createdAt: new Date(),
      grade: ['A', 'B', 'C', 'D', 'F'].includes(reviewData.grade) ? reviewData.grade : 'C',
      score: typeof reviewData.score === 'number' ? Math.max(0, Math.min(100, reviewData.score)) : 50,
      strengths: Array.isArray(reviewData.strengths)
        ? reviewData.strengths.filter((s: unknown) => typeof s === 'string').slice(0, 6)
        : [],
      weaknesses: Array.isArray(reviewData.weaknesses)
        ? reviewData.weaknesses.filter((s: unknown) => typeof s === 'string').slice(0, 6)
        : [],
      entryQuality: ['excellent', 'good', 'neutral', 'poor', 'bad'].includes(reviewData.entryQuality)
        ? reviewData.entryQuality
        : 'neutral',
      exitQuality: ['excellent', 'good', 'neutral', 'poor', 'bad'].includes(reviewData.exitQuality)
        ? reviewData.exitQuality
        : 'neutral',
      riskManagement: ['excellent', 'good', 'neutral', 'poor', 'bad'].includes(reviewData.riskManagement)
        ? reviewData.riskManagement
        : 'neutral',
      flags: Array.isArray(reviewData.flags)
        ? reviewData.flags.filter((f: unknown) =>
            typeof f === 'string' &&
            [
              'no_stop_loss', 'oversized_position', 'tight_stop', 'wide_stop',
              'held_too_long', 'cut_profits_early', 'revenge_trade', 'overtrading',
              'counter_trend', 'chased_entry', 'good_rr_ratio', 'disciplined_exit', 'strong_setup',
            ].includes(f)
          )
        : [],
      recommendations: Array.isArray(reviewData.recommendations)
        ? reviewData.recommendations.filter((r: unknown) => typeof r === 'string').slice(0, 5)
        : [],
      summary: typeof reviewData.summary === 'string' ? reviewData.summary : '',
      provider: llmResponse.provider,
      model: llmResponse.model,
    };

    await saveTradeReview(review);

    return NextResponse.json({ review, cached: false });
  } catch (err: unknown) {
    const e = err as { message?: string };
    console.error('[Review] Error:', e);
    return NextResponse.json({ error: 'Review failed', details: e.message }, { status: 500 });
  }
}

export async function GET(req: NextRequest) {
  const userId = await getUserIdFromRequest(req);
  if (!userId) {
    return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });
  }

  const { searchParams } = new URL(req.url);
  const tradeId = searchParams.get('tradeId');

  if (!tradeId) {
    return NextResponse.json({ error: 'tradeId required' }, { status: 400 });
  }

  const review = await getTradeReview(userId, tradeId);
  return NextResponse.json({ review });
}
