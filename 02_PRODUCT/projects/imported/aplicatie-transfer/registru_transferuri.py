import sys
import os
import shutil
import sqlite3
import json
import uuid
import logging
import hashlib
import configparser
from datetime import datetime
from pathlib import Path
from typing import Optional, List, Dict

# --- Dependențe Externe ---
from PyQt6.QtWidgets import (
    QApplication, QMainWindow, QWidget, QTabWidget, QVBoxLayout,
    QHBoxLayout, QLabel, QPushButton, QStatusBar, QMessageBox,
    QScrollArea, QGroupBox, QFormLayout, QLineEdit, QComboBox,
    QCheckBox, QDialog, QDialogButtonBox, QTableWidget, QTableWidgetItem, 
    QHeaderView, QCompleter, QFileDialog, QProgressBar, QAbstractItemView,
    QSplitter, QDoubleSpinBox, QTextEdit
)
from PyQt6.QtCore import Qt, pyqtSignal, QThread
from PyQt6.QtGui import QFont, QCloseEvent

from reportlab.lib.pagesizes import A4, landscape
from reportlab.lib.units import cm
from reportlab.lib import colors
from reportlab.platypus import SimpleDocTemplate, Table, TableStyle, Paragraph, Spacer
from reportlab.lib.styles import getSampleStyleSheet, ParagraphStyle
from reportlab.lib.enums import TA_CENTER

# ==========================================
# 1. CONSTANTE, REGLUI ȘI SCHEMĂ DB
# ==========================================

MEDIUM_OPTS = ['USB Flash Drive', 'HDD Extern USB', 'CD/DVD/BD', 'Rețea locală (LAN/SMB)', 'HDD Intern']

# Ierarhia de clasificare pentru validare
CLASIFICARI_NIVEL = {
    'Nesecret': 0,
    'Secret de Serviciu': 1,
    'Secret': 2,
    'Strict Secret': 3
}
CLASIFICARI = list(CLASIFICARI_NIVEL.keys())

# Schemă hibridă: include metadatele complexe ȘI căile fizice pentru transfer
SCHEMA_SQL = """
CREATE TABLE IF NOT EXISTS transferuri (
    id TEXT PRIMARY KEY,
    nr TEXT NOT NULL UNIQUE,
    date_created TEXT NOT NULL,
    operator TEXT NOT NULL,
    adresa_insotire TEXT NOT NULL,
    
    -- Sursă Metadate
    src_institutie TEXT,
    src_pc_nume TEXT,
    src_medium TEXT,
    
    -- Destinație Metadate
    dst_institutie TEXT,
    dst_pc_nume TEXT,
    dst_medium TEXT,
    
    -- Persoană (Delegat)
    pers_nume TEXT,
    pers_functie TEXT,
    
    -- Căi Fizice și Securitate (Noi)
    src_path TEXT NOT NULL,
    clasificare_sursa TEXT NOT NULL,
    dst_path TEXT NOT NULL,
    clasificare_destinatie TEXT NOT NULL,
    scanat_antivirus INTEGER DEFAULT 0,
    
    -- Integritate
    status TEXT NOT NULL DEFAULT 'transferat',
    observatii TEXT,
    hash_inregistrare TEXT,
    hash_fisiere TEXT
);

CREATE TABLE IF NOT EXISTS contoare (
    an INTEGER PRIMARY KEY,
    contor INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS autocomplete (
    categorie TEXT NOT NULL,
    valoare TEXT NOT NULL,
    frecventa INTEGER DEFAULT 1,
    ultima_data TEXT,
    PRIMARY KEY (categorie, valoare)
);

CREATE TABLE IF NOT EXISTS audit_log (
    id TEXT PRIMARY KEY,
    transfer_id TEXT,
    actiune TEXT NOT NULL,
    operator TEXT,
    timestamp TEXT NOT NULL,
    detalii TEXT
);
"""

