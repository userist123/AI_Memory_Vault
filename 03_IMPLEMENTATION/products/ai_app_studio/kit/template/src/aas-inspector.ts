// Studio-owned (W4 click-to-edit inspector). The build copies this file from the kit into every build context, over
// any project file of the same name; kit/index.html loads it next to the app entry.
//
// Preview only: it does nothing unless the page is the studio preview origin (http://p-<id16>.localhost:<port>)
// running inside a frame. An exported APK/desktop app has another origin and is never framed, so the script stays inert.
// It adds no capability to the preview: it never evaluates anything, never writes markup, sends data only to the
// parent window and only to the origin that armed it, and reacts only to {v:1, kind:"inspect", on:boolean} coming
// from the parent window itself.

type Rect = { x: number; y: number; w: number; h: number };
export type SelectMessage = {
  v: 1;
  kind: "select";
  path: string;
  tag: string;
  text: string;
  rect: Rect;
  classes: string[];
};

const PREVIEW_HOST = /^p-[0-9a-f]{16}\.localhost$/;
const MAX_TEXT = 200;
const MAX_CLASSES = 20;
const MAX_PATH = 300;
const CLASS_RE = /^[A-Za-z0-9_:/.\[\]%#-]{1,64}$/;
const TAG_RE = /^[a-z][a-z0-9-]{0,31}$/;
const BAD_CHARS = /[\u0000-\u001f\u007f\u2028\u2029\u202a-\u202e\u2066-\u2069]/g;

function cssPath(el: Element): string {
  const anchor = el.closest("[data-aas-id]");
  const id = anchor === el ? el.getAttribute("data-aas-id") : null;
  if (id && /^[\x20-\x7e]{1,200}$/.test(id)) return `aas:${id}`;
  const parts: string[] = [];
  let cur: Element | null = el;
  while (cur && cur !== document.body && cur !== document.documentElement && parts.length < 12) {
    const tag = cur.tagName.toLowerCase();
    let index = 1;
    for (let sib = cur.previousElementSibling; sib; sib = sib.previousElementSibling) {
      if (sib.tagName === cur.tagName) index += 1;
    }
    parts.unshift(index > 1 ? `${tag}:nth-of-type(${index})` : tag);
    cur = cur.parentElement;
  }
  return parts.join(">").replace(/[^\x20-\x7e]/g, "").slice(-MAX_PATH);
}

function num(n: number): number {
  return Number.isFinite(n) ? Math.max(-100000, Math.min(100000, Math.round(n))) : 0;
}

export function describe(el: Element): SelectMessage | null {
  const tag = el.tagName.toLowerCase();
  if (!TAG_RE.test(tag)) return null;
  const r = el.getBoundingClientRect();
  const text = (el.textContent ?? "").replace(BAD_CHARS, " ").replace(/\s+/g, " ").trim().slice(0, MAX_TEXT);
  const classes = Array.from(el.classList)
    .filter((c) => CLASS_RE.test(c))
    .slice(0, MAX_CLASSES);
  return {
    v: 1,
    kind: "select",
    path: cssPath(el),
    tag,
    text,
    rect: { x: num(r.left), y: num(r.top), w: Math.max(0, num(r.width)), h: Math.max(0, num(r.height)) },
    classes,
  };
}

export function isPreviewContext(win: Window): boolean {
  return PREVIEW_HOST.test(win.location.hostname) && win.parent !== win;
}

export function install(win: Window = window): () => void {
  if (!isPreviewContext(win)) return () => undefined;
  const doc = win.document;
  let armedOrigin: string | null = null;
  let box: HTMLDivElement | null = null;

  const outline = (el: Element | null) => {
    if (!el || !armedOrigin) {
      box?.remove();
      box = null;
      return;
    }
    if (!box) {
      box = doc.createElement("div");
      box.setAttribute("aria-hidden", "true");
      box.style.cssText =
        "position:fixed;pointer-events:none;z-index:2147483647;border:2px solid #2563eb;" +
        "background:rgba(37,99,235,.08);border-radius:2px;box-sizing:border-box";
      doc.documentElement.appendChild(box);
    }
    const r = el.getBoundingClientRect();
    box.style.left = `${r.left}px`;
    box.style.top = `${r.top}px`;
    box.style.width = `${r.width}px`;
    box.style.height = `${r.height}px`;
  };

  const target = (e: Event): Element | null => {
    const t = e.target;
    return t instanceof Element && t !== box ? t : null;
  };
  const onMove = (e: Event) => outline(target(e));
  const block = (e: Event) => {
    if (!armedOrigin) return;
    e.preventDefault();
    e.stopPropagation();
  };
  const onClick = (e: Event) => {
    if (!armedOrigin) return;
    e.preventDefault();
    e.stopPropagation();
    const el = target(e);
    const msg = el ? describe(el) : null;
    if (msg) win.parent.postMessage(msg, armedOrigin);
  };

  const arm = (origin: string | null) => {
    armedOrigin = origin;
    const opts = { capture: true } as const;
    if (origin) {
      doc.addEventListener("pointermove", onMove, opts);
      doc.addEventListener("pointerdown", block, opts);
      doc.addEventListener("mousedown", block, opts);
      doc.addEventListener("click", onClick, opts);
    } else {
      doc.removeEventListener("pointermove", onMove, opts);
      doc.removeEventListener("pointerdown", block, opts);
      doc.removeEventListener("mousedown", block, opts);
      doc.removeEventListener("click", onClick, opts);
      outline(null);
    }
  };

  const onMessage = (event: MessageEvent) => {
    if (event.source !== win.parent) return; // only the frame's own parent (the studio) can arm it
    const d = event.data as unknown;
    if (!d || typeof d !== "object" || Array.isArray(d)) return;
    const m = d as Record<string, unknown>;
    const keys = Object.keys(m).sort().join(",");
    if (keys !== "kind,on,v" || m.v !== 1 || m.kind !== "inspect" || typeof m.on !== "boolean") return;
    if (typeof event.origin !== "string" || event.origin === "null" || event.origin.length > 100) return;
    arm(m.on ? event.origin : null);
  };
  win.addEventListener("message", onMessage);
  return () => {
    arm(null);
    win.removeEventListener("message", onMessage);
  };
}

install();
