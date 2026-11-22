"""Excel workbooks for document-uman (phase 3): inputs / calculations / outputs, Excel Tables,
named ranges, validation with messages, conditional formatting, restrained charts, check cells,
protection without password, print setup, Romanian number/date formats, clean properties.

    import xlsx_ro as xr
    wb = xr.registru("Autor", "Titlu")
    ws = xr.foaie(wb, "Parametri", "Titlu foaie", latimi={"B": 30, "C": 15})
    xr.tabel_excel(ws, "B5", "tbl_Ap", ["Ap.", "Persoane"], rows, formate={...}, intrari=[...])
    xr.nume(wb, "rata_pen", "Parametri", "$C$6")
    xr.validare_lista(ws, "C5:C40", "=lst_Criterii", "Alegeți criteriul", "Criteriu inexistent")
    xr.verificare(ws, "C20", "ABS(x-y)<0.01", ...)
    xr.grafic_coloane(ws_out, "B20", ws_out, "C5:C14", "B5:B14", titlu_celula="'Raport'!$B$2")
    xr.tiparire(ws, "B2:K50", peisaj=True, randuri_titlu="4:4", antet="Asociația ...")
    xr.salveaza(wb, path)   # save + Excel recalculation + absPath strip + formula error scan

Formulas inside Tables: use full structured references (tbl_X[[#This Row],[Col]]); the short
form [@Col] is UI syntax and is not valid in the file.
"""
import datetime as _dt
import re

from openpyxl import Workbook
from openpyxl.chart import BarChart, Reference
from openpyxl.chart.data_source import StrRef
from openpyxl.chart.label import DataLabelList
from openpyxl.chart.shapes import GraphicalProperties
from openpyxl.chart.text import RichText, Text
from openpyxl.chart.title import Title
from openpyxl.drawing.text import CharacterProperties, Paragraph, ParagraphProperties, Font as DFont
from openpyxl.formatting.rule import FormulaRule
from openpyxl.styles import Alignment, Border, Font, NamedStyle, PatternFill, Protection, Side
from openpyxl.workbook.defined_name import DefinedName
from openpyxl.worksheet.datavalidation import DataValidation
from openpyxl.worksheet.table import Table, TableColumn, TableFormula, TableStyleInfo

ACCENT = "1F3864"
GRI = "595959"
FILL_INTRARE = "FFF2CC"   # light yellow = cells the user fills in
FILL_ANTET = ACCENT
FMT_LEI = "#,##0.00"
FMT_INT = "#,##0"
FMT_PROC = "0.0%"
FMT_PROC3 = "0.000%"
FMT_DATA = "[$-418]dd.mm.yyyy"
FMT_LUNA = "[$-418]mmmm yyyy"
FMT_LUNA_SCURT = "[$-418]mmm"

_thin = Side(style="thin", color="BFBFBF")
_dark = Side(style="thin", color="404040")
_med = Side(style="medium", color="404040")


def registru(autor, titlu, font="Calibri", marime=10, companie=None):
    wb = Workbook()
    wb.remove(wb.active)
    p = wb.properties
    p.creator = p.lastModifiedBy = autor
    p.title = titlu
    p.subject = p.description = p.keywords = ""
    now = _dt.datetime.now(_dt.timezone.utc).replace(tzinfo=None, microsecond=0)
    p.created = p.modified = now
    wb._font_name, wb._font_size = font, marime
    stiluri = {
        "Text": dict(font=Font(name=font, size=marime)),
        "Intrare": dict(font=Font(name=font, size=marime), fill=PatternFill("solid", fgColor=FILL_INTRARE),
                        protection=Protection(locked=False)),
        "Antet": dict(font=Font(name=font, size=marime, bold=True, color="FFFFFF"),
                      fill=PatternFill("solid", fgColor=FILL_ANTET),
                      alignment=Alignment(wrap_text=True, vertical="center"),
                      border=Border(bottom=_med)),
        "Titlu foaie": dict(font=Font(name=font, size=marime + 4, bold=True, color=ACCENT)),
        "Notă": dict(font=Font(name=font, size=marime - 1, italic=True, color=GRI)),
        "Total": dict(font=Font(name=font, size=marime, bold=True), border=Border(top=_dark, bottom=_dark)),
    }
    for nume_st, kw in stiluri.items():
        ns = NamedStyle(name=nume_st, **kw)
        wb.add_named_style(ns)
    return wb


