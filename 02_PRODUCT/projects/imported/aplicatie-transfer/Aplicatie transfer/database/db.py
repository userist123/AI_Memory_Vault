import sqlite3
import uuid
from datetime import datetime
from pathlib import Path
from typing import Optional, List, Dict, Any

class TransferDatabase:
    def __init__(self, db_path: str = "transferuri.db"):
        self.db_path = db_path
        self.conn = sqlite3.connect(db_path, check_same_thread=False)
        self.conn.row_factory = sqlite3.Row
        self._init_db()

    def _init_db(self):
        """Inițializare schema bază de date."""
        schema_path = Path(__file__).parent / "schema.sql"
        with open(schema_path, "r", encoding="utf-8") as f:
            self.conn.executescript(f.read())
        self.conn.commit()

    def _generate_nr_registru(self, prefix: str = "MAPN") -> str:
        """Generare număr registru: PREFIX/YYYY/NNNN."""
        year = datetime.now().year
        
        cursor = self.conn.execute(
            "SELECT nr FROM transfers WHERE nr LIKE ? ORDER BY nr DESC LIMIT 1",
            (f"{prefix}/{year}/%",)
        )
        row = cursor.fetchone()
        
        if row:
            last_nr = row["nr"].split("/")[-1]
            seq = int(last_nr) + 1
        else:
            seq = 1
        
        return f"{prefix}/{year}/{seq:04d}"

    def insert_transfer(self, data: Dict[str, Any]) -> str:
        """Inserare transfer nou."""
        record_id = str(uuid.uuid4())
        now = datetime.now().isoformat()
        
        # Generare nr. registru
        prefix = data.get("registru_prefix", "MAPN")
        nr = self._generate_nr_registru(prefix)
        
        sql = """
        INSERT INTO transfers (
            id, nr, created_at, updated_at, operator,
            src_institutie, src_pc_nume, src_medium, src_sn, src_path,
            pers_nume, pers_functie, pers_legitimatie, pers_autorizatie,
            transfer_medium, transfer_sn, transfer_label, transfer_cap_gb, transfer_free_gb,
            dst_institutie, dst_pc_nume, dst_medium, dst_sn, dst_path,
            arhiva_nume, arhiva_tip, arhiva_dim_gb, arhiva_fisiere, arhiva_hash, arhiva_descriere,
            clasificare, restrictii, aprobare_mult, baza_legala, observatii,
            status
        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
        """
        
        values = (
            record_id, nr, now, now, data["operator"],
            data["src_institutie"], data["src_pc_nume"], data["src_medium"], data.get("src_sn"), data.get("src_path"),
            data["pers_nume"], data.get("pers_functie"), data.get("pers_legitimatie"), data["pers_autorizatie"],
            data["transfer_medium"], data.get("transfer_sn"), data.get("transfer_label"), 
            data.get("transfer_cap_gb"), data.get("transfer_free_gb"),
            data["dst_institutie"], data.get("dst_pc_nume"), data.get("dst_medium"), data.get("dst_sn"), data.get("dst_path"),
            data.get("arhiva_nume"), data.get("arhiva_tip"), data.get("arhiva_dim_gb"), 
            data.get("arhiva_fisiere"), data.get("arhiva_hash"), data.get("arhiva_descriere"),
            data["clasificare"], data.get("restrictii"), data.get("aprobare_mult"), 
            data.get("baza_legala"), data.get("observatii"),
            data.get("status", "active")
        )
        
        self.conn.execute(sql, values)
        self.conn.commit()
        
        self._audit_log("INSERT", record_id, nr, data["operator"])
        self._update_autocomplete(data)
        
        return record_id

    def update_transfer(self, record_id: str, data: Dict[str, Any], operator: str):
        """Actualizare transfer existent."""
        old_record = self.get_transfer_by_id(record_id)
        if not old_record:
            raise ValueError(f"Transfer {record_id} nu există")
        
        now = datetime.now().isoformat()
        
        sql = """
        UPDATE transfers SET
            updated_at = ?, operator = ?,
            src_institutie = ?, src_pc_nume = ?, src_medium = ?, src_sn = ?, src_path = ?,
            pers_nume = ?, pers_functie = ?, pers_legitimatie = ?, pers_autorizatie = ?,
            transfer_medium = ?, transfer_sn = ?, transfer_label = ?, transfer_cap_gb = ?, transfer_free_gb = ?,
            dst_institutie = ?, dst_pc_nume = ?, dst_medium = ?, dst_sn = ?, dst_path = ?,
            arhiva_nume = ?, arhiva_tip = ?, arhiva_dim_gb = ?, arhiva_fisiere = ?, arhiva_hash = ?, arhiva_descriere = ?,
            clasificare = ?, restrictii = ?, aprobare_mult = ?, baza_legala = ?, observatii = ?
        WHERE id = ?
        """
        
        values = (
            now, operator,
            data["src_institutie"], data["src_pc_nume"], data["src_medium"], data.get("src_sn"), data.get("src_path"),
            data["pers_nume"], data.get("pers_functie"), data.get("pers_legitimatie"), data["pers_autorizatie"],
            data["transfer_medium"], data.get("transfer_sn"), data.get("transfer_label"),
            data.get("transfer_cap_gb"), data.get("transfer_free_gb"),
            data["dst_institutie"], data.get("dst_pc_nume"), data.get("dst_medium"), data.get("dst_sn"), data.get("dst_path"),
            data.get("arhiva_nume"), data.get("arhiva_tip"), data.get("arhiva_dim_gb"),
            data.get("arhiva_fisiere"), data.get("arhiva_hash"), data.get("arhiva_descriere"),
            data["clasificare"], data.get("restrictii"), data.get("aprobare_mult"),
            data.get("baza_legala"), data.get("observatii"),
            record_id
        )
        
        self.conn.execute(sql, values)
        self.conn.commit()
        
        self._audit_log_changes("UPDATE", record_id, old_record["nr"], operator, old_record, data)
        self._update_autocomplete(data)

    def soft_delete_transfer(self, record_id: str, operator: str):
        """Ștergere soft (marcare status=deleted)."""
        now = datetime.now().isoformat()
        
        self.conn.execute(
            "UPDATE transfers SET status = ?, deleted_at = ?, deleted_by = ? WHERE id = ?",
            ("deleted", now, operator, record_id)
        )
        self.conn.commit()
        
        rec = self.get_transfer_by_id(record_id)
        if rec:
            self._audit_log("DELETE", record_id, rec["nr"], operator)

    def restore_transfer(self, record_id: str):
        """Restaurare transfer șters."""
        self.conn.execute(
            "UPDATE transfers SET status = ?, deleted_at = NULL, deleted_by = NULL WHERE id = ?",
            ("active", record_id)
        )
        self.conn.commit()

    def get_transfer_by_id(self, record_id: str) -> Optional[Dict]:
        """Obținere transfer după ID."""
        cursor = self.conn.execute("SELECT * FROM transfers WHERE id = ?", (record_id,))
        row = cursor.fetchone()
        return dict(row) if row else None

    def get_all_transfers(self) -> List[Dict]:
        """Obținere toate transferurile."""
        cursor = self.conn.execute("SELECT * FROM transfers ORDER BY created_at DESC")
        return [dict(row) for row in cursor.fetchall()]

    def filter_transfers(self, filters: Dict[str, Any]) -> List[Dict]:
        """Filtrare transferuri."""
        sql = "SELECT * FROM transfers WHERE 1=1"
        params = []
        
        if "status" in filters:
            sql += " AND status = ?"
            params.append(filters["status"])
        
        if "clasificare" in filters:
            sql += " AND clasificare = ?"
            params.append(filters["clasificare"])
        
        if "search" in filters:
            search = f"%{filters['search']}%"
            sql += " AND (nr LIKE ? OR src_institutie LIKE ? OR dst_institutie LIKE ? OR pers_nume LIKE ?)"
            params.extend([search, search, search, search])
        
        sql += " ORDER BY created_at DESC"
        
        cursor = self.conn.execute(sql, params)
        return [dict(row) for row in cursor.fetchall()]

    def get_all_operators(self) -> List[Dict]:
        """Listă operatori."""
        cursor = self.conn.execute("SELECT * FROM operators ORDER BY name")
        return [dict(row) for row in cursor.fetchall()]

    def add_operator(self, name: str, rank: str = None, unit: str = None):
        """Adăugare operator nou."""
        now = datetime.now().isoformat()
        self.conn.execute(
            "INSERT INTO operators (name, rank, unit, created_at) VALUES (?, ?, ?, ?)",
            (name, rank, unit, now)
        )
        self.conn.commit()

    def get_autocomplete(self, category: str, prefix: str, limit: int = 10) -> List[str]:
        """Sugestii autocomplete."""
        cursor = self.conn.execute(
            "SELECT value FROM autocomplete_cache WHERE category = ? AND value LIKE ? ORDER BY frequency DESC LIMIT ?",
            (category, f"%{prefix}%", limit)
        )
        return [row["value"] for row in cursor.fetchall()]

    def _update_autocomplete(self, data: Dict[str, Any]):
        """Actualizare cache autocomplete."""
        now = datetime.now().isoformat()
        
        mappings = {
            "institutii": ["src_institutie", "dst_institutie"],
            "pcuri": ["src_pc_nume", "dst_pc_nume"],
            "persoane": ["pers_nume"]
        }
        
        for category, fields in mappings.items():
            for field in fields:
                value = data.get(field)
                if value:
                    self.conn.execute(
                        """
                        INSERT INTO autocomplete_cache (category, value, frequency, last_used)
                        VALUES (?, ?, 1, ?)
                        ON CONFLICT(category, value) DO UPDATE SET
                            frequency = frequency + 1,
                            last_used = ?
                        """,
                        (category, value, now, now)
                    )
        
        self.conn.commit()

    def _audit_log(self, operation: str, record_id: str, nr: str, operator: str):
        """Înregistrare audit simplu."""
        now = datetime.now().isoformat()
        self.conn.execute(
            "INSERT INTO audit_log (timestamp, operator, operation, record_id, nr_registru) VALUES (?, ?, ?, ?, ?)",
            (now, operator, operation, record_id, nr)
        )
        self.conn.commit()

    def _audit_log_changes(self, operation: str, record_id: str, nr: str, operator: str, old: Dict, new: Dict):
        """Audit modificări câmpuri."""
        now = datetime.now().isoformat()
        
        for key in old.keys():
            if key in ["id", "nr", "created_at", "updated_at", "operator"]:
                continue
            
            old_val = str(old.get(key, ""))
            new_val = str(new.get(key, ""))
            
            if old_val != new_val:
                self.conn.execute(
                    """
                    INSERT INTO audit_log (timestamp, operator, operation, record_id, nr_registru, field_name, old_value, new_value)
                    VALUES (?, ?, ?, ?, ?, ?, ?, ?)
                    """,
                    (now, operator, operation, record_id, nr, key, old_val, new_val)
                )
        
        self.conn.commit()

    def close(self):
        """Închidere conexiune."""
        self.conn.close()
