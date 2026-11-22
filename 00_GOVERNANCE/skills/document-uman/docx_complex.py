"""Complex Word documents for document-uman: numbered headings, TOC, captions + cross-references,
footnotes, sections (cover, roman front matter, landscape), professional tables, figures, annexes.

Use together with docx_ro:
    sys.path.insert(0, r"<skill dir>")
    import docx_ro, docx_complex as dc
    d = docx_ro.new_document(author, title, kind="raport")
    dc.pregateste(d)                 # styles, heading numbering, table style, footnote part
    dc.coperta(d, ...); dc.sectiune(d, format_numar="lowerRoman", start=1, leaga_antet=False)
    dc.cuprins(d); ... dc.sectiune(d, format_numar="decimal", start=1, leaga_antet=False)
    d.add_heading / dc.titlu(d, "Text", 1) ; dc.paragraf(d, "Vezi {{ref:t_chelt}}.{{nota:Sursa ...}}")
    dc.tabel_pro(...); dc.figura(...); dc.anexa(d, 1, "Titlu"); dc.marcaj_final(d)
    d.save(path); docx_ro.finalizeaza_in_word(path)   # updates TOC, SEQ, REF, PAGEREF, header fields
Field results written here are correct cached values (SEQ numbers, REF texts) so the file reads right
even before Word updates it; Word recomputes them in finalizeaza_in_word.
"""
import re

from docx.enum.section import WD_ORIENT, WD_SECTION
from docx.enum.text import WD_ALIGN_PARAGRAPH
from docx.opc.packuri import PackURI
from docx.opc.part import XmlPart
from docx.oxml import OxmlElement, parse_xml
from docx.oxml.ns import nsdecls, qn
from docx.shared import Cm, Pt

import docx_ro

_STARE = {}  # id(document element) -> state (counters, bookmarks, footnotes part)

RT_FOOTNOTES = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/footnotes"
CT_FOOTNOTES = "application/vnd.openxmlformats-officedocument.wordprocessingml.footnotes+xml"
RT_NUMBERING = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering"
CT_NUMBERING = "application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml"
ACCENT = "1F3864"


def _st(d):
    k = id(d.element)
    if k not in _STARE:
        _STARE[k] = {"seq": {}, "bm": {}, "bm_id": 100, "fn": None, "fn_id": 0, "num": None,
                     "abs_id": 10, "num_id": 10}
    return _STARE[k]


def _twips(cm):
    return int(round(cm * 567))


def latime_text(d, sec=None):
    sec = sec or d.sections[-1]
    return (sec.page_width - sec.left_margin - sec.right_margin) / 360000  # cm


# ------------------------------------------------------------------------------------------ styles
def _adauga_stil(d, xml):
    el = parse_xml(xml)
    sid = el.get(qn("w:styleId"))
    for old in d.styles.element.findall(qn("w:style")):
        if old.get(qn("w:styleId")) == sid:
            d.styles.element.remove(old)
    d.styles.element.append(el)


def _rfonts(f):
    return f'<w:rFonts w:ascii="{f}" w:hAnsi="{f}" w:cs="{f}" w:eastAsia="{f}"/>'