def foaie(wb, nume, titlu=None, latimi=None, subtitlu=None, culoare_tab=None):
    """New sheet: buffer column A (width 3) and buffer row 1, gridlines off, title in B2."""
    ws = wb.create_sheet(nume)
    ws.sheet_view.showGridLines = False
    ws.column_dimensions["A"].width = 3
    ws.row_dimensions[1].height = 10
    for col, w in (latimi or {}).items():
        ws.column_dimensions[col].width = w
    if titlu:
        c = ws["B2"]
        c.value = titlu
        c.style = "Titlu foaie"
        ws.row_dimensions[2].height = 21
    if subtitlu:
        ws["B3"].value = subtitlu
        ws["B3"].style = "Notă"
    if culoare_tab:
        ws.sheet_properties.tabColor = culoare_tab
    return ws


def _font(ws):
    wb = ws.parent
    return getattr(wb, "_font_name", "Calibri"), getattr(wb, "_font_size", 10)


def celula(ws, ref, valoare, fmt=None, intrare=False, bold=False, stil=None, aliniere=None):
    c = ws[ref]
    c.value = valoare
    c.style = stil or ("Intrare" if intrare else "Text")
    if fmt:
        c.number_format = fmt
    if bold:
        f, s = _font(ws)
        c.font = Font(name=f, size=s, bold=True)
    if aliniere:
        c.alignment = Alignment(horizontal=aliniere, vertical="center")
    return c


def tabel_excel(ws, stanga_sus, nume, antete, randuri, formate=None, intrari=(), formule=None,
                stil="TableStyleLight1", inaltime_antet=30, total=None):
    """Excel Table (ListObject) named `nume` at stanga_sus. randuri: values (None where a formula
    column is given). formule: {col_index: "tbl[[#This Row],[X]]*..."} (without '=') -> calculated
    column. intrari: column indexes the user fills in (yellow, unlocked). formate: {col: fmt}.
    Header: dark fill, white bold, wrapped. Horizontal light borders. Returns dict with ranges.
    total: {"eticheta": "Total", "coloane": {col: "SUBTOTAL(109,tbl[Col])"}} -> row under the table
    (outside the Table, so sorting/filtering does not move it)."""
    from openpyxl.utils import column_index_from_string, get_column_letter
    from openpyxl.utils.cell import coordinate_from_string
    formate = formate or {}
    formule = formule or {}
    col0_l, row0 = coordinate_from_string(stanga_sus)
    col0 = column_index_from_string(col0_l)
    n = len(randuri)
    for j, h in enumerate(antete):
        c = ws.cell(row=row0, column=col0 + j, value=h)
        c.style = "Antet"
        if j in formate and formate[j] in (FMT_LEI, FMT_INT, FMT_PROC, FMT_PROC3):
            c.alignment = Alignment(horizontal="right", vertical="center", wrap_text=True)
    ws.row_dimensions[row0].height = inaltime_antet
    for i, r in enumerate(randuri):
        rr = row0 + 1 + i
        for j in range(len(antete)):
            c = ws.cell(row=rr, column=col0 + j)
            if j in formule:
                c.value = "=" + formule[j]
                c.style = "Text"
            else:
                c.value = r[j] if j < len(r) else None
                c.style = "Intrare" if j in intrari else "Text"
            if j in formate:
                c.number_format = formate[j]
            c.border = Border(bottom=_thin)
    last_col = get_column_letter(col0 + len(antete) - 1)
    ref = f"{col0_l}{row0}:{last_col}{row0 + n}"
    t = Table(displayName=nume, ref=ref)
    t.tableStyleInfo = TableStyleInfo(name=stil, showRowStripes=False, showColumnStripes=False,
                                      showFirstColumn=False, showLastColumn=False)
    t._initialise_columns()
    for j, tc in enumerate(t.tableColumns):
        tc.name = str(antete[j])
        if j in formule:
            tc.calculatedColumnFormula = TableFormula(attr_text=formule[j])
    ws.add_table(t)
    info = {"ref": ref, "prim_rand": row0 + 1, "ultim_rand": row0 + n,
            "coloane": {antete[j]: get_column_letter(col0 + j) for j in range(len(antete))}}
    if total:
        rr = row0 + n + 1
        c = ws.cell(row=rr, column=col0, value=total.get("eticheta", "Total"))
        c.style = "Total"
        for j in range(1, len(antete)):
            cc = ws.cell(row=rr, column=col0 + j)
            cc.style = "Total"
            if j in total["coloane"]:
                cc.value = "=" + total["coloane"][j]
                cc.number_format = formate.get(j, FMT_LEI)
        info["rand_total"] = rr
    return info


