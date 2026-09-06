"""
Trading Bot — Ghid Practic Panel
Tab cu 3 sub-tab-uri:
1. Analiza Educativa (on-demand, per activ)
2. Bune Practici (permanent)
3. Biblioteca Strategii (12 strategii editabile)
"""
from PyQt6.QtWidgets import (QWidget, QVBoxLayout, QHBoxLayout, QLabel,
                              QTextEdit, QPushButton, QTabWidget, QScrollArea,
                              QFrame, QComboBox, QProgressBar, QListWidget,
                              QListWidgetItem, QSplitter, QCheckBox)
from PyQt6.QtCore import Qt, QThread, pyqtSignal
from ai.ghid_practic import (generate_ghid_entries, get_bune_practici,
                              GhidEntry, explica_miscare, explica_oportunitate,
                              explica_pattern, lectia_zilei)
from strategies.library import get_strategy_library, format_strategy_text
from data.fetcher import DataFetcher
from ai.advisor import AIAdvisor


PANEL_STYLE = """
    QTextEdit {
        background: #0a0e17; color: #c9d1d9; border: 1px solid #1e293b;
        border-radius: 6px; font-family: "Consolas", "Courier New", monospace;
        font-size: 12px; padding: 10px; line-height: 1.5;
    }
    QListWidget {
        background: #0f172a; color: #c9d1d9; border: 1px solid #1e293b;
        border-radius: 6px; font-size: 12px; padding: 4px;
    }
    QListWidget::item { padding: 8px 12px; border-radius: 4px; }
    QListWidget::item:selected { background: #1e3a5f; color: #38bdf8; }
    QListWidget::item:hover { background: #1e293b; }
    QPushButton {
        background: #1e40af; color: white; border: none; border-radius: 6px;
        padding: 10px 20px; font-weight: bold; font-size: 13px;
    }
    QPushButton:hover { background: #2563eb; }
    QPushButton:disabled { background: #1e293b; color: #64748b; }
"""


class GhidGeneratorWorker(QThread):
    """Background thread: analizeaza toate activele din watchlist + World Monitor context."""
    progress = pyqtSignal(int, int, str)  # current, total, name
    finished = pyqtSignal(dict, object)  # {symbol: AdviceReport}, WorldContext
    error = pyqtSignal(str)

    def __init__(self, watchlist: list, timeframe: str = "1D", wm_api_key: str = ""):
        super().__init__()
        self.watchlist = watchlist
        self.timeframe = timeframe
        self.wm_api_key = wm_api_key
        self.fetcher = DataFetcher()
        self.advisor = AIAdvisor()

    def run(self):
        # 1. Preia context global din World Monitor + RSS
        self.progress.emit(0, len(self.watchlist) + 1, "World Monitor & Stiri globale...")
        from data.world_monitor import WorldMonitorFetcher
        wm = WorldMonitorFetcher(self.wm_api_key)
        try:
            world_ctx = wm.fetch_full_context()
        except Exception as e:
            world_ctx = None

        # 2. Analizeaza fiecare activ
        results = {}
        total = len(self.watchlist)
        for i, symbol in enumerate(self.watchlist):
            self.progress.emit(i + 1, total, symbol)
            try:
                df = self.fetcher.fetch(symbol, self.timeframe)
                report = self.advisor.analyze(df, symbol, self.timeframe)
                results[symbol] = report
            except Exception:
                pass
        self.finished.emit(results, world_ctx)


