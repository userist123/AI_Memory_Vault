from PyQt6.QtWidgets import (QDialog, QVBoxLayout, QHBoxLayout, QLabel,
                              QLineEdit, QPushButton, QListWidget, QListWidgetItem,
                              QMessageBox, QGroupBox, QFormLayout)
from PyQt6.QtCore import Qt
from PyQt6.QtGui import QFont

class OperatorDialog(QDialog):
    def __init__(self, db):
        super().__init__()
        self.db = db
        self.selected_operator = None
        
        self.setWindowTitle("Selectare Operator")
        self.setMinimumSize(500, 600)
        self.setModal(True)
        
        self._setup_ui()
        self._load_operators()

    def _setup_ui(self):
        layout = QVBoxLayout(self)
        
        # Titlu
        lbl_title = QLabel("🗂️ REGISTRU TRANSFERURI MEDIA")
        lbl_title.setFont(QFont("Segoe UI", 16, QFont.Weight.Bold))
        lbl_title.setAlignment(Qt.AlignmentFlag.AlignCenter)
        
        lbl_subtitle = QLabel("Selectați sau adăugați operator")
        lbl_subtitle.setFont(QFont("Segoe UI", 10))
        lbl_subtitle.setAlignment(Qt.AlignmentFlag.AlignCenter)
        lbl_subtitle.setStyleSheet("color: #6B7280;")
        
        layout.addWidget(lbl_title)
        layout.addWidget(lbl_subtitle)
        layout.addSpacing(20)
        
        # Operator nou
        new_group = QGroupBox("Operator Nou")
        new_group.setFont(QFont("Segoe UI", 10, QFont.Weight.Bold))
        new_layout = QFormLayout()
        
        self.new_name = QLineEdit()
        self.new_name.setPlaceholderText("Ex: Maior Popescu Ion")
        self.new_name.returnPressed.connect(self._add_operator)
        
        self.new_rank = QLineEdit()
        self.new_rank.setPlaceholderText("Ex: Maior")
        
        self.new_unit = QLineEdit()
        self.new_unit.setPlaceholderText("Ex: Secția IT Brașov")
        
        btn_add = QPushButton("➕ Adaugă Operator")
        btn_add.clicked.connect(self._add_operator)
        
        new_layout.addRow("Nume complet *:", self.new_name)
        new_layout.addRow("Grad:", self.new_rank)
        new_layout.addRow("Unitate:", self.new_unit)
        new_layout.addRow("", btn_add)
        
        new_group.setLayout(new_layout)
        layout.addWidget(new_group)
        
        # Operatori existenți
        exist_group = QGroupBox("Operatori Existenți")
        exist_group.setFont(QFont("Segoe UI", 10, QFont.Weight.Bold))
        exist_layout = QVBoxLayout()
        
        self.search_box = QLineEdit()
        self.search_box.setPlaceholderText("🔍 Caută operator...")
        self.search_box.textChanged.connect(self._filter_operators)
        
        self.operators_list = QListWidget()
        self.operators_list.setFont(QFont("Segoe UI", 9))
        self.operators_list.itemDoubleClicked.connect(self._select_operator)
        
        exist_layout.addWidget(self.search_box)
        exist_layout.addWidget(self.operators_list)
        
        exist_group.setLayout(exist_layout)
        layout.addWidget(exist_group)
        
        # Butoane
        buttons = QHBoxLayout()
        buttons.addStretch()
        
        btn_select = QPushButton("✅ Selectare")
        btn_select.setMinimumWidth(120)
        btn_select.clicked.connect(self._select_operator)
        
        btn_cancel = QPushButton("❌ Anulare")
        btn_cancel.setMinimumWidth(120)
        btn_cancel.clicked.connect(self.reject)
        
        buttons.addWidget(btn_select)
        buttons.addWidget(btn_cancel)
        layout.addLayout(buttons)

    def _load_operators(self):
        self.operators_list.clear()
        operators = self.db.get_all_operators()
        
        for op in operators:
            item = QListWidgetItem(f"{op['name']}")
            if op.get('rank') or op.get('unit'):
                details = []
                if op.get('rank'):
                    details.append(op['rank'])
                if op.get('unit'):
                    details.append(op['unit'])
                item.setToolTip(" - ".join(details))
            
            item.setData(Qt.ItemDataRole.UserRole, op['name'])
            self.operators_list.addItem(item)

    def _filter_operators(self):
        search_text = self.search_box.text().lower()
        
        for i in range(self.operators_list.count()):
            item = self.operators_list.item(i)
            name = item.data(Qt.ItemDataRole.UserRole).lower()
            item.setHidden(search_text not in name)

    def _add_operator(self):
        name = self.new_name.text().strip()
        
        if not name:
            QMessageBox.warning(self, "Avertisment", "Introduceți numele operatorului!")
            self.new_name.setFocus()
            return
        
        rank = self.new_rank.text().strip()
        unit = self.new_unit.text().strip()
        
        try:
            self.db.add_operator(name, rank, unit)
            self._load_operators()
            
            # Selectare automată
            for i in range(self.operators_list.count()):
                item = self.operators_list.item(i)
                if item.data(Qt.ItemDataRole.UserRole) == name:
                    self.operators_list.setCurrentItem(item)
                    break
            
            QMessageBox.information(self, "Succes", f"Operator adăugat: {name}")
            
            # Clear form
            self.new_name.clear()
            self.new_rank.clear()
            self.new_unit.clear()
            
        except Exception as e:
            QMessageBox.critical(self, "Eroare", f"Eroare adăugare: {e}")

    def _select_operator(self):
        item = self.operators_list.currentItem()
        
        if not item:
            QMessageBox.warning(self, "Avertisment", "Selectați un operator!")
            return
        
        self.selected_operator = item.data(Qt.ItemDataRole.UserRole)
        self.accept()

    def get_operator(self) -> str:
        return self.selected_operator
