import { shareRender, isRenderWidth } from '@/lib/share-renders';
import { shareLogPhoto } from '@/lib/site-log';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ token: string; key: string }> };
const missing = () => new Response('Not found', { status: 404, headers: { 'cache-control': 'no-store', 'x-robots-tag': 'noindex' } });
// Public: o imagine 3D a reviziei partajate (?w=800|1600). Revizia nu se mai schimbă, deci imaginea se poate păstra în
// browser; „private” ca un proxy să nu o țină după revocarea linkului.
// ?src=log: o poză din jurnalul de șantier (cheia = id-ul pozei), doar dacă aparține unei intrări vizibile clientului a
// proiectului acestui link; altfel 404. Vizibilitatea se poate opri oricând, deci poza stă în browser doar câteva minute.
export const GET = async (req: Request, { params }: Ctx) => { const { token, key } = await params, sp = new URL(req.url).searchParams;
  if (sp.get('src') === 'log'){ const ph = await shareLogPhoto(token, key).catch(() => null); if (!ph) return missing();
    return new Response(Buffer.from(ph.data), { headers: { 'content-type': ph.mime, 'cache-control': 'private, max-age=300', 'x-robots-tag': 'noindex', 'x-content-type-options': 'nosniff', 'content-security-policy': "default-src 'none'; sandbox" } }); }
  const w = Number(sp.get('w') ?? 1600);
  const img = isRenderWidth(w) ? await shareRender(token, key, w).catch(() => null) : null;
  if (!img) return missing();
  return new Response(new Uint8Array(img.bytes), { headers: { 'content-type': img.mime, 'cache-control': 'private, max-age=86400', 'x-robots-tag': 'noindex', 'x-content-type-options': 'nosniff' } }); };