def pregateste(d, numerotare=True, h1_pagina_noua=True, accent=ACCENT):
    """Prepare a docx_ro document for a long report. Call once, right after new_document()."""
    st = d.styles
    font = st["Normal"].font.name
    size = st["Normal"].font.size.pt
    W = nsdecls("w")
    tw = _twips(latime_text(d))
    hz = int(st["Heading 1"].font.size.pt * 2)
    # paragraph styles
    _adauga_stil(d, f'<w:style {W} w:type="paragraph" w:customStyle="1" w:styleId="TitluNenumerotat">'
                    f'<w:name w:val="Titlu nenumerotat"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/>'
                    f'<w:uiPriority w:val="9"/><w:qFormat/><w:pPr><w:keepNext/><w:keepLines/>'
                    f'{"<w:pageBreakBefore/>" if h1_pagina_noua else ""}<w:spacing w:before="0" w:after="160"/>'
                    f'<w:jc w:val="left"/><w:outlineLvl w:val="0"/></w:pPr><w:rPr>{_rfonts(font)}<w:b/>'
                    f'<w:color w:val="{accent}"/><w:sz w:val="{hz}"/><w:szCs w:val="{hz}"/></w:rPr></w:style>')
    _adauga_stil(d, f'<w:style {W} w:type="paragraph" w:customStyle="1" w:styleId="TitluCuprins">'
                    f'<w:name w:val="Titlu cuprins"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/>'
                    f'<w:pPr><w:keepNext/><w:spacing w:before="0" w:after="160"/><w:jc w:val="left"/></w:pPr>'
                    f'<w:rPr>{_rfonts(font)}<w:b/><w:color w:val="{accent}"/><w:sz w:val="{hz}"/></w:rPr></w:style>')
    _adauga_stil(d, f'<w:style {W} w:type="paragraph" w:customStyle="1" w:styleId="AnexaTitlu">'
                    f'<w:name w:val="Anexa titlu"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/>'
                    f'<w:uiPriority w:val="9"/><w:qFormat/><w:pPr><w:keepNext/><w:keepLines/><w:pageBreakBefore/>'
                    f'<w:spacing w:before="0" w:after="160"/><w:jc w:val="left"/><w:outlineLvl w:val="0"/></w:pPr>'
                    f'<w:rPr>{_rfonts(font)}<w:b/><w:color w:val="{accent}"/><w:sz w:val="{hz}"/>'
                    f'<w:szCs w:val="{hz}"/></w:rPr></w:style>')
    tsz = int(round((size - 1.5) * 2))
    for sid, name, jc in (("TabelText", "Tabel text", "left"), ("TabelNumar", "Tabel număr", "right")):
        _adauga_stil(d, f'<w:style {W} w:type="paragraph" w:customStyle="1" w:styleId="{sid}">'
                        f'<w:name w:val="{name}"/><w:basedOn w:val="Normal"/><w:pPr>'
                        f'<w:spacing w:before="0" w:after="0" w:line="240" w:lineRule="auto"/><w:jc w:val="{jc}"/>'
                        f'</w:pPr><w:rPr><w:sz w:val="{tsz}"/><w:szCs w:val="{tsz}"/></w:rPr></w:style>')
    _adauga_stil(d, f'<w:style {W} w:type="paragraph" w:customStyle="1" w:styleId="TabelNota">'
                    f'<w:name w:val="Tabel notă"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/><w:pPr>'
                    f'<w:spacing w:before="60" w:after="200" w:line="240" w:lineRule="auto"/><w:jc w:val="left"/>'
                    f'</w:pPr><w:rPr><w:color w:val="404040"/><w:sz w:val="{tsz - 1}"/><w:szCs w:val="{tsz - 1}"/>'
                    f'</w:rPr></w:style>')
    _adauga_stil(d, f'<w:style {W} w:type="paragraph" w:customStyle="1" w:styleId="Figura">'
                    f'<w:name w:val="Figură"/><w:basedOn w:val="Normal"/><w:next w:val="Caption"/><w:pPr>'
                    f'<w:keepNext/><w:spacing w:before="120" w:after="60" w:line="240" w:lineRule="auto"/>'
                    f'<w:jc w:val="center"/></w:pPr></w:style>')
    # caption: label in the same font, not italic; keeps with the table that follows
    _adauga_stil(d, f'<w:style {W} w:type="paragraph" w:styleId="Caption"><w:name w:val="caption"/>'
                    f'<w:basedOn w:val="Normal"/><w:next w:val="Normal"/><w:uiPriority w:val="35"/>'
                    f'<w:unhideWhenUsed/><w:qFormat/><w:pPr><w:keepNext/><w:spacing w:before="120" w:after="80"'
                    f' w:line="240" w:lineRule="auto"/><w:jc w:val="left"/></w:pPr><w:rPr><w:b/>'
                    f'<w:color w:val="262626"/><w:sz w:val="{tsz}"/><w:szCs w:val="{tsz}"/></w:rPr></w:style>')
    # TOC levels: number in a hanging indent, dotted right tab at the text width
    for lvl, left, hang in ((1, 567, 567), (2, 1276, 709), (3, 2126, 850)):
        b = "<w:b/>" if lvl == 1 else ""
        _adauga_stil(d, f'<w:style {W} w:type="paragraph" w:styleId="TOC{lvl}"><w:name w:val="toc {lvl}"/>'
                        f'<w:basedOn w:val="Normal"/><w:next w:val="Normal"/><w:uiPriority w:val="39"/>'
                        f'<w:unhideWhenUsed/><w:pPr><w:tabs><w:tab w:val="right" w:leader="dot" w:pos="{tw}"/>'
                        f'</w:tabs><w:spacing w:before="{80 if lvl == 1 else 0}" w:after="40"/>'
                        f'<w:ind w:left="{left}" w:right="567" w:hanging="{hang}"/><w:jc w:val="left"/></w:pPr>'
                        f'<w:rPr>{b}</w:rPr></w:style>')
    _adauga_stil(d, f'<w:style {W} w:type="paragraph" w:styleId="TableofFigures">'
                    f'<w:name w:val="table of figures"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/>'
                    f'<w:uiPriority w:val="99"/><w:unhideWhenUsed/><w:pPr><w:tabs>'
                    f'<w:tab w:val="right" w:leader="dot" w:pos="{tw}"/></w:tabs><w:spacing w:after="40"/>'
                    f'<w:ind w:left="1134" w:right="567" w:hanging="1134"/><w:jc w:val="left"/></w:pPr></w:style>')
    # footnotes
    fsz = int(round((size - 2) * 2))
    _adauga_stil(d, f'<w:style {W} w:type="paragraph" w:styleId="FootnoteText"><w:name w:val="footnote text"/>'
                    f'<w:basedOn w:val="Normal"/><w:uiPriority w:val="99"/><w:unhideWhenUsed/><w:pPr>'
                    f'<w:spacing w:after="0" w:line="240" w:lineRule="auto"/><w:jc w:val="left"/></w:pPr>'
                    f'<w:rPr><w:sz w:val="{fsz}"/><w:szCs w:val="{fsz}"/></w:rPr></w:style>')
    _adauga_stil(d, f'<w:style {W} w:type="character" w:styleId="FootnoteReference">'
                    f'<w:name w:val="footnote reference"/><w:basedOn w:val="DefaultParagraphFont"/>'
                    f'<w:uiPriority w:val="99"/><w:unhideWhenUsed/><w:rPr><w:vertAlign w:val="superscript"/>'
                    f'</w:rPr></w:style>')
    # table style: horizontal rules only, shaded header, bold total row
    _adauga_stil(d, f'<w:style {W} w:type="table" w:customStyle="1" w:styleId="TabelRaport">'
                    f'<w:name w:val="Tabel raport"/><w:basedOn w:val="TableNormal"/><w:uiPriority w:val="40"/>'
                    f'<w:pPr><w:spacing w:after="0" w:line="240" w:lineRule="auto"/></w:pPr>'
                    f'<w:tblPr><w:tblBorders><w:top w:val="single" w:sz="8" w:space="0" w:color="404040"/>'
                    f'<w:left w:val="nil"/><w:bottom w:val="single" w:sz="8" w:space="0" w:color="404040"/>'
                    f'<w:right w:val="nil"/><w:insideH w:val="single" w:sz="4" w:space="0" w:color="BFBFBF"/>'
                    f'<w:insideV w:val="nil"/></w:tblBorders><w:tblCellMar><w:top w:w="34" w:type="dxa"/>'
                    f'<w:left w:w="85" w:type="dxa"/><w:bottom w:w="34" w:type="dxa"/>'
                    f'<w:right w:w="85" w:type="dxa"/></w:tblCellMar></w:tblPr>'
                    f'<w:tblStylePr w:type="firstRow"><w:rPr><w:b/><w:bCs/></w:rPr><w:tblPr/><w:trPr>'
                    f'<w:tblHeader/></w:trPr><w:tcPr><w:tcBorders><w:bottom w:val="single" w:sz="8" w:space="0"'
                    f' w:color="404040"/></w:tcBorders><w:shd w:val="clear" w:color="auto" w:fill="E4E9F0"/>'
                    f'<w:vAlign w:val="bottom"/></w:tcPr></w:tblStylePr>'
                    f'<w:tblStylePr w:type="lastRow"><w:rPr><w:b/><w:bCs/></w:rPr><w:tblPr/><w:tcPr><w:tcBorders>'
                    f'<w:top w:val="single" w:sz="8" w:space="0" w:color="404040"/></w:tcBorders></w:tcPr>'
                    f'</w:tblStylePr></w:style>')
    for nm in ("Header", "Footer"):
        docx_ro._set_font(st[nm], font, size - 2.5, color="595959")
    h1 = st["Heading 1"]
    h1.paragraph_format.page_break_before = h1_pagina_noua
    h1.paragraph_format.space_before = Pt(0)
    h1.paragraph_format.space_after = Pt(8)
    for lvl in (1, 2, 3):
        st[f"Heading {lvl}"].paragraph_format.keep_together = True
    if numerotare:
        nid = _lista_definitie(d, "titluri")
        for lvl in (1, 2, 3):
            ppr = st[f"Heading {lvl}"].element.get_or_add_pPr()
            numpr = ppr.get_or_add_numPr()
            numpr.get_or_add_ilvl().val = lvl - 1
            numpr.get_or_add_numId().val = nid
    _footnotes_part(d)
    return d


