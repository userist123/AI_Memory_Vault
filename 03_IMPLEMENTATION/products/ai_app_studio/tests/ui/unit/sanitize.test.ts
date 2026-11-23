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
