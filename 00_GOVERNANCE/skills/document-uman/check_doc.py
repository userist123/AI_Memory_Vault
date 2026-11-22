"""Checker for document-uman: flags AI tells, Romanian typography errors and machine metadata.

Usage: python -P -E check_doc.py <file.docx|file.xlsx|file.pdf|file.txt|file.md> [--word]
                                   [--date model.json] [--ignora-valori 30,90]
Exit code 1 if any FAIL. --word: also check through Word which tables really cross a page and
export a temporary PDF to find half-empty pages. --date: every amount with decimals in the text
must be a value of the data model (FAIL), integers followed by a unit/noun too (WARN).
Phase 3 adds complex-document checks (fields, TOC, tables, alt text, workbook integrity, PDF
bookmarks/labels/fonts/metadata).
"""
import re
import statistics
import sys
from pathlib import Path

PHRASES_RO = [
    "joacă un rol crucial", "joaca un rol crucial", "rol esențial", "în era digitală",
    "holistic", "este important de menționat", "este important de subliniat",
    "este de menționat faptul", "în concluzie", "în contextul actual", "o gamă largă de",
    "sper că acest mesaj", "nu ezitați să", "mai mult decât atât", "în peisajul",
    "fără cusur", "de neprețuit", "aduce valoare adăugată", "în lumea de azi",
    "nu doar", "nu este vorba doar", "servește drept", "rezumând", "pe scurt,",
]
PHRASES_EN = [
    "delve", "elevate", "seamless", "robust", "landscape", "leverage", "tapestry",
    "it's not just", "not only", "serves as", "in conclusion", "moreover", "furthermore",
    "it is important to note", "i hope this", "feel free to", "in today's", "crucial",
]
LAYOUT = []  # filled by text_and_meta for .docx
EMOJI = re.compile("[\U0001F300-\U0001FAFF☀-➿✅✨]")