# ------------------------------------------------------------------------------------------ numbering
def _numbering(d):
    s = _st(d)
    if s["num"] is not None:
        return s["num"]
    try:
        part = d.part.part_related_by(RT_NUMBERING)
    except KeyError:
        el = parse_xml(f'<w:numbering {nsdecls("w")}/>')
        from docx.parts.numbering import NumberingPart
        part = NumberingPart(PackURI("/word/numbering.xml"), CT_NUMBERING, el, d.part.package)
        d.part.relate_to(part, RT_NUMBERING)
    s["num"] = part.element
    return part.element


def _lista_definitie(d, fel):
    """fel: 'titluri' (1. / 1.1. / 1.1.1. linked to Heading 1-3), 'numerotata' (1.), 'buline' (–)."""
    s = _st(d)
    numbering = _numbering(d)
    aid, nid = s["abs_id"], s["num_id"]
    s["abs_id"] += 1
    s["num_id"] += 1
    lv = []
    for i in range(9):
        if fel == "titluri":
            text = ".".join(f"%{k + 1}" for k in range(i + 1)) + "."
            ps = f'<w:pStyle w:val="Heading{i + 1}"/>' if i < 3 else ""
            ind = '<w:ind w:left="851" w:hanging="851"/>'
            lv.append(f'<w:lvl w:ilvl="{i}"><w:start w:val="1"/><w:numFmt w:val="decimal"/>{ps}'
                      f'<w:suff w:val="tab"/><w:lvlText w:val="{text}"/><w:lvlJc w:val="left"/>'
                      f'<w:pPr><w:tabs><w:tab w:val="num" w:pos="851"/></w:tabs>{ind}</w:pPr></w:lvl>')
        elif fel == "numerotata":
            fmt, text = (("decimal", f"%{i + 1}."), ("lowerLetter", f"%{i + 1})"))[i % 2]
            left = 425 + 425 * i
            lv.append(f'<w:lvl w:ilvl="{i}"><w:start w:val="1"/><w:numFmt w:val="{fmt}"/>'
                      f'<w:lvlText w:val="{text}"/><w:lvlJc w:val="left"/>'
                      f'<w:pPr><w:ind w:left="{left}" w:hanging="425"/></w:pPr></w:lvl>')
        else:
            left = 425 + 425 * i
            lv.append(f'<w:lvl w:ilvl="{i}"><w:start w:val="1"/><w:numFmt w:val="bullet"/>'
                      f'<w:lvlText w:val="\u2013"/><w:lvlJc w:val="left"/>'
                      f'<w:pPr><w:ind w:left="{left}" w:hanging="283"/></w:pPr></w:lvl>')
    absn = parse_xml(f'<w:abstractNum {nsdecls("w")} w:abstractNumId="{aid}">'
                     f'<w:multiLevelType w:val="{"multilevel" if fel == "titluri" else "hybridMultilevel"}"/>'
                     + "".join(lv) + "</w:abstractNum>")
    first_num = numbering.find(qn("w:num"))
    if first_num is not None:
        first_num.addprevious(absn)
    else:
        numbering.append(absn)
    numbering.append(parse_xml(f'<w:num {nsdecls("w")} w:numId="{nid}"><w:abstractNumId w:val="{aid}"/></w:num>'))
    return nid


