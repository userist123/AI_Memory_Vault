"""PDF helpers for document-uman: Word/Excel -> PDF through Microsoft Office, dossier merge with
rebuilt bookmarks and page labels, catalog fixes (/Lang, DisplayDocTitle, bookmarks panel) and a
checker.

    import pdf_ro
    pdf_ro.word_la_pdf(r"...\\Raport.docx")                       # bookmarks from headings, tagged
    pdf_ro.excel_la_pdf(r"...\\Situatie.xlsx")                    # RO separators, print areas
    pdf_ro.uneste([{"pdf": a, "titlu": "Convocator", "etichete": [(0, "", "D", 1, "C")]}, ...],
                  out, titlu=..., autor=...)
    pdf_ro.verifica(out)  # prints a report; returns dict

Limits (verified on this PC, PyMuPDF 1.28, pypdf 6.16, no pikepdf/veraPDF):
- Word's UseISO19005_1 gives PDF/A-1 (PDF 1.4): no transparency allowed, so semi-transparent
  PNG charts get flattened by Word; use opaque PNGs. A merged file is NOT PDF/A any more (PyMuPDF
  re-save drops the pdfaid claim from the merged XMP and the structure tree) - deliver the Word
  PDF/A separately when PDF/A is required, and validate with veraPDF.
- Excel's ExportAsFixedFormat has no bookmarks/tags/PDF-A options; bookmarks are added in uneste().
- Merging keeps the page content, links and fonts, not the tag tree: a merged dossier is not an
  accessible (tagged) PDF. Keep the tagged Word PDF as the accessible version. (PyMuPDF insert_pdf
  and pypdf both drop /StructTreeRoot; merging tag trees needs Acrobat or pikepdf + manual work.)
- Honest metadata (2026-11-22 review): a merged file is written by MuPDF ("% Written by MuPDF"
  in the header). uneste() therefore sets Producer = "MuPDF x.y" (what wrote the file), Creator =
  the applications that made the parts (Word, Excel), CreationDate/ModDate = now, and an XMP packet
  with the same values; it never copies "Microsoft Word" into Producer and removes the stray
  /Info that PyMuPDF puts inside the Catalog. Single-source PDFs exported by Word keep Word's
  metadata untouched.
"""
import os
import sys
from pathlib import Path

WD_FORMAT_PDF = 17
WD_HEADING_BOOKMARKS = 1


def word_la_pdf(docx, pdf=None, pdfa=False, bookmarks=True, tags=True, etichete=None):
    """Export through Word: bookmarks from headings (outline levels), document structure tags,
    document properties (Title -> window title), fonts embedded (BitmapMissingFonts only as
    fallback). pdfa=True -> PDF/A-1 (no transparency; see module doc).
    etichete: page labels [(start_page0, prefix, style, first), ...], e.g. cover + roman + decimal:
    [(0, "Copertă", "", 1), (1, "", "r", 1), (4, "", "D", 1)]."""
    import win32com.client
    src = Path(docx).resolve()
    out = Path(pdf).resolve() if pdf else src.with_suffix(".pdf")
    app = win32com.client.DispatchEx("Word.Application")
    app.Visible = False
    app.DisplayAlerts = 0
    try:
        doc = app.Documents.Open(str(src), ReadOnly=True)
        doc.ExportAsFixedFormat(OutputFileName=str(out), ExportFormat=WD_FORMAT_PDF,
                                OpenAfterExport=False, OptimizeFor=0, Range=0,
                                IncludeDocProps=True, KeepIRM=True,
                                CreateBookmarks=WD_HEADING_BOOKMARKS if bookmarks else 0,
                                DocStructureTags=tags, BitmapMissingFonts=True,
                                UseISO19005_1=pdfa)
        doc.Close(False)
    finally:
        app.Quit()
    seteaza_catalog(out, lang="ro-RO", afiseaza_titlu=True, panou_bookmarks=bookmarks,
                    incremental=True, etichete=etichete)
    return out


