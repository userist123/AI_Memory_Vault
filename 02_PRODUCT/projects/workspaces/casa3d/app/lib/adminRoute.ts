import { NextResponse } from 'next/server';
import { assertAdmin } from './outbound';
import { HttpError } from './repo';
export async function admin<T>(req: Request, fn: () => Promise<T>){
  try { assertAdmin(req); return NextResponse.json(await fn()); }
  catch (e: any){ return NextResponse.json({ error: e instanceof HttpError ? e.message : 'Eroare internă.' }, { status: e instanceof HttpError ? e.status : 500 }); }
}
