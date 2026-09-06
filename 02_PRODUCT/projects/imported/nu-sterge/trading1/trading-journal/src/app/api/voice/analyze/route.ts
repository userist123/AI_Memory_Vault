import { NextRequest, NextResponse } from 'next/server';
import { callLLM } from '@/lib/ai/llm';
import { AnalyzeRequestSchema, JournalExtractionSchema } from '@/types/journal';
import { getUserIdFromRequest } from '@/lib/auth/session';

export const runtime = 'nodejs';
export const maxDuration = 30;

const SYSTEM_PROMPT_RO = `Ești un asistent AI specializat în analiza jurnalelor de trading.
Analizezi transcrierea unui jurnal vocal al unui trader și extragi informații structurate.

REGULI ABSOLUTE:
- Răspunzi DOAR cu JSON valid, conform schemei de mai jos
- NU inventa informații care nu sunt în transcript
- Dacă o informație lipsește, folosește null (pentru string-uri) sau [] (pentru liste)
- Folosește DOAR valorile enumerate pentru emoții și greșeli
- Câmpul "lesson" și "summary" le scrii în LIMBA ROMÂNĂ
- confidence între 0 și 1: 1 = foarte sigur, 0 = ghicesc

EMOȚII POSIBILE (în engleză în schema): confident, fearful, greedy, calm, frustrated, excited, patient, impulsive, disciplined, tilted

GREȘELI POSIBILE (în engleză în schema): revengeTrade, oversized, noStopLoss, movedStopLoss, fomo, overTrading, againstTrend, ignoredPlan, tookProfitsEarly, heldTooLong

DIRECȚIE: "long" sau "short" sau null

SCHEMA JSON:
{
  "instrument": string | null,
  "direction": "long" | "short" | null,
  "setup": string | null,
  "emotions": string[],
  "mistakes": string[],
  "lesson": string | null,
  "rMultipleEstimate": number | null,
  "confidence": number,
  "summary": string
}`;

const SYSTEM_PROMPT_EN = `You are an AI assistant specialized in analyzing trading journals.
Analyze the trader's voice journal transcript and extract structured information.

ABSOLUTE RULES:
- Respond ONLY with valid JSON matching the schema below
- DO NOT invent information not in the transcript
- If information is missing, use null (for strings) or [] (for lists)
- Use ONLY the enumerated values for emotions and mistakes
- "lesson" and "summary" fields should be in ENGLISH
- confidence between 0 and 1: 1 = very confident, 0 = guessing

POSSIBLE EMOTIONS: confident, fearful, greedy, calm, frustrated, excited, patient, impulsive, disciplined, tilted

POSSIBLE MISTAKES: revengeTrade, oversized, noStopLoss, movedStopLoss, fomo, overTrading, againstTrend, ignoredPlan, tookProfitsEarly, heldTooLong

DIRECTION: "long" or "short" or null

JSON SCHEMA:
{
  "instrument": string | null,
  "direction": "long" | "short" | null,
  "setup": string | null,
  "emotions": string[],
  "mistakes": string[],
  "lesson": string | null,
  "rMultipleEstimate": number | null,
  "confidence": number,
  "summary": string
}`;

export async function POST(req: NextRequest) {
  try {
    const userId = await getUserIdFromRequest(req);
    if (!userId) {
      return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });
    }

    const body = await req.json();
    const parsed = AnalyzeRequestSchema.safeParse(body);

    if (!parsed.success) {
      return NextResponse.json(
        { error: 'Invalid request', details: parsed.error.errors },
        { status: 400 }
      );
    }

    const { transcript, language } = parsed.data;

    const systemPrompt = language === 'ro' ? SYSTEM_PROMPT_RO : SYSTEM_PROMPT_EN;
    const userPrompt = language === 'ro'
      ? `Analizează această transcriere și extrage datele structurate:\n\n"""${transcript}"""`
      : `Analyze this transcript and extract structured data:\n\n"""${transcript}"""`;

    const llmResponse = await callLLM({
      systemPrompt,
      userPrompt,
      jsonMode: true,
      maxTokens: 800,
      temperature: 0.2,
    });

    let extraction;
    try {
      const raw = JSON.parse(llmResponse.content);
      const validated = JournalExtractionSchema.safeParse(raw);

      if (!validated.success) {
        extraction = {
          instrument: typeof raw.instrument === 'string' ? raw.instrument : null,
          direction: ['long', 'short'].includes(raw.direction) ? raw.direction : null,
          setup: typeof raw.setup === 'string' ? raw.setup : null,
          emotions: Array.isArray(raw.emotions) ? raw.emotions.filter((e: unknown) =>
            typeof e === 'string' &&
            ['confident', 'fearful', 'greedy', 'calm', 'frustrated', 'excited', 'patient', 'impulsive', 'disciplined', 'tilted'].includes(e)
          ) : [],
          mistakes: Array.isArray(raw.mistakes) ? raw.mistakes.filter((m: unknown) =>
            typeof m === 'string' &&
            ['revengeTrade', 'oversized', 'noStopLoss', 'movedStopLoss', 'fomo', 'overTrading', 'againstTrend', 'ignoredPlan', 'tookProfitsEarly', 'heldTooLong'].includes(m)
          ) : [],
          lesson: typeof raw.lesson === 'string' ? raw.lesson : null,
          rMultipleEstimate: typeof raw.rMultipleEstimate === 'number' ? raw.rMultipleEstimate : null,
          confidence: typeof raw.confidence === 'number' ? Math.max(0, Math.min(1, raw.confidence)) : 0.5,
          summary: typeof raw.summary === 'string' ? raw.summary : 'No summary available',
        };
      } else {
        extraction = validated.data;
      }
    } catch (parseError) {
      console.error('[Analyze] JSON parse error:', parseError);
      return NextResponse.json(
        { error: 'AI returned invalid response', raw: llmResponse.content.slice(0, 500) },
        { status: 502 }
      );
    }

    return NextResponse.json({
      extraction,
      provider: llmResponse.provider,
      model: llmResponse.model,
    });
  } catch (error: unknown) {
    const err = error as { message?: string };
    console.error('[Analyze] Error:', err);
    return NextResponse.json(
      { error: 'Analysis failed', details: err.message },
      { status: 500 }
    );
  }
}