def lista(d, elemente, numerotata=True):
    """Numbered (1. 2. 3.) or dash list from the list style; each call restarts at 1.
    Items accept the same markup as paragraf()."""
    nid = _lista_definitie(d, "numerotata" if numerotata else "buline")
    out = []
    for i, t in enumerate(elemente):
        p = paragraf(d, t, style="List Paragraph")
        numpr = p._p.get_or_add_pPr().get_or_add_numPr()
        numpr.get_or_add_ilvl().val = 0
        numpr.get_or_add_numId().val = nid
        p.paragraph_format.space_after = Pt(4 if i < len(elemente) - 1 else 8)
        out.append(p)
    return out


# ------------------------------------------------------------------------------------------ fields
def camp(p, cod, rezultat=""):
    """Insert a complex field { cod } with a cached result into paragraph p. Returns the result run."""
    def fc(t):
        r = OxmlElement("w:r")
        e = OxmlElement("w:fldChar")
        e.set(qn("w:fldCharType"), t)
        r.append(e)
        p._p.append(r)
    fc("begin")
    r = OxmlElement("w:r")
    it = OxmlElement("w:instrText")
    it.set(qn("xml:space"), "preserve")
    it.text = f" {cod} "
    r.append(it)
    p._p.append(r)
    fc("separate")
    res = p.add_run(rezultat)
    fc("end")
    return res


def _bookmark(d, p, nume, start_el=None):
    s = _st(d)
    s["bm_id"] += 1
    bid = str(s["bm_id"])
    bs = OxmlElement("w:bookmarkStart")
    bs.set(qn("w:id"), bid)
    bs.set(qn("w:name"), nume)
    be = OxmlElement("w:bookmarkEnd")
    be.set(qn("w:id"), bid)
    return bs, be


def _nume_bm(cheie):
    import zlib
    return "_Ref" + str(zlib.crc32(cheie.encode("utf-8")) % 10 ** 9).rjust(9, "0")


def legenda(d, eticheta, text, cheie=None, prefix="", seq=None):
    """Caption 'Tabelul 3. text' with { SEQ Tabelul } and a bookmark over 'Tabelul 3' for REF.
    eticheta: 'Tabelul' or 'Figura'. prefix + seq give annex numbering, e.g. prefix='A1.',
    seq='TabelulA1' -> 'Tabelul A1.1'. cheie: name used later in {{ref:cheie}}."""
    s = _st(d)
    seq = seq or eticheta
    n = s["seq"].get(seq, 0) + 1
    s["seq"][seq] = n
    p = d.add_paragraph(style="Caption")
    bm = _nume_bm(cheie or f"{seq}{n}")
    bs, be = _bookmark(d, p, bm)
    p._p.append(bs)
    p.add_run(f"{eticheta}\u00a0{prefix}")
    camp(p, f"SEQ {seq} \\* ARABIC", str(n))
    p._p.append(be)
    p.add_run(". " + docx_ro.nbsp(text))
    if cheie:
        s["bm"][cheie] = (bm, f"{eticheta}\u00a0{prefix}{n}")
        for run in s.get("pending", {}).pop(cheie, []):
            run.text = f"{eticheta}\u00a0{prefix}{n}"
    return p