class GhidPracticPanel(QWidget):
    def __init__(self, watchlist: list = None, parent=None):
        super().__init__(parent)
        self.watchlist = watchlist or [
            "BTC", "ETH", "SOL", "AAPL", "NVDA", "TSLA",
            "EURUSD", "GOLD", "SP500"
        ]
        self.ghid_entries = []
        self.world_ctx = None
        self.wm_api_key = ""
        self.worker = None
        self.setStyleSheet(PANEL_STYLE)
        self._setup_ui()

    def _setup_ui(self):
        layout = QVBoxLayout(self)
        layout.setContentsMargins(0, 0, 0, 0)

        tabs = QTabWidget()
        tabs.setStyleSheet("""
            QTabWidget::pane { border: 1px solid #1e293b; background: #0a0e17; }
            QTabBar::tab {
                background: #0f172a; color: #64748b; padding: 10px 24px;
                border: 1px solid transparent; border-bottom: none;
                border-radius: 4px 4px 0 0; font-weight: bold; font-size: 13px;
            }
            QTabBar::tab:selected { background: #0a0e17; color: #38bdf8; border-color: #1e293b; }
        """)

        # ── Tab 1: Analiza Educativa On-Demand ──────────────────
        edu_tab = QWidget()
        edu_layout = QVBoxLayout(edu_tab)
        edu_layout.setContentsMargins(12, 12, 12, 12)
        edu_layout.setSpacing(8)

        # Header + button
        header_layout = QHBoxLayout()
        edu_header = QLabel("GHID PRACTIC — ANALIZA EDUCATIVA")
        edu_header.setStyleSheet("color: #38bdf8; font-size: 16px; font-weight: bold; letter-spacing: 2px;")
        header_layout.addWidget(edu_header)
        header_layout.addStretch()

        self.generate_btn = QPushButton("GENEREAZA GHID")
        self.generate_btn.clicked.connect(self._generate_ghid)
        header_layout.addWidget(self.generate_btn)
        edu_layout.addLayout(header_layout)

        # WM API Key (optional)
        wm_layout = QHBoxLayout()
        wm_lbl = QLabel("World Monitor API Key (optional):")
        wm_lbl.setStyleSheet("color: #64748b; font-size: 10px;")
        wm_layout.addWidget(wm_lbl)
        from PyQt6.QtWidgets import QLineEdit
        self.wm_key_input = QLineEdit()
        self.wm_key_input.setPlaceholderText("wm_live_xxx (lasa gol pt. RSS fallback)")
        self.wm_key_input.setMaximumWidth(300)
        self.wm_key_input.setEchoMode(QLineEdit.EchoMode.Password)
        self.wm_key_input.setStyleSheet("""
            QLineEdit { background: #111827; border: 1px solid #1e293b; border-radius: 4px;
                        padding: 4px 8px; color: #e2e8f0; font-size: 11px; }
        """)
        wm_layout.addWidget(self.wm_key_input)
        wm_layout.addStretch()
        edu_layout.addLayout(wm_layout)

        # Progress
        self.progress_label = QLabel("")
        self.progress_label.setStyleSheet("color: #94a3b8; font-size: 11px;")
        edu_layout.addWidget(self.progress_label)
        self.progress_bar = QProgressBar()
        self.progress_bar.setFixedHeight(8)
        self.progress_bar.setStyleSheet("""
            QProgressBar { background: #111827; border-radius: 4px; }
            QProgressBar::chunk { background: #38bdf8; border-radius: 4px; }
        """)
        self.progress_bar.setVisible(False)
        edu_layout.addWidget(self.progress_bar)

        # Splitter: list + detail
        splitter = QSplitter(Qt.Orientation.Horizontal)

        self.asset_list = QListWidget()
        self.asset_list.setMaximumWidth(250)
        self.asset_list.currentRowChanged.connect(self._on_asset_selected)
        splitter.addWidget(self.asset_list)

        self.edu_detail = QTextEdit()
        self.edu_detail.setReadOnly(True)
        self.edu_detail.setPlaceholderText(
            "Apasa 'GENEREAZA GHID' pentru a analiza toate activele din watchlist.\n\n"
            "Se vor genera explicatii educationale pentru fiecare activ:\n"
            "• DE CE s-a miscat azi\n"
            "• CE oportunitate exista\n"
            "• CE pattern grafic e detectat\n"
            "• LECTIA ZILEI practica"
        )
        splitter.addWidget(self.edu_detail)
        splitter.setSizes([250, 700])
        edu_layout.addWidget(splitter)

        tabs.addTab(edu_tab, "Analiza Educativa")

        # ── Tab 2: Bune Practici (permanent) ────────────────────
        bp_tab = QWidget()
        bp_layout = QVBoxLayout(bp_tab)
        bp_layout.setContentsMargins(12, 12, 12, 12)

        bp_header = QLabel("GHID DE BUNE PRACTICI — REFERINTA PERMANENTA")
        bp_header.setStyleSheet("color: #38bdf8; font-size: 16px; font-weight: bold; letter-spacing: 2px;")
        bp_layout.addWidget(bp_header)

        bp_splitter = QSplitter(Qt.Orientation.Horizontal)

        self.bp_list = QListWidget()
        self.bp_list.setMaximumWidth(300)
        practici = get_bune_practici()
        for p in practici:
            self.bp_list.addItem(p["titlu"])
        self.bp_list.currentRowChanged.connect(self._on_bp_selected)
        bp_splitter.addWidget(self.bp_list)

        self.bp_detail = QTextEdit()
        self.bp_detail.setReadOnly(True)
        bp_splitter.addWidget(self.bp_detail)
        bp_splitter.setSizes([300, 700])
        bp_layout.addWidget(bp_splitter)

        # Auto-select first
        if self.bp_list.count() > 0:
            self.bp_list.setCurrentRow(0)

        tabs.addTab(bp_tab, "Bune Practici")

        # ── Tab 3: Biblioteca Strategii ─────────────────────────
        strat_tab = QWidget()
        strat_layout = QVBoxLayout(strat_tab)
        strat_layout.setContentsMargins(12, 12, 12, 12)

        strat_header = QLabel("BIBLIOTECA DE STRATEGII — 12 STRATEGII CLASICE")
        strat_header.setStyleSheet("color: #38bdf8; font-size: 16px; font-weight: bold; letter-spacing: 2px;")
        strat_layout.addWidget(strat_header)

        strat_splitter = QSplitter(Qt.Orientation.Horizontal)

        self.strat_list = QListWidget()
        self.strat_list.setMaximumWidth(300)
        for s in get_strategy_library():
            item = QListWidgetItem(f"[{s.category.upper()}] {s.name}")
            self.strat_list.addItem(item)
        self.strat_list.currentRowChanged.connect(self._on_strat_selected)
        strat_splitter.addWidget(self.strat_list)

        self.strat_detail = QTextEdit()
        self.strat_detail.setReadOnly(True)
        strat_splitter.addWidget(self.strat_detail)
        strat_splitter.setSizes([300, 700])
        strat_layout.addWidget(strat_splitter)

        if self.strat_list.count() > 0:
            self.strat_list.setCurrentRow(0)

        tabs.addTab(strat_tab, "Biblioteca Strategii")

        layout.addWidget(tabs)

    # ── Analiza Educativa ─────────────────────────────────────────

    def _generate_ghid(self):
        self.generate_btn.setEnabled(False)
        self.generate_btn.setText("Se genereaza...")
        self.progress_bar.setVisible(True)
        self.progress_bar.setValue(0)
        self.asset_list.clear()
        self.ghid_entries = []

        wm_key = self.wm_key_input.text().strip()
        self.worker = GhidGeneratorWorker(self.watchlist, "1D", wm_key)
        self.worker.progress.connect(self._on_progress)
        self.worker.finished.connect(self._on_ghid_done)
        self.worker.start()

    def _on_progress(self, current, total, name):
        self.progress_bar.setMaximum(total)
        self.progress_bar.setValue(current)
        self.progress_label.setText(f"Se analizeaza: {name} ({current}/{total})")

    def _on_ghid_done(self, results, world_ctx):
        self.generate_btn.setEnabled(True)
        self.generate_btn.setText("GENEREAZA GHID")
        self.progress_bar.setVisible(False)
        self.world_ctx = world_ctx

        src_count = 0
        if world_ctx:
            src_count = (len(world_ctx.top_finance_news) +
                         len(world_ctx.top_geopolitical_news) +
                         len(world_ctx.top_crypto_news))
        self.progress_label.setText(
            f"Ghid generat: {len(results)} active | {src_count} stiri globale | "
            f"Sentiment: {world_ctx.overall_sentiment if world_ctx else 'N/A'}"
        )

        self.ghid_entries = generate_ghid_entries(results)

        self.asset_list.clear()

        # First item: World Context overview
        if world_ctx:
            from data.world_monitor import format_context_for_ghid
            ctx_item = QListWidgetItem(f"🌍 CONTEXT GLOBAL — {world_ctx.overall_sentiment}")
            ctx_item.setData(Qt.ItemDataRole.UserRole, "WORLD_CONTEXT")
            self.asset_list.addItem(ctx_item)

        for e in self.ghid_entries:
            colors = {"BUY": "🟢", "SELL": "🔴", "HOLD": "🟡"}
            icon = colors.get(e.semnal, "⚪")
            item = QListWidgetItem(f"{icon} {e.name} — {e.semnal} ({e.score:.0f})")
            self.asset_list.addItem(item)

        if self.asset_list.count() > 0:
            self.asset_list.setCurrentRow(0)

    def _on_asset_selected(self, row):
        if row < 0:
            return

        # Check if it's the world context overview
        item = self.asset_list.item(row)
        if item and item.data(Qt.ItemDataRole.UserRole) == "WORLD_CONTEXT":
            self._show_world_context()
            return

        # Offset by 1 if world context is first item
        idx = row - (1 if self.world_ctx else 0)
        if idx < 0 or idx >= len(self.ghid_entries):
            return
        e = self.ghid_entries[idx]

        colors = {"BUY": "#00c853", "SELL": "#ff1744", "HOLD": "#ff9800"}
        sig_color = colors.get(e.semnal, "#999")

        # Get world impact for this asset
        impact_html = ""
        if self.world_ctx:
            from data.world_monitor import get_impact_on_asset
            impact = get_impact_on_asset(self.world_ctx, e.name)
            impact_html = f"""
            <h3 style="color: #e879f9; margin-top: 16px;">🌍 IMPACT GLOBAL PE {e.name.upper()}</h3>
            <pre style="color: #d1d5db; white-space: pre-wrap;">{impact}</pre>
            """

        html = f"""
        <div style="font-family: Consolas, monospace; color: #c9d1d9; line-height: 1.6;">
        <h2 style="color: #38bdf8; border-bottom: 2px solid #1e3a5f; padding-bottom: 8px;">
            {e.name} — <span style="color: {sig_color};">{e.semnal}</span>
            <span style="color: #94a3b8; font-size: 14px;">
                Scor: {e.score:.0f}/100 | Pret: {e.price:.6g}
            </span>
        </h2>

        {impact_html}

        <h3 style="color: #60a5fa; margin-top: 16px;">DE CE S-A MISCAT ASTAZI</h3>
        <pre style="color: #d1d5db; white-space: pre-wrap;">{e.de_ce_s_a_miscat}</pre>

        <h3 style="color: {sig_color}; margin-top: 16px;">OPORTUNITATE DE TRADING</h3>
        <pre style="color: #d1d5db; white-space: pre-wrap;">{e.oportunitate}</pre>

        <h3 style="color: #a78bfa; margin-top: 16px;">PATTERN GRAFIC DETECTAT</h3>
        <pre style="color: #d1d5db; white-space: pre-wrap;">{e.pattern_detectat}</pre>

        <h3 style="color: #fbbf24; margin-top: 16px;">LECTIA ZILEI</h3>
        <pre style="color: #d1d5db; white-space: pre-wrap;">{e.lectia_zilei}</pre>
        """

        if e.warning:
            html += f"""
            <h3 style="color: #f97316; margin-top: 16px;">AVERTIZARI</h3>
            <pre style="color: #fb923c; white-space: pre-wrap;">{e.warning}</pre>
            """

        html += "</div>"
        self.edu_detail.setHtml(html)

    def _show_world_context(self):
        """Afiseaza overview-ul global World Monitor."""
        if not self.world_ctx:
            return
        from data.world_monitor import format_context_for_ghid
        ctx_text = format_context_for_ghid(self.world_ctx)

        sentiment_colors = {"RISK_OFF": "#ff1744", "RISK_ON": "#00c853", "NEUTRAL": "#ff9800"}
        s_color = sentiment_colors.get(self.world_ctx.overall_sentiment, "#999")

        html = f"""
        <div style="font-family: Consolas, monospace; color: #c9d1d9; line-height: 1.6;">
        <h2 style="color: #38bdf8; border-bottom: 2px solid #1e3a5f; padding-bottom: 8px;">
            🌍 CONTEXT GLOBAL — 
            <span style="color: {s_color};">{self.world_ctx.overall_sentiment}</span>
            <span style="color: #94a3b8; font-size: 12px;">
                {self.world_ctx.timestamp[:16]}
            </span>
        </h2>
        """

        # Risk factors
        if self.world_ctx.risk_factors:
            html += '<h3 style="color: #ff1744; margin-top: 16px;">⚠ FACTORI DE RISC</h3>'
            for r in self.world_ctx.risk_factors[:10]:
                html += f'<p style="color: #fca5a5; margin: 4px 0 4px 16px;">• {r}</p>'

        # Opportunities
        if self.world_ctx.opportunities:
            html += '<h3 style="color: #00c853; margin-top: 16px;">✓ OPORTUNITATI</h3>'
            for o in self.world_ctx.opportunities[:8]:
                html += f'<p style="color: #86efac; margin: 4px 0 4px 16px;">• {o}</p>'

        # Finance news
        if self.world_ctx.top_finance_news:
            html += '<h3 style="color: #60a5fa; margin-top: 16px;">STIRI FINANCIARE</h3>'
            for n in self.world_ctx.top_finance_news[:10]:
                emoji = "🔴" if n.sentiment == "bearish" else "🟢" if n.sentiment == "bullish" else "⚪"
                assets = f' <span style="color: #fbbf24;">→ {", ".join(n.affected_assets)}</span>' if n.affected_assets else ""
                html += f'<p style="color: #d1d5db; margin: 4px 0 4px 8px;">{emoji} <b>[{n.source}]</b> {n.title}{assets}</p>'

        # Geopolitical
        if self.world_ctx.top_geopolitical_news:
            html += '<h3 style="color: #f97316; margin-top: 16px;">GEOPOLITICA</h3>'
            for n in self.world_ctx.top_geopolitical_news[:8]:
                emoji = "🔴" if n.sentiment == "bearish" else "🟢" if n.sentiment == "bullish" else "⚪"
                html += f'<p style="color: #d1d5db; margin: 4px 0 4px 8px;">{emoji} <b>[{n.source}]</b> {n.title}</p>'

        # Crypto
        if self.world_ctx.top_crypto_news:
            html += '<h3 style="color: #e879f9; margin-top: 16px;">CRYPTO</h3>'
            for n in self.world_ctx.top_crypto_news[:6]:
                emoji = "🔴" if n.sentiment == "bearish" else "🟢" if n.sentiment == "bullish" else "⚪"
                html += f'<p style="color: #d1d5db; margin: 4px 0 4px 8px;">{emoji} <b>[{n.source}]</b> {n.title}</p>'

        # Macro radar
        if self.world_ctx.macro_radar:
            html += '<h3 style="color: #38bdf8; margin-top: 16px;">MACRO RADAR (World Monitor)</h3>'
            mr = self.world_ctx.macro_radar
            if isinstance(mr, dict):
                html += f'<p style="color: #fbbf24; font-size: 14px;"><b>Verdict: {mr.get("verdict", "N/A")}</b></p>'

        # Conflicts
        if self.world_ctx.conflicts:
            html += '<h3 style="color: #ff1744; margin-top: 16px;">🔥 CONFLICTE ACTIVE</h3>'
            for c in self.world_ctx.conflicts[:5]:
                if isinstance(c, dict):
                    html += f'<p style="color: #fca5a5; margin: 4px 0 4px 16px;">• {c.get("name", c.get("title", str(c)))}</p>'

        html += """
        <p style="color: #475569; margin-top: 20px; font-size: 10px;">
            Surse: World Monitor API, Reuters, CNBC, Bloomberg, BBC, Al Jazeera, CoinDesk, CoinTelegraph
        </p>
        </div>"""
        self.edu_detail.setHtml(html)

    # ── Bune Practici ─────────────────────────────────────────────

    def _on_bp_selected(self, row):
        practici = get_bune_practici()
        if row < 0 or row >= len(practici):
            return
        p = practici[row]

        html = f"""
        <div style="font-family: Consolas, monospace; color: #c9d1d9; line-height: 1.6;">
        <h2 style="color: #38bdf8; border-bottom: 2px solid #1e3a5f; padding-bottom: 8px;">
            {p['titlu']}
        </h2>
        """

        for subtitle, text in p["sectiuni"]:
            html += f"""
            <h3 style="color: #60a5fa; margin-top: 16px;">{subtitle}</h3>
            <pre style="color: #d1d5db; white-space: pre-wrap; font-size: 12px;">{text}</pre>
            """

        html += "</div>"
        self.bp_detail.setHtml(html)

    # ── Biblioteca Strategii ──────────────────────────────────────

    def _on_strat_selected(self, row):
        strategies = get_strategy_library()
        if row < 0 or row >= len(strategies):
            return
        s = strategies[row]

        cat_colors = {
            "trend": "#00c853", "momentum": "#ff9800", "mean_reversion": "#9c27b0",
            "breakout": "#2196f3", "scalping": "#f44336", "swing": "#00bcd4",
        }
        cat_color = cat_colors.get(s.category, "#999")

        html = f"""
        <div style="font-family: Consolas, monospace; color: #c9d1d9; line-height: 1.6;">
        <h2 style="color: #38bdf8; border-bottom: 2px solid #1e3a5f; padding-bottom: 8px;">
            {s.name}
            <span style="background: {cat_color}; color: white; padding: 2px 8px;
                         border-radius: 4px; font-size: 11px; margin-left: 8px;">
                {s.category.upper()}
            </span>
        </h2>

        <p style="color: #94a3b8; font-size: 13px; margin: 8px 0;">{s.description}</p>

        <p style="color: #64748b;">Timeframe: <span style="color: #fbbf24;">{', '.join(s.timeframes)}</span></p>

        <h3 style="color: #00c853; margin-top: 16px;">CONDITII ENTRY</h3>
        """
        for r in s.entry_rules:
            html += f'<p style="color: #d1d5db; margin: 4px 0 4px 16px;">• {r}</p>'

        html += '<h3 style="color: #ff1744; margin-top: 16px;">CONDITII EXIT</h3>'
        for r in s.exit_rules:
            html += f'<p style="color: #d1d5db; margin: 4px 0 4px 16px;">• {r}</p>'

        html += f"""
        <h3 style="color: #f97316; margin-top: 16px;">RISK MANAGEMENT</h3>
        <p style="color: #d1d5db;"><b>Stop Loss:</b> {s.stop_loss_rule}</p>
        <p style="color: #d1d5db;"><b>Take Profit:</b> {s.take_profit_rule}</p>
        <p style="color: #d1d5db;"><b>Risc/Trade:</b> {s.risk_per_trade}</p>
        <p style="color: #d1d5db;"><b>R:R Minim:</b> 1:{s.risk_reward_min}</p>

        <h3 style="color: #00c853; margin-top: 16px;">CAND SA FOLOSESTI</h3>
        <p style="color: #d1d5db;">{s.when_to_use}</p>

        <h3 style="color: #ff1744; margin-top: 16px;">CAND SA EVITI</h3>
        <p style="color: #d1d5db;">{s.when_to_avoid}</p>

        <h3 style="color: #60a5fa; margin-top: 16px;">INDICATORI NECESARI</h3>
        <p style="color: #fbbf24;">{', '.join(s.indicators_needed)}</p>
        """

        if s.notes:
            html += f"""
            <h3 style="color: #a78bfa; margin-top: 16px;">NOTE</h3>
            <p style="color: #d1d5db;">{s.notes}</p>
            """

        html += "</div>"
        self.strat_detail.setHtml(html)

    def update_watchlist(self, watchlist: list):
        self.watchlist = watchlist
