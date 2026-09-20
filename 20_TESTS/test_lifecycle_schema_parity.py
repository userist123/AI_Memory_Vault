"""The lifecycle enum, the SQLite CHECK and the transition policy name the same states.

`Lifecycle` had nine states; the production schema's CHECK accepted eight. The
missing one was RECONSOLIDATING, which `learning/consolidation.py` assigns when
a canonical memory is challenged — so that write failed on a constraint no
caller could see, and only on databases that use the SQLite engine.

These tests hold the three definitions together, and prove the migration
recovers a database created before the state existed.
"""
from __future__ import annotations

import re
import sqlite3
import sys
from pathlib import Path

import pytest

REPO = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(REPO / "03_IMPLEMENTATION" / "packages"))

CONTROLLER = REPO / "03_IMPLEMENTATION" / "packages" / "memory" / "controller.py"
ENGINE = REPO / "03_IMPLEMENTATION" / "packages" / "memory" / "storage" / "sqlite_engine.py"
POLICY = REPO / "03_IMPLEMENTATION" / "packages" / "lifecycle" / "policy.py"

from memory_controller.storage.sqlite_engine import SQLiteStorageEngine  # noqa: E402


def enum_states() -> set[str]:
    body = re.search(r"class Lifecycle\(str, enum\.Enum\):(.*?)\n\n", CONTROLLER.read_text(encoding="utf-8"), re.S)
    return {value for _, value in re.findall(r'^\s+(\w+)\s*=\s*"([^"]+)"', body.group(1), re.M)}


def schema_states() -> set[str]:
    check = re.search(r"CHECK\(lifecycle IN \(([^)]+)\)", ENGINE.read_text(encoding="utf-8"))
    return {value.strip().strip("'\"") for value in check.group(1).split(",")}


def policy_states() -> set[str]:
    body = re.search(r"class \w*State\w*\([^)]*\):(.*?)\n\n", POLICY.read_text(encoding="utf-8"), re.S)
    if not body:
        pytest.skip("the policy module no longer declares its states as an enum")
    return {value for _, value in re.findall(r'^\s+(\w+)\s*=\s*"([^"]+)"', body.group(1), re.M)}


def a_row(columns: list[str], lifecycle: str, note_id: str = "note-1") -> list[str]:
    defaults = {"id": note_id, "type": "knowledge", "lifecycle": lifecycle,
                "source_type": "execution", "confidence": "high", "verification": "unverified"}
    return [defaults.get(column, "x") for column in columns]


def test_the_schema_accepts_exactly_the_states_the_enum_declares():
    assert schema_states() == enum_states()


def test_the_declared_state_tuple_matches_the_ddl():
    """The tuple beside the DDL is what the migration reads; the two must agree."""
    assert set(SQLiteStorageEngine.LIFECYCLE_STATES) == schema_states()


def test_the_policy_knows_no_state_the_database_would_reject():
    assert policy_states() <= schema_states()


def test_a_reconsolidating_note_can_be_written():
    engine = SQLiteStorageEngine(":memory:")
    connection = engine._get_connection()
    columns = [row[1] for row in connection.execute("PRAGMA table_info(notes)")]
    statement = f"INSERT INTO notes ({', '.join(columns)}) VALUES ({', '.join('?' * len(columns))})"
    connection.execute(statement, a_row(columns, "RECONSOLIDATING"))
    assert connection.execute("SELECT COUNT(*) FROM notes").fetchone()[0] == 1


def test_an_invented_state_is_still_refused():
    """The CHECK must remain a constraint, not a formality."""
    engine = SQLiteStorageEngine(":memory:")
    connection = engine._get_connection()
    columns = [row[1] for row in connection.execute("PRAGMA table_info(notes)")]
    statement = f"INSERT INTO notes ({', '.join(columns)}) VALUES ({', '.join('?' * len(columns))})"
    with pytest.raises(sqlite3.IntegrityError):
        connection.execute(statement, a_row(columns, "TOTALLY_MADE_UP"))


def test_an_old_database_is_migrated_and_keeps_its_rows(tmp_path):
    """A database created before RECONSOLIDATING existed, reopened by today's engine."""
    db = tmp_path / "old.db"
    old_schema = SQLiteStorageEngine.SCHEMA.replace("'RECONSOLIDATING', ", "")
    assert "RECONSOLIDATING" not in old_schema

    connection = sqlite3.connect(db)
    connection.executescript(old_schema)
    columns = [row[1] for row in connection.execute("PRAGMA table_info(notes)")]
    statement = f"INSERT INTO notes ({', '.join(columns)}) VALUES ({', '.join('?' * len(columns))})"
    connection.execute(statement, a_row(columns, "ACTIVE", note_id="before-migration"))
    connection.commit()
    connection.close()

    engine = SQLiteStorageEngine(str(db))
    connection = engine._get_connection()

    assert connection.execute("SELECT COUNT(*) FROM notes").fetchone()[0] == 1, "the existing row survived"
    connection.execute(statement, a_row(columns, "RECONSOLIDATING", note_id="after-migration"))
    assert connection.execute("SELECT COUNT(*) FROM notes").fetchone()[0] == 2
    kept = connection.execute("SELECT lifecycle FROM notes WHERE id = 'before-migration'").fetchone()
    assert kept[0] == "ACTIVE", "the migrated row kept its state"
    leftovers = connection.execute(
        "SELECT name FROM sqlite_master WHERE name LIKE '%pre_lifecycle_migration%'").fetchall()
    assert leftovers == [], "the migration left its temporary table behind"


def test_the_migration_does_nothing_to_an_up_to_date_database(tmp_path):
    db = tmp_path / "current.db"
    engine = SQLiteStorageEngine(str(db))
    assert engine._migrate_lifecycle_check(engine._get_connection()) is False