def referinte_nerezolvate(d):
    """Keys used in {{ref:..}} that never got a caption - must be empty before saving."""
    return sorted(_st(d).get("pending", {}))


def ref(d, p, cheie, pagina=False):
    """Cross-reference to a caption: REF (label and number, as hyperlink) or PAGEREF.
    Forward references (text before the table, as experts cite) are filled in by legenda()."""
    s = _st(d)
    bm, txt = s["bm"].get(cheie, (_nume_bm(cheie), None))
    if pagina:
        return camp(p, f"PAGEREF {bm} \\h", "")
    run = camp(p, f"REF {bm} \\h", txt or "")
    if txt is None:
        s.setdefault("pending", {}).setdefault(cheie, []).append(run)
    return run


_MARK = re.compile(r"\{\{(ref|pag|nota):(.*?)\}\}|\*\*(.+?)\*\*", re.S)


def paragraf(d, text, style=None, align=None, keep_with_next=False):
    """Paragraph with inline markup: {{ref:cheie}} cross-reference, {{pag:cheie}} its page,
    {{nota:text}} footnote, **bold**. Text pieces get docx_ro.nbsp()."""
    p = d.add_paragraph(style=style)
    pos = 0
    for m in _MARK.finditer(text):
        if m.start() > pos:
            p.add_run(docx_ro.nbsp(text[pos:m.start()]))
        if m.group(3):
            p.add_run(docx_ro.nbsp(m.group(3))).bold = True
        elif m.group(1) == "ref":
            ref(d, p, m.group(2))
        elif m.group(1) == "pag":
            ref(d, p, m.group(2), pagina=True)
        else:
            nota(d, p, m.group(2))
        pos = m.end()
    if pos < len(text):
        p.add_run(docx_ro.nbsp(text[pos:]))
    if align:
        p.alignment = {"center": WD_ALIGN_PARAGRAPH.CENTER, "right": WD_ALIGN_PARAGRAPH.RIGHT,
                       "left": WD_ALIGN_PARAGRAPH.LEFT, "justify": WD_ALIGN_PARAGRAPH.JUSTIFY}[align]
    if keep_with_next:
        p.paragraph_format.keep_with_next = True
    return p


# ------------------------------------------------------------------------------------------ footnotes
def _footnotes_part(d):
    s = _st(d)
    if s["fn"] is not None:
        return s["fn"]
    try:
        part = d.part.part_related_by(RT_FOOTNOTES)
    except KeyError:
        sep = ('<w:p><w:pPr><w:spacing w:after="0" w:line="240" w:lineRule="auto"/></w:pPr>'
               '<w:r><w:{0}/></w:r></w:p>')
        el = parse_xml(f'<w:footnotes {nsdecls("w", "r")}>'
                       f'<w:footnote w:type="separator" w:id="-1">{sep.format("separator")}</w:footnote>'
                       f'<w:footnote w:type="continuationSeparator" w:id="0">'
                       f'{sep.format("continuationSeparator")}</w:footnote></w:footnotes>')
        part = XmlPart(PackURI("/word/footnotes.xml"), CT_FOOTNOTES, el, d.part.package)
        d.part.relate_to(part, RT_FOOTNOTES)
        settings = d.settings.element
        if settings.find(qn("w:footnotePr")) is None:
            fp = parse_xml(f'<w:footnotePr {nsdecls("w")}><w:footnote w:id="-1"/><w:footnote w:id="0"/>'
                           f'</w:footnotePr>')
            anchor = settings.find(qn("w:compat"))
            if anchor is not None:
                anchor.addprevious(fp)
    s["fn"] = part
    return part


def nota(d, p, text):
    """Real Word footnote (auto-numbered by Word) at the end of paragraph p so far."""
    s = _st(d)
    part = _footnotes_part(d)
    s["fn_id"] += 1
    fid = s["fn_id"]
    fn = parse_xml(f'<w:footnote {nsdecls("w")} w:id="{fid}"><w:p><w:pPr><w:pStyle w:val="FootnoteText"/>'
                   f'</w:pPr><w:r><w:rPr><w:rStyle w:val="FootnoteReference"/></w:rPr><w:footnoteRef/></w:r>'
                   f'<w:r><w:t xml:space="preserve"> </w:t></w:r></w:p></w:footnote>')
    r = OxmlElement("w:r")
    t = OxmlElement("w:t")
    t.text = docx_ro.nbsp(text)
    t.set(qn("xml:space"), "preserve")
    r.append(t)
    fn.find(qn("w:p")).append(r)
    part.element.append(fn)
    run = p.add_run()
    rpr = run._r.get_or_add_rPr()
    rs = OxmlElement("w:rStyle")
    rs.set(qn("w:val"), "FootnoteReference")
    rpr.append(rs)
    ref_el = OxmlElement("w:footnoteReference")
    ref_el.set(qn("w:id"), str(fid))
    run._r.append(ref_el)
    return run


