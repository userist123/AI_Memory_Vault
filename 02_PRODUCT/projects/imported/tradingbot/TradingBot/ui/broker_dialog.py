"""
Trading Bot — Broker Auth Dialog
Secure login with encrypted credential storage.
"""
from PyQt6.QtWidgets import (QDialog, QVBoxLayout, QHBoxLayout, QLabel,
                              QLineEdit, QComboBox, QPushButton, QCheckBox,
                              QMessageBox, QFrame, QFormLayout, QGroupBox)
from PyQt6.QtCore import Qt, pyqtSignal
from core.config import BROKER_PRESETS, AppConfig
from core.security import save_credentials, load_credentials, has_credentials, delete_credentials


class BrokerAuthDialog(QDialog):
    connected = pyqtSignal(str, str, str, str, bool)  # broker, key, secret, pass, sandbox

    STYLE = """
        QDialog { background: #0a0e17; color: #c9d1d9; }
        QLabel { color: #94a3b8; font-size: 12px; }
        QLineEdit {
            background: #111827; border: 1px solid #1e3a5f; border-radius: 6px;
            padding: 8px 12px; color: #e2e8f0; font-size: 13px;
        }
        QLineEdit:focus { border-color: #38bdf8; }
        QComboBox {
            background: #111827; border: 1px solid #1e3a5f; border-radius: 6px;
            padding: 6px 10px; color: #e2e8f0; min-width: 200px;
        }
        QComboBox QAbstractItemView { background: #111827; color: #e2e8f0; border: 1px solid #1e3a5f; }
        QCheckBox { color: #94a3b8; font-size: 12px; }
        QPushButton {
            background: #1e40af; color: white; border: none; border-radius: 6px;
            padding: 10px 24px; font-weight: bold; font-size: 13px;
        }
        QPushButton:hover { background: #2563eb; }
        QPushButton#dangerBtn { background: #7f1d1d; }
        QPushButton#dangerBtn:hover { background: #991b1b; }
        QGroupBox { border: 1px solid #1e3a5f; border-radius: 8px; margin-top: 10px; padding: 16px; padding-top: 24px; }
        QGroupBox::title { color: #38bdf8; font-weight: bold; subcontrol-origin: margin; left: 12px; }
    """

    def __init__(self, parent=None):
        super().__init__(parent)
        self.setWindowTitle("Trading Bot — Conectare Broker")
        self.setFixedSize(520, 580)
        self.setStyleSheet(self.STYLE)
        self._setup_ui()

    def _setup_ui(self):
        layout = QVBoxLayout(self)
        layout.setSpacing(12)

        title = QLabel("AUTENTIFICARE BROKER")
        title.setAlignment(Qt.AlignmentFlag.AlignCenter)
        title.setStyleSheet("font-size: 18px; font-weight: bold; color: #38bdf8; letter-spacing: 3px; padding: 10px;")
        layout.addWidget(title)

        # Master password
        pw_group = QGroupBox("Parola Master (cripteaza credentialele local)")
        pw_layout = QFormLayout(pw_group)
        self.master_pw = QLineEdit()
        self.master_pw.setEchoMode(QLineEdit.EchoMode.Password)
        self.master_pw.setPlaceholderText("Parola pentru criptare locala...")
        pw_layout.addRow("Parola:", self.master_pw)
        layout.addWidget(pw_group)

        # Broker selection
        broker_group = QGroupBox("Selecteaza Broker")
        broker_layout = QFormLayout(broker_group)

        self.broker_combo = QComboBox()
        self.broker_combo.addItems([
            "Binance", "Binance Futures", "Kraken", "Coinbase",
            "Bybit", "KuCoin", "OKX", "Alpaca (Stocks US)",
            "Interactive Brokers", "XTB", "Custom"
        ])
        broker_layout.addRow("Broker:", self.broker_combo)

        self.api_key = QLineEdit()
        self.api_key.setPlaceholderText("API Key...")
        broker_layout.addRow("API Key:", self.api_key)

        self.api_secret = QLineEdit()
        self.api_secret.setEchoMode(QLineEdit.EchoMode.Password)
        self.api_secret.setPlaceholderText("API Secret...")
        broker_layout.addRow("API Secret:", self.api_secret)

        self.passphrase = QLineEdit()
        self.passphrase.setEchoMode(QLineEdit.EchoMode.Password)
        self.passphrase.setPlaceholderText("Passphrase (optional, pt. Coinbase/KuCoin)...")
        broker_layout.addRow("Passphrase:", self.passphrase)

        self.custom_url = QLineEdit()
        self.custom_url.setPlaceholderText("URL custom (optional)...")
        broker_layout.addRow("Custom URL:", self.custom_url)

        self.sandbox_cb = QCheckBox("Paper Trading / Testnet (recomandat la inceput)")
        self.sandbox_cb.setChecked(True)
        broker_layout.addRow("", self.sandbox_cb)

        layout.addWidget(broker_group)

        # Actions
        btn_layout = QHBoxLayout()

        self.load_btn = QPushButton("Incarca Salvate")
        self.load_btn.clicked.connect(self._load_saved)
        btn_layout.addWidget(self.load_btn)

        self.save_btn = QPushButton("Salveaza & Conecteaza")
        self.save_btn.clicked.connect(self._save_and_connect)
        btn_layout.addWidget(self.save_btn)

        layout.addLayout(btn_layout)

        self.delete_btn = QPushButton("Sterge Credentiale")
        self.delete_btn.setObjectName("dangerBtn")
        self.delete_btn.clicked.connect(self._delete_creds)
        layout.addWidget(self.delete_btn)

        # Status
        self.status = QLabel("")
        self.status.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.status.setStyleSheet("font-size: 11px; padding: 6px;")
        layout.addWidget(self.status)

        layout.addStretch()

        # Info
        info = QLabel(
            "Credentialele sunt criptate local cu AES-256.\n"
            "Nu sunt trimise nicaieri. Doar tu le poti decripta cu parola master."
        )
        info.setAlignment(Qt.AlignmentFlag.AlignCenter)
        info.setStyleSheet("color: #475569; font-size: 10px;")
        layout.addWidget(info)

    def _get_broker_key(self) -> str:
        text = self.broker_combo.currentText().lower()
        mapping = {
            "binance": "binance", "binance futures": "binance_futures",
            "kraken": "kraken", "coinbase": "coinbase",
            "bybit": "bybit", "kucoin": "kucoin", "okx": "okx",
            "alpaca (stocks us)": "alpaca", "interactive brokers": "interactive_brokers",
            "xtb": "xtb", "custom": "custom",
        }
        return mapping.get(text, "custom")

    def _save_and_connect(self):
        pw = self.master_pw.text().strip()
        if len(pw) < 4:
            self.status.setText("Parola master trebuie sa aiba minim 4 caractere")
            self.status.setStyleSheet("color: #ff1744; font-size: 11px;")
            return

        key = self.api_key.text().strip()
        secret = self.api_secret.text().strip()
        if not key or not secret:
            self.status.setText("API Key si Secret sunt obligatorii")
            self.status.setStyleSheet("color: #ff1744; font-size: 11px;")
            return

        broker = self._get_broker_key()
        data = {
            "broker": broker,
            "api_key": key,
            "api_secret": secret,
            "passphrase": self.passphrase.text().strip(),
            "sandbox": self.sandbox_cb.isChecked(),
            "custom_url": self.custom_url.text().strip(),
        }

        try:
            save_credentials(data, pw)
            self.status.setText("Credentiale salvate si criptate cu succes")
            self.status.setStyleSheet("color: #00c853; font-size: 11px;")
            self.connected.emit(
                broker, key, secret,
                data["passphrase"], data["sandbox"]
            )
            self.accept()
        except Exception as e:
            self.status.setText(f"Eroare: {e}")
            self.status.setStyleSheet("color: #ff1744; font-size: 11px;")

    def _load_saved(self):
        pw = self.master_pw.text().strip()
        if not pw:
            self.status.setText("Introdu parola master pentru decriptare")
            self.status.setStyleSheet("color: #ff9800; font-size: 11px;")
            return

        data = load_credentials(pw)
        if not data:
            self.status.setText("Parola incorecta sau nu exista credentiale salvate")
            self.status.setStyleSheet("color: #ff1744; font-size: 11px;")
            return

        self.api_key.setText(data.get("api_key", ""))
        self.api_secret.setText(data.get("api_secret", ""))
        self.passphrase.setText(data.get("passphrase", ""))
        self.sandbox_cb.setChecked(data.get("sandbox", True))
        self.custom_url.setText(data.get("custom_url", ""))

        # Set broker combo
        broker = data.get("broker", "")
        reverse_map = {
            "binance": 0, "binance_futures": 1, "kraken": 2, "coinbase": 3,
            "bybit": 4, "kucoin": 5, "okx": 6, "alpaca": 7,
            "interactive_brokers": 8, "xtb": 9, "custom": 10,
        }
        idx = reverse_map.get(broker, 10)
        self.broker_combo.setCurrentIndex(idx)

        self.status.setText("Credentiale incarcate cu succes")
        self.status.setStyleSheet("color: #00c853; font-size: 11px;")

        # Auto-connect
        self.connected.emit(
            broker, data.get("api_key", ""), data.get("api_secret", ""),
            data.get("passphrase", ""), data.get("sandbox", True)
        )
        self.accept()

    def _delete_creds(self):
        reply = QMessageBox.question(
            self, "Confirmare",
            "Esti sigur ca vrei sa stergi credentialele salvate?",
            QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.No
        )
        if reply == QMessageBox.StandardButton.Yes:
            delete_credentials()
            self.api_key.clear()
            self.api_secret.clear()
            self.passphrase.clear()
            self.status.setText("Credentiale sterse")
            self.status.setStyleSheet("color: #ff9800; font-size: 11px;")
