import { NextResponse } from 'next/server';
import { getDb } from '@/lib/db';
import { isDemo } from '@/lib/demo';
export const dynamic = 'force-dynamic';
export async function GET(){ const { mode } = await getDb(); return NextResponse.json({ db: mode, persistent: mode !== 'pglite-memory', demo: isDemo() }); }
