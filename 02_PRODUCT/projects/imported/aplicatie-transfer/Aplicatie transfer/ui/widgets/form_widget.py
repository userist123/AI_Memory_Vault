from PyQt6.QtWidgets import (QWidget, QVBoxLayout, QHBoxLayout, QFormLayout,
                              QLineEdit, QComboBox, QTextEdit, QDoubleSpinBox,
                              QPushButton, QGroupBox, QMessageBox, QFileDialog,
                              QSpinBox, QCompleter, QScrollArea)
from PyQt6.QtCore import Qt, pyqtSignal, QStringListModel
from PyQt6.QtGui import QFont
import hashlib
from pathlib import Path

class TransferFormWidget(QWidget):
    record_saved = pyqtSignal(str, str)

    def __init__(self, db, operator_name: str, config: dict):
        super().__init__()
        self.db = db
        self.operator_name = operator_name
        self.config = config
        self.current_record_id = None
        self.selected_file_path = None
        self._setup_ui()
        self._load_defaults()

    def _setup_ui(self):
        layout = QVBoxLayout(self)
        
        scroll = QScrollArea()
        scroll.setWidgetResizable(True)
        scroll.setHorizontalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAlwaysOff)
        
        form_container = QWidget()
        form_layout = QVBoxLayout(form_container)
        
        form_layout.addWidget(self._create_sursa_section())
        form_layout.addWidget(self._create_persoana_section())
        form_layout.addWidget(self._create_transfer_section())
        form_layout.addWidget(self._create_destinatie_section())
        form_layout.addWidget(self._create_arhiva_section())
        form_layout.addWidget(self._create_conformitate_section())
        
        form_layout.addStretch()
        scroll.setWidget(form_container)
        layout.addWidget(scroll)
        
        # Butoane
        buttons = QHBoxLayout()
        buttons.addStretch()
        
        self.btn_save = QPushButton("💾 Salvare")
        self.btn_save.setMinimumWidth(150)
        self.btn_save.clicked.connect(self._save_record)
        
        self.btn_cancel = QPushButton("❌ Anulare")
        self.btn_cancel.setMinimumWidth(150)
        self.btn_cancel.clicked.connect(self._clear_form)
        
        buttons.addWidget(self.btn_save)
        buttons.addWidget(self.btn_cancel)
        layout.addLayout(buttons)

    def _create_sursa_section(self):
        group = QGroupBox("SURSĂ")
        group.setFont(QFont("Segoe UI", 10, QFont.Weight.Bold))
        layout = QFormLayout()
        
        self.src_institutie = QLineEdit()
        self.src_institutie.setPlaceholderText("Ex: MApN Brașov")
        
        self.src_pc_nume = QLineEdit()
        self.src_pc_nume.setPlaceholderText("Ex: PC-LAB-01")
        
        self.src_medium = QComboBox()
        self.src_medium.addItems([
            "HDD Intern", "HDD Extern", "SSD Intern", "SSD Extern",
            "USB Flash Drive", "DVD/CD", "Blu-ray", "NAS/Network", "Cloud"
        ])
        
        self.src_sn = QLineEdit()
        self.src_path = QLineEdit()
        
        layout.addRow("Instituție Sursă *:", self.src_institutie)
        layout.addRow("Nume PC *:", self.src_pc_nume)
        layout.addRow("Mediu Sursă *:", self.src_medium)
        layout.addRow("Serial Number:", self.src_sn)
        layout.addRow("Path:", self.src_path)
        
        group.setLayout(layout)
        return group

    def _create_persoana_section(self):
        group = QGroupBox("PERSOANĂ PRIMITOR")
        group.setFont(QFont("Segoe UI", 10, QFont.Weight.Bold))
        layout = QFormLayout()
        
        self.pers_nume = QLineEdit()
        self.pers_functie = QLineEdit()
        self.pers_legitimatie = QLineEdit()
        
        self.pers_autorizatie = QComboBox()
        self.pers_autorizatie.addItems([
            "Nesecurizat", "Acces Secret de Serviciu", "Acces Secret", "Acces Strict Secret"
        ])
        
        layout.addRow("Nume *:", self.pers_nume)
        layout.addRow("Funcție:", self.pers_functie)
        layout.addRow("Nr. Legitimație:", self.pers_legitimatie)
        layout.addRow("Autorizație *:", self.pers_autorizatie)
        
        group.setLayout(layout)
        return group

    def _create_transfer_section(self):
        group = QGroupBox("MEDIU DE TRANSFER")
        group.setFont(QFont("Segoe UI", 10, QFont.Weight.Bold))
        layout = QFormLayout()
        
        self.transfer_medium = QComboBox()
        self.transfer_medium.addItems([
            "USB Flash Drive", "HDD Extern", "SSD Extern", "DVD/CD", 
            "Blu-ray", "SD Card", "MicroSD", "Alte medii"
        ])
        
        self.transfer_sn = QLineEdit()
        self.transfer_label = QLineEdit()
        
        self.transfer_cap_gb = QDoubleSpinBox()
        self.transfer_cap_gb.setRange(0, 100000)
        self.transfer_cap_gb.setSuffix(" GB")
        self.transfer_cap_gb.setDecimals(2)
        
        self.transfer_free_gb = QDoubleSpinBox()
        self.transfer_free_gb.setRange(0, 100000)
        self.transfer_free_gb.setSuffix(" GB")
        self.transfer_free_gb.setDecimals(2)
        
        layout.addRow("Tip Mediu *:", self.transfer_medium)
        layout.addRow("Serial Number:", self.transfer_sn)
        layout.addRow("Label:", self.transfer_label)
        layout.addRow("Capacitate:", self.transfer_cap_gb)
        layout.addRow("Spațiu Liber:", self.transfer_free_gb)
        
        group.setLayout(layout)
        return group

    def _create_destinatie_section(self):
        group = QGroupBox("DESTINAȚIE")
        group.setFont(QFont("Segoe UI", 10, QFont.Weight.Bold))
        layout = QFormLayout()
        
        self.dst_institutie = QLineEdit()
        self.dst_pc_nume = QLineEdit()
        self.dst_medium = QComboBox()
        self.dst_medium.addItems([
            "HDD Intern", "HDD Extern", "SSD Intern", "SSD Extern",
            "USB Flash Drive", "DVD/CD", "NAS/Network"
        ])
        self.dst_sn = QLineEdit()
        self.dst_path = QLineEdit()
        
        btn_copy = QPushButton("📋 Copiere Sursă → Destinație")
        btn_copy.clicked.connect(self._copy_source_to_dest)
        
        layout.addRow("Instituție Dest *:", self.dst_institutie)
        layout.addRow("Nume PC:", self.dst_pc_nume)
        layout.addRow("Mediu Dest:", self.dst_medium)
        layout.addRow("Serial Number:", self.dst_sn)
        layout.addRow("Path:", self.dst_path)
        layout.addRow("", btn_copy)
        
        group.setLayout(layout)
        return group

    def _create_arhiva_section(self):
        group = QGroupBox("ARHIVĂ")
        group.setFont(QFont("Segoe UI", 10, QFont.Weight.Bold))
        layout = QFormLayout()
        
        self.arhiva_nume = QLineEdit()
        self.arhiva_tip = QComboBox()
        self.arhiva_tip.addItems(["ZIP", "7Z", "RAR", "TAR.GZ", "TAR.XZ", "Altul"])
        
        self.arhiva_dim_gb = QDoubleSpinBox()
        self.arhiva_dim_gb.setRange(0, 10000)
        self.arhiva_dim_gb.setSuffix(" GB")
        self.arhiva_dim_gb.setDecimals(3)
        
        self.arhiva_fisiere = QSpinBox()
        self.arhiva_fisiere.setRange(0, 1000000)
        
        hash_layout = QHBoxLayout()
        self.arhiva_hash = QLineEdit()
        btn_browse = QPushButton("Browse...")
        btn_browse.clicked.connect(self._browse_file_for_hash)
        btn_calc = QPushButton("Calculate")
        btn_calc.clicked.connect(self._calculate_hash)
        hash_layout.addWidget(self.arhiva_hash, 3)
        hash_layout.addWidget(btn_browse)
        hash_layout.addWidget(btn_calc)
        
        self.arhiva_descriere = QTextEdit()
        self.arhiva_descriere.setMaximumHeight(80)
        
        layout.addRow("Nume Fișier:", self.arhiva_nume)
        layout.addRow("Tip:", self.arhiva_tip)
        layout.addRow("Dimensiune:", self.arhiva_dim_gb)
        layout.addRow("Nr. Fișiere:", self.arhiva_fisiere)
        layout.addRow("Hash SHA256:", hash_layout)
        layout.addRow("Descriere:", self.arhiva_descriere)
        
        group.setLayout(layout)
        return group

    def _create_conformitate_section(self):
        group = QGroupBox("CONFORMITATE HG 585/2002")
        group.setFont(QFont("Segoe UI", 10, QFont.Weight.Bold))
        layout = QFormLayout()
        
        self.clasificare = QComboBox()
        self.clasificare.addItems([
            "Nesecret", "Secret de Serviciu", "Secret", "Strict Secret"
        ])
        
        self.restrictii = QLineEdit()
        self.aprobare_mult = QLineEdit()
        self.baza_legala = QLineEdit()
        self.observatii = QTextEdit()
        self.observatii.setMaximumHeight(80)
        
        layout.addRow("Clasificare *:", self.clasificare)
        layout.addRow("Restricții:", self.restrictii)
        layout.addRow("Aprobare Mult:", self.aprobare_mult)
        layout.addRow("Bază Legală:", self.baza_legala)
        layout.addRow("Observații:", self.observatii)
        
        group.setLayout(layout)
        return group

    def _load_defaults(self):
        self.clasificare.setCurrentText(self.config.get("clasificare_default", "Nesecret"))

    def _copy_source_to_dest(self):
        self.dst_institutie.setText(self.src_institutie.text())
        self.dst_pc_nume.setText(self.src_pc_nume.text())
        self.dst_medium.setCurrentText(self.src_medium.currentText())
        self.dst_sn.setText(self.src_sn.text())

    def _browse_file_for_hash(self):
        file, _ = QFileDialog.getOpenFileName(self, "Selectare fișier arhivă")
        if file:
            self.selected_file_path = file
            self.arhiva_nume.setText(Path(file).name)
            self.arhiva_dim_gb.setValue(Path(file).stat().st_size / (1024**3))

    def _calculate_hash(self):
        if not self.selected_file_path:
            QMessageBox.warning(self, "Avertisment", "Selectați un fișier mai întâi!")
            return
        try:
            sha256 = hashlib.sha256()
            with open(self.selected_file_path, "rb") as f:
                for chunk in iter(lambda: f.read(8192), b""):
                    sha256.update(chunk)
            self.arhiva_hash.setText(sha256.hexdigest())
        except Exception as e:
            QMessageBox.critical(self, "Eroare", f"Eroare calculare hash: {e}")

    def _validate_form(self) -> tuple[bool, str]:
        if not self.src_institutie.text().strip():
            return False, "Instituție Sursă este obligatorie"
        if not self.src_pc_nume.text().strip():
            return False, "Nume PC Sursă este obligatoriu"
        if not self.pers_nume.text().strip():
            return False, "Nume Persoană este obligatoriu"
        if not self.dst_institutie.text().strip():
            return False, "Instituție Destinație este obligatorie"
        
        if self.arhiva_hash.text() and len(self.arhiva_hash.text()) != 64:
            return False, "Hash SHA256 trebuie să aibă exact 64 caractere"
        
        if self.transfer_free_gb.value() > self.transfer_cap_gb.value():
            return False, "Spațiu liber nu poate fi mai mare decât capacitatea"
        
        return True, ""

    def _save_record(self):
        valid, msg = self._validate_form()
        if not valid:
            QMessageBox.warning(self, "Validare", msg)
            return
        
        data = {
            "operator": self.operator_name,
            "src_institutie": self.src_institutie.text(),
            "src_pc_nume": self.src_pc_nume.text(),
            "src_medium": self.src_medium.currentText(),
            "src_sn": self.src_sn.text(),
            "src_path": self.src_path.text(),
            "pers_nume": self.pers_nume.text(),
            "pers_functie": self.pers_functie.text(),
            "pers_legitimatie": self.pers_legitimatie.text(),
            "pers_autorizatie": self.pers_autorizatie.currentText(),
            "transfer_medium": self.transfer_medium.currentText(),
            "transfer_sn": self.transfer_sn.text(),
            "transfer_label": self.transfer_label.text(),
            "transfer_cap_gb": self.transfer_cap_gb.value() if self.transfer_cap_gb.value() > 0 else None,
            "transfer_free_gb": self.transfer_free_gb.value() if self.transfer_free_gb.value() > 0 else None,
            "dst_institutie": self.dst_institutie.text(),
            "dst_pc_nume": self.dst_pc_nume.text(),
            "dst_medium": self.dst_medium.currentText(),
            "dst_sn": self.dst_sn.text(),
            "dst_path": self.dst_path.text(),
            "arhiva_nume": self.arhiva_nume.text(),
            "arhiva_tip": self.arhiva_tip.currentText(),
            "arhiva_dim_gb": self.arhiva_dim_gb.value() if self.arhiva_dim_gb.value() > 0 else None,
            "arhiva_fisiere": self.arhiva_fisiere.value() if self.arhiva_fisiere.value() > 0 else None,
            "arhiva_hash": self.arhiva_hash.text(),
            "arhiva_descriere": self.arhiva_descriere.toPlainText(),
            "clasificare": self.clasificare.currentText(),
            "restrictii": self.restrictii.text(),
            "aprobare_mult": self.aprobare_mult.text(),
            "baza_legala": self.baza_legala.text(),
            "observatii": self.observatii.toPlainText(),
            "status": "active",
        }
        
        try:
            if self.current_record_id:
                self.db.update_transfer(self.current_record_id, data, self.operator_name)
                rec = self.db.get_transfer_by_id(self.current_record_id)
                nr = rec["nr"]
            else:
                record_id = self.db.insert_transfer(data)
                rec = self.db.get_transfer_by_id(record_id)
                nr = rec["nr"]
            
            QMessageBox.information(self, "Succes", f"Înregistrare salvată: {nr}")
            self.record_saved.emit(self.current_record_id or record_id, nr)
            self._clear_form()
        except Exception as e:
            QMessageBox.critical(self, "Eroare", f"Eroare salvare: {e}")

    def _clear_form(self):
        self.current_record_id = None
        for widget in self.findChildren((QLineEdit, QTextEdit)):
            if isinstance(widget, QLineEdit):
                widget.clear()
            else:
                widget.clear()
        for widget in self.findChildren((QDoubleSpinBox, QSpinBox)):
            widget.setValue(0)
        self._load_defaults()

    def load_record(self, record_id: str):
        rec = self.db.get_transfer_by_id(record_id)
        if not rec:
            return
        
        self.current_record_id = record_id
        self.src_institutie.setText(rec.get("src_institutie", ""))
        self.src_pc_nume.setText(rec.get("src_pc_nume", ""))
        self.src_medium.setCurrentText(rec.get("src_medium", ""))
        self.src_sn.setText(rec.get("src_sn", "") or "")
        self.src_path.setText(rec.get("src_path", "") or "")
        self.pers_nume.setText(rec.get("pers_nume", ""))
        self.pers_functie.setText(rec.get("pers_functie", "") or "")
        self.pers_legitimatie.setText(rec.get("pers_legitimatie", "") or "")
        self.pers_autorizatie.setCurrentText(rec.get("pers_autorizatie", "Nesecurizat"))
        self.transfer_medium.setCurrentText(rec.get("transfer_medium", ""))
        self.transfer_sn.setText(rec.get("transfer_sn", "") or "")
        self.transfer_label.setText(rec.get("transfer_label", "") or "")
        self.transfer_cap_gb.setValue(rec.get("transfer_cap_gb") or 0)
        self.transfer_free_gb.setValue(rec.get("transfer_free_gb") or 0)
        self.dst_institutie.setText(rec.get("dst_institutie", ""))
        self.dst_pc_nume.setText(rec.get("dst_pc_nume", "") or "")
        self.dst_medium.setCurrentText(rec.get("dst_medium", "") or "HDD Intern")
        self.dst_sn.setText(rec.get("dst_sn", "") or "")
        self.dst_path.setText(rec.get("dst_path", "") or "")
        self.arhiva_nume.setText(rec.get("arhiva_nume", "") or "")
        self.arhiva_tip.setCurrentText(rec.get("arhiva_tip", "") or "ZIP")
        self.arhiva_dim_gb.setValue(rec.get("arhiva_dim_gb") or 0)
        self.arhiva_fisiere.setValue(rec.get("arhiva_fisiere") or 0)
        self.arhiva_hash.setText(rec.get("arhiva_hash", "") or "")
        self.arhiva_descriere.setPlainText(rec.get("arhiva_descriere", "") or "")
        self.clasificare.setCurrentText(rec.get("clasificare", "Nesecret"))
        self.restrictii.setText(rec.get("restrictii", "") or "")
        self.aprobare_mult.setText(rec.get("aprobare_mult", "") or "")
        self.baza_legala.setText(rec.get("baza_legala", "") or "")
        self.observatii.setPlainText(rec.get("observatii", "") or "")
