// Citirea corpului JSON al unei cereri publice cu o limită de octeți reală: contează octeții primiți, nu antetul
// content-length (care lipsește la transferul „chunked”). Peste limită citirea se oprește și răspunsul e 413.
import { HttpError } from './repo';

export async function readJsonLimited(req: Request, maxBytes: number): Promise<unknown> {
  const declared = Number(req.headers.get('content-length') ?? 0); if (declared > maxBytes) throw new HttpError(413, 'Cerere prea mare.');
  if (!req.body) throw new HttpError(400, 'Cerere invalidă.');
  const reader = req.body.getReader(), parts: Uint8Array[] = []; let n = 0;
  for (;;){ const { done, value } = await reader.read(); if (done) break; n += value.byteLength;
    if (n > maxBytes){ await reader.cancel().catch(() => {}); throw new HttpError(413, 'Cerere prea mare.'); } parts.push(value); }
  const buf = new Uint8Array(n); let o = 0; for (const p of parts){ buf.set(p, o); o += p.byteLength; }
  try { return JSON.parse(new TextDecoder().decode(buf)); } catch { throw new HttpError(400, 'Cerere invalidă.'); }
}
