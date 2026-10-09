import subprocess
import sys
from pathlib import Path


def _upserted_point_id(repo_root: Path, note_id: str) -> int:
    script = r"""
import sys
from pathlib import Path

root = Path(sys.argv[1])
sys.path.insert(0, str(root / "03_IMPLEMENTATION" / "packages"))
from retrieval.qdrant_retrieval import QdrantIndex

captured = {}
index = QdrantIndex()
index._request = lambda method, path, body=None: captured.update(body=body) or {"result": "ok"}
index.upsert([(sys.argv[2], [0.1, 0.2], {})])
print(captured["body"]["points"][0]["id"])
"""
    result = subprocess.run(
        [sys.executable, "-c", script, str(repo_root), note_id],
        check=True,
        capture_output=True,
        text=True,
    )
    return int(result.stdout.strip())


def test_qdrant_point_id_is_stable_across_python_processes():
    repo_root = Path(__file__).resolve().parents[1]

    first = _upserted_point_id(repo_root, "memory-note-stability-probe")
    second = _upserted_point_id(repo_root, "memory-note-stability-probe")

    assert first == second, (
        "Qdrant point IDs must be deterministic across Python processes; "
        f"got {first} and {second}"
    )
    assert 0 <= first < 2**63


def test_qdrant_point_ids_differ_for_distinct_note_ids():
    repo_root = Path(__file__).resolve().parents[1]

    first = _upserted_point_id(repo_root, "memory-note-alpha")
    second = _upserted_point_id(repo_root, "memory-note-beta")

    assert first != second
