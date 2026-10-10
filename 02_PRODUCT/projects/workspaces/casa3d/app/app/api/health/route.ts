import { NextResponse } from 'next/server';
import { getDb } from '@/lib/db';
export const dynamic = 'force-dynamic';
export async function GET(){ const { mode } = await getDb(); return NextResponse.json({ db: mode, persistent: mode !== 'pglite-memory' }); }
