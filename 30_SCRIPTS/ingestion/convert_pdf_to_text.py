#!/usr/bin/env python3
"""
Converts PDF sources into the normalized text that extract_book_concepts.py
consumes.

This is the step that was missing. `extract_book_concepts.py` says it "ingests
plain text converted from PDF/epub" without saying who converts it, and the
one `.txt` in the corpus was produced outside the repository. Nineteen books
had no path in.

## Why headings, and not just text

`split_into_structural_chunks()` splits on heading markers, and its first
pattern is `^#+\\s+.*` — any markdown heading. The pre-existing
`sarfraz22a.txt` carries `# Section Page 1` per page, so it chunks per PAGE.
On a 561-page monograph that yields 561 fragments whose boundaries fall
wherever the typesetter broke the page, which is not where the argument
breaks. A concept whose definition straddles a page break is invisible to a
sentence-level extractor.

So the job here is not "get the characters out". It is to recover the
document's own section structure and emit it as headings.

## Three modes, in order of preference

toc      The PDF carries an embedded outline. This is the author's own
         structure and is always preferred. Measured on this corpus: 5 of 20.

font     No outline, so headings are recovered from typography — a line set
         markedly larger than the body text, short, and not repeating on
         every page. This is the mode the monographs need, which is to say
         it is the mode that matters. Ashby, Newell, Squire & Kandel and
         Schacter & Tulving all carry no outline.

pages    Neither worked. Falls back to page grouping, and says so. Chunks
         produced this way have arbitrary boundaries and should be treated
         as lower-confidence input, not as sections.

The mode used is reported per book rather than hidden, because it changes how
much the downstream chunking can be trusted.

## What this refuses to do quietly

A PDF with no text layer is a scan. It extracts to nothing, ingests to zero
concepts, and looks exactly like a book that simply had no definitions in it
— a silent success that is really a silent failure. Two books in this corpus
are scans. They are flagged OCR_REQUIRED and are not written.
"""

from __future__ import annotations

import argparse
import json
import pathlib
import re
import sys
from collections import Counter
from typing import Iterable

try:
    import pymupdf
except ImportError:  # pragma: no cover - environment guard
    sys.exit(
        "pymupdf is required. Install it with:  python -m pip install pymupdf"
    )

#: Below this many characters per page, averaged over the document, the PDF
#: is treated as having no usable text layer. A dense page of a printed book
#: runs 1500-4500 characters; the scans in this corpus measure 0. The
#: threshold sits far above zero so that a PDF carrying only page numbers or
#: a stray watermark is caught too.
MIN_CHARS_PER_PAGE = 120

#: A heading is set larger than the body. 1.15 is deliberately modest —
#: academic monographs often set section heads only a point or two above
#: body text, and a stricter ratio finds chapter titles while missing every
#: section inside them.
HEADING_SIZE_RATIO = 1.15

#: Longer than this and it is a sentence that happens to be emphasised, not
#: a heading.
HEADING_MAX_CHARS = 120

#: Text repeating on at least this fraction of pages is a running head or a
#: footer, whatever size it is set in. Without this, "CHAPTER 4" printed at
#: the top of forty consecutive pages becomes forty chapter headings.
RUNNING_HEAD_PAGE_FRACTION = 0.25

#: Font-size heading detection assumes the PDF reports honest font sizes. A
#: PDF that is itself the output of OCR does not: sizes are per-word guesses
#: and vary by more than the heading ratio, so ordinary prose crosses the
#: threshold. Measured on this corpus, that failure is unmistakable in the
#: heading COUNT rather than in the text — Ashby's Design for a Brain
#: produced 6798 headings across 304 pages, 22 per page, and Newell 912
#: across 561. Under-detection is the same failure inverted: How Can the
#: Human Mind Occur yielded 13 headings for 460 pages, which is not a book
#: with three sections, it is a book whose headings were missed.
#:
#: A real book falls between roughly one heading every ten pages and two per
#: page. Outside that band the typography is not telling us the structure,
#: and page grouping is the honest answer.
MIN_HEADINGS_PER_PAGE = 0.05
MAX_HEADINGS_PER_PAGE = 2.0

#: Fraction of letters that must be vowels before a word is believed. Garbage
#: OCR drops them: "delivered" becomes "dnlivrrrd", "the" becomes "(hr".
VOWEL_RATIO_FLOOR = 0.20