# ------------------------------------------------------------------------------------------ sections
def sectiune(d, orientare=None, format_numar=None, start=None, leaga_antet=True,
             prima_diferita=False):
    """New section on a new page. orientare 'portret'/'peisaj' (default: like the previous one),
    format_numar 'lowerRoman'/'decimal'/'upperLetter', start=1 restarts numbering,
    leaga_antet=False unlinks header and footer (new, empty ones you then fill).
    The section break is moved into the last paragraph, so no empty line is left behind."""
    sec = d.add_section(WD_SECTION.NEW_PAGE)
    body = d.element.body
    brk = body[-2]  # the new paragraph that carries the previous section's sectPr
    prev = brk.getprevious()
    if prev is not None and prev.tag == qn("w:p") and prev.find(qn("w:pPr") + "/" + qn("w:sectPr")) is None:
        sp = brk.find(qn("w:pPr") + "/" + qn("w:sectPr"))
        prev.get_or_add_pPr().append(sp)
        body.remove(brk)
    else:  # previous block is a table: keep a tiny paragraph
        ppr = brk.find(qn("w:pPr"))
        ppr.insert(0, parse_xml(f'<w:spacing {nsdecls("w")} w:before="0" w:after="0" w:line="20"'
                                f' w:lineRule="exact"/>'))
    sp = sec._sectPr
    pg = _pgnumtype(sp)
    if pg.get(qn("w:start")) is not None:
        del pg.attrib[qn("w:start")]
    if format_numar:
        pg.set(qn("w:fmt"), format_numar)
    if start is not None:
        pg.set(qn("w:start"), str(start))
    tp = sp.find(qn("w:titlePg"))
    if tp is not None and not prima_diferita:
        sp.remove(tp)
    if prima_diferita:
        sec.different_first_page_header_footer = True
    if orientare:
        want = WD_ORIENT.LANDSCAPE if orientare == "peisaj" else WD_ORIENT.PORTRAIT
        w, h = sec.page_width, sec.page_height
        if (want == WD_ORIENT.LANDSCAPE) != (w > h):
            sec.page_width, sec.page_height = h, w
        sec.orientation = want
    if not leaga_antet:
        sec.header.is_linked_to_previous = False
        sec.footer.is_linked_to_previous = False
    return sec


_DUPA_PGNUM = ("cols", "formProt", "vAlign", "noEndnote", "titlePg", "textDirection", "bidi",
               "rtlGutter", "docGrid", "printerSettings")


def _pgnumtype(sp):
    """w:pgNumType at its schema position inside w:sectPr."""
    pg = sp.find(qn("w:pgNumType"))
    if pg is None:
        pg = OxmlElement("w:pgNumType")
        for tag in _DUPA_PGNUM:
            nxt = sp.find(qn(f"w:{tag}"))
            if nxt is not None:
                nxt.addprevious(pg)
                break
        else:
            sp.append(pg)
    return pg


def antet_raport(sec, stanga, dreapta, cu_linie=True):
    """Small header: left text, right text at the right margin (alignment tab, works in landscape)."""
    hdr = sec.header
    hdr.is_linked_to_previous = False
    p = hdr.paragraphs[0]
    for r in list(p.runs):
        r._r.getparent().remove(r._r)
    p.add_run(docx_ro.nbsp(stanga))
    pt = OxmlElement("w:ptab")
    pt.set(qn("w:relativeTo"), "margin")
    pt.set(qn("w:alignment"), "right")
    pt.set(qn("w:leader"), "none")
    r = p.add_run()
    r._r.append(pt)
    p.add_run(docx_ro.nbsp(dreapta))
    if cu_linie:
        ppr = p._p.get_or_add_pPr()
        ppr.append(parse_xml(f'<w:pBdr {nsdecls("w")}><w:bottom w:val="single" w:sz="4" w:space="4"'
                             f' w:color="808080"/></w:pBdr>'))
    return p


def subsol_numar(sec, fel="simplu", ultima=None, aliniere="center"):
    """Footer page number. fel 'simplu' -> { PAGE } (shows i, ii in a lowerRoman section);
    'pagina_din' -> 'Pagina { PAGE } din { PAGEREF ultima }' (ultima = bookmark from marcaj_final,
    so the cover and front matter are not counted, unlike NUMPAGES)."""
    ftr = sec.footer
    ftr.is_linked_to_previous = False
    p = ftr.paragraphs[0]
    for r in list(p.runs):
        r._r.getparent().remove(r._r)
    p.alignment = {"center": WD_ALIGN_PARAGRAPH.CENTER, "right": WD_ALIGN_PARAGRAPH.RIGHT}[aliniere]
    if fel == "pagina_din":
        p.add_run("Pagina ")
        camp(p, "PAGE", "1")
        p.add_run(" din ")
        camp(p, f"PAGEREF {ultima} \\h" if ultima else "NUMPAGES", "1")
    else:
        camp(p, "PAGE", "1")
    return p


