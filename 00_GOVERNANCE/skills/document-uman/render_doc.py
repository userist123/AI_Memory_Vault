"""Render a document for visual review.

Usage: python -P -E render_doc.py <file.docx|file.xlsx|file.pdf> [outdir]
- .docx/.xlsx -> PDF through Microsoft Word/Excel (COM), so the PDF Producer is Microsoft, not a library.
- PDF pages -> PNG (pymupdf, 110 dpi) in outdir (default: <file>_render/).
Prints the PDF path and the PNG paths. Look at every PNG before delivering.
"""
import sys
from pathlib import Path


def to_pdf(src: Path) -> Path:
    import win32com.client  # pywin32
    out = src.with_suffix(".pdf")
    if src.suffix.lower() == ".docx":
        # bookmarks from headings, structure tags, doc properties, /Lang ro-RO (pdf_ro)
        sys.path.insert(0, str(Path(__file__).parent))
        import pdf_ro
        pdf_ro.word_la_pdf(src, out)
    elif src.suffix.lower() == ".xlsx":
        app = win32com.client.DispatchEx("Excel.Application")
        app.Visible = False
        app.DisplayAlerts = False
        old = (app.UseSystemSeparators, app.DecimalSeparator, app.ThousandsSeparator)
        try:
            # Romanian separators for the export even when Windows is en-US; restored below
            app.UseSystemSeparators = False
            app.DecimalSeparator, app.ThousandsSeparator = ",", "."
            wb = app.Workbooks.Open(str(src.resolve()), ReadOnly=True)
            wb.ExportAsFixedFormat(0, str(out.resolve()), IncludeDocProperties=True)  # xlTypePDF
            wb.Close(False)
        finally:
            app.UseSystemSeparators, app.DecimalSeparator, app.ThousandsSeparator = old
            app.Quit()
    else:
        raise SystemExit(f"unsupported: {src.suffix}")
    return out


def main():
    src = Path(sys.argv[1])
    pdf = src if src.suffix.lower() == ".pdf" else to_pdf(src)
    outdir = Path(sys.argv[2]) if len(sys.argv) > 2 else src.parent / (src.stem + "_render")
    outdir.mkdir(parents=True, exist_ok=True)
    import pymupdf
    doc = pymupdf.open(str(pdf))
    print("PDF", pdf, "pages", doc.page_count, "producer", doc.metadata.get("producer"))
    for i, page in enumerate(doc):
        png = outdir / f"pagina_{i + 1}.png"
        page.get_pixmap(dpi=110).save(str(png))
        print("PNG", png)


if __name__ == "__main__":
    main()
