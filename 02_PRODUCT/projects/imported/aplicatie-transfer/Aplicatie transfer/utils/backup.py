import shutil
import zipfile
from pathlib import Path
from datetime import datetime

class BackupManager:
    def __init__(self, config: dict):
        self.config = config
        self.db_path = Path(config.get("db_path", "transferuri.db"))
        self.backup_dir = Path(config.get("backup_dir", "./backup"))
        self.backup_dir.mkdir(exist_ok=True)

    def create_backup(self, compress: bool = None) -> str:
        """Creează backup bază de date cu timestamp."""
        if compress is None:
            compress = self.config.get("backup_compress", True)
        
        timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
        
        if compress:
            backup_file = self.backup_dir / f"backup_{timestamp}.zip"
            with zipfile.ZipFile(backup_file, 'w', zipfile.ZIP_DEFLATED) as zf:
                zf.write(self.db_path, self.db_path.name)
        else:
            backup_file = self.backup_dir / f"backup_{timestamp}.db"
            shutil.copy2(self.db_path, backup_file)
        
        return str(backup_file)

    def restore_backup(self, backup_file: str) -> bool:
        """Restaurează bază de date din backup."""
        backup_path = Path(backup_file)
        
        if not backup_path.exists():
            raise FileNotFoundError(f"Backup nu există: {backup_file}")
        
        # Backup curent înainte de restaurare
        self.create_backup(compress=False)
        
        if backup_path.suffix == '.zip':
            with zipfile.ZipFile(backup_path, 'r') as zf:
                zf.extractall(self.db_path.parent)
        else:
            shutil.copy2(backup_path, self.db_path)
        
        return True

    def cleanup_old_backups(self):
        """Șterge backup-uri vechi conform config."""
        keep_count = self.config.get("backup_keep_count", 10)
        
        backups = sorted(
            self.backup_dir.glob("backup_*.{db,zip}"),
            key=lambda p: p.stat().st_mtime,
            reverse=True
        )
        
        for backup in backups[keep_count:]:
            backup.unlink()

    def get_backup_list(self) -> list:
        """Returnează listă backup-uri disponibile."""
        backups = []
        
        for backup_file in sorted(self.backup_dir.glob("backup_*"), reverse=True):
            stat = backup_file.stat()
            backups.append({
                "file": str(backup_file),
                "name": backup_file.name,
                "size_mb": stat.st_size / (1024 * 1024),
                "created": datetime.fromtimestamp(stat.st_mtime).isoformat()
            })
        
        return backups