# ==========================================
# 2. LOGICĂ THREAD TRANSFER FIȘIERE (Cu Hash)
# ==========================================
class TransferWorker(QThread):
    progress = pyqtSignal(int)
    finished = pyqtSignal(str, str) # Emite: mesaj, hash_fisiere
    error = pyqtSignal(str)

    def __init__(self, src: str, dst: str):
        super().__init__()
        self.src = src
        self.dst = dst

    def run(self):
        try:
            if not os.path.exists(self.src):
                raise FileNotFoundError(f"Sursa nu există: {self.src}")
            
            sha256 = hashlib.sha256()
            
            if os.path.isfile(self.src):
                nume_fisier = os.path.basename(self.src)
                dst_file = os.path.join(self.dst, nume_fisier)
                shutil.copy2(self.src, dst_file)
                self._update_hash(dst_file, sha256)
                
            elif os.path.isdir(self.src):
                nume_folder = os.path.basename(self.src.rstrip(os.sep))
                dst_dir = os.path.join(self.dst, nume_folder)
                shutil.copytree(self.src, dst_dir, dirs_exist_ok=True)
                
                # Calcul hash pentru întregul folder copiat
                for root, _, files in os.walk(dst_dir):
                    for file in files:
                        self._update_hash(os.path.join(root, file), sha256)
                
            self.progress.emit(100)
            self.finished.emit("Transfer finalizat cu succes!", sha256.hexdigest())
        except Exception as e:
            self.error.emit(str(e))
            
    def _update_hash(self, filepath, hasher):
        with open(filepath, 'rb') as f:
            while chunk := f.read(65536):
                hasher.update(chunk)

# ==========================================
# 3. LOGICĂ DE BAZĂ DE DATE (SQLite)
# ==========================================
class DatabaseManager:
    def __init__(self, db_path: str = "registru_hibrid.db"):
        self.db_path = Path(db_path)
        self.conn = sqlite3.connect(str(self.db_path), check_same_thread=False)
        self.conn.row_factory = sqlite3.Row
        self._init_schema()

    def _init_schema(self):
        self.conn.executescript(SCHEMA_SQL)
        self.conn.commit()

    def get_next_nr(self, prefix="REG") -> str:
        an = datetime.now().year
        cursor = self.conn.execute(
            "INSERT INTO contoare(an, contor) VALUES(?, 1) "
            "ON CONFLICT(an) DO UPDATE SET contor=contor+1 RETURNING contor",
            (an,)
        )
        contor = cursor.fetchone()[0]
        self.conn.commit()
        return f"{prefix}-{an}-{contor:04d}"

    def insert_transfer(self, data: dict, prefix="REG") -> str:
        record_id = str(uuid.uuid4())
        nr = self.get_next_nr(prefix)
        now = datetime.now().isoformat()

        # Hash integritate rând DB
        hash_val = hashlib.sha256(f"{nr}{now}{data.get('src_path')}{data.get('dst_path')}".encode()).hexdigest()

        fields = {**data, "id": record_id, "nr": nr, "date_created": now, "hash_inregistrare": hash_val}
        cols = ", ".join(fields.keys())
        placeholders = ", ".join(["?" for _ in fields])
        
        self.conn.execute(f"INSERT INTO transferuri ({cols}) VALUES ({placeholders})", list(fields.values()))
        self.conn.commit()

        self._log_audit(record_id, "TRANSFER_COMPLET", data.get("operator", ""), f"Ref: {data.get('adresa_insotire')}")
        self._update_autocomplete(data)
        return record_id

    def _update_autocomplete(self, data: dict):
        ac_map = {
            "institutii": [data.get("src_institutie"), data.get("dst_institutie")],
            "pcuri": [data.get("src_pc_nume"), data.get("dst_pc_nume")],
            "persoane": [data.get("pers_nume")]
        }
        now = datetime.now().isoformat()
        for cat, vals in ac_map.items():
            for val in vals:
                if val and str(val).strip():
                    self.conn.execute(
                        "INSERT INTO autocomplete(categorie, valoare, frecventa, ultima_data) "
                        "VALUES(?,?,1,?) ON CONFLICT(categorie, valoare) "
                        "DO UPDATE SET frecventa=frecventa+1, ultima_data=?",
                        (cat, str(val).strip(), now, now)
                    )
        self.conn.commit()

    def get_autocomplete(self, categorie: str, prefix: str = "") -> List[str]:
        rows = self.conn.execute(
            "SELECT valoare FROM autocomplete WHERE categorie=? AND valoare LIKE ? ORDER BY frecventa DESC LIMIT 20",
            (categorie, f"{prefix}%")
        ).fetchall()
        return [r[0] for r in rows]

    def get_all_transfers(self, search_text="") -> List[Dict]:
        query = "SELECT * FROM transferuri "
        params = []
        if search_text:
            query += "WHERE adresa_insotire LIKE ? OR pers_nume LIKE ? OR nr LIKE ? "
            params = [f"%{search_text}%", f"%{search_text}%", f"%{search_text}%"]
        query += "ORDER BY date_created DESC"
        
        rows = self.conn.execute(query, params).fetchall()
        return [dict(r) for r in rows]

    def _log_audit(self, transfer_id: str, actiune: str, operator: str, detalii: str = ""):
        self.conn.execute(
            "INSERT INTO audit_log(id, transfer_id, actiune, operator, timestamp, detalii) VALUES(?,?,?,?,?,?)",
            (str(uuid.uuid4()), transfer_id, actiune, operator, datetime.now().isoformat(), detalii)
        )
        self.conn.commit()

    def get_stats(self) -> dict:
        stats = {}
        stats["total"] = self.conn.execute("SELECT COUNT(*) FROM transferuri").fetchone()[0]
        stats["clasificate"] = self.conn.execute("SELECT COUNT(*) FROM transferuri WHERE clasificare_sursa != 'Nesecret'").fetchone()[0]
        luna = datetime.now().strftime("%Y-%m")
        stats["luna_curenta"] = self.conn.execute("SELECT COUNT(*) FROM transferuri WHERE substr(date_created,1,7)=?", (luna,)).fetchone()[0]
        return stats

    def close(self):
        self.conn.close()

