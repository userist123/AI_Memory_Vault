"""Static guard: note/ingestion writers must write LF (newline="\n") on every platform.

On Windows a text-mode write turns "\n" into "\r\n". The repository policy is LF, so
every text-mode write at the listed sites must pass newline="\n". Checked via AST so a
regression is caught on Linux CI too.
"""
import ast
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[1]

# file -> line numbers (1-based, start of the call) of the write sites
SITES = {
    "30_SCRIPTS/ingestion/apply_row_disposition.py": [280],
    "30_SCRIPTS/ingestion/merge_candidate_concepts.py": [300],
    "30_SCRIPTS/ingestion/promote_candidate_concept.py": [253, 266],
    "30_SCRIPTS/ingestion/promote_verified_batch.py": [191, 207],
    "30_SCRIPTS/ingestion/purge_rejected_rows.py": [418],
    "03_IMPLEMENTATION/packages/interfaces/real_execution_harness.py": [625, 639, 832, 957],
}


def _is_text_write(call: ast.Call) -> bool:
    func = call.func
    if isinstance(func, ast.Attribute) and func.attr == "write_text":
        return True
    if isinstance(func, ast.Name) and func.id == "open":
        mode = call.args[1] if len(call.args) > 1 else None
        for kw in call.keywords:
            if kw.arg == "mode":
                mode = kw.value
        return isinstance(mode, ast.Constant) and isinstance(mode.value, str) and "w" in mode.value and "b" not in mode.value
    return False


def _newline_is_lf(call: ast.Call) -> bool:
    return any(
        kw.arg == "newline" and isinstance(kw.value, ast.Constant) and kw.value.value == "\n"
        for kw in call.keywords
    )


@pytest.mark.parametrize("rel_path", sorted(SITES))
def test_text_writes_use_lf(rel_path):
    tree = ast.parse((ROOT / rel_path).read_text(encoding="utf-8"))
    writes = [n for n in ast.walk(tree) if isinstance(n, ast.Call) and _is_text_write(n)]
    # Every text-mode write in the file must be LF, and every known site must still exist.
    bad = [n.lineno for n in writes if not _newline_is_lf(n)]
    assert not bad, f"{rel_path}: text writes without newline='\n' at lines {bad}"
    assert len(writes) >= len(SITES[rel_path]), f"{rel_path}: expected >= {len(SITES[rel_path])} text writes"
