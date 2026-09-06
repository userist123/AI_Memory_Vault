from reportlab.lib.pagesizes import A4
from reportlab.lib.units import mm
from reportlab.pdfgen import canvas
from reportlab.lib import colors
from datetime import datetime
from pathlib import Path

class PDFExporter:
    def __init__(self, config: dict):
        self.config = config

    def export_transfer(self, record: dict, filename: str):
        c = canvas.Canvas(filename, pagesize=A4)
        width, height = A4

        # Watermark
        if self.config.get("watermark_pdf", True) and record.get("clasificare") != "Nesecret":
            c.saveState()
            c.setFont("Helvetica-Bold", 60)
            c.setFillColorRGB(0.9, 0.9, 0.9)
            c.translate(width / 2, height / 2)
            c.rotate(45)
            c.drawCentredString(0, 0, record["clasificare"].upper())
            c.restoreState()

        # Header
        c.setFont("Helvetica-Bold", 16)
        c.drawString(30*mm, height - 30*mm, "REGISTRU DE EVIDENȚĂ TRANSFERURI")
        c.setFont("Helvetica", 10)
        c.drawString(30*mm, height - 40*mm, f"{self.config.get('denumire', '')} - {self.config.get('functionar', '')}")

        y = height - 60*mm
        c.setFont("Helvetica-Bold", 12)
        c.drawString(30*mm, y, f"Nr. Registru: {record['nr']}")
        y -= 6*mm

        # Detalii
        fields = [
            ("Data Creare", record.get("date_created", "")[:10]),
            ("Operator", record.get("operator", "")),
            ("Clasificare", record.get("clasificare", "")),
            ("Sursă Instituție", record.get("src_institutie", "")),
            ("Sursă PC", record.get("src_pc_nume", "")),
            ("Sursă Mediu", record.get("src_medium", "")),
            ("Sursă S/N", record.get("src_sn", "")),
            ("Persoană Primitor", record.get("pers_nume", "")),
            ("Funcție", record.get("pers_functie", "")),
            ("Legitimație", record.get("pers_legitimatie", "")),
            ("Autorizație", record.get("pers_autorizatie", "")),
            ("Transfer Mediu", record.get("transfer_medium", "")),
            ("Transfer S/N", record.get("transfer_sn", "")),
            ("Transfer Label", record.get("transfer_label", "")),
            ("Capacitate GB", str(record.get("transfer_cap_gb", ""))),
            ("Destinație Instituție", record.get("dst_institutie", "")),
            ("Destinație PC", record.get("dst_pc_nume", "")),
            ("Arhivă Nume", record.get("arhiva_nume", "")),
            ("Arhivă Tip", record.get("arhiva_tip", "")),
            ("Arhivă Dimensiune GB", str(record.get("arhiva_dim_gb", ""))),
            ("Arhivă Fișiere", str(record.get("arhiva_fisiere", ""))),
            ("Arhivă Hash", record.get("arhiva_hash", "")),
            ("Restricții", record.get("restrictii", "")),
            ("Aprobare Multiplicare", record.get("aprobare_mult", "")),
            ("Bază Legală", record.get("baza_legala", "")),
            ("Observații", record.get("observatii", "")),
        ]

        c.setFont("Helvetica", 9)
        for label, value in fields:
            if value:
                c.drawString(30*mm, y, f"{label}: {value}")
                y -= 5*mm
                if y < 40*mm:
                    c.showPage()
                    y = height - 30*mm

        # Footer
        c.setFont("Helvetica-Oblique", 8)
        c.drawString(30*mm, 20*mm, f"Generat: {datetime.now().strftime('%d.%m.%Y %H:%M')} | Hash: {record.get('hash_inregistrare', '')[:16]}")

        c.save()
        return filename

    def export_registru(self, records: list, filename: str):
        """Export PDF al registrului complet."""
        c = canvas.Canvas(filename, pagesize=A4)
        width, height = A4
        
        c.setFont("Helvetica-Bold", 14)
        c.drawString(30*mm, height - 30*mm, "REGISTRU TRANSFERURI - LISTA COMPLETĂ")
        c.setFont("Helvetica", 8)
        
        y = height - 45*mm
        c.drawString(30*mm, y, "Nr.")
        c.drawString(50*mm, y, "Data")
        c.drawString(75*mm, y, "Sursă Instituție")
        c.drawString(115*mm, y, "Persoană")
        c.drawString(155*mm, y, "Mediu")
        c.drawString(180*mm, y, "Clasificare")
        y -= 5*mm
        
        for rec in records:
            if rec.get("status") == "anulat":
                continue
            c.drawString(30*mm, y, rec.get("nr", "")[:15])
            c.drawString(50*mm, y, rec.get("date_created", "")[:10])
            c.drawString(75*mm, y, rec.get("src_institutie", "")[:20])
            c.drawString(115*mm, y, rec.get("pers_nume", "")[:20])
            c.drawString(155*mm, y, rec.get("transfer_medium", "")[:15])
            c.drawString(180*mm, y, rec.get("clasificare", "")[:12])
            y -= 5*mm
            if y < 30*mm:
                c.showPage()
                y = height - 30*mm
        
        c.save()
        return filename