# ==========================================
# 4. EXPORT PDF
# ==========================================
def export_registru_pdf(records: list, output_path: str, operator_name: str):
    doc = SimpleDocTemplate(output_path, pagesize=landscape(A4), topMargin=1.5*cm, bottomMargin=1.5*cm)
    styles = getSampleStyleSheet()
    story = []

    story.append(Paragraph("REGISTRU EVIDENȚĂ ȘI EXECUTARE TRANSFERURI (SISTEM ÎNCHIS)", ParagraphStyle('Title', fontSize=14, fontName='Helvetica-Bold', alignment=TA_CENTER)))
    story.append(Paragraph(f"Generat de: {operator_name} | Data: {datetime.now().strftime('%d.%m.%Y %H:%M')}", ParagraphStyle('Sub', fontSize=9, alignment=TA_CENTER, textColor=colors.grey)))
    story.append(Spacer(1, 0.5*cm))

    col_headers = ['Nr.', 'Data', 'Adresa', 'Inst. Sursă', 'Clasificare', 'Delegat', 'Destinație']
    col_widths = [2.5*cm, 2.5*cm, 3.5*cm, 4*cm, 2.5*cm, 3.5*cm, 4*cm]
    data = [col_headers]

    for r in records:
        data.append([
            r.get('nr', ''), r.get('date_created', '')[:10], r.get('adresa_insotire', ''),
            r.get('src_institutie', ''), r.get('clasificare_sursa', ''), r.get('pers_nume', ''),
            r.get('dst_institutie', '')
        ])

    table = Table(data, colWidths=col_widths, repeatRows=1)
    table.setStyle(TableStyle([
        ('BACKGROUND', (0,0), (-1,0), colors.HexColor('#1a1d27')),
        ('TEXTCOLOR', (0,0), (-1,0), colors.white),
        ('FONTNAME', (0,0), (-1,0), 'Helvetica-Bold'),
        ('FONTSIZE', (0,0), (-1,-1), 8),
        ('GRID', (0,0), (-1,-1), 0.5, colors.lightgrey),
        ('VALIGN', (0,0), (-1,-1), 'MIDDLE'),
    ]))
    story.append(table)
    doc.build(story)