def nume(wb, nume_def, foaie_nume=None, ref=None, formula=None):
    """Defined name: nume(wb, "rata", "Parametri", "$C$6") or nume(wb, "lst", formula="tbl[Col]")."""
    txt = formula if formula else f"'{foaie_nume}'!{ref}"
    wb.defined_names[nume_def] = DefinedName(nume_def, attr_text=txt)


def _dv(ws, interval, dv, titlu_mesaj, mesaj, eroare, titlu_eroare="Valoare nepermisă"):
    dv.promptTitle = titlu_mesaj[:32]
    dv.prompt = mesaj[:255]
    dv.errorTitle = titlu_eroare[:32]
    dv.error = eroare[:225]
    dv.showInputMessage = True
    dv.showErrorMessage = True
    dv.errorStyle = "stop"
    ws.add_data_validation(dv)
    dv.add(interval)
    return dv


def validare_lista(ws, interval, sursa, mesaj, eroare, titlu="Alegeți din listă"):
    """sursa: '=lst_Nume' (a defined name over a Table column, so the list grows)."""
    return _dv(ws, interval, DataValidation(type="list", formula1=sursa, allow_blank=False), titlu,
               mesaj, eroare)


def validare_data(ws, interval, start, end, mesaj, eroare, titlu="Dată"):
    f1 = f"DATE({start.year},{start.month},{start.day})"
    f2 = f"DATE({end.year},{end.month},{end.day})"
    return _dv(ws, interval, DataValidation(type="date", operator="between", formula1=f1, formula2=f2,
                                            allow_blank=False), titlu, mesaj, eroare)


def validare_numar(ws, interval, minim, maxim=None, intreg=False, mesaj="", eroare="", titlu="Valoare"):
    tip = "whole" if intreg else "decimal"
    if maxim is None:
        dv = DataValidation(type=tip, operator="greaterThanOrEqual", formula1=str(minim))
    else:
        dv = DataValidation(type=tip, operator="between", formula1=str(minim), formula2=str(maxim))
    return _dv(ws, interval, dv, titlu, mesaj, eroare)


def validare_formula(ws, interval, formula, mesaj, eroare, titlu="Verificare"):
    """Custom rule, e.g. no duplicates: 'COUNTIF($B$5:$B$44,B5)=1'."""
    return _dv(ws, interval, DataValidation(type="custom", formula1=formula), titlu, mesaj, eroare)


