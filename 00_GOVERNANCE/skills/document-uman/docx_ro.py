"""Builder for Romanian professional .docx files with sane defaults.

Import with: sys.path.insert(0, r"<skill dir>"); import docx_ro
Everything is set through styles; metadata is clean; page is A4.
"""
import datetime
import re

import docx
from docx.enum.section import WD_SECTION
from docx.enum.table import WD_TABLE_ALIGNMENT
from docx.enum.text import WD_ALIGN_PARAGRAPH, WD_TAB_ALIGNMENT, WD_BREAK
from docx.oxml import OxmlElement
from docx.oxml.ns import qn
from docx.shared import Cm, Pt, RGBColor

from pathlib import Path
BAZA = Path(__file__).with_name("baza_ro.docx")

LUNI = ["ianuarie", "februarie", "martie", "aprilie", "mai", "iunie", "iulie",
        "august", "septembrie", "octombrie", "noiembrie", "decembrie"]


def data_lunga(d: datetime.date) -> str:
    return f"{d.day} {LUNI[d.month - 1]} {d.year}"


def data_scurta(d: datetime.date) -> str:
    return d.strftime("%d.%m.%Y")


def suma(x: float, moneda: str = "lei") -> str:
    """1.234,56 lei - rounded ROUND_HALF_UP through cifre_ro (Excel-compatible, not half-even)."""
    import cifre_ro
    return f"{cifre_ro.ro(x, 2)} {moneda}"


def _set_font(style, name, size, bold=None, italic=None, color=None):
    style.font.name = name
    style.font.size = Pt(size)
    rpr = style.element.get_or_add_rPr()
    fonts = rpr.find(qn("w:rFonts"))
    if fonts is None:
        fonts = OxmlElement("w:rFonts")
        rpr.append(fonts)
    for attr in ("w:ascii", "w:hAnsi", "w:cs", "w:eastAsia"):
        fonts.set(qn(attr), name)
    # theme attributes override explicit fonts (Title/Heading use majorHAnsi) - drop them
    for attr in ("w:asciiTheme", "w:hAnsiTheme", "w:cstheme", "w:eastAsiaTheme"):
        if fonts.get(qn(attr)) is not None:
            del fonts.attrib[qn(attr)]
    if bold is not None:
        style.font.bold = bold
    if italic is not None:
        style.font.italic = italic
    if color is not None:
        style.font.color.rgb = RGBColor.from_string(color)


def new_document(author: str, title: str, kind: str = "scrisoare",
                 font: str | None = None, size: float | None = None, justify: bool | None = None):
    """kind: 'scrisoare' (cerere, adresă, notificare, contract) or 'raport'."""
    # base created by Microsoft Word on this PC (ro-RO), not the python-docx default template
    d = docx.Document(str(BAZA))
    official = kind != "raport"
    font = font or ("Times New Roman" if official else "Calibri")
    size = size or (12 if official else 11)
    justify = official if justify is None else justify

    for s in d.sections:
        s.page_width, s.page_height = Cm(21), Cm(29.7)
        s.left_margin = Cm(2.5)
        s.right_margin = Cm(2)
        s.top_margin = s.bottom_margin = Cm(2)

    st = d.styles
    _set_font(st["Normal"], font, size)
    pf = st["Normal"].paragraph_format
    pf.space_before = Pt(0)
    pf.space_after = Pt(6 if official else 8)
    pf.line_spacing = 1.15
    pf.alignment = WD_ALIGN_PARAGRAPH.JUSTIFY if justify else WD_ALIGN_PARAGRAPH.LEFT
    pf.widow_control = True

    _set_font(st["Title"], font, size + 2 if official else 22, bold=official, color="000000")
    st["Title"].paragraph_format.alignment = WD_ALIGN_PARAGRAPH.CENTER if official else WD_ALIGN_PARAGRAPH.LEFT
    st["Title"].paragraph_format.space_before = Pt(12)
    st["Title"].paragraph_format.space_after = Pt(12)
    # remove the blue bottom border Word puts on Title
    ppr = st["Title"].element.get_or_add_pPr()
    for b in ppr.findall(qn("w:pBdr")):
        ppr.remove(b)

    accent = "000000" if official else "1F3864"
    for lvl, sz in ((1, size + 3), (2, size + 1), (3, size)):
        h = st[f"Heading {lvl}"]
        _set_font(h, font, sz, bold=True, italic=False, color=accent)
        h.paragraph_format.space_before = Pt(12 if lvl == 1 else 8)
        h.paragraph_format.space_after = Pt(4)
        h.paragraph_format.keep_with_next = True
        h.paragraph_format.alignment = WD_ALIGN_PARAGRAPH.LEFT
    _set_font(st["Caption"], font, size - 1, italic=True, color="404040")

    # proofing language ro-RO on the default run properties and Normal style
    for rpr in (st["Normal"].element.get_or_add_rPr(),):
        lang = rpr.find(qn("w:lang"))
        if lang is None:
            lang = OxmlElement("w:lang")
            rpr.append(lang)
        lang.set(qn("w:val"), "ro-RO")
        lang.set(qn("w:eastAsia"), "ro-RO")
        lang.set(qn("w:bidi"), "ar-SA")
    defaults = d.styles.element.find(qn("w:docDefaults"))
    if defaults is not None:
        for l in defaults.iter(qn("w:lang")):
            l.set(qn("w:val"), "ro-RO")

    cp = d.core_properties
    now = datetime.datetime.now(datetime.timezone.utc).replace(tzinfo=None, microsecond=0)
    cp.author = cp.last_modified_by = author
    cp.title = title
    cp.comments = cp.subject = cp.keywords = cp.category = ""
    cp.created = cp.modified = now
    cp.revision = 1
    return d


