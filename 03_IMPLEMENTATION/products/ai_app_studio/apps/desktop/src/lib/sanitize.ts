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
// OSC: ESC ] ... terminated by BEL / ST (also the 8-bit C1 form 0x9D ... 0x07/0x9C). The body never spans a
// newline, and an UNTERMINATED OSC is not matched at all: only its introducer is removed (ESC2 / CONTROL below),
// so the text after it is kept instead of being swallowed to the end of the string.
const OSC = /(?:\u001b\]|\u009d)[^\u0007\u001b\u009c\n\r]*(?:\u0007|\u001b\\|\u009c)/g;
// CSI: ESC [ params final ; also 8-bit 0x9B
// The final byte is required: an incomplete "ESC[12" must lose only its introducer (ESC2 / CONTROL below), not the
// text that follows (S6: the optional final byte swallowed one extra character).
const CSI = /(?:\u001b\[|\u009b)[0-?]*[ -/]*[@-~]/g;
// DCS/SOS/PM/APC: ESC P/X/^/_ ... ST. Same rule: only a terminated string is removed as a whole.
const DCS = /\u001b[PX^_][^\u001b\n\r]*\u001b\\/g;
const ESC2 = /\u001b[ -~]?/g;
// C0 except \t (09) and \n (0A); DEL; C1
const CONTROL = /[\u0000-\u0008\u000b-\u001f\u007f-\u009f]/g;
// Same list as the backend (activity.py _DROP): soft hyphen, Arabic letter mark, zero-width/LRM/RLM, line and
// paragraph separators, bidi embeddings/overrides/isolates, word joiner and invisible operators, variation
// selectors, BOM
// plus (S6) the invisible fillers U+034F, U+115F, U+180E, U+3164 (backend list is the same)
const INVISIBLE = /[\u00ad\u034f\u061c\u115f\u180e\u200b-\u200f\u2028\u2029\u202a-\u202e\u2060-\u2064\u2066-\u2069\u3164\ufe00-\ufe0f\ufeff]/g;
const INVISIBLE_ONE = /^[\u00ad\u034f\u061c\u115f\u180e\u200b-\u200f\u2028\u2029\u202a-\u202e\u2060-\u2064\u2066-\u2069\u3164\ufe00-\ufe0f\ufeff]$/;
// Unicode tag block U+E0000-U+E007F (surrogate pair DB40 DC00-DC7F) and variation selectors supplement
// U+E0100-U+E01EF (DB40 DD00-DDEF)
const TAGS = /\udb40[\udc00-\udc7f\udd00-\uddef]/g;
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

/** Would the sanitiser remove or alter this single code point? (strict: also \n and \t) */
function isHidden(ch: string, strict: boolean): boolean {
  const cp = ch.codePointAt(0)!;
  if (cp === 0x0a || cp === 0x09) return strict;
  if (cp <= 0x1f || (cp >= 0x7f && cp <= 0x9f)) return true;
  if (cp >= 0xd800 && cp <= 0xdfff) return true; // lone surrogate (a valid pair arrives as one code point)
  if (cp >= 0xe0000 && cp <= 0xe007f) return true;
  if (cp >= 0xe0100 && cp <= 0xe01ef) return true;
  return INVISIBLE_ONE.test(ch);
}

function escapeOne(ch: string): string {
  const cp = ch.codePointAt(0)!;
  if (cp === 0x0a) return "\\n";
  if (cp === 0x09) return "\\t";
  if (cp === 0x0d) return "\\r";
  if (cp <= 0xff) return `\\x${cp.toString(16).padStart(2, "0")}`;
  return `U+${cp.toString(16).toUpperCase().padStart(4, "0")}`;
}

export interface Inspected {
  /** printable rendering: hidden/control code points are shown as text (\x1b, U+202E), never as themselves */
  text: string;
  /** true when anything was escaped, truncated or not a string: the owner must not be asked to approve it */
  unsafe: boolean;
}

/**
 * Fail-closed view of an untrusted single-line field (approval argv/target, tool input). Unlike clean(), which
 * silently removes characters, this shows every raw code point of a suspicious value and reports `unsafe`.
 */
export function inspect(value: unknown, cap: number = SHORT_CAP, strict = true): Inspected {
  if (value === undefined || value === null) return { text: "", unsafe: false };
  if (typeof value !== "string") return { text: "", unsafe: true };
  const truncated = value.length > cap;
  const part = truncated ? value.slice(0, cap) : value;
  let out = "";
  let escaped = false;
  for (const ch of part) {
    if (isHidden(ch, strict)) {
      out += escapeOne(ch);
      escaped = true;
    } else out += ch;
  }
  return { text: truncated ? out + TRUNCATION_MARK : out, unsafe: escaped || truncated };
}

/** inspect() over a list of strings (argv). A non-array, an oversize list or any unsafe item makes it unsafe. */
export function inspectList(value: unknown, maxItems = 64, cap = SHORT_CAP): { items: string[]; unsafe: boolean } {
  if (value === undefined || value === null) return { items: [], unsafe: false };
  if (!Array.isArray(value)) return { items: [], unsafe: true };
  const parts = value.slice(0, maxItems).map((v) => inspect(v, cap));
  return { items: parts.map((p) => p.text), unsafe: value.length > maxItems || parts.some((p) => p.unsafe) };
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