# ==========================================
# 5. COMPONENTE UI (PyQt6)
# ==========================================

class TabTransferHibrid(QWidget):
    record_saved = pyqtSignal(str)

    def __init__(self, db, operator: str, parent=None):
        super().__init__(parent)
        self.db = db
        self.operator = operator
        self.worker = None
        self._build_ui()

    def _ac_input(self, categorie: str) -> QLineEdit:
        inp = QLineEdit()
        completer = QCompleter(self.db.get_autocomplete(categorie))
        completer.setCaseSensitivity(Qt.CaseSensitivity.CaseInsensitive)
        inp.setCompleter(completer)
        return inp

    def _build_ui(self):
        scroll = QScrollArea()
        scroll.setWidgetResizable(True)
        scroll.setFrameShape(QFrame.Shape.NoFrame)

        container = QWidget()
        layout = QVBoxLayout(container)
        layout.setSpacing(15)
        
        # --- A. METADATE ADMINISTRATIVE ---
        splitter = QSplitter(Qt.Orientation.Horizontal)
        
        grp_admin_stg = QGroupBox("Metadate Sursă & Context")
        g1 = QFormLayout(grp_admin_stg)
        self.txt_adresa = QLineEdit()
        self.txt_src_institutie = self._ac_input("institutii")
        self.txt_src_pc = self._ac_input("pcuri")
        self.txt_pers_nume = self._ac_input("persoane")
        g1.addRow("Adresa / Borderou *:", self.txt_adresa)
        g1.addRow("Instituție Sursă:", self.txt_src_institutie)
        g1.addRow("PC Sursă / Terminal:", self.txt_src_pc)
        g1.addRow("Nume Delegat *:", self.txt_pers_nume)
        
        grp_admin_dr = QGroupBox("Metadate Destinație")
        g2 = QFormLayout(grp_admin_dr)
        self.txt_dst_institutie = self._ac_input("institutii")
        self.txt_dst_pc = self._ac_input("pcuri")
        self.txt_observatii = QLineEdit()
        g2.addRow("Instituție Destinație:", self.txt_dst_institutie)
        g2.addRow("PC Destinație / Terminal:", self.txt_dst_pc)
        g2.addRow("Observații:", self.txt_observatii)
        
        splitter.addWidget(grp_admin_stg)
        splitter.addWidget(grp_admin_dr)
        layout.addWidget(splitter)

        # --- B. EXECUȚIE TRANSFER FIZIC ---
        grp_transfer = QGroupBox("Motor Execuție Transfer (Fizic)")
        t_lay = QVBoxLayout(grp_transfer)
        
        self.chk_scanat = QCheckBox("CONFIRM: Dispozitivul sursă / datele au fost scanate antivirus și sunt sigure.")
        self.chk_scanat.setStyleSheet("color: #e0a020; font-weight: bold; font-size: 13px;")
        t_lay.addWidget(self.chk_scanat)

        # Căi
        g3 = QFormLayout()
        
        # Sursa Cale
        box_src = QHBoxLayout()
        self.txt_src_path = QLineEdit(); self.txt_src_path.setReadOnly(True)
        btn_src_f = QPushButton("Fișier"); btn_src_f.clicked.connect(lambda: self._aleg("src_f"))
        btn_src_d = QPushButton("Folder"); btn_src_d.clicked.connect(lambda: self._aleg("src_d"))
        box_src.addWidget(self.txt_src_path); box_src.addWidget(btn_src_f); box_src.addWidget(btn_src_d)
        
        self.cbo_cls_sursa = QComboBox(); self.cbo_cls_sursa.addItems(CLASIFICARI)
        
        g3.addRow("Sursă Date (De unde?):", box_src)
        g3.addRow("Clasificare Date:", self.cbo_cls_sursa)

        # Dest Cale
        box_dst = QHBoxLayout()
        self.txt_dst_path = QLineEdit(); self.txt_dst_path.setReadOnly(True)
        btn_dst_d = QPushButton("Folder Destinație"); btn_dst_d.clicked.connect(lambda: self._aleg("dst_d"))
        box_dst.addWidget(self.txt_dst_path); box_dst.addWidget(btn_dst_d)
        
        self.cbo_cls_dst = QComboBox(); self.cbo_cls_dst.addItems(CLASIFICARI)
        
        g3.addRow("Destinație (Unde?):", box_dst)
        g3.addRow("Clasificare Mediu Destinație:", self.cbo_cls_dst)
        
        t_lay.addLayout(g3)
        layout.addWidget(grp_transfer)

        # --- C. STATUS & START ---
        self.lbl_status = QLabel("Așteptare comenzi...")
        self.lbl_status.setStyleSheet("color: #8b91a8;")
        self.progress = QProgressBar()
        self.progress.setValue(0)
        
        self.btn_executa = QPushButton("VALIDEAZĂ, COPIAZĂ ȘI ÎNREGISTREAZĂ")
        self.btn_executa.setObjectName("btnPrimary")
        self.btn_executa.setFixedHeight(45)
        self.btn_executa.clicked.connect(self._initiaza_transfer)
        
        layout.addWidget(self.lbl_status)
        layout.addWidget(self.progress)
        layout.addWidget(self.btn_executa)

        scroll.setWidget(container)
        main_vbox = QVBoxLayout(self)
        main_vbox.setContentsMargins(0,0,0,0)
        main_vbox.addWidget(scroll)

    def _aleg(self, tip):
        if tip == "src_f":
            path, _ = QFileDialog.getOpenFileName(self, "Alege fișier")
            if path: self.txt_src_path.setText(path)
        elif tip == "src_d":
            path = QFileDialog.getExistingDirectory(self, "Alege folder")
            if path: self.txt_src_path.setText(path)
        elif tip == "dst_d":
            path = QFileDialog.getExistingDirectory(self, "Alege folder destinație")
            if path: self.txt_dst_path.setText(path)

    def _initiaza_transfer(self):
        # 1. Validări Metadate
        if not self.txt_adresa.text().strip(): return self._err("Adresa / Borderoul este obligatorie.")
        if not self.txt_pers_nume.text().strip(): return self._err("Numele delegatului este obligatoriu.")
        
        # 2. Validări Securitate
        if not self.chk_scanat.isChecked(): return self._err("Trebuie să bifați confirmarea scanării antivirus!")
        if not self.txt_src_path.text() or not self.txt_dst_path.text(): return self._err("Selectați calea sursă și folderul destinație.")
        
        # 3. Validări Clasificare (Bell-LaPadula)
        c_src = self.cbo_cls_sursa.currentText()
        c_dst = self.cbo_cls_dst.currentText()
        if CLASIFICARI_NIVEL[c_src] > CLASIFICARI_NIVEL[c_dst]:
            return self._err(f"BLOCAT!\nRegula de secrietizare interzice scrierea datelor '{c_src}' pe un mediu destinat '{c_dst}'.")

        # Start Thread
        self.btn_executa.setEnabled(False)
        self.lbl_status.setText("Se calculează și se transferă datele...")
        self.progress.setRange(0, 0) 

        self.worker = TransferWorker(self.txt_src_path.text(), self.txt_dst_path.text())
        self.worker.progress.connect(self.progress.setValue)
        self.worker.finished.connect(self._transfer_incheiat)
        self.worker.error.connect(self._eroare_thread)
        self.worker.start()

    def _err(self, msg):
        self.lbl_status.setText(msg)
        self.lbl_status.setStyleSheet("color: #e04040; font-weight: bold;")
        QMessageBox.warning(self, "Eroare Securitate", msg)

    def _eroare_thread(self, err_msg):
        self.btn_executa.setEnabled(True)
        self.progress.setRange(0, 100); self.progress.setValue(0)
        self._err(f"Eroare I/O: {err_msg}")

    def _transfer_incheiat(self, msg, hash_val):
        self.btn_executa.setEnabled(True)
        self.progress.setRange(0, 100); self.progress.setValue(100)
        self.lbl_status.setText(f"{msg} Hash Integritate: {hash_val[:16]}...")
        self.lbl_status.setStyleSheet("color: #3dba6e; font-weight: bold;")
        
        # Construire pachet metadate complet
        data = {
            "operator": self.operator,
            "adresa_insotire": self.txt_adresa.text().strip(),
            "src_institutie": self.txt_src_institutie.text().strip(),
            "src_pc_nume": self.txt_src_pc.text().strip(),
            "pers_nume": self.txt_pers_nume.text().strip(),
            "dst_institutie": self.txt_dst_institutie.text().strip(),
            "dst_pc_nume": self.txt_dst_pc.text().strip(),
            "observatii": self.txt_observatii.text().strip(),
            "scanat_antivirus": 1,
            "src_path": self.txt_src_path.text(),
            "clasificare_sursa": self.cbo_cls_sursa.currentText(),
            "dst_path": self.txt_dst_path.text(),
            "clasificare_destinatie": self.cbo_cls_dst.currentText(),
            "status": "Transferat cu succes",
            "hash_fisiere": hash_val
        }
        
        try:
            rec_id = self.db.insert_transfer(data)
            self.record_saved.emit(rec_id)
            QMessageBox.information(self, "Succes", "Datele au fost transferate și actul adițional înregistrat în Registrul Digital.")
            self._reset_ui()
        except Exception as e:
            self._err(f"Eroare salvare DB: {e}")

    def _reset_ui(self):
        for w in [self.txt_adresa, self.txt_src_institutie, self.txt_src_pc, self.txt_pers_nume,
                  self.txt_dst_institutie, self.txt_dst_pc, self.txt_observatii,
                  self.txt_src_path, self.txt_dst_path]:
            w.clear()
        self.chk_scanat.setChecked(False)
        self.progress.setValue(0)
        self.lbl_status.setText("Așteptare comenzi...")
        self.lbl_status.setStyleSheet("color: #8b91a8;")