_NB = [
    (re.compile(r"(\d)\s+(" + "|".join(LUNI) + r")\b"), "\\1\u00a0\\2"),      # 15 decembrie
    (re.compile(r"\b(nr|art|alin|lit|pct|str|bl|sc|ap|et)\.\s+(?=\w)"), "\\1.\u00a0"),  # nr. 12
    (re.compile(r"(\d)\s+(lei|euro|EUR|RON|kg|t|mp|m|km|zile|ore|%)\b"), "\\1\u00a0\\2"),
    (re.compile(r"\b(0\d{3}) (\d{3}) (\d{3})\b"), "\\1\u00a0\\2\u00a0\\3"),   # 0745 333 444
    (re.compile(r"(\d) (\d{3})\b"), "\\1\u00a0\\2"),
    (re.compile(r"\b(alin|art)\.\u00a0?\s*(\(?\d+\)?)"), "\\1.\u00a0\\2"),          # alin. (1)
    (re.compile(r"\b(lit|pct)\.\u00a0?\s*([a-z0-9]\))"), "\\1.\u00a0\\2"),          # lit. a)
    (re.compile(r"\bRO\d{2}(?: [A-Z0-9]{4}){1,7}"), lambda m: m.group(0).replace(" ", "\u00a0")),  # IBAN
]


def nbsp(text: str) -> str:  # noqa: D401
    """Non-breaking spaces where Romanian typography forbids a line break."""
    for rx, rep in _NB:
        text = rx.sub(rep, text)
    return text


def par(d, text="", align=None, bold_prefix=None, indent_first=False, space_after=None, style=None):
    p = d.add_paragraph(style=style)
    if bold_prefix:
        p.add_run(nbsp(bold_prefix)).bold = True
    if text:
        p.add_run(nbsp(text))
    if align == "center":
        p.alignment = WD_ALIGN_PARAGRAPH.CENTER
    elif align == "right":
        p.alignment = WD_ALIGN_PARAGRAPH.RIGHT
    elif align == "left":
        p.alignment = WD_ALIGN_PARAGRAPH.LEFT
    if indent_first:
        p.paragraph_format.first_line_indent = Cm(1.25)
    if space_after is not None:
        p.paragraph_format.space_after = Pt(space_after)
    return p


def titlu(d, text):
    return d.add_paragraph(text, style="Title")


def spatiu(d, pt=12):
    p = d.add_paragraph()
    p.paragraph_format.space_after = Pt(0)
    p.paragraph_format.space_before = Pt(0)
    p.paragraph_format.line_spacing = Pt(pt)
    return p


def doua_coloane(d, stanga, dreapta, space_before=18):
    """Date on the left, signature on the right, on the same lines (tab stop at the right margin)."""
    sec = d.sections[-1]
    width = sec.page_width - sec.left_margin - sec.right_margin
    st, dr = stanga.split("\n"), dreapta.split("\n")
    n = max(len(st), len(dr))
    st += [""] * (n - len(st))
    dr += [""] * (n - len(dr))
    for i, (a, b) in enumerate(zip(st, dr)):
        p = d.add_paragraph()
        p.alignment = WD_ALIGN_PARAGRAPH.LEFT
        p.paragraph_format.tab_stops.add_tab_stop(width, WD_TAB_ALIGNMENT.RIGHT)
        p.paragraph_format.space_after = Pt(0)
        if i == 0:
            p.paragraph_format.space_before = Pt(space_before)
        p.add_run(f"{a}\t{b}")