def text_and_meta(path: Path):
    ext = path.suffix.lower()
    meta = {}
    if ext == ".docx":
        import docx
        from docx.oxml.ns import qn
        d = docx.Document(str(path))
        paras = [p.text for p in d.paragraphs]
        for t in d.tables:
            for row in t.rows:
                for c in row.cells:
                    paras.append(c.text)
        for i, s in enumerate(d.sections):
            if s.page_width is not None and min(abs(s.page_width.mm - 210), abs(s.page_width.mm - 297)
                                                    if s.page_height is not None and abs(s.page_height.mm - 210) < 2
                                                    else 99) > 2:
                LAYOUT.append(f"section {i + 1} page width {s.page_width.mm:.0f} mm - not A4 (python-docx default is US Letter)")
            if s.left_margin is not None and not 15 <= s.left_margin.mm <= 35:
                LAYOUT.append(f"section {i + 1} left margin {s.left_margin.mm:.0f} mm")
        normal = d.styles["Normal"].font
        if normal.name is None:
            LAYOUT.append("Normal style font not set deliberately (inherits theme default)")
        langs = {l.get(qn("w:val")) for l in d.styles.element.iter(qn("w:lang"))}
        if "ro-RO" not in langs:
            LAYOUT.append(f"proofing language not ro-RO ({sorted(x for x in langs if x)}) - Word flags every word")
        app_xml = None
        try:
            import zipfile
            app_xml = zipfile.ZipFile(str(path)).read("docProps/app.xml").decode("utf-8", "replace")
        except KeyError:
            pass
        if app_xml and "Macintosh" in app_xml:
            LAYOUT.append("app.xml is the python-docx template (Macintosh Word) - run finalizeaza_in_word")
        manual = sum(1 for p in d.paragraphs for r in p.runs if r.font.name or r.font.size)
        if manual > max(5, len(d.paragraphs) // 2):
            LAYOUT.append(f"{manual} runs with hand-set font/size - use styles")
        cp = d.core_properties
        meta = {"author": cp.author, "last_modified_by": cp.last_modified_by,
                "comments": cp.comments, "title": cp.title,
                "created": str(cp.created)}
        return paras, meta
    if ext == ".xlsx":
        import openpyxl
        wb = openpyxl.load_workbook(str(path))
        paras = []
        for ws in wb.worksheets:
            for row in ws.iter_rows(values_only=True):
                for v in row:
                    if isinstance(v, str) and not v.startswith("="):  # prose only, not formulas
                        paras.append(v)
        import zipfile
        wbxml = zipfile.ZipFile(str(path)).read("xl/workbook.xml")
        if b"absPath" in wbxml:
            LAYOUT.append("xlsx stores the folder path (x15ac:absPath) - run finalizeaza_xlsx")
        p = wb.properties
        meta = {"author": p.creator, "last_modified_by": p.lastModifiedBy, "title": p.title}
        return paras, meta
    if ext == ".pdf":
        try:
            from pypdf import PdfReader
        except ImportError:
            print("WARN  pypdf missing (pip install --user pypdf); PDF text not checked")
            return [], {}
        r = PdfReader(str(path))
        txt = "\n".join((pg.extract_text() or "") for pg in r.pages)
        info = r.metadata or {}
        meta = {"author": info.get("/Author"), "producer": info.get("/Producer"),
                "creator": info.get("/Creator"), "title": info.get("/Title")}
        return re.split(r"\n\s*\n", txt), meta
    return re.split(r"\n\s*\n", path.read_text(encoding="utf-8")), meta


ERORI_CAMPURI = ("error! reference source not found", "error! bookmark not defined",
                 "error! no table of contents entries found", "error! no text of specified style",
                 "eroare! sursa referinței nu a fost găsită", "eroare! sursa de referință nu a fost găsită",
                 "eroare! marcaj în document nedefinit", "eroare! marcajul nu este definit",
                 "eroare! nu s-a găsit nicio intrare de cuprins")


def complex_docx(path, fails, warns, word=False):
    """Long-document checks (phase 3): field errors, leftover markup, empty-paragraph spacing,
    long tables without repeated header, images without alt text, TOC presence and freshness,
    captions without SEQ. word=True: exact page-crossing check through Word COM."""
    import zipfile
    import docx
    from docx.oxml.ns import qn
    d = docx.Document(str(path))
    body = d.element.body
    z = zipfile.ZipFile(str(path))
    allxml = " ".join(z.read(n).decode("utf-8", "replace") for n in z.namelist()
                      if n.startswith("word/") and n.endswith(".xml"))
    import re as _re
    alltext = " ".join(_re.findall(r"<w:t(?: [^>]*)?>([^<]*)</w:t>", allxml)).lower()
    for e in ERORI_CAMPURI:
        if e in alltext:
            fails.append(f"field error text in the document: {e!r} - update fields / fix bookmarks")
    for mk in ("{{ref:", "{{nota:", "{{pag:", "**"):
        if mk in alltext:
            fails.append(f"leftover builder markup {mk!r} in the text")
    # consecutive empty body paragraphs (spacing by Enter)
    run_len, worst = 0, 0
    for el in body.iterchildren():
        if el.tag != qn("w:p"):
            run_len = 0
            continue
        txt = "".join(t.text or "" for t in el.iter(qn("w:t"))).strip()
        special = any(True for _ in el.iter(qn("w:drawing"))) or any(True for _ in el.iter(qn("w:sectPr"))) \
            or any(b.get(qn("w:type")) == "page" for b in el.iter(qn("w:br"))) \
            or any(True for _ in el.iter(qn("w:fldChar")))
        if not txt and not special:
            run_len += 1
            worst = max(worst, run_len)
        else:
            run_len = 0
    if worst >= 3:
        fails.append(f"{worst} consecutive empty paragraphs - use space before/after or a page break")
    elif worst == 2:
        warns.append("2 consecutive empty paragraphs - spacing by Enter")
    # tables
    for i, t in enumerate(d.tables):
        rows = t._tbl.findall(qn("w:tr"))
        if len(rows) < 2:
            continue
        hdr = rows[0].find(qn("w:trPr"))
        has_hdr = hdr is not None and hdr.find(qn("w:tblHeader")) is not None
        if not has_hdr:  # inherited from the table style's firstRow (Word moves it there)
            look = t._tbl.tblPr.find(qn("w:tblLook"))
            sid = t._tbl.tblPr.find(qn("w:tblStyle"))
            if sid is not None and (look is None or look.get(qn("w:firstRow")) in ("1", "true", None)):
                for st in d.styles.element.findall(qn("w:style")):
                    if st.get(qn("w:styleId")) == sid.get(qn("w:val")):
                        for tsp in st.findall(qn("w:tblStylePr")):
                            if tsp.get(qn("w:type")) == "firstRow" and tsp.find(
                                    qn("w:trPr") + "/" + qn("w:tblHeader")) is not None:
                                has_hdr = True
        borders = t._tbl.tblPr.find(qn("w:tblBorders"))
        decorative = borders is not None and all(b.get(qn("w:val")) in ("nil", "none") for b in borders)
        if decorative:
            continue  # layout table (signatures)
        if len(rows) >= 15 and not has_hdr:
            fails.append(f"table {i + 1}: {len(rows)} rows, header row not repeated (w:tblHeader)")
        elif len(rows) >= 8 and not has_hdr:
            warns.append(f"table {i + 1}: {len(rows)} rows without a repeated header row")
        split = sum(1 for r in rows if r.find(qn("w:trPr")) is None or r.find(qn("w:trPr")).find(qn("w:cantSplit")) is None)
        if len(rows) >= 8 and split:
            warns.append(f"table {i + 1}: {split} rows may break across pages (no w:cantSplit)")
    # images
    no_alt = sum(1 for dp in body.iter(qn("wp:docPr")) if not (dp.get("descr") or "").strip())
    if no_alt:
        fails.append(f"{no_alt} image(s)/chart(s) without alt text (docPr descr)")
    # headings and TOC
    styles_outline = set()
    for st in d.styles.element.findall(qn("w:style")):
        ol = st.find(qn("w:pPr") + "/" + qn("w:outlineLvl"))
        if ol is not None and int(ol.get(qn("w:val"))) <= 2:
            styles_outline.add(st.get(qn("w:styleId")))
    heads = 0
    for p in body.iter(qn("w:p")):
        ps = p.find(qn("w:pPr") + "/" + qn("w:pStyle"))
        if ps is not None and ps.get(qn("w:val")) in styles_outline:
            heads += 1
    instr = " ".join(it.text or "" for it in body.iter(qn("w:instrText")))
    has_toc = bool(_re.search(r"\bTOC\s+\\o", instr)) or "w:docPartGallery w:val=\"Table of Contents\"" in allxml
    try:
        pages = int(_re.search(r"<Pages>(\d+)</Pages>", z.read("docProps/app.xml").decode()).group(1))
    except (KeyError, AttributeError):
        pages = 0
    if heads >= 10 and pages >= 8 and not has_toc:
        fails.append(f"{heads} headings on {pages} pages and no table of contents (TOC field)")
    elif heads >= 10 and pages == 0 and not has_toc:
        warns.append(f"{heads} headings and no TOC (page count unknown - long document?)")
    if has_toc:
        # TOC result = paragraphs between the TOC field begin and its end
        toc_txt = ""
        inside = depth = 0
        for p in body.iter(qn("w:p")):
            for r in p.iter():
                if r.tag == qn("w:instrText") and _re.search(r"\bTOC\s+\\o", r.text or ""):
                    inside = 1
                elif inside and r.tag == qn("w:fldChar"):
                    tpe = r.get(qn("w:fldCharType"))
                    depth += 1 if tpe == "begin" else (-1 if tpe == "end" else 0)
                    if depth < 0:
                        inside = 0
                elif inside and r.tag == qn("w:t"):
                    toc_txt += r.text or ""
            if not inside and toc_txt:
                break
        if len(toc_txt.strip()) < 5:
            fails.append("TOC field has no entries - run finalizeaza_in_word (updates TOC) before delivery")
    # captions
    for p in d.paragraphs:
        if p.style is not None and p.style.name.lower() == "caption" and _re.match(r"(Tabelul|Figura)\s", p.text):
            if not any("SEQ" in (it.text or "") for it in p._p.iter(qn("w:instrText"))):
                warns.append(f"caption typed by hand (no SEQ field): {p.text[:40]!r}")
    if heads == 0 and len(d.paragraphs) > 120:
        warns.append("long document without heading styles - no outline, TOC or PDF bookmarks")
    if word:
        _tabele_word(path, fails)
        _pagini_goale_word(path, fails)


def _pagini_goale_word(path, fails):
    """Export a temporary PDF through Word and flag half-empty pages (pdf_ro.pagini_goale)."""
    import tempfile
    sys.path.insert(0, str(Path(__file__).parent))
    import pdf_ro
    tmp = Path(tempfile.mkdtemp()) / (Path(path).stem + ".pdf")
    pdf_ro.word_la_pdf(path, tmp)
    for pg, gol in pdf_ro.pagini_goale(tmp):
        fails.append(f"page {pg} is {int(gol * 100)}% empty and does not end a chapter")
    try:
        tmp.unlink()
    except OSError:
        pass


def _tabele_word(path, fails):
    """Exact check through Word: a table that crosses a page must repeat its header row."""
    import os
    import win32com.client
    app = win32com.client.DispatchEx("Word.Application")
    app.Visible = False
    try:
        doc = app.Documents.Open(os.path.abspath(str(path)), ReadOnly=True)
        doc.Repaginate()
        for i in range(1, doc.Tables.Count + 1):
            t = doc.Tables(i)
            a = doc.Range(t.Range.Start, t.Range.Start).Information(3)
            b = doc.Range(t.Range.End - 1, t.Range.End - 1).Information(3)
            if a != b and not t.Rows(1).HeadingFormat:
                fails.append(f"table {i} crosses pages {a}-{b} without a repeated header row")
        doc.Close(False)
    finally:
        app.Quit()


def complex_xlsx(path, fails, warns):
    """Workbook checks (phase 3): cached formula errors, failing check cells, hard-coded constants,
    validation on input sheets, print setup on every used sheet."""
    import openpyxl
    sys.path.insert(0, str(Path(__file__).parent))
    import xlsx_ro
    wbf = openpyxl.load_workbook(str(path))
    wbv = openpyxl.load_workbook(str(path), data_only=True)
    errs = []
    for ws in wbv.worksheets:
        for row in ws.iter_rows():
            for c in row:
                if isinstance(c.value, str) and (c.value in xlsx_ro.ERORI or c.value == "EROARE"):
                    errs.append(f"{ws.title}!{c.coordinate}={c.value}")
    if errs:
        fails.append(f"formula errors / failing checks: {errs[:6]}{' ...' if len(errs) > 6 else ''}")
    nform = sum(1 for ws in wbf.worksheets for row in ws.iter_rows() for c in row
                if isinstance(c.value, str) and c.value.startswith("="))
    if nform and all(c.value is None for ws in wbv.worksheets for row in ws.iter_rows() for c in row
                     if isinstance(wbf[ws.title][c.coordinate].value, str)
                     and str(wbf[ws.title][c.coordinate].value).startswith("=")):
        fails.append("formulas have no cached values - recalculate in Excel (finalizeaza_xlsx)")
    const = xlsx_ro.constante_in_formule(str(path))
    if const:
        warns.append(f"{len(const)} formula(s) with hard-coded constants, e.g. {const[0]} - move the "
                     "rate/limit to a labelled input cell")
    for ws in wbf.worksheets:
        if ws.max_row <= 1 and ws.max_column <= 1:
            continue
        unlocked = any(c.protection is not None and c.protection.locked is False
                       for row in ws.iter_rows() for c in row if c.value is not None)
        if (ws.tables or unlocked) and unlocked and not ws.data_validations.dataValidation:
            fails.append(f"sheet {ws.title!r}: input cells/tables without data validation")
        for dv in ws.data_validations.dataValidation:
            if not (dv.error or "").strip():
                warns.append(f"sheet {ws.title!r}: validation {dv.sqref} without an error message")
                break
        ps = ws.page_setup
        fit = ws.sheet_properties.pageSetUpPr is not None and ws.sheet_properties.pageSetUpPr.fitToPage
        if not ws.print_area and not fit:
            fails.append(f"sheet {ws.title!r}: no print setup (print area / fit to page)")
        elif ps.paperSize not in (9, "9"):
            warns.append(f"sheet {ws.title!r}: paper size not A4")
        if "&P" not in (ws.oddFooter.center.text or "") + (ws.oddFooter.right.text or "") + \
                (ws.oddFooter.left.text or ""):
            warns.append(f"sheet {ws.title!r}: footer without page number")
        if ws.sheet_view.showGridLines is not False:
            warns.append(f"sheet {ws.title!r}: gridlines shown")


def complex_pdf(path, fails, warns, text):
    sys.path.insert(0, str(Path(__file__).parent))
    import pdf_ro
    r = pdf_ro.verifica(str(path), afiseaza=False)
    fails.extend(r["fail"])
    warns.extend(r["warn"])
    low = text.lower()
    for e in ERORI_CAMPURI:
        if e in low:
            fails.append(f"field error text in the PDF: {e!r}")
    print(f"INFO  PDF {r['pagini']} pages, {r['bookmarks']} bookmarks, tagged={r['tagged']}, "
          f"lang={r['lang']}, labels={', '.join(x or '-' for x in r['etichete'][:8])}"
          f"{' ...' if len(r['etichete']) > 8 else ''}")


def main():
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    path = Path(sys.argv[1])
    word = "--word" in sys.argv[2:]
    ext_in = path.suffix.lower()
    paras, meta = text_and_meta(path)
    full = "\n".join(paras)
    low = full.lower()
    fails, warns = [], []

    # metadata
    bad_meta = ("python-docx", "openpyxl", "generated by", "reportlab", "chatgpt", "claude", "ai")
    for k, v in meta.items():
        if v is None or v == "":
            if k in ("author", "title"):
                warns.append(f"metadata {k} is empty")
            continue
        sv = str(v).lower()
        if any(b == sv or b in sv.split() or (len(b) > 2 and b in sv) for b in bad_meta):
            (warns if k in ("producer", "creator") else fails).append(f"metadata {k} = {v!r}")
    if ext_in in (".docx", ".xlsx") and meta.get("author") and \
            (meta.get("last_modified_by") or "") != meta.get("author"):
        fails.append(f"lastModifiedBy {meta.get('last_modified_by')!r} != author {meta.get('author')!r} "
                     "(Office stamps this PC's user - finalizeaza_in_word / finalizeaza_xlsx restore it)")
    if "2013-12-23" in str(meta.get("created", "")):
        fails.append("metadata created = python-docx template date 2013-12-23")

    for msg in LAYOUT:
        (fails if any(k in msg for k in ("not A4", "proofing language", "app.xml", "absPath")) else warns).append(msg)

    # Romanian typography
    ced = re.findall("[şţŞŢ]", full)
    if ced:
        fails.append(f"{len(ced)} cedilla diacritics (ş/ţ) - use ș/ț comma-below")
    straight = len(re.findall(r'"[^"\n]{1,80}"', full))
    if straight:
        warns.append(f"{straight} straight-quoted spans - Romanian uses „...”")
    if re.search(r"(?<![\d./])\d{1,2}\.\d{1,2}\.\d{2}(?![\d.])", full):
        warns.append("2-digit year date found")
    # drafting errors found by the round-1 review
    if re.search(r"\b(În atenția|In atentia|Referitor la|Privitor la)\s*:", full):
        warns.append("colon after 'În atenția'/'Referitor la' - they govern the next word; drop ':'")
    if re.search(r"\bNr\.?\s*\d+\s+/\s+\d", full):
        warns.append("registration number with spaces around '/' - write Nr. 418/22.11.2026")
    if re.search(r"\bRO\d{2}\s?AAAA", full):
        fails.append("textbook sample IBAN (bank code AAAA)")
    if re.search(r"\bCUI\s+RO\s+\d", full):
        warns.append("'CUI RO 123..' - VAT code is written RO12345678 without space")
    sys.path.insert(0, str(Path(__file__).parent))
    import cifre_ro
    de = cifre_ro.lipsa_de(full)
    if de:
        fails.append(f"DOOM 'de' rule (de after 20-99 and 00, none after 1-19/x01-x19): {de[:6]}")
    import datetime as _dt
    for dd, mm, yy in re.findall(r"(?<![\d./])(\d{2})\.(\d{2})\.(\d{4})(?![\d.])", full):
        try:
            if _dt.date(int(yy), int(mm), int(dd)).weekday() >= 5:
                warns.append(f"date {dd}.{mm}.{yy} falls on a weekend - check it is plausible")
        except ValueError:
            fails.append(f"invalid date {dd}.{mm}.{yy}")
    if re.search(r"\b(remindere?|feedback-ul|deadline-ul|meeting-ul|task-ul|a adresa o problemă|face sens)\b", low):
        warns.append("anglicism/calque (reminder, deadline, a adresa o problemă, face sens)")
    for marker in ("lorem ipsum", "[todo", "todo:", "generated by", "as an ai", "ca model de limbaj"):
        if marker in low:
            fails.append(f"leftover marker: {marker!r}")

    # AI tells
    words = max(len(full.split()), 1)
    em = full.count("—")
    if em and em / words * 1000 > 2:
        warns.append(f"em dash density {em} in {words} words")
    elif em:
        warns.append(f"{em} em dash(es) - check each is deliberate")
    en_dash = full.count(" – ")
    real = [p for p in paras if len(p.split()) >= 15]
    if en_dash > max(3, len(real)):
        warns.append(f"{en_dash} spaced en dashes - more than one per paragraph")
    hits = [p for p in PHRASES_RO + PHRASES_EN if p in low]
    if hits:
        (fails if len(hits) >= 4 else warns).append(f"AI-tell phrases: {hits}")
    if EMOJI.search(full):
        fails.append("emoji in document")
    triplets = len(re.findall(r"\b\w+, \w+ (și|and) \w+\b", full))
    if triplets > max(2, len(real) // 2):
        warns.append(f"{triplets} 'a, b și c' triplets - rule-of-three pattern")
    if len(real) >= 5:
        lens = [len(p.split()) for p in real]
        cv = statistics.pstdev(lens) / statistics.mean(lens)
        if cv < 0.25:
            warns.append(f"uniform paragraph lengths (CV {cv:.2f}) - vary them")
    hedges = len(re.findall(r"\b(ar putea|poate|eventual|probabil|might|may|could|potentially)\b", low))
    if hedges / words * 100 > 1.5:
        warns.append(f"hedge density {hedges} in {words} words")

    if "--date" in sys.argv:
        import json
        mp = Path(sys.argv[sys.argv.index("--date") + 1])
        model = json.loads(mp.read_text(encoding="utf-8"))
        ign = ()
        if "--ignora-valori" in sys.argv:
            ign = [float(x) for x in sys.argv[sys.argv.index("--ignora-valori") + 1].split(",")]
        nep = cifre_ro.reconciliaza_text(full, model, ignora=ign)
        zec = [t for t in nep if "," in t]
        intregi = [t for t in nep if "," not in t]
        if zec:
            fails.append(f"{len(zec)} amount(s) in the text not found in {mp.name}: {zec[:8]}")
        if intregi:
            warns.append(f"{len(intregi)} number(s)/count(s) in the text not found in {mp.name}: {intregi[:8]}")
        print(f"INFO  reconciled numbers against {mp.name}: {len(cifre_ro.numere_in_text(full))} found, "
              f"{len(nep)} unmatched")

    ext = path.suffix.lower()
    if ext == ".docx":
        complex_docx(path, fails, warns, word=word)
    elif ext == ".xlsx":
        complex_xlsx(path, fails, warns)
    elif ext == ".pdf":
        complex_pdf(path, fails, warns, full)

    for f in fails:
        print("FAIL ", f)
    for w in warns:
        print("WARN ", w)
    print(f"RESULT {'FAIL' if fails else 'PASS'}  fails={len(fails)} warns={len(warns)} words={words}")
    sys.exit(1 if fails else 0)


if __name__ == "__main__":
    main()
