import { handle, body } from '@/lib/http';
import { revisionRenderState, saveRevisionRender } from '@/lib/share-renders';
export const dynamic = 'force-dynamic';
type Ctx = { params: Promise<{ id: string; n: string }> };
// Imaginile 3D ale reviziei pentru linkul clientului: designerul le randează o dată și le trimite aici, câte una (lib/share-renders.ts)
export const GET = async (_: Request, { params }: Ctx) => { const { id, n } = await params; return handle(o => revisionRenderState(o, id, n)); };
// body: { key, width: 800 | 1600, image: data URL WebP/JPEG } — o imagine de cel mult 1,2 MB, deci corpul are o limită pe măsură
export const PUT = async (req: Request, { params }: Ctx) => { const { id, n } = await params; return handle(async o => saveRevisionRender(o, id, n, await body(req, 1.7 * 1024 * 1024))); };