def antet_gol(sec):
    """Unlinked, empty header and footer (cover, title page)."""
    for hf in (sec.header, sec.footer):
        hf.is_linked_to_previous = False
        for p in hf.paragraphs:
            for r in list(p.runs):
                r._r.getparent().remove(r._r)


def marcaj_final(d, nume="_UltimaPagina"):
    """Bookmark on the last paragraph: target of 'Pagina X din Y' in subsol_numar."""
    p = d.paragraphs[-1]
    bs, be = _bookmark(d, p, nume)
    p._p.insert(1 if p._p.pPr is not None else 0, bs)
    p._p.append(be)
    return nume


# ------------------------------------------------------------------------------------------ cover, TOC
def coperta(d, organizatie, titlu, subtitlu=None, detalii=(), loc_data=None, spatiu_sus_cm=6.5,
            spatiu_jos_cm=7.5):
    """Cover page in the first section (call before any other content). No header/footer is set on
    section 1; give the next section its own with sectiune(..., leaga_antet=False)."""
    p = d.paragraphs[0] if d.paragraphs and not d.paragraphs[0].text else d.add_paragraph()
    p.add_run(docx_ro.nbsp(organizatie.upper())).bold = True
    p.paragraph_format.space_after = Pt(2)
    t = d.add_paragraph(docx_ro.nbsp(titlu), style="Title")
    t.paragraph_format.space_before = Cm(spatiu_sus_cm)
    t.paragraph_format.keep_with_next = True
    last = t
    if subtitlu:
        last = d.add_paragraph(docx_ro.nbsp(subtitlu), style="Subtitle")
    first = True
    for linie in detalii:
        q = d.add_paragraph(docx_ro.nbsp(linie))
        q.paragraph_format.space_after = Pt(2)
        q.alignment = WD_ALIGN_PARAGRAPH.LEFT
        if first:
            q.paragraph_format.space_before = Cm(spatiu_jos_cm)
            first = False
        last = q
    if loc_data:
        q = d.add_paragraph(docx_ro.nbsp(loc_data))
        q.paragraph_format.space_before = Pt(10)
        last = q
    return last


def cuprins(d, titlu="Cuprins", niveluri="1-3"):
    """TOC field from heading outline levels (Heading 1-3 + styles with an outline level, e.g.
    'Anexa titlu', 'Titlu nenumerotat'). Filled by finalizeaza_in_word()."""
    d.add_paragraph(titlu, style="Titlu cuprins")
    p = d.add_paragraph(style="toc 1")
    camp(p, f'TOC \\o "{niveluri}" \\h \\z \\u', "")
    return p


def lista_legende(d, titlu, eticheta="Tabelul", pagina_noua=False, alte_secvente=()):
    """Table of figures / tables (TOC \\c) - only when there are about five or more.
    pagina_noua=True starts it on a new page (the TOC page is often full).
    alte_secvente: annex caption sequences ("TabelulA1", "TabelulA2") listed after the main ones -
    without them the list silently skips Tabelul A1.1 (2026-11-22 review)."""
    q = d.add_paragraph(titlu, style="Titlu cuprins")
    q.paragraph_format.space_before = Pt(0 if pagina_noua else 18)
    q.paragraph_format.page_break_before = pagina_noua
    p = d.add_paragraph(style="table of figures")
    camp(p, f'TOC \\h \\z \\c "{eticheta}"', "")
    for seq in alte_secvente:
        camp(d.add_paragraph(style="table of figures"), f'TOC \\h \\z \\c "{seq}"', "")
    return p


def titlu(d, text, nivel=1):
    """Numbered heading (Heading 1-3). Numbers come from the linked list, never typed."""
    return d.add_paragraph(docx_ro.nbsp(text), style=f"Heading {nivel}")


def titlu_nenumerotat(d, text):
    """Unnumbered heading that still goes into the TOC and the PDF bookmarks (Rezumat, Bibliografie)."""
    return d.add_paragraph(docx_ro.nbsp(text), style="Titlu nenumerotat")


def anexa(d, numar, text):
    """'Anexa 1. Titlu' on a new page, in the TOC, outside the 1/1.1 numbering."""
    return d.add_paragraph(docx_ro.nbsp(f"Anexa {numar}. {text}"), style="Anexa titlu")


# ------------------------------------------------------------------------------------------ tables
def numar_ro(x, zecimale=2):
    """1.234,56 rounded ROUND_HALF_UP (cifre_ro.ro) - the same result as Excel ROUND/number format."""
    import cifre_ro
    return cifre_ro.ro(x, zecimale).replace("−", "-")


def _fmt(v, zec):
    if isinstance(v, bool) or v is None:
        return "" if v is None else str(v)
    if isinstance(v, int):
        return numar_ro(v, 0)
    if isinstance(v, float):
        return numar_ro(v, zec)
    return docx_ro.nbsp(str(v))


