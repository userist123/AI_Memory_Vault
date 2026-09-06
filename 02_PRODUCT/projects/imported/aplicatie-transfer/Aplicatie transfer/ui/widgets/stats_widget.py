"""
Stats Widget - Versiune CORECTATĂ pentru PyQt6
Fix: RenderHint -> renderHints() method
"""
from PyQt6.QtWidgets import (QWidget, QVBoxLayout, QHBoxLayout, QGroupBox,
                             QLabel, QTableWidget, QTableWidgetItem, QComboBox,
                             QPushButton, QFrame, QScrollArea, QHeaderView)
from PyQt6.QtCore import Qt, QTimer, pyqtSignal
from PyQt6.QtGui import QFont, QPainter
from PyQt6.QtCharts import QChart, QChartView, QPieSeries
from datetime import datetime, timedelta

class StatsWidget(QWidget):
    """Widget pentru afișarea statisticilor și graficelor"""
    
    refresh_requested = pyqtSignal()
    
    def __init__(self, db_manager, parent=None):
        super().__init__(parent)
        self.db = db_manager
        self._setup_ui()
        self.load_stats()
        
        # Auto-refresh la 30 secunde
        self.refresh_timer = QTimer()
        self.refresh_timer.timeout.connect(self.load_stats)
        self.refresh_timer.start(30000)
    
    def _setup_ui(self):
        layout = QVBoxLayout(self)
        layout.setSpacing(16)
        
        # Header
        header = QLabel("📊 Statistici și Rapoarte")
        header.setFont(QFont("Segoe UI", 14, QFont.Weight.Bold))
        layout.addWidget(header)
        
        # Toolbar
        toolbar = self._create_toolbar()
        layout.addWidget(toolbar)
        
        # Scroll area
        scroll = QScrollArea()
        scroll.setWidgetResizable(True)
        scroll.setFrameShape(QFrame.Shape.NoFrame)
        
        content = QWidget()
        content_layout = QVBoxLayout(content)
        content_layout.setSpacing(16)
        
        # KPI Cards
        self.kpi_layout = QHBoxLayout()
        content_layout.addLayout(self.kpi_layout)
        
        content_layout.addWidget(self._create_separator())
        
        # Charts
        charts_layout = QHBoxLayout()
        
        # Chart Clasificare
        clasificare_group = QGroupBox("📋 Distribuție Clasificare")
        clasificare_layout = QVBoxLayout()
        self.clasificare_chart = QChart()
        self.clasificare_chart.setTitle("")
        self.clasificare_chart.setAnimationOptions(QChart.AnimationOption.SeriesAnimations)
        self.clasificare_chart_view = QChartView(self.clasificare_chart)
        # FIX: Folosește renderHints() în loc de RenderHint
        self.clasificare_chart_view.setRenderHints(QPainter.RenderHint.Antialiasing)
        clasificare_layout.addWidget(self.clasificare_chart_view)
        clasificare_group.setLayout(clasificare_layout)
        charts_layout.addWidget(clasificare_group)
        
        # Chart Medii
        medii_group = QGroupBox("💾 Tipuri Medii Transfer")
        medii_layout = QVBoxLayout()
        self.medii_chart = QChart()
        self.medii_chart.setTitle("")
        self.medii_chart.setAnimationOptions(QChart.AnimationOption.SeriesAnimations)
        self.medii_chart_view = QChartView(self.medii_chart)
        # FIX: Folosește renderHints() în loc de RenderHint
        self.medii_chart_view.setRenderHints(QPainter.RenderHint.Antialiasing)
        medii_layout.addWidget(self.medii_chart_view)
        medii_group.setLayout(medii_layout)
        charts_layout.addWidget(medii_group)
        
        content_layout.addLayout(charts_layout)
        
        content_layout.addWidget(self._create_separator())
        
        # Top Lists
        top_layout = QHBoxLayout()
        
        # Top Instituții
        inst_group = QGroupBox("🏢 Top 5 Instituții Destinație")
        inst_layout = QVBoxLayout()
        self.top_institutions = self._create_simple_table()
        inst_layout.addWidget(self.top_institutions)
        inst_group.setLayout(inst_layout)
        top_layout.addWidget(inst_group)
        
        # Top Primitori
        prim_group = QGroupBox("👤 Top 5 Primitori")
        prim_layout = QVBoxLayout()
        self.top_receivers = self._create_simple_table()
        prim_layout.addWidget(self.top_receivers)
        prim_group.setLayout(prim_layout)
        top_layout.addWidget(prim_group)
        
        content_layout.addLayout(top_layout)
        
        content_layout.addStretch()
        scroll.setWidget(content)
        layout.addWidget(scroll, 1)
    
    def _create_toolbar(self):
        toolbar = QFrame()
        toolbar.setFrameShape(QFrame.Shape.StyledPanel)
        layout = QHBoxLayout(toolbar)
        
        layout.addWidget(QLabel("Perioadă:"))
        
        self.period_combo = QComboBox()
        self.period_combo.addItems([
            "Astăzi", "Ultimele 7 zile", "Luna curentă",
            "Anul curent", "Toate"
        ])
        self.period_combo.setCurrentIndex(2)
        self.period_combo.currentIndexChanged.connect(self.load_stats)
        layout.addWidget(self.period_combo)
        
        layout.addStretch()
        
        refresh_btn = QPushButton("🔄 Reîmprospătare")
        refresh_btn.clicked.connect(self.load_stats)
        layout.addWidget(refresh_btn)
        
        return toolbar
    
    def _create_separator(self):
        line = QFrame()
        line.setFrameShape(QFrame.Shape.HLine)
        line.setFrameShadow(QFrame.Shadow.Sunken)
        return line
    
    def _create_simple_table(self):
        table = QTableWidget()
        table.setColumnCount(2)
        table.setHorizontalHeaderLabels(["Categorie", "Număr"])
        table.horizontalHeader().setStretchLastSection(True)
        table.horizontalHeader().setSectionResizeMode(0, QHeaderView.ResizeMode.Stretch)
        table.setEditTriggers(QTableWidget.EditTrigger.NoEditTriggers)
        table.setSelectionMode(QTableWidget.SelectionMode.NoSelection)
        table.setMaximumHeight(200)
        return table
    
    def _get_date_filter(self):
        period = self.period_combo.currentText()
        now = datetime.now()
        
        if period == "Astăzi":
            return now.replace(hour=0, minute=0, second=0)
        elif period == "Ultimele 7 zile":
            return now - timedelta(days=7)
        elif period == "Luna curentă":
            return now.replace(day=1, hour=0, minute=0, second=0)
        elif period == "Anul curent":
            return now.replace(month=1, day=1, hour=0, minute=0, second=0)
        return None
    
    def load_stats(self):
        date_filter = self._get_date_filter()
        self._update_kpis(date_filter)
        self._update_classification_chart(date_filter)
        self._update_media_chart(date_filter)
        self._update_top_institutions(date_filter)
        self._update_top_receivers(date_filter)
    
    def _update_kpis(self, date_filter):
        while self.kpi_layout.count():
            item = self.kpi_layout.takeAt(0)
            if item.widget():
                item.widget().deleteLater()
        
        query = "SELECT COUNT(*), SUM(capacitate_sursa) FROM transferuri WHERE deleted=0"
        params = []
        if date_filter:
            query += " AND data_transfer >= ?"
            params.append(date_filter.isoformat())
        
        result = self.db.conn.execute(query, params).fetchone()
        total_count = result[0] or 0
        total_volume = (result[1] or 0) / 1024
        
        today = datetime.now().replace(hour=0, minute=0, second=0)
        today_count = self.db.conn.execute(
            "SELECT COUNT(*) FROM transferuri WHERE deleted=0 AND data_transfer >= ?",
            (today.isoformat(),)
        ).fetchone()[0]
        
        active_ops = self.db.conn.execute(
            "SELECT COUNT(DISTINCT operator_id) FROM transferuri WHERE deleted=0"
        ).fetchone()[0]
        
        kpis = [
            ("📊 Total Transferuri", str(total_count), "#3b82f6"),
            ("📅 Astăzi", str(today_count), "#10b981"),
            ("💾 Volum Total", f"{total_volume:.1f} GB", "#8b5cf6"),
            ("👥 Operatori", str(active_ops), "#f59e0b")
        ]
        
        for title, value, color in kpis:
            card = self._create_kpi_card(title, value, color)
            self.kpi_layout.addWidget(card)
    
    def _create_kpi_card(self, title, value, color):
        card = QFrame()
        card.setFrameShape(QFrame.Shape.StyledPanel)
        card.setStyleSheet(f"""
            QFrame {{
                background: white;
                border: 2px solid {color};
                border-radius: 8px;
                padding: 12px;
            }}
        """)
        
        layout = QVBoxLayout(card)
        layout.setSpacing(4)
        
        title_label = QLabel(title)
        title_label.setFont(QFont("Segoe UI", 9))
        title_label.setStyleSheet("color: #6b7280;")
        layout.addWidget(title_label)
        
        value_label = QLabel(value)
        value_label.setFont(QFont("Segoe UI", 20, QFont.Weight.Bold))
        value_label.setStyleSheet(f"color: {color};")
        layout.addWidget(value_label)
        
        return card
    
    def _update_classification_chart(self, date_filter):
        query = "SELECT clasificare, COUNT(*) FROM transferuri WHERE deleted=0"
        params = []
        if date_filter:
            query += " AND data_transfer >= ?"
            params.append(date_filter.isoformat())
        query += " GROUP BY clasificare"
        
        results = self.db.conn.execute(query, params).fetchall()
        
        self.clasificare_chart.removeAllSeries()
        series = QPieSeries()
        
        for name, count in results:
            series.append(name or "Nedefinit", count)
        
        self.clasificare_chart.addSeries(series)
        self.clasificare_chart.legend().setVisible(True)
        self.clasificare_chart.legend().setAlignment(Qt.AlignmentFlag.AlignBottom)
    
    def _update_media_chart(self, date_filter):
        query = "SELECT tip_mediu_transfer, COUNT(*) FROM transferuri WHERE deleted=0"
        params = []
        if date_filter:
            query += " AND data_transfer >= ?"
            params.append(date_filter.isoformat())
        query += " GROUP BY tip_mediu_transfer LIMIT 5"
        
        results = self.db.conn.execute(query, params).fetchall()
        
        self.medii_chart.removeAllSeries()
        series = QPieSeries()
        
        for name, count in results:
            series.append(name or "Nedefinit", count)
        
        self.medii_chart.addSeries(series)
        self.medii_chart.legend().setVisible(True)
        self.medii_chart.legend().setAlignment(Qt.AlignmentFlag.AlignBottom)
    
    def _update_top_institutions(self, date_filter):
        query = """
            SELECT institutie_dest, COUNT(*) as cnt
            FROM transferuri
            WHERE deleted=0 AND institutie_dest IS NOT NULL AND institutie_dest != ''
        """
        params = []
        if date_filter:
            query += " AND data_transfer >= ?"
            params.append(date_filter.isoformat())
        query += " GROUP BY institutie_dest ORDER BY cnt DESC LIMIT 5"
        
        results = self.db.conn.execute(query, params).fetchall()
        
        self.top_institutions.setRowCount(len(results))
        for i, (name, count) in enumerate(results):
            self.top_institutions.setItem(i, 0, QTableWidgetItem(name))
            self.top_institutions.setItem(i, 1, QTableWidgetItem(str(count)))
    
    def _update_top_receivers(self, date_filter):
        query = """
            SELECT nume_persoana, COUNT(*) as cnt
            FROM transferuri
            WHERE deleted=0 AND nume_persoana IS NOT NULL AND nume_persoana != ''
        """
        params = []
        if date_filter:
            query += " AND data_transfer >= ?"
            params.append(date_filter.isoformat())
        query += " GROUP BY nume_persoana ORDER BY cnt DESC LIMIT 5"
        
        results = self.db.conn.execute(query, params).fetchall()
        
        self.top_receivers.setRowCount(len(results))
        for i, (name, count) in enumerate(results):
            self.top_receivers.setItem(i, 0, QTableWidgetItem(name))
            self.top_receivers.setItem(i, 1, QTableWidgetItem(str(count)))
