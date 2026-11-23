/**
 * Brand-kit client (W5): GET/PUT /projects/<id>/brand. The server owns the glyph list (a bundled, hash-checked Lucide
 * subset) and renders the preview; every response is size-capped and validated, strings are sanitised and shown as text.
 * Glyph SVG is NOT injected as markup: only its path data is parsed into a React path list.
 */
import type { Session } from "./session";
import { inspect } from "./sanitize";

const MAX_BODY = 2 * 1024 * 1024;
const PROJECT_RE = /^prj_[0-9a-f]{16}$/;
const HEX_RE = /^#[0-9a-fA-F]{6}$/;
const NAME_RE = /^[a-z0-9]+(?:-[a-z0-9]+)*$/;
const B64_RE = /^[A-Za-z0-9+/=]*$/;

export interface BrandGlyph {
  name: string;
  /** path data of the glyph (circle/line/rect are reduced to path data here) */
  paths: string[];
}

export interface BrandKit {
  glyph: string;
  background: string;
  foreground: string | null;
  caption: string;
  versionMajor: number;
  versionMinor: number;
}

export interface BrandView {
  kit: BrandKit;
  colours: { background: string; foreground: string };
  appLabel: string;
  nextVersionCode: number;
  nextVersionName: string;
  glyphs: BrandGlyph[];
  previewPng: string;
}

export interface BrandApi {
  load: (projectId: string) => Promise<BrandView>;
  save: (projectId: string, kit: BrandKit) => Promise<BrandView>;
}

function num(v: unknown, lo: number, hi: number, d: number): number {
  return typeof v === "number" && Number.isInteger(v) && v >= lo && v <= hi ? v : d;
}

/** Path data of the bundled icons: <path d>, and the circle/line/rect shapes reduced to path data. */
export function glyphPaths(svg: string): string[] {
  const out: string[] = [];
  for (const m of svg.matchAll(/<path\s+[^>]*?\bd="([^"]{1,2000})"/g)) out.push(String(m[1]));
  for (const m of svg.matchAll(/<circle\s+[^>]*?\bcx="([-0-9.]+)"\s+cy="([-0-9.]+)"\s+r="([-0-9.]+)"/g)) {
    const [cx, cy, r] = [Number(m[1]), Number(m[2]), Number(m[3])];
    out.push(`M${cx - r} ${cy}a${r} ${r} 0 1 0 ${2 * r} 0a${r} ${r} 0 1 0 ${-2 * r} 0`);
  }
  for (const m of svg.matchAll(/<line\s+[^>]*?\bx1="([-0-9.]+)"\s+x2="([-0-9.]+)"\s+y1="([-0-9.]+)"\s+y2="([-0-9.]+)"/g))
    out.push(`M${m[1]} ${m[3]}L${m[2]} ${m[4]}`);
  for (const m of svg.matchAll(/<rect\s+[^>]*?\bwidth="([-0-9.]+)"\s+height="([-0-9.]+)"\s+x="([-0-9.]+)"\s+y="([-0-9.]+)"/g))
    out.push(`M${m[3]} ${m[4]}h${m[1]}v${m[2]}h-${m[1]}z`);
  return out.slice(0, 12);
}

export function parseBrand(body: Record<string, unknown>): BrandView | null {
  const k = (body.kit && typeof body.kit === "object" ? body.kit : null) as Record<string, unknown> | null;
  const c = (body.colours && typeof body.colours === "object" ? body.colours : null) as Record<string, unknown> | null;
  if (!k || !c || typeof k.glyph !== "string" || !NAME_RE.test(k.glyph)) return null;
  if (typeof k.background !== "string" || !HEX_RE.test(k.background)) return null;
  if (typeof c.background !== "string" || !HEX_RE.test(c.background) || typeof c.foreground !== "string" || !HEX_RE.test(c.foreground))
    return null;
  const glyphs: BrandGlyph[] = [];
  for (const g of (Array.isArray(body.glyphs) ? body.glyphs : []).slice(0, 80)) {
    const r = (g && typeof g === "object" ? g : {}) as Record<string, unknown>;
    if (typeof r.name === "string" && NAME_RE.test(r.name) && typeof r.svg === "string" && r.svg.length < 4000)
      glyphs.push({ name: r.name, paths: glyphPaths(r.svg) });
  }
  const png = typeof body.previewPng === "string" && body.previewPng.length < MAX_BODY && B64_RE.test(body.previewPng) ? body.previewPng : "";
  return {
    kit: {
      glyph: k.glyph,
      background: k.background,
      foreground: typeof k.foreground === "string" && HEX_RE.test(k.foreground) ? k.foreground : null,
      caption: inspect(k.caption, 60).text,
      versionMajor: num(k.versionMajor, 0, 999, 1),
      versionMinor: num(k.versionMinor, 0, 999, 0),
    },
    colours: { background: c.background, foreground: c.foreground },
    appLabel: inspect(body.appLabel, 60).text,
    nextVersionCode: num(body.nextVersionCode, 1, 2_100_000_000, 1),
    nextVersionName: inspect(body.nextVersionName, 20).text,
    glyphs,
    previewPng: png,
  };
}

async function call(s: Session, projectId: string, init: RequestInit): Promise<BrandView> {
  if (!PROJECT_RE.test(projectId)) throw new Error("invalid id");
  const res = await fetch(`${s.apiBase}/api/v1/projects/${projectId}/brand`, {
    ...init,
    headers: { Authorization: `Bearer ${s.token}`, "Content-Type": "application/json" },
    credentials: "omit",
    redirect: "error",
    referrerPolicy: "no-referrer",
  });
  const text = await res.text();
  if (text.length > MAX_BODY) throw new Error("response too large");
  let body: Record<string, unknown> = {};
  try {
    const j = text ? (JSON.parse(text) as unknown) : {};
    if (j && typeof j === "object" && !Array.isArray(j)) body = j as Record<string, unknown>;
  } catch {
    throw new Error(`HTTP ${res.status}`);
  }
  if (!res.ok) throw new Error(inspect(body.message ?? `HTTP ${res.status}`, 200).text);
  const v = parseBrand(body);
  if (!v) throw new Error("malformed brand response");
  return v;
}

export function brandApi(s: Session): BrandApi {
  return {
    load: (p) => call(s, p, { method: "GET" }),
    save: (p, kit) =>
      call(s, p, {
        method: "PUT",
        body: JSON.stringify({
          glyph: kit.glyph,
          background: kit.background,
          ...(kit.foreground ? { foreground: kit.foreground } : {}),
          caption: kit.caption,
          versionMajor: kit.versionMajor,
          versionMinor: kit.versionMinor,
        }),
      }),
  };
}
