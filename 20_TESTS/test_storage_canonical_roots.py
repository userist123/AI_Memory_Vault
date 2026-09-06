"""The storage engine and the index must agree on what the vault is.

They had diverged silently. `FileStorageEngine` still scanned "00_CORE",
"02_PROJECTS" and "03_PROCEDURES" — none of which survived the restructuring
into numbered roots — so on a fresh checkout it matched 0 files and the
production storage layer held zero notes, while 663 id-bearing notes sat in
the vault, 633 of them in `01_ARCHITECTURE`, a root that was never listed.

Nothing caught it because the suite runs against the in-memory StorageEngine
with fixtures. Only a run against the real vault exposes it, so these tests do
exactly that.
"""
from pathlib import Path

import pytest

from memory_controller.storage.file_engine import (
    CANONICAL_FOLDERS,
    CONTENT_ROOTS,
    LEGACY_WRITE_ROOTS,
    FileStorageEngine,
)
from retrieval.vault_index import DEFAULT_ROOTS

REPO = Path(__file__).resolve().parents[1]


def test_storage_covers_every_root_the_index_reads():
    """Divergence here means the controller and the graph read two different
    vaults, which is exactly the state this fixed."""
    assert set(DEFAULT_ROOTS) <= set(CANONICAL_FOLDERS)
    assert set(CONTENT_ROOTS) == set(DEFAULT_ROOTS)


@pytest.mark.parametrize("folder", CONTENT_ROOTS)
def test_every_content_root_exists(folder):
    """A folder that does not exist is scanned silently and yields nothing —
    which is precisely how this defect survived for a whole restructuring."""
    assert (REPO / folder).is_dir(), f"{folder} is scanned but does not exist"


@pytest.mark.parametrize("stale", ["00_CORE", "02_PROJECTS", "03_PROCEDURES"])
def test_legacy_names_are_reads_only_not_content_roots(stale):
    """They stay scanned because path_resolver.py still WRITES there, but they
    are not content roots and must never be mistaken for them."""
    assert stale in LEGACY_WRITE_ROOTS
    assert stale not in CONTENT_ROOTS


def test_engine_loads_notes_from_the_real_vault():
    """The regression that mattered: 0 notes loaded on a clean checkout."""
    engine = FileStorageEngine(str(REPO))
    assert len(engine.id_to_path) > 100, (
        f"storage loaded only {len(engine.id_to_path)} notes from the real "
        "vault; the canonical folder list has drifted from the layout again"
    )


def test_untrusted_and_projection_paths_stay_excluded():
    engine = FileStorageEngine(str(REPO))
    for path in engine.id_to_path.values():
        assert "RAW_IMPORTS" not in path, "untrusted inbox material must not load"
        assert "Obsidian" not in path, "the Obsidian projection is not canonical"