class TabRegistruCentral(QWidget):
    def __init__(self, db, operator, parent=None):
        super().__init__(parent)
        self.db = db
        self.operator = operator
        self._build_ui()

    def _build_ui(self):
        layout = QVBoxLayout(self)
        
        tools = QHBoxLayout()
        self.search = QLineEdit()
        self.search.setPlaceholderText("Caută adresă, persoană, număr...")
        self.search.textChanged.connect(self.refresh)
        tools.addWidget(self.search)

        btn_pdf = QPushButton("Export PDF")
        btn_pdf.clicked.connect(self._export)
        tools.addWidget(btn_pdf)
        layout.addLayout(tools)

        self.table = QTableWidget()
        self.table.setColumnCount(8)
        self.table.setHorizontalHeaderLabels(["Nr.", "Data", "Adresa", "Sursa", "Clasif.", "Delegat", "Destinație", "Hash (Integritate)"])
        self.table.horizontalHeader().setSectionResizeMode(QHeaderView.ResizeMode.Stretch)
        self.table.setEditTriggers(QAbstractItemView.EditTrigger.NoEditTriggers)
        self.table.setSelectionBehavior(QAbstractItemView.SelectionBehavior.SelectRows)
        layout.addWidget(self.table)
        
        self.refresh()

    def refresh(self):
        records = self.db.get_all_transfers(self.search.text().strip())
        self.table.setRowCount(len(records))
        for r_idx, r in enumerate(records):
            self.table.setItem(r_idx, 0, QTableWidgetItem(r.get("nr", "")))
            self.table.setItem(r_idx, 1, QTableWidgetItem(r.get("date_created", "")[:16].replace('T', ' ')))
            self.table.setItem(r_idx, 2, QTableWidgetItem(r.get("adresa_insotire", "")))
            self.table.setItem(r_idx, 3, QTableWidgetItem(r.get("src_institutie", "")))
            self.table.setItem(r_idx, 4, QTableWidgetItem(r.get("clasificare_sursa", "")))
            self.table.setItem(r_idx, 5, QTableWidgetItem(r.get("pers_nume", "")))
            self.table.setItem(r_idx, 6, QTableWidgetItem(r.get("dst_institutie", "")))
            
            # Afișare hash trunchiat pentru a încăpea în coloană
            hash_f = r.get("hash_fisiere", "")
            self.table.setItem(r_idx, 7, QTableWidgetItem(f"{hash_f[:10]}..." if hash_f else ""))

    def _export(self):
        path, _ = QFileDialog.getSaveFileName(self, "Export Registru", "Registru_Hibrid.pdf", "PDF (*.pdf)")
        if path:
            records = self.db.get_all_transfers()
            export_registru_pdf(records, path, self.operator)
            QMessageBox.information(self, "Succes", "Fișierul PDF a fost generat.")