def incheiere(d, formula, nume, functie=None, space_before=12, loc_semnatura=36):
    """'Cu stimă,' + room for a handwritten signature + name/function, left-aligned."""
    p = par(d, formula, align="left", space_after=0)
    p.paragraph_format.space_before = Pt(space_before)
    p.paragraph_format.keep_with_next = True
    gap = par(d, "", space_after=0)
    gap.paragraph_format.line_spacing = Pt(loc_semnatura)
    gap.paragraph_format.keep_with_next = True
    q = par(d, nume, align="left", space_after=0)
    if functie:
        q.paragraph_format.keep_with_next = True
        par(d, functie, align="left", space_after=0)


def semnatura_dreapta(d, linii, space_before=18):
    for i, l in enumerate(linii.split("\n")):
        p = d.add_paragraph(l)
        p.alignment = WD_ALIGN_PARAGRAPH.RIGHT
        p.paragraph_format.space_after = Pt(0)
        if i == 0:
            p.paragraph_format.space_before = Pt(space_before)


def numar_pagina(d, format_ro=True):
    """Footer 'Pagina X din Y', centered, smaller."""
    for sec in d.sections:
        p = sec.footer.paragraphs[0]
        p.alignment = WD_ALIGN_PARAGRAPH.CENTER
        def field(code):
            r = p.add_run()
            for t, val in (("begin", None), (None, code), ("end", None)):
                if t:
                    e = OxmlElement("w:fldChar"); e.set(qn("w:fldCharType"), t); r._r.append(e)
                else:
                    e = OxmlElement("w:instrText"); e.set(qn("xml:space"), "preserve"); e.text = val; r._r.append(e)
            r.font.size = Pt(9)
        lbl = p.add_run("Pagina "); lbl.font.size = Pt(9)
        field("PAGE")
        mid = p.add_run(" din "); mid.font.size = Pt(9)
        field("NUMPAGES")


def tabel(d, header, rows, widths_cm=None, align_right_cols=()):
    t = d.add_table(rows=1, cols=len(header))
    t.style = "Table Grid"
    t.alignment = WD_TABLE_ALIGNMENT.CENTER
    for i, h in enumerate(header):
        c = t.rows[0].cells[i]
        c.text = ""
        r = c.paragraphs[0].add_run(h)
        r.bold = True
        c.paragraphs[0].alignment = WD_ALIGN_PARAGRAPH.CENTER
    for row in rows:
        cells = t.add_row().cells
        for i, v in enumerate(row):
            cells[i].text = str(v)
            cells[i].paragraphs[0].alignment = (WD_ALIGN_PARAGRAPH.RIGHT if i in align_right_cols
                                                else WD_ALIGN_PARAGRAPH.LEFT)
    for row in t.rows:
        for c in row.cells:
            for p in c.paragraphs:
                p.paragraph_format.space_after = Pt(0)
    if widths_cm:
        for row in t.rows:
            for i, w in enumerate(widths_cm):
                row.cells[i].width = Cm(w)
    return t


def actualizeaza_campuri(doc):
    """Update every field the way a careful person does before saving/exporting: body, footnotes,
    headers/footers of every section (StoryRanges), TOC and table-of-figures, twice, because the
    TOC length moves page numbers. doc = Word COM Document."""
    for _ in range(2):
        doc.Repaginate()
        for st in doc.StoryRanges:
            r = st
            while r is not None:
                r.Fields.Update()
                try:
                    r = r.NextStoryRange
                except Exception:  # noqa: BLE001
                    r = None
        for s in doc.Sections:
            for hf in list(s.Headers) + list(s.Footers):
                if hf.Exists:
                    hf.Range.Fields.Update()
        for i in range(1, doc.TablesOfContents.Count + 1):
            doc.TablesOfContents(i).Update()
        for i in range(1, doc.TablesOfFigures.Count + 1):
            doc.TablesOfFigures(i).Update()