def excel_la_pdf(xlsx, pdf=None, foi=None):
    """Export through Excel with Romanian separators (1.234,56) even on an en-US Windows; print
    areas and page setup of each sheet are respected. foi: list of sheet names to export as ONE print
    job (so '&P din &N' in the footers runs across them); default: the whole workbook."""
    import win32com.client
    src = Path(xlsx).resolve()
    out = Path(pdf).resolve() if pdf else src.with_suffix(".pdf")
    app = win32com.client.DispatchEx("Excel.Application")
    app.Visible = False
    app.DisplayAlerts = False
    old = (app.UseSystemSeparators, app.DecimalSeparator, app.ThousandsSeparator)
    try:
        app.UseSystemSeparators = False
        app.DecimalSeparator, app.ThousandsSeparator = ",", "."
        wb = app.Workbooks.Open(str(src), ReadOnly=True)
        if foi:
            wb.Worksheets(foi[0]).Activate()
            for i, nume in enumerate(foi):
                wb.Worksheets(nume).Select(i == 0)  # Replace=False after the first -> group selection
            app.ActiveSheet.ExportAsFixedFormat(0, str(out), 0, True, False)
        else:
            wb.ExportAsFixedFormat(0, str(out), 0, True, False)  # xlTypePDF, std quality, docprops
        wb.Close(False)
    finally:
        app.UseSystemSeparators, app.DecimalSeparator, app.ThousandsSeparator = old
        app.Quit()
    return out


def _eticheta(e):
    s0, prefix, style, first = e[:4]
    lab = {"startpage": s0, "prefix": prefix, "firstpagenum": first}
    if style:
        lab["style"] = style
    return lab


def _prefix_unicode(doc):
    """PyMuPDF writes label prefixes as raw UTF-8 bytes (octal escapes); viewers read PDFDocEncoding
    and show mojibake. Rewrite every non-ASCII prefix as a UTF-16BE hex string."""
    import re as _re
    cat = doc.pdf_catalog()
    typ, val = doc.xref_get_key(cat, "PageLabels")
    if typ != "dict" or "\\" not in val:
        return

    def hexp(m):
        raw = _re.sub(r"\\([0-7]{3})", lambda o: chr(int(o.group(1), 8)), m.group(1))
        txt = raw.encode("latin-1").decode("utf-8")
        return "/P<FEFF" + txt.encode("utf-16-be").hex().upper() + ">"
    doc.xref_set_key(cat, "PageLabels", _re.sub(r"/P\(([^)]*)\)", hexp, val))


def seteaza_catalog(pdf, lang="ro-RO", afiseaza_titlu=True, panou_bookmarks=True, incremental=True,
                    doc=None, etichete=None):
    """/Lang, ViewerPreferences/DisplayDocTitle, PageMode /UseOutlines (opens with bookmarks)."""
    import pymupdf
    own = doc is None
    doc = doc or pymupdf.open(str(pdf))
    cat = doc.pdf_catalog()
    if lang:  # Word writes /Lang from the UI language (en), not from the text
        doc.xref_set_key(cat, "Lang", f"({lang})")
    if afiseaza_titlu:
        doc.xref_set_key(cat, "ViewerPreferences", "<</DisplayDocTitle true>>")
    if etichete:
        doc.set_page_labels([_eticheta(e) for e in etichete])
        _prefix_unicode(doc)
    if panou_bookmarks and doc.get_toc():
        doc.xref_set_key(cat, "PageMode", "/UseOutlines")
    if own:
        if incremental and doc.can_save_incrementally():
            doc.saveIncr()
        else:
            tmp = str(pdf) + ".tmp"
            doc.save(tmp, garbage=3, deflate=True)
            doc.close()
            os.replace(tmp, str(pdf))
            return
        doc.close()


