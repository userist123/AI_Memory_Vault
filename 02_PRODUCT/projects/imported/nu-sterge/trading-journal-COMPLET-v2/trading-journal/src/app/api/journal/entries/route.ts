import { NextRequest, NextResponse } from 'next/server';
import { saveJournalEntry, getJournalEntriesByUser } from '@/lib/db/mongo';
import { getUserIdFromRequest } from '@/lib/auth/session';
import { z } from 'zod';

export const runtime = 'nodejs';

const CreateEntrySchema = z.object({
  transcript: z.string().min(1),
  language: z.enum(['ro', 'en']),
  extraction: z.any().nullable().optional(),
  audioDurationSec: z.number().nullable().optional(),
  tags: z.array(z.string()).default([]),
  notes: z.string().nullable().optional(),
});

export async function POST(req: NextRequest) {
  try {
    const userId = await getUserIdFromRequest(req);
    if (!userId) {
      return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });
    }

    const body = await req.json();
    const parsed = CreateEntrySchema.safeParse(body);

    if (!parsed.success) {
      return NextResponse.json(
        { error: 'Invalid request', details: parsed.error.errors },
        { status: 400 }
      );
    }

    const now = new Date();
    const entry = {
      userId,
      tradeId: null,
      createdAt: now,
      updatedAt: now,
      audioUrl: null,
      audioDurationSec: parsed.data.audioDurationSec ?? null,
      language: parsed.data.language,
      transcript: parsed.data.transcript,
      transcriptConfidence: null,
      extraction: parsed.data.extraction ?? null,
      tags: parsed.data.tags,
      notes: parsed.data.notes ?? null,
    };

    const id = await saveJournalEntry(entry);

    return NextResponse.json({ id, entry: { ...entry, _id: id } }, { status: 201 });
  } catch (error: unknown) {
    const err = error as { message?: string };
    console.error('[Journal] Save error:', err);
    return NextResponse.json(
      { error: 'Failed to save entry', details: err.message },
      { status: 500 }
    );
  }
}

export async function GET(req: NextRequest) {
  try {
    const userId = await getUserIdFromRequest(req);
    if (!userId) {
      return NextResponse.json({ error: 'Unauthorized' }, { status: 401 });
    }

    const entries = await getJournalEntriesByUser(userId, 50);
    return NextResponse.json({ entries });
  } catch (error: unknown) {
    const err = error as { message?: string };
    console.error('[Journal] List error:', err);
    return NextResponse.json(
      { error: 'Failed to list entries', details: err.message },
      { status: 500 }
    );
  }
}