def finalizeaza_in_word(path: str, actualizeaza=True):
    """Open and re-save the file in Microsoft Word: Word rewrites app.xml (Application, page/word
    counts, editing time) like a document saved by a person, and updates all fields (TOC, SEQ, REF,
    PAGEREF, header/footer fields) before saving. Call after d.save(path)."""
    import os
    import win32com.client
    app = win32com.client.DispatchEx("Word.Application")
    app.Visible = False
    app.DisplayAlerts = 0
    try:
        doc = app.Documents.Open(os.path.abspath(path))
        doc.Range().LanguageID = 1048  # wdRomanian
        if actualizeaza:
            actualizeaza_campuri(doc)
        # field results (TOC, table of figures) get the Word UI language (en-GB on this PC):
        # set ro-RO again on every story after the update, including paragraph marks
        for st in doc.StoryRanges:
            r = st
            while r is not None:
                for prop in ("LanguageID", "LanguageIDFarEast"):
                    try:  # some story types (e.g. footnote separators) refuse one of them
                        setattr(r, prop, 1048)
                    except Exception:  # noqa: BLE001
                        pass
                try:
                    r = r.NextStoryRange
                except Exception:  # noqa: BLE001
                    r = None
        doc.Save()
        doc.Close(False)
    finally:
        app.Quit()
    _repara_core(path)


def _repara_core(path: str):
    """Word stamps lastModifiedBy with this PC's Office user; restore the document author."""
    import re as _re
    import shutil
    import zipfile
    tmp = path + ".tmp"
    with zipfile.ZipFile(path) as zin, zipfile.ZipFile(tmp, "w", zipfile.ZIP_DEFLATED) as zout:
        core = zin.read("docProps/core.xml").decode("utf-8")
        m = _re.search(r"<dc:creator>(.*?)</dc:creator>", core)
        for item in zin.infolist():
            data = zin.read(item.filename)
            if item.filename == "docProps/core.xml" and m:
                data = _re.sub(r"<cp:lastModifiedBy>.*?</cp:lastModifiedBy>",
                               f"<cp:lastModifiedBy>{m.group(1)}</cp:lastModifiedBy>", core).encode("utf-8")
            elif item.filename.startswith("word/") and item.filename.endswith(".xml"):
                # TOC/caption runs regenerated by Word carry the UI language (en-GB/en-US) in
                # w:lang even after Range.LanguageID (eastAsia attribute): normalise to ro-RO
                data = _re.sub(rb'(<w:lang\b[^>]*?w:(?:val|eastAsia)=")en-(?:GB|US)(")', rb"\1ro-RO\2", data)
                data = _re.sub(rb'(<w:lang\b[^>]*?w:(?:val|eastAsia)=")en-(?:GB|US)(")', rb"\1ro-RO\2", data)
            zout.writestr(item, data)
    shutil.move(tmp, path)


def pasti_ortodox(an: int) -> datetime.date:
    """Orthodox Easter (Meeus, Julian computus converted to Gregorian)."""
    a, b, c = an % 4, an % 7, an % 19
    d = (19 * c + 15) % 30
    e = (2 * a + 4 * b - d + 34) % 7
    luna, zi = divmod(d + e + 114, 31)
    return datetime.date(an, luna, zi + 1) + datetime.timedelta(days=13)


def sarbatori_legale(an: int) -> set:
    """Romanian public holidays (Codul muncii art. 139 - verify for new additions)."""
    p = pasti_ortodox(an)
    t = datetime.timedelta
    fixe = [(1, 1), (1, 2), (1, 6), (1, 7), (1, 24), (5, 1), (6, 1), (8, 15), (11, 30), (12, 1),
            (12, 25), (12, 26)]
    s = {datetime.date(an, m, z) for m, z in fixe}
    s |= {p - t(days=2), p, p + t(days=1), p + t(days=49), p + t(days=50)}  # Vinerea Mare..Rusalii
    return s


def zi_lucratoare(d: datetime.date) -> bool:
    return d.weekday() < 5 and d not in sarbatori_legale(d.year)


def adauga_zile_lucratoare(start: datetime.date, n: int) -> datetime.date:
    """Date of the n-th working day AFTER start (start itself not counted). Use it for every
    'N zile lucrătoare' term (preaviz, termen de livrare, termen de plată) - never count by hand."""
    d = start
    while n > 0:
        d += datetime.timedelta(days=1)
        if zi_lucratoare(d):
            n -= 1
    return d


def antet(d, linii, bold_prima=True):
    """Company letterhead in the Word header (same text in every document of that company).
    linii: list of strings, e.g. ["FIRMA SRL", "Sediul: ... | CUI RO.. | J..", "IBAN .. | tel .."]."""
    st = d.styles["Header"]
    _set_font(st, d.styles["Normal"].font.name, 9, color="000000")
    for sec in d.sections:
        h = sec.header
        ps = [h.paragraphs[0]] + [h.add_paragraph(style="Header") for _ in linii[1:]]
        for i, (p, t) in enumerate(zip(ps, linii)):
            r = p.add_run(nbsp(t))
            r.bold = bold_prima and i == 0
            p.alignment = WD_ALIGN_PARAGRAPH.LEFT
            p.paragraph_format.space_after = Pt(0)
        ppr = ps[-1]._p.get_or_add_pPr()
        bdr = OxmlElement("w:pBdr")
        b = OxmlElement("w:bottom")
        for k, v in (("w:val", "single"), ("w:sz", "6"), ("w:space", "4"), ("w:color", "000000")):
            b.set(qn(k), v)
        bdr.append(b)
        ppr.append(bdr)