#: Fraction of a PAGE's words that may fail that test before the page is
#: called degraded. Deliberately applied per page and never to the document
#: as a whole: three separate document-level metrics were tried against this
#: corpus and all three failed, because a wrecked ten-page front matter is
#: invisible in the average of a 200,000-word book. Newell scores cleaner
#: document-wide than a paper that is genuinely clean. Corruption is local,
#: so it has to be measured locally.
DEGRADED_PAGE_THRESHOLD = 0.06

_VOWELS = frozenset("aeiouy")


#: Where Tesseract's language data lives. PyMuPDF takes the path directly, so
#: OCR does not depend on TESSDATA_PREFIX being exported — which it was not on
#: the machine this was built for, even after a clean install.
_TESSDATA_CANDIDATES = (
    r"C:\Program Files\Tesseract-OCR\tessdata",
    r"C:\Program Files (x86)\Tesseract-OCR\tessdata",
    "/usr/share/tesseract-ocr/5/tessdata",
    "/usr/share/tesseract-ocr/4.00/tessdata",
    "/usr/local/share/tessdata",
    "/opt/homebrew/share/tessdata",
)


def find_tessdata() -> str:
    """The tessdata directory, or "" when Tesseract is not installed."""
    import os

    env = os.environ.get("TESSDATA_PREFIX", "").strip()
    if env and pathlib.Path(env).is_dir():
        return env
    for candidate in _TESSDATA_CANDIDATES:
        if pathlib.Path(candidate).is_dir():
            return candidate
    return ""


def _extract_with_ocr(
    doc: "pymupdf.Document", tessdata: str, dpi: int, per_chunk: int
) -> tuple[str, int]:
    """Rasterise and OCR every page. Returns (text, degraded page count).

    The degraded count is measured here rather than assumed. OCR output is
    exactly where character-level damage is most likely — it is how Newell's
    front matter became "Copynyhrnd Moterrol" — so reporting zero without
    checking would be the same unmeasured claim this converter exists to
    avoid.
    """
    out: list[str] = []
    degraded = 0
    for i in range(doc.page_count):
        page = doc[i]
        textpage = page.get_textpage_ocr(dpi=dpi, full=True, tessdata=tessdata)
        text = page.get_text(textpage=textpage)
        if _page_is_degraded(text):
            degraded += 1
        #: Page grouping is the only structure available here — a scan has no
        #: font metadata worth trusting and no outline, so recovering sections
        #: from it would be inventing them. But group the same way `pages`
        #: mode does rather than emitting one heading per page: a heading per
        #: page put Minsky at a median of 2,892 characters per chunk, below
        #: the 5,000-15,000 band that the recurrence measurements were taken
        #: in. Chunk size was the confound in every earlier selectivity
        #: result, so a second path that quietly uses a different one is the
        #: same mistake with a new name.
        if i % per_chunk == 0:
            last = min(i + per_chunk, doc.page_count)
            out.append(f"\n\n# Pages {i + 1}-{last}\n")
        out.append(text)
    return "".join(out), degraded


def _body_size(doc: "pymupdf.Document", sample_pages: list[int]) -> float:
    """The dominant font size, weighted by how much text is set in it.

    Weighted by character count rather than by span count: a page's body is a
    few long spans and its furniture is many short ones, so counting spans
    would let page numbers outvote the prose.
    """
    weight: Counter[float] = Counter()
    for i in sample_pages:
        for block in doc[i].get_text("dict").get("blocks", []):
            for line in block.get("lines", []):
                for span in line.get("spans", []):
                    text = span.get("text", "").strip()
                    if text:
                        weight[round(span.get("size", 0.0), 1)] += len(text)
    if not weight:
        return 0.0
    return weight.most_common(1)[0][0]


def _iter_lines(page: "pymupdf.Page") -> Iterable[tuple[str, float]]:
    """Every line on the page as (text, largest font size in that line)."""
    for block in page.get_text("dict").get("blocks", []):
        for line in block.get("lines", []):
            spans = line.get("spans", [])
            text = "".join(s.get("text", "") for s in spans).strip()
            if not text:
                continue
            yield text, max((s.get("size", 0.0) for s in spans), default=0.0)