def _data_pdf(now=None):
    import datetime as _dt
    now = now or _dt.datetime.now().astimezone()
    off = now.utcoffset() or _dt.timedelta(0)
    m = int(off.total_seconds() // 60)
    sign = "+" if m >= 0 else "-"
    return (now.strftime("D:%Y%m%d%H%M%S") + f"{sign}{abs(m) // 60:02d}'{abs(m) % 60:02d}'",
            now.isoformat(timespec="seconds"))


def _xmp(titlu, autor, subiect, creator, producer, iso, lang="ro-RO"):
    from xml.sax.saxutils import escape as e
    return ('<?xpacket begin="\ufeff" id="W5M0MpCehiHzreSzNTczkc9d"?>\n'
            '<x:xmpmeta xmlns:x="adobe:ns:meta/"><rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">'
            '<rdf:Description rdf:about="" xmlns:dc="http://purl.org/dc/elements/1.1/" '
            'xmlns:xmp="http://ns.adobe.com/xap/1.0/" xmlns:pdf="http://ns.adobe.com/pdf/1.3/">'
            '<dc:format>application/pdf</dc:format>'
            f'<dc:title><rdf:Alt><rdf:li xml:lang="x-default">{e(titlu)}</rdf:li></rdf:Alt></dc:title>'
            f'<dc:creator><rdf:Seq><rdf:li>{e(autor)}</rdf:li></rdf:Seq></dc:creator>'
            f'<dc:description><rdf:Alt><rdf:li xml:lang="x-default">{e(subiect)}</rdf:li></rdf:Alt></dc:description>'
            f'<dc:language><rdf:Bag><rdf:li>{lang}</rdf:li></rdf:Bag></dc:language>'
            f'<xmp:CreatorTool>{e(creator)}</xmp:CreatorTool><xmp:CreateDate>{iso}</xmp:CreateDate>'
            f'<xmp:ModifyDate>{iso}</xmp:ModifyDate><xmp:MetadataDate>{iso}</xmp:MetadataDate>'
            f'<pdf:Producer>{e(producer)}</pdf:Producer></rdf:Description></rdf:RDF></x:xmpmeta>\n'
            '<?xpacket end="w"?>')


def uneste(parti, out, titlu, autor, subiect="", cuvinte="", lang="ro-RO", pastreaza_bookmarks=True,
           creator=None, etichete_continue=False):
    """Merge PDFs into a dossier.
    parti: list of dicts {"pdf": path, "titlu": bookmark title of the part,
                          "etichete": [(start_in_part, prefix, style, first_number), ...],
                          "subcapitole": [(title, page_in_part_1based), ...]}  (optional)
      style: "D" decimal, "r" roman lower, "R", "a", "A", "" (prefix only).
    Each part gets one level-1 bookmark; its own bookmarks (from Word headings) go one level
    deeper (pastreaza_bookmarks). Page labels make the viewer page box match the printed numbers.
    Metadata Title/Author/Subject/Keywords; Producer = MuPDF (it writes the merged file - claiming
    Word there was the 2026-11-22 finding); Creator = `creator` or the distinct Creator apps of the
    parts ("Microsoft® Word 2021; Microsoft® Excel 2021"); dates = now; XMP mirrors /Info.
    etichete_continue=True: one page-label range 1..N for the whole dossier (so the viewer page box
    matches the page numbers written in the opis); otherwise labels per part."""
    import pymupdf
    res = pymupdf.open()
    toc, labels = [], []
    aplicatii = []
    for parte in parti:
        src = pymupdf.open(str(parte["pdf"]))
        app = (src.metadata or {}).get("creator") or ""
        if app and app not in aplicatii:
            aplicatii.append(app)
        start = res.page_count
        res.insert_pdf(src, links=True, annots=True)
        toc.append([1, parte["titlu"], start + 1])
        for titlu_sub, pagina_sub in parte.get("subcapitole", []):
            toc.append([2, titlu_sub, start + pagina_sub])
        if pastreaza_bookmarks:
            for lvl, t, pg in src.get_toc(simple=True):
                if pg > 0:
                    toc.append([lvl + 1, t, start + pg])
        for et in parte.get("etichete", [(0, "", "D", 1)]):
            labels.append(_eticheta((start + et[0],) + tuple(et[1:4])))
        src.close()
    res.set_toc(toc)
    res.set_page_labels([_eticheta((0, "", "D", 1))] if etichete_continue else labels)
    _prefix_unicode(res)
    # a real tool writes ONE Creator: the application of the main (first) part
    creator = creator if creator is not None else (aplicatii[0] if aplicatii else "")
    producer = f"MuPDF {pymupdf.mupdf_version}"
    d_pdf, d_iso = _data_pdf()
    meta = {"title": titlu, "author": autor, "subject": subiect, "keywords": cuvinte,
            "creator": creator, "producer": producer, "creationDate": d_pdf, "modDate": d_pdf}
    res.set_metadata(meta)
    res.set_xml_metadata(_xmp(titlu, autor, subiect, creator, producer, d_iso, lang))
    cat = res.pdf_catalog()
    if res.xref_get_key(cat, "Info")[0] != "null":   # PyMuPDF's stray /Info inside the Catalog
        res.xref_set_key(cat, "Info", "null")
    seteaza_catalog(out, lang=lang, doc=res)
    res.save(str(out), garbage=3, deflate=True)
    res.close()
    return out


def pagini_goale(pdf, prag=0.6, ignora=()):
    """Pages whose body is more than `prag` empty and that do not end a chapter.
    Body = page minus header/footer bands (top 6.3 %, bottom 6.2 % of the height); content = text,
    image and vector blocks (table rules, shading). A page ends a chapter when it is the last page or
    the next page starts (top 20 %) with a level-1 bookmark (chapter, annex, dossier part), or with a
    level-2 bookmark when all level-1 entries open a page (merged dossier: part > chapter).
    Returns [(page_1based, empty_fraction)]."""
    import pymupdf
    doc = pymupdf.open(str(pdf))
    toc = doc.get_toc(simple=False)
    top_of = {}

    def sus(pg_i, y):
        h = doc[pg_i].rect.height
        return y <= 0.20 * h

    niveluri = {}
    for lvl, _t, pg, dest in toc:
        if pg < 1:
            continue
        to = dest.get("to") if isinstance(dest, dict) else None
        y = (doc[pg - 1].rect.height - to.y) if to is not None and to.y > doc[pg - 1].rect.height * 0.5             and False else (to.y if to is not None else 0)
        # PyMuPDF gives 'to' in top-left coordinates for get_toc(simple=False)
        niveluri.setdefault(lvl, []).append((pg - 1, sus(pg - 1, y)))
    # level 1 always counts (a Heading 1 / annex / dossier part that opens a page); level 2 too when
    # every level-1 entry opens a page (dossier: parts at level 1, report chapters at level 2)
    v1 = niveluri.get(1, [])
    cap = {1} | ({2} if v1 and sum(1 for _, s in v1 if s) / len(v1) >= 0.9 else set())
    for lvl, v in niveluri.items():
        if lvl in cap:
            for pg_i, s in v:
                if s:
                    top_of[pg_i] = True
    out = []
    for i, page in enumerate(doc):
        if i + 1 in ignora or i == doc.page_count - 1 or top_of.get(i + 1):
            continue
        H = page.rect.height
        b0, b1 = 0.067 * H, 0.933 * H
        ys = [bl[3] for bl in page.get_text("blocks") if bl[1] >= 0.063 * H and bl[3] <= 0.938 * H
              and (bl[4].strip() if bl[6] == 0 else True)]
        for dr in page.get_drawings():
            r = dr.get("rect")
            if r is not None and r.y0 >= 0.063 * H and r.y1 <= 0.938 * H:
                ys.append(r.y1)
        jos = max(ys) if ys else b0
        gol = 1 - max(0.0, min(1.0, (jos - b0) / (b1 - b0)))
        if gol > prag:
            out.append((i + 1, round(gol, 2)))
    doc.close()
    return out


def _etichete_pypdf(pdf, doc):
    """Page labels as a viewer shows them (pypdf decodes UTF-16 prefixes; PyMuPDF does not)."""
    cat = doc.pdf_catalog()
    if doc.xref_get_key(cat, "PageLabels")[0] == "null":
        return [""] * doc.page_count
    from pypdf import PdfReader
    return list(PdfReader(str(pdf)).page_labels)


def verifica(pdf, afiseaza=True):
    """Report: pages, bookmarks, page labels, metadata, /Lang, DisplayDocTitle, tagging, fonts not
    embedded, page sizes. Returns dict with 'fail' and 'warn' lists."""
    import pymupdf
    doc = pymupdf.open(str(pdf))
    cat = doc.pdf_catalog()
    r = {"pagini": doc.page_count, "bookmarks": len(doc.get_toc()), "meta": doc.metadata,
         "lang": doc.xref_get_key(cat, "Lang")[1],
         "display_title": doc.xref_get_key(cat, "ViewerPreferences/DisplayDocTitle")[1],
         "tagged": doc.xref_get_key(cat, "MarkInfo/Marked")[1] == "true"
                   or doc.xref_get_key(cat, "StructTreeRoot")[0] != "null",
         "etichete": _etichete_pypdf(pdf, doc),
         "fail": [], "warn": []}
    neinglobate = set()
    for page in doc:
        for f in page.get_fonts(full=True):
            if f[1] in ("n/a", "") or not f[1]:
                neinglobate.add(f[3])
    r["fonturi_neinglobate"] = sorted(neinglobate)
    sizes = {(round(p.rect.width), round(p.rect.height)) for p in doc}
    sizes_norm = {tuple(sorted(s)) for s in sizes}
    r["formate_pagina"] = sorted(sizes)
    m = r["meta"] or {}
    if not m.get("title"):
        r["fail"].append("PDF Title metadata empty - viewer shows the file name")
    if r["pagini"] >= 10 and r["bookmarks"] == 0:
        r["fail"].append(f"{r['pagini']} pages and no bookmarks")
    if neinglobate:
        r["fail"].append(f"fonts not embedded: {r['fonturi_neinglobate']}")
    if r["lang"] in ("null", "", None):
        r["warn"].append("no /Lang in the catalog (screen readers, PDF/UA)")
    if r["display_title"] != "true":
        r["warn"].append("ViewerPreferences/DisplayDocTitle not true")
    import re as _re
    libs = ("reportlab", "python", "pymupdf", "mupdf", "pypdf", "openpyxl", "docx")
    for bad in libs:
        if bad in (m.get("creator") or "").lower():
            r["warn"].append(f"Creator mentions {bad!r} - Creator names the authoring application")
    with open(str(pdf), "rb") as fh:
        head = fh.read(1024)
    scris = _re.search(rb"%\s*Written by ([^\r\n]+)", head)
    prod_l = (m.get("producer") or "").lower()
    if scris:
        w = scris.group(1).decode("latin-1").strip()
        if "mupdf" in w.lower() and "mupdf" not in prod_l:
            r["fail"].append(f"Producer {m.get('producer')!r} but the file header says 'Written by {w}' - "
                             "a merged file must not claim Word (pdf_ro.uneste sets it honestly)")
    elif any(b in prod_l for b in libs):
        r["warn"].append(f"Producer mentions a library ({m.get('producer')!r})")
    xmp = doc.get_xml_metadata() or ""
    xp = _re.search(r"<pdf:Producer>([^<]*)</pdf:Producer>|pdf:Producer=\"([^\"]*)\"", xmp)
    if xp:
        xv = (xp.group(1) or xp.group(2) or "").strip()
        if xv != (m.get("producer") or "").strip():
            r["fail"].append(f"XMP pdf:Producer {xv!r} differs from /Info Producer {m.get('producer')!r}")
    xt = _re.search(r"<xmp:CreatorTool>([^<]*)</xmp:CreatorTool>", xmp)
    if xt and xt.group(1).strip() != (m.get("creator") or "").strip():
        r["fail"].append(f"XMP CreatorTool {xt.group(1)!r} differs from /Info Creator {m.get('creator')!r}")
    if not m.get("creationDate") or not m.get("modDate"):
        r["warn"].append("CreationDate/ModDate missing in /Info")
    if doc.xref_get_key(cat, "Info")[0] != "null":
        r["fail"].append("stray /Info inside the Catalog (PyMuPDF new-document leftover)")
    if len(sizes_norm) > 1:
        r["warn"].append(f"mixed page sizes {r['formate_pagina']} (landscape pages are fine)")
    r["pagini_goale"] = pagini_goale(pdf)
    for pg, gol in r["pagini_goale"]:
        r["fail"].append(f"page {pg} is {int(gol * 100)}% empty and does not end a chapter - move a table/"
                         "figure, shorten keep-with-next chains or end the chapter there")
    labels = [x for x in r["etichete"] if x]
    if r["pagini"] >= 10 and not labels:
        r["warn"].append("no page labels - the viewer page box will not match printed numbers")
    if afiseaza:
        print(f"PDF {pdf}: {r['pagini']} pagini, {r['bookmarks']} bookmarks, tagged={r['tagged']}, "
              f"lang={r['lang']}, DisplayDocTitle={r['display_title']}")
        print(f"  title={m.get('title')!r} author={m.get('author')!r} producer={m.get('producer')!r}")
        print(f"  etichete: {', '.join(x or '-' for x in r['etichete'])}")
        print(f"  formate pagina (pt): {r['formate_pagina']}; fonturi neinglobate: {r['fonturi_neinglobate']}")
        for f in r["fail"]:
            print("FAIL ", f)
        for w in r["warn"]:
            print("WARN ", w)
    doc.close()
    return r


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    res = verifica(sys.argv[1])
    sys.exit(1 if res["fail"] else 0)