def tabel_pro(d, antet, randuri, latimi_cm, numerice=(), total=None, total_coloane=None,
              legenda_text=None, cheie=None, sursa=None, nota_text=None, compact=None, zecimale=2,
              prefix="", seq=None, formate=None):
    """Report table: caption above (SEQ + bookmark), style 'Tabel raport' (horizontal rules, shaded
    header), header row repeated on every page, rows never split, fixed column widths, numeric
    columns right-aligned with their headers, units belong in the header text ('Valoare (lei)').
    randuri: raw values (float -> 1.234,56; int -> 1.234; str as is). total: label of a totals row
    computed here from the numeric columns (total_coloane restricts which). compact: keep the whole
    table on one page (default: tables up to 12 rows). sursa / nota_text: line under the table."""
    formate = formate or {}
    if legenda_text:
        legenda(d, "Tabelul", legenda_text, cheie=cheie, prefix=prefix, seq=seq)
    rows = [list(r) for r in randuri]
    if total:
        cols = total_coloane if total_coloane is not None else numerice
        tr = [""] * len(antet)
        tr[0] = total
        for c in cols:
            import cifre_ro
            tr[c] = cifre_ro.suma((r[c] for r in rows if isinstance(r[c], (int, float))), zecimale)
            if all(isinstance(r[c], int) for r in rows):
                tr[c] = int(tr[c])
        rows.append(tr)
    t = d.add_table(rows=len(rows) + 1, cols=len(antet))
    t.style = d.styles["Tabel raport"]
    tblpr = t._tbl.tblPr
    look = tblpr.find(qn("w:tblLook"))
    if look is None:
        look = OxmlElement("w:tblLook")
        tblpr.append(look)
    for k, v in (("w:val", "04A0"), ("w:firstRow", "1"), ("w:lastRow", "1" if total else "0"),
                 ("w:firstColumn", "0"), ("w:lastColumn", "0"), ("w:noHBand", "1"), ("w:noVBand", "1")):
        look.set(qn(k), v)
    # fixed layout and widths
    tw = sum(latimi_cm)
    tblw = tblpr.find(qn("w:tblW"))
    if tblw is None:
        tblw = OxmlElement("w:tblW")
        tblpr.append(tblw)
    tblw.set(qn("w:w"), str(_twips(tw)))
    tblw.set(qn("w:type"), "dxa")
    lay = OxmlElement("w:tblLayout")
    lay.set(qn("w:type"), "fixed")
    look.addprevious(lay)
    for gc, w in zip(t._tbl.tblGrid.findall(qn("w:gridCol")), latimi_cm):
        gc.set(qn("w:w"), str(_twips(w)))
    n = len(t.rows)
    keep = compact if compact is not None else n <= 13
    for ri, row in enumerate(t.rows):
        trpr = row._tr.get_or_add_trPr()
        trpr.append(OxmlElement("w:cantSplit"))
        if ri == 0:
            trpr.append(OxmlElement("w:tblHeader"))
        vals = antet if ri == 0 else rows[ri - 1]
        for ci, cell in enumerate(row.cells):
            cell.width = Cm(latimi_cm[ci])
            p = cell.paragraphs[0]
            p.style = d.styles["Tabel număr" if ci in numerice else "Tabel text"]
            v = vals[ci]
            txt = v if ri == 0 else (formate[ci](v) if ci in formate and v != "" else _fmt(v, zecimale))
            p.add_run(docx_ro.nbsp(str(txt)) if ri == 0 else txt)
            if (keep and ri < n - 1) or (ri == n - 1 and (sursa or nota_text)):
                # whole short table on one page; last row never separated from its source line
                p.paragraph_format.keep_with_next = True
    if sursa or nota_text:
        txt = " ".join(x for x in (f"Sursa: {sursa}." if sursa else "", nota_text or "") if x)
        paragraf(d, txt, style="Tabel notă")
    else:
        # small gap after the table without an empty paragraph
        t._tbl.addnext(OxmlElement("w:p"))
        gap = d.paragraphs[-1]
        gap.paragraph_format.space_after = Pt(0)
        gap.paragraph_format.line_spacing = Pt(6)
    return t


# ------------------------------------------------------------------------------------------ figures
def figura(d, cale_imagine, alt_text, legenda_text, cheie=None, latime_cm=15.0, sursa=None):
    """Inline picture in its own centred paragraph (kept with its caption), alt text in docPr/descr,
    caption below: 'Figura n. text'."""
    p = d.add_paragraph(style="Figură")
    run = p.add_run()
    run.add_picture(cale_imagine, width=Cm(latime_cm))
    for dp in run._r.iter(qn("wp:docPr")):
        dp.set("descr", alt_text)
        dp.set("title", legenda_text[:60])
    cap = legenda(d, "Figura", legenda_text, cheie=cheie)
    cap.paragraph_format.keep_with_next = bool(sursa)
    cap.paragraph_format.space_after = Pt(4 if sursa else 12)
    if sursa:
        paragraf(d, f"Sursa: {sursa}.", style="Tabel notă")
    return p