def format_conditionat(ws, interval, formula, culoare_fundal, culoare_text=None, bold=False):
    """One meaning per rule (e.g. arrears > 90 days), one hue; formula relative to the top-left cell."""
    font = Font(color=culoare_text, bold=bold) if (culoare_text or bold) else None
    ws.conditional_formatting.add(interval, FormulaRule(formula=[formula], font=font,
                                                        fill=PatternFill("solid", bgColor=culoare_fundal)))


def verificare(ws, ref, conditie, eticheta_ref=None, eticheta=None):
    """Check cell: =IF(conditie,"OK","EROARE"), green/red text by conditional formatting."""
    if eticheta_ref:
        celula(ws, eticheta_ref, eticheta)
    c = celula(ws, ref, f'=IF({conditie},"OK","EROARE")', bold=True, aliniere="center")
    format_conditionat(ws, ref, f'{ref.replace("$", "")}="EROARE"', "FFC7CE", "9C0006", True)
    format_conditionat(ws, ref, f'{ref.replace("$", "")}="OK"', "E2EFDA", "375623", True)
    return c


def _txpr(font, size, color=GRI, bold=False):
    cp = CharacterProperties(sz=int(size * 100), b=bold, solidFill=color, latin=DFont(typeface=font))
    return RichText(p=[Paragraph(pPr=ParagraphProperties(defRPr=cp), endParaRPr=cp)])


def _grafic(ws, tip_bara, ancora, ws_date, valori, categorii, titlu_celula, latime_cm, inaltime_cm,
            culoare, format_eticheta, culori_puncte=None):
    font, size = _font(ws)
    ch = BarChart()
    ch.type = tip_bara  # "col" or "bar"
    ch.grouping = "clustered"
    ch.style = 2
    data = Reference(ws_date, range_string=f"'{ws_date.title}'!{valori}")
    cats = Reference(ws_date, range_string=f"'{ws_date.title}'!{categorii}")
    ch.add_data(data, titles_from_data=False)
    ch.set_categories(cats)
    s = ch.series[0]
    s.graphicalProperties = GraphicalProperties(solidFill=culoare)
    s.graphicalProperties.line.noFill = True
    if culori_puncte:
        from openpyxl.chart.marker import DataPoint
        for idx, col in culori_puncte.items():
            pt = DataPoint(idx=idx)
            pt.graphicalProperties = GraphicalProperties(solidFill=col)
            pt.graphicalProperties.line.noFill = True
            s.dPt.append(pt)
    ch.gapWidth = 60
    ch.legend = None  # single series with direct labels: no legend
    ch.dataLabels = DataLabelList()
    ch.dataLabels.showVal = True
    for k in ("showSerName", "showCatName", "showLegendKey", "showPercent"):
        setattr(ch.dataLabels, k, False)
    ch.dataLabels.numFmt = format_eticheta
    ch.dataLabels.position = "outEnd"
    ch.dataLabels.txPr = _txpr(font, size - 1, "404040")
    # series-level labels too: Excel ignores the chart-level number format otherwise
    s.dLbls = DataLabelList(showVal=True, showSerName=False, showCatName=False, showLegendKey=False,
                            showPercent=False, dLblPos="outEnd")
    s.dLbls.txPr = _txpr(font, size - 1, "404040")
    # openpyxl writes numFmt without sourceLinked and Excel drops it; salveaza() patches the XML
    ws.parent.__dict__.setdefault("_fmt_grafice", []).append(format_eticheta)
    if tip_bara == "bar":  # room on the right for the outside-end labels
        from openpyxl.chart.layout import Layout, ManualLayout
        ch.layout = Layout(manualLayout=ManualLayout(xMode="edge", yMode="edge", x=0.02, y=0.14,
                                                               w=0.84, h=0.82))
    ch.y_axis.majorGridlines = None
    ch.y_axis.delete = True          # values are on the bars
    ch.x_axis.delete = False
    ch.x_axis.txPr = _txpr(font, size - 1)
    ch.x_axis.majorTickMark = "none"
    ch.y_axis.scaling.min = 0
    if tip_bara == "bar":
        ch.x_axis.scaling.orientation = "maxMin"  # first category on top
    ch.title = Title(tx=Text(strRef=StrRef(f=titlu_celula)), overlay=False)
    ch.title.txPr = _txpr(font, size + 1, "262626", bold=True)
    ch.width, ch.height = latime_cm, inaltime_cm
    ch.roundedCorners = False
    from openpyxl.chart.shapes import GraphicalProperties as GP
    ch.graphical_properties = GP()
    ch.graphical_properties.line.noFill = True
    ws.add_chart(ch, ancora)
    return ch


