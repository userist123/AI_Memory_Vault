/**
 * Client-side text sanitiser (defence in depth; the backend runs the same pipeline, DESIGN 5.3).
 * Order: strip ANSI CSI/OSC (incl. OSC 52 clipboard writes) -> drop C0 except \n \t, DEL, C1,
 * Unicode tag chars U+E0000-E007F, bidi overrides/isolates, zero-width chars, BOM, lone surrogates -> cap.
 * The result is only ever rendered as a React text node.
 */

export const TEXT_CAP = 4000;
export const SHORT_CAP = 300;
export const TRUNCATION_MARK = " …[truncated]";

// OSC: ESC ] ... (BEL | ESC \) ; also the 8-bit C1 form 0x9D ... 0x07/0x9C
const OSC = /(?:\u001b\]|\u009d)[^\u0007\u001b\u009c]*(?:\u0007|\u001b\\|\u009c)?/g;
// CSI: ESC [ params final ; also 8-bit 0x9B
const CSI = /(?:\u001b\[|\u009b)[0-?]*[ -/]*[@-~]?/g;
// other two-char escapes (ESC P/X/^/_ string commands up to ST, then plain ESC x)
const DCS = /\u001b[PX^_][^\u001b]*(?:\u001b\\)?/g;
const ESC2 = /\u001b[ -~]?/g;
// C0 except \t (09) and \n (0A); DEL; C1
const CONTROL = /[\u0000-\u0008\u000b-\u001f\u007f-\u009f]/g;
// bidi embeddings/overrides/isolates, zero-width/LRM/RLM, word joiner etc., BOM
const INVISIBLE = /[​-‏‪-‮⁠-⁤⁦-⁩﻿]/g;
// Unicode tag block U+E0000-U+E007F (surrogate pair DB40 DC00-DC7F)
const TAGS = /\udb40[\udc00-\udc7f]/g;
// lone surrogates
const LONE = /[\ud800-\udbff](?![\udc00-\udfff])|(?<![\ud800-\udbff])[\udc00-\udfff]/g;

export function stripUnsafe(input: string): string {
  return input
    .replace(OSC, "")
    .replace(DCS, "")
    .replace(CSI, "")
    .replace(ESC2, "")
    .replace(CONTROL, "")
    .replace(TAGS, "")
    .replace(INVISIBLE, "")
    .replace(LONE, "�");
}

/** Sanitise any untrusted value into a capped, printable string. Non-strings become "". */
export function clean(value: unknown, cap: number = TEXT_CAP): string {
  if (typeof value !== "string") {
    if (typeof value === "number" && Number.isFinite(value)) return String(value);
    return "";
  }
  // pre-cap before regex work so a 10 MB field costs O(cap), not O(size)
  const pre = value.length > cap * 4 ? value.slice(0, cap * 4) : value;
  const s = stripUnsafe(pre);
  if (s.length > cap || value.length > pre.length) {
    return s.slice(0, cap) + TRUNCATION_MARK;
  }
  return s;
}

/** A non-negative finite number or null. */
export function num(value: unknown): number | null {
  return typeof value === "number" && Number.isFinite(value) && value >= 0 ? value : null;
}

/** Sanitised list of short strings (argv etc.), capped in count and per item. */
export function cleanList(value: unknown, maxItems = 64, cap = SHORT_CAP): string[] {
  if (!Array.isArray(value)) return [];
  return value.slice(0, maxItems).map((v) => clean(v, cap));
}
