import { shareRender, isRenderWidth } from '@/lib/share-renders';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ token: string; key: string }> };
// Public: o imagine 3D a reviziei partajate (?w=800|1600). Revizia nu se mai schimbă, deci imaginea se poate păstra în
// browser; „private” ca un proxy să nu o țină după revocarea linkului.
export const GET = async (req: Request, { params }: Ctx) => { const { token, key } = await params, w = Number(new URL(req.url).searchParams.get('w') ?? 1600);
  const img = isRenderWidth(w) ? await shareRender(token, key, w).catch(() => null) : null;
  if (!img) return new Response('Not found', { status: 404, headers: { 'cache-control': 'no-store', 'x-robots-tag': 'noindex' } });
  return new Response(new Uint8Array(img.bytes), { headers: { 'content-type': img.mime, 'cache-control': 'private, max-age=86400', 'x-robots-tag': 'noindex', 'x-content-type-options': 'nosniff' } }); };
