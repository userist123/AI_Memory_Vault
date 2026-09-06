import { NextRequest, NextResponse } from 'next/server';
import { callLLM } from '@/lib/ai/llm';
import { analyzeTrades } from '@/lib/ai/pattern-detector';
import {
  getTradesByUser,
  saveCoachReport,
  getLatestCoachReport,
} from '@/lib/db/mongo';
import { getUserIdFromRequest } from '@/lib/auth/session';
import { consumeQuota, quotaExceededResponse } from '@/lib/billing/quota';
import { CoachRequestSchema } from '@/types/ai-review';
import type { CoachReport } from '@/types/ai-review';

export const runtime = 'nodejs';
export const maxDuration = 60;

const SYSTEM_PROMPT_RO = `Ești un coach senior de trading. Analizezi performanța unui trader pe ultima perioadă și livrezi un raport complet cu plan de acțiune.

PRINCIPII:
- Ești direct și onest. Nu linguțești.
- Te bazezi EXCLUSIV pe datele și pattern-urile furnizate — nu inventezi.
- Dai sfaturi concrete, nu generalități.
- Răspunzi DOAR cu JSON valid.

SCHEMA JSON:
{
  "grade": "A" | "B" | "C" | "D" | "F",
  "momentum": "improving" | "stable" | "declining",
  "patterns": [{
    "type": "revenge_trading" | "overtrading" | "position_sizing_drift" | "time_of_day_edge" | "symbol_bias" | "direction_bias" | "tilt_detected" | "risk_management_slip" | "profitability_by_setup" | "consistency",
    "severity": 1-5,
    "description": "scurtă descriere în română",
    "evidence": "date concrete în română"
  }],
  "strengths": ["punct forte 1 în română", ...] (2-5 items),
  "weaknesses": ["punct slab 1 în română", ...] (2-5 items),
  "actionPlan": [{
    "priority": "critical" | "high" | "medium" | "low",
    "action": "acțiune concretă în română",
    "rationale": "de ce e importantă în română"
  }] (3-6 items),
  "summary": "2-4 propoziții în română",
  "headline": "o singură propoziție sintetică în română"
}

EVALUARE GRADE:
- A: Profit consistent, disciplină solidă, fără erori majore
- B: Profitabil, dar cu loc de îmbunătățit
- C: Break-even sau profit mic, probleme vizibile
- D: Pierderi, greșeli repetate de risk management
- F: Pierderi grave, revenge trading, overtrading, lipsă totală de disciplină`;

const SYSTEM_PROMPT_EN = `You are a senior trading coach. Analyze a trader's performance over the period and deliver a complete report with action plan.

PRINCIPLES:
- Direct and honest. No sugar-coating.
- Base conclusions EXCLUSIVELY on provided data and patterns — never invent.
- Give concrete advice, not generalities.
- Respond ONLY with valid JSON.

JSON SCHEMA:
{
  "grade": "A" | "B" | "C" | "D" | "F",
  "momentum": "improving" | "stable" | "declining",
  "patterns": [{
    "type": "revenge_trading" | "overtrading" | "position_sizing_drift" | "time_of_day_edge" | "symbol_bias" | "direction_bias" | "tilt_detected" | "risk_management_slip" | "profitability_by_setup" | "consistency",
    "severity": 1-5,
    "description": "brief description in English",
    "evidence": "concrete data in English"
  }],
  "strengths": ["strength 1", ...] (2-5 items),
  "weaknesses": ["weakness 1", ...] (2-5 items),
  "actionPlan": [{
    "priority": "critical" | "high" | "medium" | "low",
    "action": "concrete action in English",
    "rationale": "why it matters in English"
  }] (3-6 items),
  "summary": "2-4 sentences in English",
  "headline": "single synthetic headline in English"
}

GRADING:
- A: Consistent profit, solid discipline, no major errors
- B: Profitable but room to improve
- C: Break-even or small profit, visible issues
- D: Losses, repeated risk management mistakes
- F: Heavy losses, revenge trading, overtrading, no discipline`;

