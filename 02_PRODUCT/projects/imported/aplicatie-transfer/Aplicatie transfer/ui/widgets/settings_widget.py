from PyQt6.QtWidgets import (QWidget, QVBoxLayout, QFormLayout, QLineEdit,
                              QComboBox, QPushButton, QGroupBox, QSpinBox,
                              QCheckBox, QFileDialog, QMessageBox, QLabel,
                              QHBoxLayout, QTextEdit)
from PyQt6.QtGui import QFont
from PyQt6.QtCore import pyqtSignal

class SettingsWidget(QWidget):
    settings_changed = pyqtSignal(dict)

    def __init__(self, config: dict):
        super().__init__()
        self.config = config.copy()
        self._setup_ui()
        self._load_settings()

    def _setup_ui(self):
        layout = QVBoxLayout(self)
        
        # Titlu
        lbl_title = QLabel("⚙️ Setări Aplicație")
        lbl_title.setFont(QFont("Segoe UI", 14, QFont.Weight.Bold))
        layout.addWidget(lbl_title)
        
        # Setări generale
        layout.addWidget(self._create_general_section())
        
        # Setări registru
        layout.addWidget(self._create_registru_section())
        
        # Setări backup
        layout.addWidget(self._create_backup_section())
        
        # Setări afișare
        layout.addWidget(self._create_display_section())
        
        layout.addStretch()
        
        # Butoane
        buttons = QHBoxLayout()
        buttons.addStretch()
        
        btn_save = QPushButton("💾 Salvare Setări")
        btn_save.setMinimumWidth(150)
        btn_save.clicked.connect(self._save_settings)
        
        btn_cancel = QPushButton("❌ Anulare")
        btn_cancel.setMinimumWidth(150)
        btn_cancel.clicked.connect(self._load_settings)
        
        buttons.addWidget(btn_save)
        buttons.addWidget(btn_cancel)
        layout.addLayout(buttons)

    def _create_general_section(self):
        group = QGroupBox("Setări Generale")
        group.setFont(QFont("Segoe UI", 10, QFont.Weight.Bold))
        layout = QFormLayout()
        
        self.db_path = QLineEdit()
        db_browse = QPushButton("Browse...")
        db_browse.clicked.connect(self._browse_db_path)
        db_layout = QHBoxLayout()
        db_layout.addWidget(self.db_path, 3)
        db_layout.addWidget(db_browse)
        
        self.backup_dir = QLineEdit()
        backup_browse = QPushButton("Browse...")
        backup_browse.clicked.connect(self._browse_backup_dir)
        backup_layout = QHBoxLayout()
        backup_layout.addWidget(self.backup_dir, 3)
        backup_layout.addWidget(backup_browse)
        
        self.export_dir = QLineEdit()
        export_browse = QPushButton("Browse...")
        export_browse.clicked.connect(self._browse_export_dir)
        export_layout = QHBoxLayout()
        export_layout.addWidget(self.export_dir, 3)
        export_layout.addWidget(export_browse)
        
        layout.addRow("Cale bază de date:", db_layout)
        layout.addRow("Director backup:", backup_layout)
        layout.addRow("Director export:", export_layout)
        
        group.setLayout(layout)
        return group

    def _create_registru_section(self):
        group = QGroupBox("Setări Registru")
        group.setFont(QFont("Segoe UI", 10, QFont.Weight.Bold))
        layout = QFormLayout()
        
        self.registru_prefix = QLineEdit()
        self.registru_prefix.setPlaceholderText("Ex: MAPN")
        self.registru_prefix.setMaxLength(10)
        
        self.clasificare_default = QComboBox()
        self.clasificare_default.addItems([
            "Nesecret", "Secret de Serviciu", "Secret", "Strict Secret"
        ])
        
        self.log_medium_default = QComboBox()
        self.log_medium_default.addItems([
            "HDD Intern", "HDD Extern", "SSD Intern", "SSD Extern",
            "USB Flash Drive", "DVD/CD", "NAS/Network"
        ])
        
        self.baza_legala_default = QLineEdit()
        self.baza_legala_default.setPlaceholderText("Ex: HG 585/2002 Art. 69, 71")
        
        layout.addRow("Prefix nr. registru:", self.registru_prefix)
        layout.addRow("Clasificare implicită:", self.clasificare_default)
        layout.addRow("Mediu stocare implicit:", self.log_medium_default)
        layout.addRow("Bază legală implicită:", self.baza_legala_default)
        
        group.setLayout(layout)
        return group

    def _create_backup_section(self):
        group = QGroupBox("Setări Backup")
        group.setFont(QFont("Segoe UI", 10, QFont.Weight.Bold))
        layout = QFormLayout()
        
        self.backup_enabled = QCheckBox("Activează backup automat")
        
        self.backup_interval = QSpinBox()
        self.backup_interval.setRange(1, 168)
        self.backup_interval.setSuffix(" ore")
        
        self.backup_keep_count = QSpinBox()
        self.backup_keep_count.setRange(1, 100)
        self.backup_keep_count.setSuffix(" fișiere")
        
        self.backup_compress = QCheckBox("Comprimare backup (ZIP)")
        
        layout.addRow("", self.backup_enabled)
        layout.addRow("Interval backup:", self.backup_interval)
        layout.addRow("Păstrare backup-uri:", self.backup_keep_count)
        layout.addRow("", self.backup_compress)
        
        group.setLayout(layout)
        return group

    def _create_display_section(self):
        group = QGroupBox("Setări Afișare")
        group.setFont(QFont("Segoe UI", 10, QFont.Weight.Bold))
        layout = QFormLayout()
        
        self.table_font_size = QSpinBox()
        self.table_font_size.setRange(8, 16)
        self.table_font_size.setSuffix(" px")
        
        self.table_row_height = QSpinBox()
        self.table_row_height.setRange(20, 50)
        self.table_row_height.setSuffix(" px")
        
        self.enable_tooltips = QCheckBox("Afișează tooltips")
        self.enable_notifications = QCheckBox("Notificări desktop")
        self.enable_sound = QCheckBox("Sunete aplicație")
        
        layout.addRow("Dimensiune font tabel:", self.table_font_size)
        layout.addRow("Înălțime rând tabel:", self.table_row_height)
        layout.addRow("", self.enable_tooltips)
        layout.addRow("", self.enable_notifications)
        layout.addRow("", self.enable_sound)
        
        group.setLayout(layout)
        return group

    def _browse_db_path(self):
        file, _ = QFileDialog.getSaveFileName(
            self, "Selectare bază de date", "", "SQLite DB (*.db)"
        )
        if file:
            self.db_path.setText(file)

    def _browse_backup_dir(self):
        dir_ = QFileDialog.getExistingDirectory(self, "Selectare director backup")
        if dir_:
            self.backup_dir.setText(dir_)

    def _browse_export_dir(self):
        dir_ = QFileDialog.getExistingDirectory(self, "Selectare director export")
        if dir_:
            self.export_dir.setText(dir_)

    def _load_settings(self):
        self.db_path.setText(self.config.get("db_path", "transferuri.db"))
        self.backup_dir.setText(self.config.get("backup_dir", "./backup"))
        self.export_dir.setText(self.config.get("export_dir", "./export"))
        
        self.registru_prefix.setText(self.config.get("registru_prefix", "MAPN"))
        self.clasificare_default.setCurrentText(self.config.get("clasificare_default", "Nesecret"))
        self.log_medium_default.setCurrentText(self.config.get("log_medium_default", "HDD Intern"))
        self.baza_legala_default.setText(self.config.get("baza_legala_default", "HG 585/2002"))
        
        self.backup_enabled.setChecked(self.config.get("backup_enabled", True))
        self.backup_interval.setValue(self.config.get("backup_interval_hours", 24))
        self.backup_keep_count.setValue(self.config.get("backup_keep_count", 10))
        self.backup_compress.setChecked(self.config.get("backup_compress", True))
        
        self.table_font_size.setValue(self.config.get("table_font_size", 9))
        self.table_row_height.setValue(self.config.get("table_row_height", 30))
        self.enable_tooltips.setChecked(self.config.get("enable_tooltips", True))
        self.enable_notifications.setChecked(self.config.get("enable_notifications", False))
        self.enable_sound.setChecked(self.config.get("enable_sound", False))

    def _save_settings(self):
        self.config["db_path"] = self.db_path.text()
        self.config["backup_dir"] = self.backup_dir.text()
        self.config["export_dir"] = self.export_dir.text()
        
        self.config["registru_prefix"] = self.registru_prefix.text()
        self.config["clasificare_default"] = self.clasificare_default.currentText()
        self.config["log_medium_default"] = self.log_medium_default.currentText()
        self.config["baza_legala_default"] = self.baza_legala_default.text()
        
        self.config["backup_enabled"] = self.backup_enabled.isChecked()
        self.config["backup_interval_hours"] = self.backup_interval.value()
        self.config["backup_keep_count"] = self.backup_keep_count.value()
        self.config["backup_compress"] = self.backup_compress.isChecked()
        
        self.config["table_font_size"] = self.table_font_size.value()
        self.config["table_row_height"] = self.table_row_height.value()
        self.config["enable_tooltips"] = self.enable_tooltips.isChecked()
        self.config["enable_notifications"] = self.enable_notifications.isChecked()
        self.config["enable_sound"] = self.enable_sound.isChecked()
        
        self.settings_changed.emit(self.config)
        QMessageBox.information(self, "Succes", "Setări salvate cu succes!")

    def get_config(self) -> dict:
        return self.config.copy()