def _running_heads(doc: "pymupdf.Document", candidates: Counter[str]) -> set[str]:
    """Candidate heading texts that repeat often enough to be furniture."""
    threshold = max(3, int(doc.page_count * RUNNING_HEAD_PAGE_FRACTION))
    return {text for text, n in candidates.items() if n >= threshold}


def _extract_by_toc(doc: "pymupdf.Document") -> str:
    """Emit the document's own outline as headings, text under each."""
    #: page number -> headings starting on that page, outermost first
    starts: dict[int, list[tuple[int, str]]] = {}
    for level, title, page in doc.get_toc():
        if page >= 1:
            starts.setdefault(page - 1, []).append((max(1, level), title.strip()))

    out: list[str] = []
    for i in range(doc.page_count):
        for level, title in starts.get(i, []):
            if title:
                out.append(f"\n\n{'#' * min(level, 6)} {title}\n")
        out.append(doc[i].get_text())
    return "".join(out)


def _extract_by_font(doc: "pymupdf.Document", body: float) -> tuple[str, int]:
    """Recover headings from typography. Returns (text, headings found)."""
    cutoff = body * HEADING_SIZE_RATIO

    #: First pass: find what looks like a heading, and count repeats so that
    #: running heads can be told apart from real section titles.
    candidates: Counter[str] = Counter()
    for i in range(doc.page_count):
        for text, size in _iter_lines(doc[i]):
            if size >= cutoff and len(text) <= HEADING_MAX_CHARS:
                candidates[text] += 1
    furniture = _running_heads(doc, candidates)

    out: list[str] = []
    found = 0
    for i in range(doc.page_count):
        for text, size in _iter_lines(doc[i]):
            is_heading = (
                size >= cutoff
                and len(text) <= HEADING_MAX_CHARS
                and text not in furniture
                and any(c.isalpha() for c in text)
            )
            if is_heading:
                #: Two sizes above body reads as a chapter, one as a section.
                level = 1 if size >= body * 1.5 else 2
                out.append(f"\n\n{'#' * level} {text}\n")
                found += 1
            else:
                out.append(text + "\n")
    return "".join(out), found


def _page_is_degraded(text: str) -> bool:
    """Whether this page's words look like the output of a failed OCR pass."""
    words = re.findall(r"[A-Za-z]{3,}", text)
    if len(words) < 20:
        #: Too little text to judge — a figure page, not evidence of damage.
        return False
    bad = sum(
        1 for w in words
        if sum(c in _VOWELS for c in w.lower()) / len(w) < VOWEL_RATIO_FLOOR
    )
    return bad / len(words) > DEGRADED_PAGE_THRESHOLD


def _degraded_pages(doc: "pymupdf.Document") -> int:
    return sum(1 for i in range(doc.page_count) if _page_is_degraded(doc[i].get_text()))


def _extract_by_pages(doc: "pymupdf.Document", per_chunk: int) -> str:
    """Last resort. Groups pages so the chunks are at least paragraph-sized."""
    out: list[str] = []
    for i in range(doc.page_count):
        if i % per_chunk == 0:
            last = min(i + per_chunk, doc.page_count)
            out.append(f"\n\n# Pages {i + 1}-{last}\n")
        out.append(doc[i].get_text())
    return "".join(out)


def _normalize(text: str) -> str:
    """Repair what PDF text extraction reliably breaks.

    Headings are normalized separately from prose, and that separation is the
    whole point rather than a tidiness measure. Collapsing newlines with a
    single regex over the whole document merges each heading into the
    paragraph beneath it, and the consumer's heading pattern is `^#+\\s+.*`
    where `.*` stops at a newline — so the match swallows the entire section
    body, leaves an empty `content`, and the section is dropped by its
    `len(content) > 10` check.

    Measured before this was split out: Schacter & Tulving normalized to
    1.18 MB across 57 lines and produced ZERO chunks, as did seven other
    books. The conversion reported OK for all of them. A heading is only a
    boundary while it still has a line of its own.
    """
    #: Hyphenated line breaks: "consol-\nidation" is one word.
    text = re.sub(r"(\w)-\n(\w)", r"\1\2", text)

    out: list[str] = []
    paragraph: list[str] = []

    def flush() -> None:
        if paragraph:
            #: Single newlines inside a paragraph are line wrapping, not
            #: structure, so the lines join into running prose.
            joined = " ".join(" ".join(paragraph).split())
            if joined:
                out.append(joined)
            paragraph.clear()

    for line in text.split("\n"):
        if line.startswith("#"):
            flush()
            out.append(line.rstrip())
        elif not line.strip():
            flush()
        else:
            paragraph.append(line)
    flush()

    return "\n\n".join(out).strip() + "\n"