def grafic_coloane(ws, ancora, ws_date, valori, categorii, titlu_celula, latime_cm=16, inaltime_cm=7.5,
                   culoare=ACCENT, format_eticheta="#,##0", culori_puncte=None):
    """Column chart, one series, direct labels, no legend/gridlines/value axis, title = cell."""
    return _grafic(ws, "col", ancora, ws_date, valori, categorii, titlu_celula, latime_cm, inaltime_cm,
                   culoare, format_eticheta, culori_puncte)


def grafic_bare(ws, ancora, ws_date, valori, categorii, titlu_celula, latime_cm=16, inaltime_cm=7.5,
                culoare=ACCENT, format_eticheta="#,##0", culori_puncte=None):
    """Horizontal bars (sorted data reads best), same restraint as grafic_coloane."""
    return _grafic(ws, "bar", ancora, ws_date, valori, categorii, titlu_celula, latime_cm, inaltime_cm,
                   culoare, format_eticheta, culori_puncte)


def protejeaza(ws):
    """Sheet protection WITHOUT password (guards against accidental overwrite of formulas); input
    cells are the 'Intrare' style, already unlocked. Filtering and sorting stay allowed."""
    ws.protection.sheet = True
    ws.protection.autoFilter = False
    ws.protection.sort = False
    ws.protection.formatColumns = False
    ws.protection.formatRows = False


def tiparire(ws, zona, peisaj=False, randuri_titlu=None, antet=None, centrat=True, o_pagina=False):
    """A4, fit 1 page wide x N tall (o_pagina=True: 1x1 for a dashboard), centred, print area,
    repeated title rows, header (left text, right sheet name), footer 'Pagina X din Y'."""
    ps = ws.page_setup
    ps.paperSize = ws.PAPERSIZE_A4
    ps.orientation = "landscape" if peisaj else "portrait"
    ws.sheet_properties.pageSetUpPr.fitToPage = True
    ps.fitToWidth = 1
    ps.fitToHeight = 1 if o_pagina else 0
    ws.print_options.horizontalCentered = centrat
    ws.print_area = zona
    if randuri_titlu:
        ws.print_title_rows = randuri_titlu
    m = ws.page_margins
    m.left = m.right = 0.6
    m.top, m.bottom, m.header, m.footer = 0.75, 0.7, 0.3, 0.3
    font, size = _font(ws)
    if antet:
        ws.oddHeader.left.text = antet
        ws.oddHeader.left.size = size - 2
        ws.oddHeader.left.font = f"{font},Regular"
        ws.oddHeader.right.text = "&A"
        ws.oddHeader.right.size = size - 2
        ws.oddHeader.right.font = f"{font},Regular"
    ws.oddFooter.center.text = "Pagina &P din &N"
    ws.oddFooter.center.size = size - 2
    ws.oddFooter.center.font = f"{font},Regular"


def ascunde_restul(ws, ultima_coloana, ultimul_rand):
    """Dashboard: hide columns after the content and rows below it, so the sheet ends with the
    content (rows keep their explicit heights)."""
    from openpyxl.utils import column_index_from_string, get_column_letter
    nxt = get_column_letter(column_index_from_string(ultima_coloana) + 1)
    ws.column_dimensions.group(nxt, "XFD", hidden=True)
    for r in range(1, ultimul_rand + 1):
        if ws.row_dimensions[r].height is None:
            ws.row_dimensions[r].height = 13.8
    ws.sheet_format.zeroHeight = True


