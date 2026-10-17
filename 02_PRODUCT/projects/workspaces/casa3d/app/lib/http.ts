import { NextResponse } from 'next/server';
import { HttpError } from './repo';
import { ownerId } from './owner';
export async function handle<T>(fn: (owner: string) => Promise<T>, status = 200){
  try { const owner = (await ownerId(true))!; return NextResponse.json(await fn(owner), { status }); }
  catch (e: any){ if (e instanceof HttpError) return NextResponse.json({ error: e.message, details: e.details ?? null }, { status: e.status });
    console.error(e); return NextResponse.json({ error: 'Eroare internă.' }, { status: 500 }); }
}
export async function body(req: Request){ try { return await req.json(); } catch { throw new HttpError(400, 'Cerere invalidă.'); } }