export async function POST(req: NextRequest) {
  try {
    const userId = await getUserIdFromRequest(req);
    if (!userId) {
      return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });
    }

    const body = await req.json().catch(() => ({}));
    const parsed = CoachRequestSchema.safeParse(body);

    if (!parsed.success) {
      return NextResponse.json(
        { error: 'Invalid request', details: parsed.error.errors },
        { status: 400 }
      );
    }

    const { language, periodDays } = parsed.data;

    // Enforce quota (Free: 4/month, Pro: 30/month, Elite: unlimited)
    const quota = await consumeQuota(userId, 'coachReport');
    if (!quota.allowed) {
      const resp = quotaExceededResponse(quota, language);
      return NextResponse.json(resp.body, { status: resp.status });
    }

    const since = new Date();
    since.setDate(since.getDate() - periodDays);

    const trades = await getTradesByUser(userId, {
      limit: 1000,
      since,
    });

    if (trades.length === 0) {
      return NextResponse.json(
        {
          error: 'no_trades',
          message:
            language === 'ro'
              ? `Nu ai tranzacții în ultimele ${periodDays} zile. Importă din broker pentru a primi analiză.`
              : `No trades in the last ${periodDays} days. Import from your broker to get analysis.`,
        },
        { status: 200 }
      );
    }

    const analysis = analyzeTrades(trades);

    const llmInput = {
      periodDays,
      metrics: analysis.metrics,
      detectedPatterns: analysis.patterns,
      timeOfDay: Object.entries(analysis.timeOfDayPerformance)
        .filter(([, v]) => v.count >= 2)
        .sort((a, b) => b[1].pnl - a[1].pnl)
        .slice(0, 5),
      topSymbols: Object.entries(analysis.symbolPerformance)
        .sort((a, b) => b[1].count - a[1].count)
        .slice(0, 5),
      direction: analysis.directionPerformance,
    };

    const systemPrompt = language === 'ro' ? SYSTEM_PROMPT_RO : SYSTEM_PROMPT_EN;
    const userPrompt =
      language === 'ro'
        ? `Analizează performanța acestui trader pe ultimele ${periodDays} zile și livrează raportul complet:\n\n${JSON.stringify(llmInput, null, 2)}`
        : `Analyze this trader's performance over the last ${periodDays} days and deliver the complete report:\n\n${JSON.stringify(llmInput, null, 2)}`;

    const llmResponse = await callLLM({
      systemPrompt,
      userPrompt,
      jsonMode: true,
      maxTokens: 1800,
      temperature: 0.4,
    });

    let reportData;
    try {
      reportData = JSON.parse(llmResponse.content);
    } catch {
      return NextResponse.json(
        { error: 'AI returned invalid JSON', raw: llmResponse.content.slice(0, 500) },
        { status: 502 }
      );
    }

    const now = new Date();
    const report: CoachReport = {
      userId,
      language,
      createdAt: now,
      periodStart: since,
      periodEnd: now,
      periodType: periodDays <= 1 ? 'day' : periodDays <= 7 ? 'week' : 'month',
      stats: {
        totalTrades: analysis.metrics.totalTrades,
        closedTrades: analysis.metrics.closedTrades,
        winRate: analysis.metrics.winRate,
        totalPnL: analysis.metrics.totalPnL,
        profitFactor: isFinite(analysis.metrics.profitFactor) ? analysis.metrics.profitFactor : 0,
        avgWin: analysis.metrics.avgWin,
        avgLoss: analysis.metrics.avgLoss,
        maxDrawdown: analysis.metrics.maxDrawdown,
        avgRMultiple: analysis.metrics.avgRMultiple,
        bestTrade: analysis.metrics.bestTrade,
        worstTrade: analysis.metrics.worstTrade,
      },
      grade: ['A', 'B', 'C', 'D', 'F'].includes(reportData.grade) ? reportData.grade : 'C',
      momentum: ['improving', 'stable', 'declining'].includes(reportData.momentum)
        ? reportData.momentum : 'stable',
      patterns: Array.isArray(reportData.patterns)
        ? reportData.patterns.slice(0, 10).map((p: unknown) => {
            const pat = p as { type?: string; severity?: number; description?: string; evidence?: string };
            const validTypes = [
              'revenge_trading', 'overtrading', 'position_sizing_drift',
              'time_of_day_edge', 'symbol_bias', 'direction_bias',
              'tilt_detected', 'risk_management_slip', 'profitability_by_setup', 'consistency',
            ];
            return {
              type: validTypes.includes(pat.type ?? '') ? (pat.type as CoachReport['patterns'][0]['type']) : 'consistency',
              severity: Math.max(1, Math.min(5, Math.round(pat.severity ?? 1))),
              description: pat.description ?? '',
              evidence: pat.evidence ?? '',
            };
          })
        : [],
      strengths: Array.isArray(reportData.strengths)
        ? reportData.strengths.filter((s: unknown) => typeof s === 'string').slice(0, 6)
        : [],
      weaknesses: Array.isArray(reportData.weaknesses)
        ? reportData.weaknesses.filter((s: unknown) => typeof s === 'string').slice(0, 6)
        : [],
      actionPlan: Array.isArray(reportData.actionPlan)
        ? reportData.actionPlan.slice(0, 6).map((a: unknown) => {
            const action = a as { priority?: string; action?: string; rationale?: string };
            return {
              priority: ['critical', 'high', 'medium', 'low'].includes(action.priority ?? '')
                ? (action.priority as 'critical' | 'high' | 'medium' | 'low') : 'medium',
              action: action.action ?? '',
              rationale: action.rationale ?? '',
            };
          })
        : [],
      summary: typeof reportData.summary === 'string' ? reportData.summary : '',
      headline: typeof reportData.headline === 'string' ? reportData.headline : '',
      provider: llmResponse.provider,
      model: llmResponse.model,
    };

    const id = await saveCoachReport(report);

    return NextResponse.json({ report: { ...report, _id: id } });
  } catch (err: unknown) {
    const e = err as { message?: string };
    console.error('[Coach] Error:', e);
    return NextResponse.json({ error: 'Coach report failed', details: e.message }, { status: 500 });
  }
}

export async function GET(req: NextRequest) {
  const userId = await getUserIdFromRequest(req);
  if (!userId) {
    return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });
  }
  const report = await getLatestCoachReport(userId);
  return NextResponse.json({ report });
}