def semnaturi(d, stanga, dreapta, space_before=18, latime_cm=8.2):
    """Two signature columns (PRESTATOR / BENEFICIAR, AM PREDAT / AM PRIMIT) as a borderless
    table, both columns left-aligned inside their cell, kept on one page. First line bold."""
    sp = d.add_paragraph()
    sp.paragraph_format.space_before = Pt(space_before)
    sp.paragraph_format.space_after = Pt(0)
    sp.paragraph_format.keep_with_next = True
    t = d.add_table(rows=1, cols=2)
    borders = OxmlElement("w:tblBorders")
    for e in ("top", "left", "bottom", "right", "insideH", "insideV"):
        b = OxmlElement(f"w:{e}")
        b.set(qn("w:val"), "nil")
        borders.append(b)
    t._tbl.tblPr.append(borders)
    for cell, txt in zip(t.rows[0].cells, (stanga, dreapta)):
        cell.width = Cm(latime_cm)
        for i, l in enumerate(txt.split("\n")):
            p = cell.paragraphs[0] if i == 0 else cell.add_paragraph()
            p.alignment = WD_ALIGN_PARAGRAPH.LEFT
            p.paragraph_format.space_after = Pt(0)
            p.paragraph_format.keep_with_next = True
            p.add_run(nbsp(l)).bold = i == 0
    t.rows[0]._tr.get_or_add_trPr().append(OxmlElement("w:cantSplit"))
    return t


def tabel_lung(t):
    """For tables that may cross pages: repeat header row, never split a row."""
    hdr = t.rows[0]._tr.get_or_add_trPr()
    hdr.append(OxmlElement("w:tblHeader"))
    for row in t.rows:
        row._tr.get_or_add_trPr().append(OxmlElement("w:cantSplit"))


def finalizeaza_xlsx(path: str):
    """Open and re-save an .xlsx in Excel (recalculates formulas, writes Excel app properties)."""
    import os
    import win32com.client
    app = win32com.client.DispatchEx("Excel.Application")
    app.Visible = False
    app.DisplayAlerts = False
    try:
        wb = app.Workbooks.Open(os.path.abspath(path))
        wb.Application.CalculateFull()
        wb.Save()
        wb.Close(False)
    finally:
        app.Quit()
    _curata_xlsx(path)


def _curata_xlsx(path: str):
    """Remove the absolute folder path Excel stores in workbook.xml (x15ac:absPath) and restore
    lastModifiedBy = creator (Excel stamps the Windows/Office user of this PC on save)."""
    import re as _re
    import shutil
    import zipfile
    tmp = path + ".tmp"
    with zipfile.ZipFile(path) as zin, zipfile.ZipFile(tmp, "w", zipfile.ZIP_DEFLATED) as zout:
        for item in zin.infolist():
            data = zin.read(item.filename)
            if item.filename == "xl/workbook.xml":
                data = _re.sub(rb"<mc:AlternateContent[^>]*>\s*<mc:Choice[^>]*>\s*<x15ac:absPath[^>]*/>"
                               rb"\s*</mc:Choice>\s*</mc:AlternateContent>", b"", data)
                data = _re.sub(rb"<x15ac:absPath[^>]*/>", b"", data)
            if item.filename == "docProps/core.xml":
                m = _re.search(rb"<dc:creator>(.*?)</dc:creator>", data)
                if m:
                    if b"<cp:lastModifiedBy" in data:
                        data = _re.sub(rb"<cp:lastModifiedBy>.*?</cp:lastModifiedBy>|<cp:lastModifiedBy/>",
                                       b"<cp:lastModifiedBy>" + m.group(1) + b"</cp:lastModifiedBy>", data)
                    else:
                        data = data.replace(b"</dc:creator>", b"</dc:creator><cp:lastModifiedBy>" + m.group(1)
                                            + b"</cp:lastModifiedBy>", 1)
            zout.writestr(item, data)
    shutil.move(tmp, path)


def pagina_noua(d):
    d.add_paragraph().add_run().add_break(WD_BREAK.PAGE)
