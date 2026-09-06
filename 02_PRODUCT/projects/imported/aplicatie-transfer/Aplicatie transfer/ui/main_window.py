from PyQt6.QtWidgets import (QMainWindow, QWidget, QVBoxLayout, QHBoxLayout,
                              QTabWidget, QMenuBar, QStatusBar, QMenu, QMessageBox,
                              QPushButton, QLabel, QFileDialog)
from PyQt6.QtCore import Qt, QTimer, pyqtSignal
from PyQt6.QtGui import QAction, QFont, QIcon
from pathlib import Path
import json

from .widgets.form_widget import TransferFormWidget
from .widgets.table_widget import TransferTableWidget
from .widgets.stats_widget import StatsWidget
from .widgets.settings_widget import SettingsWidget

class MainWindow(QMainWindow):
    def __init__(self, db, operator_name: str, config: dict):
        super().__init__()
        self.db = db
        self.operator_name = operator_name
        self.config = config
        
        self.setWindowTitle(f"Registru Transferuri Media - {operator_name}")
        self.setMinimumSize(1400, 900)
        
        self._setup_ui()
        self._setup_menu()
        self._setup_statusbar()
        self._start_autosave_timer()

    def _setup_ui(self):
        central = QWidget()
        self.setCentralWidget(central)
        layout = QVBoxLayout(central)
        
        # Header
        header = QHBoxLayout()
        lbl_title = QLabel("🗂️ REGISTRU TRANSFERURI MEDIA")
        lbl_title.setFont(QFont("Segoe UI", 16, QFont.Weight.Bold))
        
        lbl_operator = QLabel(f"Operator: {self.operator_name}")
        lbl_operator.setFont(QFont("Segoe UI", 10))
        lbl_operator.setStyleSheet("color: #6B7280; font-style: italic;")
        
        btn_logout = QPushButton("🚪 Schimbare Operator")
        btn_logout.clicked.connect(self._logout)
        
        header.addWidget(lbl_title)
        header.addStretch()
        header.addWidget(lbl_operator)
        header.addWidget(btn_logout)
        
        layout.addLayout(header)
        
        # Tabs
        self.tabs = QTabWidget()
        self.tabs.setFont(QFont("Segoe UI", 10))
        
        # Tab 1: Form
        self.form_widget = TransferFormWidget(self.db, self.operator_name, self.config)
        self.form_widget.record_saved.connect(self._on_record_saved)
        self.tabs.addTab(self.form_widget, "📝 Înregistrare Nouă")
        
        # Tab 2: Tabel
        self.table_widget = TransferTableWidget(self.db)
        self.table_widget.record_selected.connect(self._on_record_selected)
        self.table_widget.record_deleted.connect(self._on_record_deleted)
        self.tabs.addTab(self.table_widget, "📋 Registru Complet")
        
        # Tab 3: Statistici
        self.stats_widget = StatsWidget(self.db)
        self.tabs.addTab(self.stats_widget, "📊 Statistici")
        
        # Tab 4: Setări
        self.settings_widget = SettingsWidget(self.config)
        self.settings_widget.settings_changed.connect(self._on_settings_changed)
        self.tabs.addTab(self.settings_widget, "⚙️ Setări")
        
        layout.addWidget(self.tabs)

    def _setup_menu(self):
        menubar = self.menuBar()
        
        # Fișier
        file_menu = menubar.addMenu("&Fișier")
        
        act_new = QAction("📝 Înregistrare Nouă", self)
        act_new.setShortcut("Ctrl+N")
        act_new.triggered.connect(lambda: self.tabs.setCurrentIndex(0))
        
        act_backup = QAction("💾 Backup Bază de Date", self)
        act_backup.setShortcut("Ctrl+B")
        act_backup.triggered.connect(self._create_backup)
        
        act_import = QAction("📥 Import CSV", self)
        act_import.triggered.connect(self._import_csv)
        
        act_export = QAction("📤 Export CSV", self)
        act_export.setShortcut("Ctrl+E")
        act_export.triggered.connect(self._export_csv)
        
        act_export_pdf = QAction("📄 Export PDF", self)
        act_export_pdf.triggered.connect(self._export_pdf)
        
        act_exit = QAction("❌ Ieșire", self)
        act_exit.setShortcut("Alt+F4")
        act_exit.triggered.connect(self.close)
        
        file_menu.addAction(act_new)
        file_menu.addSeparator()
        file_menu.addAction(act_backup)
        file_menu.addAction(act_import)
        file_menu.addAction(act_export)
        file_menu.addAction(act_export_pdf)
        file_menu.addSeparator()
        file_menu.addAction(act_exit)
        
        # Vizualizare
        view_menu = menubar.addMenu("&Vizualizare")
        
        act_registru = QAction("📋 Registru", self)
        act_registru.triggered.connect(lambda: self.tabs.setCurrentIndex(1))
        
        act_stats = QAction("📊 Statistici", self)
        act_stats.triggered.connect(lambda: self.tabs.setCurrentIndex(2))
        
        act_refresh = QAction("🔄 Reîmprospătare", self)
        act_refresh.setShortcut("F5")
        act_refresh.triggered.connect(self._refresh_all)
        
        view_menu.addAction(act_registru)
        view_menu.addAction(act_stats)
        view_menu.addSeparator()
        view_menu.addAction(act_refresh)
        
        # Instrumente
        tools_menu = menubar.addMenu("&Instrumente")
        
        act_search = QAction("🔍 Căutare Avansată", self)
        act_search.setShortcut("Ctrl+F")
        act_search.triggered.connect(self._show_advanced_search)
        
        act_audit = QAction("📜 Jurnal Audit", self)
        act_audit.triggered.connect(self._show_audit_log)
        
        act_verify = QAction("✅ Verificare Integritate", self)
        act_verify.triggered.connect(self._verify_integrity)
        
        tools_menu.addAction(act_search)
        tools_menu.addAction(act_audit)
        tools_menu.addAction(act_verify)
        
        # Ajutor
        help_menu = menubar.addMenu("&Ajutor")
        
        act_manual = QAction("📖 Manual Utilizare", self)
        act_manual.triggered.connect(self._show_manual)
        
        act_about = QAction("ℹ️ Despre", self)
        act_about.triggered.connect(self._show_about)
        
        help_menu.addAction(act_manual)
        help_menu.addAction(act_about)

    def _setup_statusbar(self):
        self.statusbar = self.statusBar()
        self.statusbar.setFont(QFont("Segoe UI", 9))
        
        self.lbl_records = QLabel()
        self.lbl_db_size = QLabel()
        self.lbl_last_backup = QLabel()
        
        self.statusbar.addWidget(self.lbl_records)
        self.statusbar.addWidget(QLabel(" | "))
        self.statusbar.addWidget(self.lbl_db_size)
        self.statusbar.addWidget(QLabel(" | "))
        self.statusbar.addWidget(self.lbl_last_backup)
        self.statusbar.addPermanentWidget(QLabel("HG 585/2002"))
        
        self._update_statusbar()

    def _start_autosave_timer(self):
        self.autosave_timer = QTimer()
        self.autosave_timer.timeout.connect(self._autosave)
        self.autosave_timer.start(300000)  # 5 minute

    def _update_statusbar(self):
        count = len(self.db.get_all_transfers())
        self.lbl_records.setText(f"Total: {count} înregistrări")
        
        db_path = Path(self.config.get("db_path", "transferuri.db"))
        if db_path.exists():
            size_mb = db_path.stat().st_size / (1024 * 1024)
            self.lbl_db_size.setText(f"BD: {size_mb:.2f} MB")
        
        # TODO: Last backup time from config
        self.lbl_last_backup.setText("Backup: N/A")

    def _on_record_saved(self, record_id: str, nr: str):
        self.statusbar.showMessage(f"✅ Salvat: {nr}", 3000)
        self._update_statusbar()
        self.table_widget.refresh_table()
        self.stats_widget.refresh_stats()

    def _on_record_selected(self, record_id: str):
        self.form_widget.load_record(record_id)
        self.tabs.setCurrentIndex(0)

    def _on_record_deleted(self, record_id: str):
        self.statusbar.showMessage(f"🗑️ Înregistrare ștearsă", 3000)
        self._update_statusbar()
        self.stats_widget.refresh_stats()

    def _on_settings_changed(self, config: dict):
        self.config = config
        self._save_config()
        self.statusbar.showMessage("⚙️ Setări salvate", 3000)

    def _save_config(self):
        config_path = Path("config.json")
        with open(config_path, "w", encoding="utf-8") as f:
            json.dump(self.config, f, indent=2, ensure_ascii=False)

    def _create_backup(self):
        from ..utils.backup import BackupManager
        bm = BackupManager(self.config)
        try:
            backup_file = bm.create_backup()
            QMessageBox.information(self, "Succes", f"Backup creat:\n{backup_file}")
            self.statusbar.showMessage(f"✅ Backup: {Path(backup_file).name}", 5000)
        except Exception as e:
            QMessageBox.critical(self, "Eroare", f"Eroare backup: {e}")

    def _import_csv(self):
        QMessageBox.information(self, "Info", "Funcționalitate în dezvoltare")

    def _export_csv(self):
        self.table_widget._export_csv()

    def _export_pdf(self):
        QMessageBox.information(self, "Info", "Funcționalitate în dezvoltare")

    def _refresh_all(self):
        self.table_widget.refresh_table()
        self.stats_widget.refresh_stats()
        self._update_statusbar()
        self.statusbar.showMessage("🔄 Reîmprospătat", 2000)

    def _show_advanced_search(self):
        QMessageBox.information(self, "Info", "Căutare avansată în dezvoltare")

    def _show_audit_log(self):
        QMessageBox.information(self, "Info", "Jurnal audit în dezvoltare")

    def _verify_integrity(self):
        records = self.db.get_all_transfers()
        issues = []
        
        for rec in records:
            if not rec.get("src_institutie"):
                issues.append(f"{rec['nr']}: Lipsă instituție sursă")
            if not rec.get("pers_nume"):
                issues.append(f"{rec['nr']}: Lipsă nume persoană")
        
        if issues:
            QMessageBox.warning(self, "Probleme găsite", "\n".join(issues[:10]))
        else:
            QMessageBox.information(self, "Succes", "Verificare OK!")

    def _show_manual(self):
        QMessageBox.information(self, "Manual", 
            "📖 Manual Utilizare\n\n"
            "1. Înregistrare: Tab 'Înregistrare Nouă'\n"
            "2. Vizualizare: Tab 'Registru Complet'\n"
            "3. Statistici: Tab 'Statistici'\n"
            "4. Setări: Tab 'Setări'\n\n"
            "Conform HG 585/2002"
        )

    def _show_about(self):
        QMessageBox.about(self, "Despre", 
            "📋 Registru Transferuri Media v1.0\n\n"
            f"Operator curent: {self.operator_name}\n\n"
            "Conformitate: HG 585/2002\n"
            "Dezvoltat: 2026"
        )

    def _logout(self):
        reply = QMessageBox.question(
            self, "Confirmare",
            "Sigur doriți să vă deconectați?",
            QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.No
        )
        
        if reply == QMessageBox.StandardButton.Yes:
            self.close()
            # App va relua cu operator dialog

    def _autosave(self):
        self._save_config()
        if self.config.get("backup_enabled", True):
            from ..utils.backup import BackupManager
            bm = BackupManager(self.config)
            bm.cleanup_old_backups()

    def closeEvent(self, event):
        self._save_config()
        event.accept()
