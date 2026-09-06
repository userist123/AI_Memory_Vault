from PyQt6.QtWidgets import (QWidget, QVBoxLayout, QHBoxLayout, QTableWidget,
                              QTableWidgetItem, QPushButton, QLineEdit, QComboBox,
                              QLabel, QMenu, QMessageBox, QHeaderView)
from PyQt6.QtCore import Qt, pyqtSignal
from PyQt6.QtGui import QFont, QColor, QBrush
from datetime import datetime

class TransferTableWidget(QWidget):
    record_selected = pyqtSignal(str)
    record_deleted = pyqtSignal(str)

    def __init__(self, db):
        super().__init__()
        self.db = db
        self.current_filters = {}
        self._setup_ui()
        self.refresh_table()

    def _setup_ui(self):
        layout = QVBoxLayout(self)
        
        # Filtre
        filter_layout = QHBoxLayout()
        
        self.filter_search = QLineEdit()
        self.filter_search.setPlaceholderText("Caută (nume, instituție, nr...)");
        self.filter_search.textChanged.connect(self._apply_filters)
        
        self.filter_status = QComboBox()
        self.filter_status.addItems(["Toate", "Activ", "Șterse"])
        self.filter_status.currentTextChanged.connect(self._apply_filters)
        
        self.filter_clasificare = QComboBox()
        self.filter_clasificare.addItems(["Toate", "Nesecret", "Secret de Serviciu", "Secret", "Strict Secret"])
        self.filter_clasificare.currentTextChanged.connect(self._apply_filters)
        
        btn_refresh = QPushButton("🔄")
        btn_refresh.setMaximumWidth(40)
        btn_refresh.clicked.connect(self.refresh_table)
        
        btn_export = QPushButton("📊 Export CSV")
        btn_export.clicked.connect(self._export_csv)
        
        filter_layout.addWidget(QLabel("Căutare:"))
        filter_layout.addWidget(self.filter_search, 2)
        filter_layout.addWidget(QLabel("Status:"))
        filter_layout.addWidget(self.filter_status)
        filter_layout.addWidget(QLabel("Clasificare:"))
        filter_layout.addWidget(self.filter_clasificare)
        filter_layout.addWidget(btn_refresh)
        filter_layout.addWidget(btn_export)
        
        layout.addLayout(filter_layout)
        
        # Tabel
        self.table = QTableWidget()
        self.table.setColumnCount(13)
        self.table.setHorizontalHeaderLabels([
            "Nr. Reg", "Data/Ora", "Operator", "Instituție Sursă", "PC Sursă",
            "Persoană", "Autorizație", "Mediu Transfer", "Instituție Dest",
            "Clasificare", "Hash", "Dimensiune (GB)", "Status"
        ])
        
        header = self.table.horizontalHeader()
        header.setSectionResizeMode(QHeaderView.ResizeMode.Interactive)
        header.setStretchLastSection(False)
        header.setSectionResizeMode(0, QHeaderView.ResizeMode.ResizeToContents)
        header.setSectionResizeMode(1, QHeaderView.ResizeMode.ResizeToContents)
        
        self.table.setSelectionBehavior(QTableWidget.SelectionBehavior.SelectRows)
        self.table.setSelectionMode(QTableWidget.SelectionMode.SingleSelection)
        self.table.setAlternatingRowColors(True)
        self.table.setContextMenuPolicy(Qt.ContextMenuPolicy.CustomContextMenu)
        self.table.customContextMenuRequested.connect(self._show_context_menu)
        self.table.cellDoubleClicked.connect(self._on_row_double_click)
        
        layout.addWidget(self.table)
        
        # Footer stats
        self.lbl_stats = QLabel()
        self.lbl_stats.setFont(QFont("Segoe UI", 9))
        layout.addWidget(self.lbl_stats)

    def refresh_table(self):
        self.current_filters = {}
        records = self.db.get_all_transfers()
        self._populate_table(records)

    def _apply_filters(self):
        search = self.filter_search.text().strip()
        status = self.filter_status.currentText()
        clasif = self.filter_clasificare.currentText()
        
        self.current_filters = {}
        if search:
            self.current_filters["search"] = search
        if status != "Toate":
            self.current_filters["status"] = "active" if status == "Activ" else "deleted"
        if clasif != "Toate":
            self.current_filters["clasificare"] = clasif
        
        records = self.db.filter_transfers(self.current_filters)
        self._populate_table(records)

    def _populate_table(self, records: list):
        self.table.setRowCount(0)
        self.table.setSortingEnabled(False)
        
        for row, rec in enumerate(records):
            self.table.insertRow(row)
            
            # Nr. Reg
            item = QTableWidgetItem(rec["nr"])
            item.setData(Qt.ItemDataRole.UserRole, rec["id"])
            item.setFont(QFont("Segoe UI", 9, QFont.Weight.Bold))
            self.table.setItem(row, 0, item)
            
            # Data
            dt = datetime.fromisoformat(rec["created_at"])
            self.table.setItem(row, 1, QTableWidgetItem(dt.strftime("%Y-%m-%d %H:%M")))
            
            # Operator
            self.table.setItem(row, 2, QTableWidgetItem(rec["operator"]))
            
            # Instituții
            self.table.setItem(row, 3, QTableWidgetItem(rec["src_institutie"]))
            self.table.setItem(row, 4, QTableWidgetItem(rec["src_pc_nume"]))
            
            # Persoană
            self.table.setItem(row, 5, QTableWidgetItem(rec["pers_nume"]))
            self.table.setItem(row, 6, QTableWidgetItem(rec["pers_autorizatie"]))
            
            # Transfer
            self.table.setItem(row, 7, QTableWidgetItem(rec["transfer_medium"]))
            self.table.setItem(row, 8, QTableWidgetItem(rec.get("dst_institutie", "")))
            
            # Clasificare
            clasif_item = QTableWidgetItem(rec["clasificare"])
            clasif_item.setBackground(self._get_clasificare_color(rec["clasificare"]))
            self.table.setItem(row, 9, clasif_item)
            
            # Hash (primele 16 caractere)
            hash_val = rec.get("arhiva_hash", "")
            self.table.setItem(row, 10, QTableWidgetItem(hash_val[:16] + "..." if hash_val else ""))
            
            # Dimensiune
            dim = rec.get("arhiva_dim_gb")
            dim_str = f"{dim:.2f}" if dim else ""
            self.table.setItem(row, 11, QTableWidgetItem(dim_str))
            
            # Status
            status_item = QTableWidgetItem("🟢 Activ" if rec["status"] == "active" else "🔴 Șters")
            if rec["status"] == "deleted":
                for col in range(self.table.columnCount()):
                    it = self.table.item(row, col)
                    if it:
                        it.setForeground(QBrush(QColor(150, 150, 150)))
            self.table.setItem(row, 12, status_item)
        
        self.table.setSortingEnabled(True)
        self.table.resizeColumnsToContents()
        
        # Stats
        total = len(records)
        active = sum(1 for r in records if r["status"] == "active")
        deleted = total - active
        total_gb = sum(r.get("arhiva_dim_gb", 0) or 0 for r in records)
        
        self.lbl_stats.setText(
            f"Total: {total} înregistrări  |  Active: {active}  |  Șterse: {deleted}  |  "
            f"Volum total: {total_gb:.2f} GB"
        )

    def _get_clasificare_color(self, clasificare: str) -> QBrush:
        colors = {
            "Nesecret": QColor(230, 240, 230),
            "Secret de Serviciu": QColor(255, 250, 205),
            "Secret": QColor(255, 230, 200),
            "Strict Secret": QColor(255, 200, 200)
        }
        return QBrush(colors.get(clasificare, QColor(255, 255, 255)))

    def _show_context_menu(self, pos):
        row = self.table.rowAt(pos.y())
        if row < 0:
            return
        
        item = self.table.item(row, 0)
        record_id = item.data(Qt.ItemDataRole.UserRole)
        rec = self.db.get_transfer_by_id(record_id)
        
        menu = QMenu(self)
        
        act_view = menu.addAction("👁️ Vizualizare")
        act_edit = menu.addAction("✏️ Editare")
        menu.addSeparator()
        act_duplicate = menu.addAction("📋 Duplicare")
        act_export = menu.addAction("📄 Export PDF")
        menu.addSeparator()
        
        if rec["status"] == "active":
            act_delete = menu.addAction("🗑️ Ștergere")
        else:
            act_restore = menu.addAction("♻️ Restaurare")
        
        action = menu.exec(self.table.viewport().mapToGlobal(pos))
        
        if action == act_view or action == act_edit:
            self.record_selected.emit(record_id)
        elif action == act_duplicate:
            self._duplicate_record(record_id)
        elif action == act_export:
            self._export_pdf_single(record_id)
        elif rec["status"] == "active" and action == act_delete:
            self._delete_record(record_id)
        elif rec["status"] == "deleted" and action == act_restore:
            self._restore_record(record_id)

    def _on_row_double_click(self, row, col):
        item = self.table.item(row, 0)
        if item:
            record_id = item.data(Qt.ItemDataRole.UserRole)
            self.record_selected.emit(record_id)

    def _delete_record(self, record_id: str):
        reply = QMessageBox.question(
            self, "Confirmare", 
            "Sigur doriți să ștergeți această înregistrare?",
            QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.No
        )
        
        if reply == QMessageBox.StandardButton.Yes:
            operator = "System"  # Sau ia-l din main window
            self.db.soft_delete_transfer(record_id, operator)
            self.record_deleted.emit(record_id)
            self.refresh_table()
            QMessageBox.information(self, "Succes", "Înregistrare ștearsă.")

    def _restore_record(self, record_id: str):
        self.db.restore_transfer(record_id)
        self.refresh_table()
        QMessageBox.information(self, "Succes", "Înregistrare restaurată.")

    def _duplicate_record(self, record_id: str):
        rec = self.db.get_transfer_by_id(record_id)
        if not rec:
            return
        
        data = dict(rec)
        del data["id"]
        del data["nr"]
        del data["created_at"]
        del data["updated_at"]
        
        new_id = self.db.insert_transfer(data)
        self.refresh_table()
        QMessageBox.information(self, "Succes", f"Înregistrare duplicată: {new_id}")

    def _export_csv(self):
        from PyQt6.QtWidgets import QFileDialog
        import csv
        
        file, _ = QFileDialog.getSaveFileName(
            self, "Export CSV", "", "CSV Files (*.csv)"
        )
        
        if not file:
            return
        
        records = self.db.filter_transfers(self.current_filters)
        
        try:
            with open(file, "w", newline="", encoding="utf-8-sig") as f:
                if not records:
                    f.write("Nu există date pentru export\n")
                    return
                
                writer = csv.DictWriter(f, fieldnames=records[0].keys())
                writer.writeheader()
                writer.writerows(records)
            
            QMessageBox.information(self, "Succes", f"Export complet: {len(records)} înregistrări")
        except Exception as e:
            QMessageBox.critical(self, "Eroare", f"Eroare export: {e}")

    def _export_pdf_single(self, record_id: str):
        QMessageBox.information(self, "Info", "Funcționalitate PDF în dezvoltare")