class MainWindow(QMainWindow):
    def __init__(self, db_manager, operator: str):
        super().__init__()
        self.db = db_manager
        self.operator = operator
        self.setWindowTitle(f"Sistem Hibrid: Execuție Transfer & Evidență — Operator: {operator}")
        self.setMinimumSize(1250, 750)
        self._build_ui()
        self._apply_stylesheet()

    def _build_ui(self):
        main_w = QWidget()
        layout = QVBoxLayout(main_w)
        layout.setContentsMargins(0,0,0,0)

        # Tabs
        self.tabs = QTabWidget()
        self.tab_hibrid = TabTransferHibrid(self.db, self.operator)
        self.tab_registru = TabRegistruCentral(self.db, self.operator)
        
        self.tabs.addTab(self.tab_hibrid, "⚡ Execută & Înregistrează Transfer")
        self.tabs.addTab(self.tab_registru, "🗄 Registru Evidență (Istoric)")
        
        layout.addWidget(self.tabs)
        self.setCentralWidget(main_w)
        
        self.tab_hibrid.record_saved.connect(self._on_saved)

    def _on_saved(self):
        self.tab_registru.refresh()
        self.tabs.setCurrentIndex(1)

    def _apply_stylesheet(self):
        self.setStyleSheet("""
            QMainWindow, QWidget { background-color: #0f1117; color: #e8eaf0; font-family: 'Segoe UI', sans-serif; font-size: 13px; }
            QTabWidget::pane { border: 1px solid #2e3144; }
            QTabBar::tab { background: #1a1d27; color: #8b91a8; padding: 12px 20px; font-weight: bold; border-top-left-radius: 4px; border-top-right-radius: 4px; }
            QTabBar::tab:selected { background: #4f7ef8; color: white; }
            QLineEdit, QComboBox, QTextEdit { background-color: #252836; border: 1px solid #2e3144; border-radius: 4px; padding: 8px; color: white; }
            QPushButton { background-color: #252836; border: 1px solid #2e3144; border-radius: 4px; padding: 8px 15px; color: white; }
            QPushButton:hover { background-color: #3b4261; }
            QPushButton#btnPrimary { background-color: #4f7ef8; font-weight: bold; font-size: 14px; }
            QPushButton#btnPrimary:hover { background-color: #3b6ae8; }
            QGroupBox { border: 1px solid #2e3144; border-radius: 6px; margin-top: 15px; padding-top: 15px; font-weight: bold; color: #8b91a8; }
            QGroupBox::title { subcontrol-origin: margin; left: 10px; top: -8px; }
            QProgressBar { border: 1px solid #2e3144; border-radius: 4px; text-align: center; color: white; }
            QProgressBar::chunk { background-color: #3dba6e; width: 20px; }
            QTableWidget { background-color: #1a1d27; gridline-color: #2e3144; border: none; }
            QHeaderView::section { background-color: #252836; color: #8b91a8; padding: 6px; border: none; border-bottom: 1px solid #2e3144; font-weight: bold; }
            QSplitter::handle { background-color: #2e3144; width: 2px; }
        """)

def main():
    sys.excepthook = lambda cls, exc, tb: QMessageBox.critical(None, "Eroare", str(exc))

    app = QApplication(sys.argv)
    
    # Bază de date nouă integrată
    db = DatabaseManager("registru_hibrid_v2.db")

    operator = "Ofițer Securitate" 

    window = MainWindow(db, operator)
    window.show()

    sys.exit(app.exec())

if __name__ == "__main__":
    main()