def convert(
    path: pathlib.Path, pages_per_chunk: int, force_mode: str = "",
    ocr_tessdata: str = "", ocr_dpi: int = 200,
) -> dict[str, object]:
    """Convert one PDF. Returns its metrics; never raises for a bad PDF."""
    record: dict[str, object] = {"book": str(path), "folder": path.parent.name}
    try:
        doc = pymupdf.open(path)
    except Exception as exc:
        record.update(status="OPEN_FAILED", error=f"{type(exc).__name__}: {exc}")
        return record

    with doc:
        n = doc.page_count
        record["pages"] = n
        if n == 0:
            record["status"] = "EMPTY"
            return record

        sample = [int(n * i / 8) for i in range(8)] if n >= 8 else list(range(n))
        raw_chars = sum(len(doc[i].get_text()) for i in sample)
        density = raw_chars / max(len(sample), 1)

        #: Decided before any extraction work, so a scan costs one pass.
        if density < MIN_CHARS_PER_PAGE and ocr_tessdata:
            #: Measured on Ashby's Introduction to Cybernetics: ~1 second and
            #: ~3,500 characters per page at 200 dpi, legible enough to read.
            #: The two scans in this corpus are 492 pages together, so this is
            #: minutes rather than an obstacle — but it stays opt-in, because
            #: OCR text is a different quality of input from a real text layer
            #: and the record says which one produced a book.
            raw, degraded = _extract_with_ocr(
                doc, ocr_tessdata, ocr_dpi, pages_per_chunk
            )
            text = _normalize(raw)
            record.update(
                status="OK",
                mode="ocr",
                headings=-(-n // pages_per_chunk),
                headings_per_page=round(1 / pages_per_chunk, 2),
                degraded_pages=degraded,
                degraded_fraction=round(degraded / n, 3),
                characters=len(text),
                chars_per_page=round(len(text) / n, 1),
                ocr_dpi=ocr_dpi,
            )
            record["_text"] = text
            return record

        if density < MIN_CHARS_PER_PAGE:
            record.update(
                status="OCR_REQUIRED",
                chars_per_page=round(density, 1),
                mode=None,
                error=(
                    "no usable text layer; this is a scanned image PDF. "
                    "PyMuPDF can OCR it via page.get_textpage_ocr(), but that "
                    "needs Tesseract on PATH and TESSDATA_PREFIX set — "
                    "neither is present here, so OCR is a separate "
                    "prerequisite rather than something this script can do"
                ),
            )
            return record

        toc = doc.get_toc()
        rejected = None
        if force_mode == "pages":
            #: A deliberate per-book override, because the automatic band
            #: cannot catch every failure. Newell's Unified Theories sits
            #: inside the plausibility band at 1.63 headings per page while
            #: marking body paragraphs as headings, and the result is 912
            #: chunks of ~870 characters — 51% of the whole corpus run, spent
            #: on the book with the worst structure and 14 OCR-degraded
            #: pages. At 3 pages per chunk the same book is 187.
            mode, text = "pages", _extract_by_pages(doc, pages_per_chunk)
            headings = -(-n // pages_per_chunk)
        elif len(toc) >= 3:
            mode, text, headings = "toc", _extract_by_toc(doc), len(toc)
        else:
            body = _body_size(doc, sample)
            text, headings = _extract_by_font(doc, body) if body else ("", 0)
            per_page = headings / n
            if MIN_HEADINGS_PER_PAGE <= per_page <= MAX_HEADINGS_PER_PAGE:
                mode = "font"
            else:
                #: Recorded rather than dropped: "font mode found 22 headings
                #: per page" is the diagnosis for this book, and a later
                #: reader should not have to rediscover it.
                rejected = {
                    "mode": "font",
                    "headings": headings,
                    "headings_per_page": round(per_page, 2),
                    "reason": (
                        "outside the plausible band "
                        f"{MIN_HEADINGS_PER_PAGE}-{MAX_HEADINGS_PER_PAGE} per page"
                    ),
                }
                mode, text = "pages", _extract_by_pages(doc, pages_per_chunk)
                headings = -(-n // pages_per_chunk)

        degraded = _degraded_pages(doc)
        text = _normalize(text)
        record.update(
            status="OK",
            mode=mode,
            headings=headings,
            headings_per_page=round(headings / n, 2),
            degraded_pages=degraded,
            degraded_fraction=round(degraded / n, 3),
            characters=len(text),
            chars_per_page=round(len(text) / n, 1),
        )
        if rejected:
            record["rejected_mode"] = rejected
        record["_text"] = text
        return record


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("root", type=pathlib.Path, help="folder to scan for PDFs")
    ap.add_argument(
        "--pages-per-chunk", type=int, default=10,
        help="pages per chunk in the 'pages' fallback mode (default: 10)",
    )
    ap.add_argument(
        "--ocr", action="store_true",
        help="OCR books that have no text layer. Off by default: OCR text is "
             "a different quality of input from a real text layer, and which "
             "one produced a book should be a deliberate choice recorded in "
             "the report, not a silent fallback",
    )
    ap.add_argument(
        "--ocr-dpi", type=int, default=200,
        help="rasterisation dpi for OCR (default 200; ~1s and ~3,500 "
             "characters per page measured at this setting)",
    )
    ap.add_argument(
        "--force-mode", default="", choices=("", "pages"),
        help="override structure detection for this run. The plausibility "
             "band cannot catch every failure: a book can sit inside it and "
             "still mark body paragraphs as headings",
    )
    ap.add_argument(
        "--report", type=pathlib.Path,
        help="write the per-book metrics as JSON here",
    )
    ap.add_argument(
        "--dry-run", action="store_true",
        help="measure and report without writing any .txt",
    )
    args = ap.parse_args()

    tessdata = ""
    if args.ocr:
        tessdata = find_tessdata()
        if not tessdata:
            #: Loudly. A silent no-op here reproduces the exact failure this
            #: whole flag exists to remove: a book reported as converted that
            #: contains nothing.
            sys.exit(
                "--ocr was requested but Tesseract's tessdata could not be "
                "found. Set TESSDATA_PREFIX, or install Tesseract "
                "(winget install --id UB-Mannheim.TesseractOCR)."
            )
        print(f"OCR enabled, tessdata: {tessdata}")

    pdfs = sorted(args.root.rglob("*.pdf"))
    if not pdfs:
        print(f"no PDFs under {args.root}", file=sys.stderr)
        return 1

    records = []
    for path in pdfs:
        rec = convert(
            path, args.pages_per_chunk, args.force_mode, tessdata, args.ocr_dpi
        )
        text = rec.pop("_text", None)
        if text is not None and not args.dry_run:
            path.with_suffix(".txt").write_text(text, encoding="utf-8")
            rec["written"] = str(path.with_suffix(".txt"))
        records.append(rec)
        print(
            f"{rec['status']:13} {str(rec.get('mode') or '-'):6} "
            f"{str(rec.get('pages', '-')):>5}p "
            f"{str(rec.get('chars_per_page', '-')):>8} c/pg  {str(rec.get('degraded_pages','-')):>4}dg  {path.name[:44]}"
        )

    ok = [r for r in records if r["status"] == "OK"]
    failed = [r for r in records if r["status"] != "OK"]
    print(f"\n{len(ok)}/{len(records)} converted, {len(failed)} unusable")
    for r in failed:
        print(f"  {r['status']}: {pathlib.Path(str(r['book'])).name} — {r.get('error', '')}")
    if ok:
        by_mode = Counter(str(r["mode"]) for r in ok)
        print(f"  modes: {dict(by_mode)}")
        print(f"  total characters: {sum(int(r['characters']) for r in ok):,}")

    if args.report:
        args.report.write_text(json.dumps(records, indent=2), encoding="utf-8")
        print(f"  report: {args.report}")

    #: Unusable books are a reportable outcome, not a crash — but they must
    #: not read as success either.
    return 0 if not failed else 2


if __name__ == "__main__":
    raise SystemExit(main())
