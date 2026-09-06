import sys
import json
from pathlib import Path
from PyQt6.QtWidgets import QApplication, QMessageBox
from PyQt6.QtCore import Qt

from database.db import TransferDatabase
from ui.operator_dialog import OperatorDialog
from ui.main_window import MainWindow

def load_config() -> dict:
    config_path = Path("config.json")
    
    default_config = {
        "db_path": "transferuri.db",
        "backup_dir": "./backup",
        "export_dir": "./export",
        "registru_prefix": "MAPN",
        "clasificare_default": "Nesecret",
        "log_medium_default": "HDD Intern",
        "baza_legala_default": "HG 585/2002 Art. 69, 71",
        "backup_enabled": True,
        "backup_interval_hours": 24,
        "backup_keep_count": 10,
        "backup_compress": True,
        "table_font_size": 9,
        "table_row_height": 30,
        "enable_tooltips": True,
        "enable_notifications": False,
        "enable_sound": False
    }
    
    if config_path.exists():
        try:
            with open(config_path, "r", encoding="utf-8") as f:
                loaded = json.load(f)
                default_config.update(loaded)
        except Exception as e:
            print(f"Eroare încărcare config: {e}")
    
    return default_config

def save_config(config: dict):
    try:
        with open("config.json", "w", encoding="utf-8") as f:
            json.dump(config, f, indent=2, ensure_ascii=False)
    except Exception as e:
        print(f"Eroare salvare config: {e}")

def main():
    app = QApplication(sys.argv)
    app.setApplicationName("Registru Transferuri Media")
    app.setOrganizationName("MApN")
    
    # Stil modern
    app.setStyle("Fusion")
    
    # Încărcare config
    config = load_config()
    
    # Inițializare DB
    try:
        db = TransferDatabase(config["db_path"])
    except Exception as e:
        QMessageBox.critical(None, "Eroare", f"Eroare deschidere bază de date:\n{e}")
        return 1
    
    # Selectare operator
    operator_dialog = OperatorDialog(db)
    if operator_dialog.exec() != operator_dialog.DialogCode.Accepted:
        return 0
    
    operator_name = operator_dialog.get_operator()
    if not operator_name:
        QMessageBox.warning(None, "Avertisment", "Nu ați selectat niciun operator!")
        return 0
    
    # Fereastra principală
    window = MainWindow(db, operator_name, config)
    window.show()
    
    return app.exec()

if __name__ == "__main__":
    sys.exit(main())
