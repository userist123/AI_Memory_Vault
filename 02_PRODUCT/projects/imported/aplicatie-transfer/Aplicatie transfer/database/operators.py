import uuid
from typing import List, Dict, Optional

class OperatorManager:
    def __init__(self, db_conn):
        self.conn = db_conn

    def add_operator(self, nume: str, functie: str = "", autorizatie: str = "Nesecurizat") -> str:
        op_id = str(uuid.uuid4())
        self.conn.execute(
            "INSERT INTO operatori(id, nume, functie, autorizatie, activ) VALUES(?,?,?,?,1)",
            (op_id, nume, functie, autorizatie)
        )
        self.conn.commit()
        return op_id

    def update_operator(self, op_id: str, **kwargs):
        fields = ", ".join([f"{k}=?" for k in kwargs])
        self.conn.execute(f"UPDATE operatori SET {fields} WHERE id=?", [*kwargs.values(), op_id])
        self.conn.commit()

    def get_all_operators(self, active_only=True) -> List[Dict]:
        query = "SELECT * FROM operatori"
        if active_only:
            query += " WHERE activ=1"
        query += " ORDER BY nume"
        rows = self.conn.execute(query).fetchall()
        return [dict(r) for r in rows]

    def get_operator_by_id(self, op_id: str) -> Optional[Dict]:
        row = self.conn.execute("SELECT * FROM operatori WHERE id=?", (op_id,)).fetchone()
        return dict(row) if row else None

    def deactivate_operator(self, op_id: str):
        self.conn.execute("UPDATE operatori SET activ=0 WHERE id=?", (op_id,))
        self.conn.commit()
