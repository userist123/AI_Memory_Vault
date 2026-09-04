"""
Unit tests for SQLite Persistence and SHA-256 Chained Audit Logging.
"""

import os
import tempfile
import unittest
from datetime import datetime, timezone

from xau_kinetic.domain.models import TickData
from xau_kinetic.infrastructure.persistence import SQLitePersistence


class TestPersistence(unittest.TestCase):
    def setUp(self):
        self.temp_db = tempfile.NamedTemporaryFile(delete=False, suffix=".db")
        self.temp_db.close()
        self.persistence = SQLitePersistence(db_path=self.temp_db.name)

    def tearDown(self):
        if os.path.exists(self.temp_db.name):
            os.remove(self.temp_db.name)

    def test_save_and_fetch_ticks(self):
        tick = TickData(
            symbol="XAUUSD",
            bid=2650.00,
            ask=2650.40,
            last=2650.20,
            volume=5.0,
            timestamp=datetime.now(timezone.utc),
        )
        self.persistence.save_ticks([tick])

    def test_sha256_audit_chain_integrity(self):
        # Log multiple audit events
        e1 = self.persistence.log_audit_event("INIT", {"status": "started"})
        self.assertEqual(e1.prev_hash, SQLitePersistence.GENESIS_HASH)

        e2 = self.persistence.log_audit_event("SIGNAL", {"symbol": "XAUUSD", "type": "BUY"})
        self.assertEqual(e2.prev_hash, e1.current_hash)

        e3 = self.persistence.log_audit_event("ORDER", {"ticket": 12345, "volume": 0.1})
        self.assertEqual(e3.prev_hash, e2.current_hash)

        # Verify cryptographic chain integrity
        valid, msg = self.persistence.verify_chain_integrity()
        self.assertTrue(valid, f"Chain verification failed: {msg}")


if __name__ == "__main__":
    unittest.main()
