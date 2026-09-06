"""journal.py – jurnal SQLite pentru înregistrarea tranzacţiilor.

Acest modul este foarte simplu: înregistrează deschiderea și închiderea
unei poziţii paper‑trade și calculează profitul.  Pentru o aplicaţie reală
ar conţine mult mai multe câmpuri (SL, TP, comisioane, etc.)."""

import sqlite3
import datetime
from pathlib import Path
from typing import Optional, Tuple


class Journal:
    """Wrapper simplu în jurul unei baze SQLite.

    Fișierul `trades.db` este creat în directorul de lucru curent.
    """

    def __init__(self, db_path: Path = Path("trades.db")):
        self.db_path = db_path
        self.conn = sqlite3.connect(self.db_path, check_same_thread=False)
        self._create_table()

    def _create_table(self) -> None:
        self.conn.execute(
            """
            CREATE TABLE IF NOT EXISTS trades (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                direction INTEGER,          -- 1 = BUY, -1 = SELL
                entry_price REAL,
                entry_time TEXT,
                exit_price REAL,
                exit_time TEXT,
                profit REAL
            )
            """
        )
        self.conn.commit()

    def open_trade(self, direction: int, entry_price: float) -> int:
        """Înregistrează o poziţie deschisă și returnează `trade_id`."""
        now = datetime.datetime.utcnow().isoformat()
        cur = self.conn.cursor()
        cur.execute(
            """
            INSERT INTO trades (direction, entry_price, entry_time)
            VALUES (?, ?, ?)
            """,
            (direction, entry_price, now),
        )
        self.conn.commit()
        return cur.lastrowid

    def close_trade(self, trade_id: int, exit_price: float) -> None:
        """Încheie poziţia și calculează profitul.

        Profit = (exit_price - entry_price) * direction
        """
        now = datetime.datetime.utcnow().isoformat()
        cur = self.conn.cursor()
        cur.execute("SELECT direction, entry_price FROM trades WHERE id = ?", (trade_id,))
        row = cur.fetchone()
        if row is None:
            raise ValueError(f"Trade id {trade_id} not found")
        direction, entry_price = row
        profit = (exit_price - entry_price) * direction
        cur.execute(
            """
            UPDATE trades
            SET exit_price = ?, exit_time = ?, profit = ?
            WHERE id = ?
            """,
            (exit_price, now, profit, trade_id),
        )
        self.conn.commit()

    def last_trade(self) -> Optional[Tuple]:
        """Returnează ultima tranzacţie (tuple cu toate coloanele) sau `None`."""
        cur = self.conn.cursor()
        cur.execute("SELECT * FROM trades ORDER BY id DESC LIMIT 1")
        return cur.fetchone()

    def close(self) -> None:
        self.conn.close()
