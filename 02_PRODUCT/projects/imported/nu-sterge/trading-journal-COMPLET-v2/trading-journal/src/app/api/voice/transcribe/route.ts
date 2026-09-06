import { NextRequest, NextResponse } from 'next/server';
import { groq, GROQ_MODELS, isGroqAvailable } from '@/lib/ai/groq';
import { getUserIdFromRequest } from '@/lib/auth/session';
import { consumeQuota, quotaExceededResponse } from '@/lib/billing/quota';

export const runtime = 'nodejs';
export const maxDuration = 60;

export async function POST(req: NextRequest) {
  try {
    const userId = await getUserIdFromRequest(req);
    if (!userId) {
      return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });
    }

    // Enforce quota (Free: 3/day, Pro/Elite: unlimited)
    const quota = await consumeQuota(userId, 'voiceJournal');
    if (!quota.allowed) {
      const resp = quotaExceededResponse(quota);
      return NextResponse.json(resp.body, { status: resp.status });
    }

    if (!isGroqAvailable() || !groq) {
      return NextResponse.json(
        {
          error: 'Transcription service not configured',
          hint: 'Set GROQ_API_KEY in .env.local (get free key at console.groq.com)',
        },
        { status: 503 }
      );
    }

    const formData = await req.formData();
    const audioFile = formData.get('audio') as File | null;
    const language = (formData.get('language') as string) || 'ro';

    if (!audioFile) {
      return NextResponse.json({ error: 'No audio file provided' }, { status: 400 });
    }

    const MAX_SIZE = 25 * 1024 * 1024;
    if (audioFile.size > MAX_SIZE) {
      return NextResponse.json(
        {
          error: 'Audio file too large',
          maxSize: '25 MB',
          actualSize: `${(audioFile.size / 1024 / 1024).toFixed(2)} MB`,
        },
        { status: 413 }
      );
    }

    if (!['ro', 'en'].includes(language)) {
      return NextResponse.json(
        { error: 'Unsupported language. Use "ro" or "en"' },
        { status: 400 }
      );
    }

    const startTime = Date.now();
    const transcription = await groq.audio.transcriptions.create({
      file: audioFile,
      model: GROQ_MODELS.WHISPER,
      language,
      response_format: 'verbose_json',
      temperature: 0.0,
    });

    const durationMs = Date.now() - startTime;

    return NextResponse.json({
      transcript: transcription.text,
      language: transcription.language || language,
      duration: (transcription as unknown as { duration?: number }).duration || null,
      processingTimeMs: durationMs,
      segments: (transcription as unknown as { segments?: unknown[] }).segments || [],
    });
  } catch (error: unknown) {
    const err = error as { status?: number; message?: string };
    console.error('[Transcribe] Error:', err);

    if (err.status === 429) {
      return NextResponse.json(
        {
          error: 'Rate limit exceeded. Free tier: 20 req/min, 2000 req/day.',
          retryAfter: 60,
        },
        { status: 429 }
      );
    }

    return NextResponse.json(
      { error: 'Transcription failed', details: err.message || 'Unknown error' },
      { status: 500 }
    );
  }
}
