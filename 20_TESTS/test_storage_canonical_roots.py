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


def test_a_local_untracked_note_reusing_the_test_sentinel_id_does_not_collide(tmp_path):
    """WP-0 (r024) reproduction of the actual reported crash, not a synthetic
    one: a local, untracked note under 01_KNOWLEDGE/ (the legacy write root
    `path_resolver.py` sends `type: knowledge` notes to) reused the same
    all-zeros sentinel id as the tracked fixture that used to live at
    01_ARCHITECTURE/knowledge/test_00000000.md -- so every session on that
    working tree failed to construct FileStorageEngine at all.

    The fix moved that fixture out of the content root entirely (to
    20_TESTS/fixtures/), which is the only thing that closes the *class* of
    collision: the sentinel id is clearly one a local test/demo script keeps
    re-emitting (the reported local copy was dated 4 days after the tracked
    one), so leaving any tracked copy of it in a content root would just
    collide again the next time that script runs. This test proves a fresh
    local note reusing that id no longer collides with anything tracked.

    Writes into the REAL repo's 01_KNOWLEDGE/ (a legacy write root, not
    canonical content) because the defect is specifically about the real
    vault's tracked corpus, not a synthetic temp_vault; the file is removed
    unconditionally in a finally block so a failed run leaves no residue.
    """
    sentinel_id = "00000000-0000-0000-0000-000000000000"
    local_dir = REPO / "01_KNOWLEDGE"
    local_dir.mkdir(parents=True, exist_ok=True)
    local_path = local_dir / "test_00000000.md"
    assert not local_path.exists(), (
        "a real local file already exists at this path -- refusing to touch "
        "the user's own untracked file; run this test on a clean working tree"
    )
    local_path.write_text(
        "---\n"
        "type: knowledge\n"
        "category: test\n"
        "lifecycle: RAW\n"
        f"id: {sentinel_id}\n"
        "---\n",
        encoding="utf-8",
    )
    try:
        engine = FileStorageEngine(str(REPO))  # must not raise
        assert engine.id_to_path.get(sentinel_id) == str(local_path)
    finally:
        local_path.unlink(missing_ok=True)