def _numfmt_etichete(path, formate):
    """Insert <c:numFmt sourceLinked="0"> into the data labels of chart1..n (creation order).
    Works on a file already saved by Excel (c: prefix, Excel element order)."""
    import shutil
    import zipfile
    if not formate:
        return
    tmp = path + ".tmp"
    with zipfile.ZipFile(path) as zin, zipfile.ZipFile(tmp, "w", zipfile.ZIP_DEFLATED) as zout:
        for item in zin.infolist():
            data = zin.read(item.filename)
            m = re.match(r"xl/charts/chart(\d+)\.xml$", item.filename)
            if m and int(m.group(1)) <= len(formate):
                f = formate[int(m.group(1)) - 1].replace('"', "&quot;")
                x = data.decode("utf-8")
                x = re.sub(r"(<(c:)?dLbls>)<(c:)?numFmt [^>]*/>", r"", x)
                x = re.sub(r"<(c:)?dLbls>", lambda mm: f'{mm.group(0)}<{mm.group(1) or ""}numFmt '
                           f'formatCode="{f}" sourceLinked="0"/>', x)
                data = x.encode("utf-8")
            zout.writestr(item, data)
    shutil.move(tmp, path)


ERORI = ("#REF!", "#NAME?", "#VALUE!", "#DIV/0!", "#N/A", "#NUM!", "#NULL!", "#SPILL!", "#CALC!")


def salveaza(wb, path, verifica=True):
    """Save, recalculate and re-save in Excel (docx_ro.finalizeaza_xlsx), then scan the cached
    values for Excel errors. Returns list of error cells (empty = good)."""
    import docx_ro
    import openpyxl
    wb.save(path)
    docx_ro.finalizeaza_xlsx(path)
    if getattr(wb, "_fmt_grafice", None):
        # Excel rejects a numFmt inside openpyxl's dLbls but accepts it in its own XML: patch the
        # Excel-saved file, then let Excel save it once more
        _numfmt_etichete(path, wb._fmt_grafice)
        docx_ro.finalizeaza_xlsx(path)
    erori = []
    if verifica:
        wbv = openpyxl.load_workbook(path, data_only=True)
        for ws in wbv.worksheets:
            for row in ws.iter_rows():
                for c in row:
                    if isinstance(c.value, str) and c.value in ERORI:
                        erori.append(f"{ws.title}!{c.coordinate} {c.value}")
                    if c.value == "EROARE":
                        erori.append(f"{ws.title}!{c.coordinate} check EROARE")
    return erori


_CONST = re.compile(r"(?<![A-Za-z_$\d.\[])(\d+\.\d+|\d{2,})(?![\d.]*[A-Za-z_(\]!:])")


def constante_in_formule(path, permise=("0", "1", "2", "100", "109")):
    """Numeric literals inside formulas (=B5*0.19): rule B37 - rates/limits belong in input cells.
    Returns list of 'Sheet!A1: formula'."""
    import openpyxl
    wb = openpyxl.load_workbook(path)
    out = []
    for ws in wb.worksheets:
        for row in ws.iter_rows():
            for c in row:
                v = c.value
                if isinstance(v, str) and v.startswith("="):
                    body = re.sub(r'"[^"]*"', "", v)
                    body = re.sub(r"DATE\([^)]*\)", "", body)
                    while re.search(r"\[[^\[\]]*\]", body):  # structured-reference names
                        body = re.sub(r"\[[^\[\]]*\]", "", body)
                    lits = [m for m in _CONST.findall(body) if m not in permise]
                    if lits:
                        out.append(f"{ws.title}!{c.coordinate}: {v[:80]}")
    return out
