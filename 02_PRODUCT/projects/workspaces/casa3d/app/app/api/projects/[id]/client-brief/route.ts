import { handle, body } from '@/lib/http';
import * as cb from '@/lib/client-brief';
import { HttpError } from '@/lib/repo';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ id: string }> };
// Chestionarul clientului, pentru designer (doar proprietarul proiectului): starea linkului, răspunsurile și pozele
export const GET = async (_: Request, { params }: Ctx) => { const { id } = await params; return handle(o => cb.getClientBrief(o, id)); };
// body: { expiresInDays? } — link nou (cel vechi încetează; răspunsurile strânse trec pe cel nou)
export const POST = async (req: Request, { params }: Ctx) => { const { id } = await params; return handle(async o => cb.createBriefLink(o, id, (await body(req, 1024))?.expiresInDays), 201); };
// body: { action: 'revoke' | 'reopen' }
export const PATCH = async (req: Request, { params }: Ctx) => { const { id } = await params; return handle(async o => { const a = (await body(req, 1024))?.action;
  if (a === 'revoke') return cb.revokeBriefLink(o, id); if (a === 'reopen') return cb.reopenClientBrief(o, id); throw new HttpError(400, 'Acțiune necunoscută.'); }); };
// șterge chestionarul cu tot cu poze
export const DELETE = async (_: Request, { params }: Ctx) => { const { id } = await params; return handle(o => cb.deleteClientBrief(o, id)); };
