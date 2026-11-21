"""AKE shadow runner: snapshot -> deterministic stages -> one hashed job record.

Read-only by construction:
- the only things it reads are the notes, through the controller's storage (``collect_notes``);
- the stages are the production ``SleepConsolidator`` (which runs ``ConflictDetector``), the
  non-mutating ``ConflictReviewWorkflow`` and, when a frozen benchmark is given, ``retrieval_quality``
  (``MemoryController.search`` as AI_AGENT, read path only);
- the only thing it writes is its own job record, and only outside the canonical content tree
  (a vault-relative output must sit under ``08_OBSERVABILITY/``).

Idempotency: a job is identified by the trigger, a SHA-256 of the eligible notes (ACTIVE,
VERIFIED, REVIEW) and a SHA-256 of the configuration, which includes the UTC date because the
age thresholds depend on it. A COMPLETED record for the same job is returned as is. A FAILED or
STALE_SNAPSHOT record is kept (renamed per attempt) and the job runs again.
"""
from __future__ import annotations

import enum
import hashlib
import inspect
import json
import os
import sys
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Dict, List, Optional

from cognitive_core.conflict_review import ConflictReviewWorkflow
from cognitive_core.sleep_consolidation import SleepConsolidator, collect_notes

from .readability import agent_readability
from .retrieval_quality import AGENT_PAGE_SIZE, canonical_digest, evaluate, load_frozen_benchmark

SCHEMA_VERSION = "ake.job/1"
ELIGIBLE_LIFECYCLES = frozenset({"ACTIVE", "VERIFIED", "REVIEW"})
TRIGGERS = frozenset({"manual", "scheduled", "ci"})
STAGES = ("integrity", "conflict_review", "agent_readability")
OBSERVABILITY_DIR = "08_OBSERVABILITY"
LIMITATIONS = (
    "shadow run: no proposal, promotion, attestation or note write is made",
    "owner-approval binding (ApprovalBroker) is not wired into production",
    "age-based findings depend on as_of_utc_date",
    "conflict detection is lexical (negation + token overlap), not semantic",
)


class JobStatus(str, enum.Enum):
    COMPLETED = "COMPLETED"
    COMPLETED_WITH_SKIPS = "COMPLETED_WITH_SKIPS"  # stages ran, but storage could not read some note files
    FAILED = "FAILED"
    STALE_SNAPSHOT = "STALE_SNAPSHOT"


REUSABLE = frozenset({JobStatus.COMPLETED.value, JobStatus.COMPLETED_WITH_SKIPS.value})


class OutputLocationError(ValueError):
    """The job record would land inside the canonical content tree."""


def _canonical(obj: Any) -> bytes:
    return json.dumps(obj, sort_keys=True, ensure_ascii=False, separators=(",", ":"), default=str).encode("utf-8")


def _sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def storage_skips(controller) -> Dict[str, Any]:
    """Note files the storage engine could not read (paths and reasons only, never content)."""
    storage = getattr(controller, "storage", None)
    report = getattr(storage, "skip_report", None)
    if not callable(report):
        return {"skipped_count": 0, "skipped_paths": []}
    data = report(cap=sys.maxsize)  # uncapped: every skipped path is part of the snapshot
    paths = sorted(str(p) for p in data.get("skipped_paths", []))
    return {"skipped_count": int(data.get("skipped_corrupt", len(paths))), "skipped_paths": paths}


def snapshot_of(notes: List[Dict[str, Any]], skips: Optional[Dict[str, Any]] = None) -> Dict[str, Any]:
    """Content-addressed manifest of the notes in scope. Each note is hashed whole (every field a
    stage could read: category, content, lifecycle, dates, ...), never copied. The set of skipped
    files is part of the snapshot too: a run that could read more or fewer files is another job."""
    entries = []
    for note in notes:
        if note.get("lifecycle") not in ELIGIBLE_LIFECYCLES:
            continue
        entries.append({"id": str(note.get("id")), "note_sha256": _sha256(_canonical(note))})
    entries.sort(key=lambda e: e["id"])
    skipped = (skips or {}).get("skipped_paths", [])
    return {"note_count": len(entries), "skipped_count": len(skipped),
            "sha256": _sha256(_canonical({"notes": entries, "skipped": skipped}))}


PACKAGES_ROOT = Path(__file__).resolve().parents[1]
#: Source that decides what a stage measures; any change makes a different job. Whole packages on
#: the read path (search, storage, authorization, disclosure, lifecycle, graph, trace) are hashed,
#: not hand-picked modules: a hand-picked list missed `security/verified_reduction.py` and
#: `sanitize_query` (review round 2). A listed source that is missing fails the job (no silent skip).
FINGERPRINTED_SOURCES = (
    "active_knowledge_engine",
    "memory",
    "retrieval",
    "security",
    "lifecycle",
    "graph",
    "observability",
    "learning/sleep_consolidation.py",
    "interfaces/vault_cli.py",
    "interfaces/memory_access.py",
)
#: Controller settings that change ranking or the candidate pool.
CONTROLLER_SETTINGS = ("ranking_arm", "classifier_filter_arm", "enable_graph_expansion",
                       "strict_graph_expansion", "graph_expansion_budget", "enable_cognitive_core")


def fingerprinted_files() -> List[Path]:
    files: List[Path] = []
    for rel in FINGERPRINTED_SOURCES:
        path = PACKAGES_ROOT / rel
        if path.is_dir():
            files.extend(p for p in sorted(path.rglob("*.py"))
                         if "__pycache__" not in p.parts and "tests" not in p.parts)
        elif path.is_file():
            files.append(path)
        else:
            raise FileNotFoundError(f"fingerprinted source missing: {rel}")
    return files


def code_fingerprint(controller) -> Dict[str, Any]:
    """SHA-256 over the read-path packages plus the controller's own source file."""
    files = fingerprinted_files()
    try:
        controller_src = inspect.getsourcefile(type(controller))
    except TypeError:
        controller_src = None
    if controller_src and Path(controller_src).is_file():
        files.append(Path(controller_src))
    digest = hashlib.sha256()
    for path in files:
        rel = path.relative_to(PACKAGES_ROOT).as_posix() if PACKAGES_ROOT in path.parents else path.name
        digest.update(rel.encode("utf-8") + bytes([0]))
        digest.update(path.read_bytes().replace(bytes([13, 10]), bytes([10])))
    return {"sha256": digest.hexdigest(), "files": len(files),
            "controller_class": f"{type(controller).__module__}.{type(controller).__qualname__}"}


def controller_settings(controller) -> Dict[str, str]:
    return {name: repr(getattr(controller, name, None)) for name in CONTROLLER_SETTINGS}


def _free_name(directory: Path, stem: str, label: str) -> Path:
    n = 1
    while (directory / f"{stem}.{label}{n}.json").exists():
        n += 1
    return directory / f"{stem}.{label}{n}.json"


def _error(exc: BaseException) -> str:
    """Exception type and a digest of the message: a message can quote note text."""
    return f"{type(exc).__name__} (message sha256 {_sha256(str(exc).encode('utf-8'))[:16]})"


class AKEShadowRunner:
    def __init__(self, controller, *, output_dir, vault_root, dormant_days: int = 60,
                 stale_review_days: int = 14, max_items: Optional[int] = None,
                 benchmark_path=None, k: int = AGENT_PAGE_SIZE):
        self.controller = controller
        self.vault_root = Path(vault_root).resolve()
        self.output_dir = Path(output_dir).resolve()
        self._check_output_location()
        self.dormant_days = dormant_days
        self.stale_review_days = stale_review_days
        self.max_items = max_items
        self.benchmark_path = Path(benchmark_path).resolve() if benchmark_path else None
        self.k = k

    def _check_output_location(self) -> None:
        try:
            rel = self.output_dir.relative_to(self.vault_root)
        except ValueError:
            return  # outside the vault entirely
        if not rel.parts or rel.parts[0] != OBSERVABILITY_DIR:
            raise OutputLocationError(
                f"AKE records must stay under {OBSERVABILITY_DIR}/ inside the vault, not {rel.as_posix() or '.'}"
            )

    def _config(self, as_of: str) -> Dict[str, Any]:
        return {
            "schema_version": SCHEMA_VERSION,
            "stages": list(STAGES) + (["retrieval_quality"] if self.benchmark_path else []),
            "dormant_days": self.dormant_days,
            "stale_review_days": self.stale_review_days,
            "max_items": self.max_items,
            "as_of_utc_date": as_of,
            "benchmark": self.benchmark_path.name if self.benchmark_path else None,
            "benchmark_sha256": self._benchmark_digest(),
            "benchmark_sidecar_sha256": self._sidecar_digest(),
            "k": self.k,
            "code_fingerprint": code_fingerprint(self.controller),
            "controller": controller_settings(self.controller),
        }

    def _sidecar_digest(self) -> Optional[str]:
        if not self.benchmark_path:
            return None
        sidecar = self.benchmark_path.with_suffix(self.benchmark_path.suffix + ".sha256")
        try:
            return _sha256(sidecar.read_bytes().strip())
        except OSError:
            return None

    def _benchmark_digest(self) -> Optional[str]:
        if not self.benchmark_path:
            return None
        try:
            return canonical_digest(self.benchmark_path)
        except OSError:
            return None

    def run(self, *, trigger: str = "manual") -> Dict[str, Any]:
        if trigger not in TRIGGERS:
            raise ValueError(f"unknown trigger {trigger!r}; allowed: {sorted(TRIGGERS)}")
        now = datetime.now(timezone.utc)
        as_of = now.date().isoformat()
        notes = collect_notes(self.controller)  # reading may add read-time skips, so skips come after
        skips = storage_skips(self.controller)
        snapshot = snapshot_of(notes, skips)
        config = self._config(as_of)
        config_sha = _sha256(_canonical(config))
        key = _sha256(_canonical({"trigger": trigger, "snapshot": snapshot["sha256"], "config": config_sha}))
        job_id = f"ake-{key[:24]}"
        path = self.output_dir / f"{job_id}.json"

        previous_attempts = 0
        if path.is_file():
            previous = self._load_valid(path, key)
            if previous is not None and previous["status"] in REUSABLE:
                return {**previous, "reused_existing_record": True}
            if previous is None:  # unreadable or edited (checksum mismatch): kept aside, never trusted.
                # record_sha256 is a checksum, not a signature: it catches corruption and careless
                # edits, not a writer who recomputes it. The record carries no authority.
                os.replace(path, _free_name(self.output_dir, job_id, "invalid"))
            else:
                os.replace(path, _free_name(self.output_dir, job_id, "attempt"))
        previous_attempts = (sum(1 for _ in self.output_dir.glob(f"{job_id}.attempt*.json"))
                             + sum(1 for _ in self.output_dir.glob(f"{job_id}.invalid*.json")))

        stages = self._run_stages()
        after_notes = collect_notes(self.controller)
        after = snapshot_of(after_notes, storage_skips(self.controller))
        if after["sha256"] != snapshot["sha256"]:
            status = JobStatus.STALE_SNAPSHOT
        elif not all(s["status"] == "COMPLETED" for s in stages):
            status = JobStatus.FAILED
        elif skips["skipped_count"]:
            status = JobStatus.COMPLETED_WITH_SKIPS
        else:
            status = JobStatus.COMPLETED
        attempt = previous_attempts + 1

        record = {
            "schema_version": SCHEMA_VERSION,
            "job_id": job_id,
            "idempotency_key": key,
            "attempt": attempt,
            "trigger": trigger,
            "mode": "SHADOW_READ_ONLY",
            "as_of_utc_date": as_of,
            "created_at_utc": now.isoformat(),
            "source_snapshot_sha256": snapshot["sha256"],
            "snapshot": snapshot,
            "snapshot_after_sha256": after["sha256"],
            "agent_config_sha256": config_sha,
            "config": config,
            "budget": {"max_items": self.max_items},
            "storage": skips,
            "stages": stages,
            "status": status.value,
            "canonical_writes": 0,
            "proposals_created": 0,
            "limitations": list(LIMITATIONS),
        }
        record["record_sha256"] = _sha256(_canonical(record))
        self._write(path, record)
        return {**record, "reused_existing_record": False}

    @staticmethod
    def _load_valid(path: Path, key: str) -> Optional[Dict[str, Any]]:
        """The record at `path` if it is one this runner wrote for this job, else None."""
        try:
            data = json.loads(path.read_text(encoding="utf-8"))
        except (OSError, ValueError):
            return None
        if not isinstance(data, dict) or data.get("idempotency_key") != key:
            return None
        body = {k: v for k, v in data.items() if k != "record_sha256"}
        if data.get("record_sha256") != _sha256(_canonical(body)):
            return None
        return data

    def _run_stages(self) -> List[Dict[str, Any]]:
        stages: List[Dict[str, Any]] = []
        integrity: Dict[str, Any] = {"name": "integrity", "component": "learning/sleep_consolidation.py"}
        try:
            report = SleepConsolidator(
                self.controller, dormant_days=self.dormant_days,
                stale_review_days=self.stale_review_days, max_items_per_run=self.max_items,
            ).run().to_dict()
            integrity.update(status="COMPLETED", output=report)
        except Exception as exc:  # recorded, never swallowed into a COMPLETED job
            integrity.update(status="FAILED", error=_error(exc))
        stages.append(integrity)

        review: Dict[str, Any] = {"name": "conflict_review", "component": "lifecycle/conflict_review.py"}
        if integrity["status"] != "COMPLETED":
            review.update(status="SKIPPED", reason="integrity stage did not complete")
        else:
            try:
                workflow = ConflictReviewWorkflow()
                cases = []
                for pair in integrity["output"].get("conflict_pairs", []):
                    case = workflow.open_case(
                        memory_ids=(pair["note_a"], pair["note_b"]),
                        reasons=(f"{pair['severity']}: token overlap {pair['overlap']}",),
                        conflict_type=str(pair["severity"]),
                    )
                    cases.append(case.as_dict())
                review.update(status="COMPLETED", output={"cases": cases, "case_count": len(cases)})
            except Exception as exc:
                review.update(status="FAILED", error=_error(exc))
        stages.append(review)

        readability: Dict[str, Any] = {"name": "agent_readability", "component": "interfaces/vault_cli.py::eligible_for_agent"}
        try:
            readability.update(status="COMPLETED", output=agent_readability(self.controller))
        except Exception as exc:
            readability.update(status="FAILED", error=_error(exc))
        stages.append(readability)

        if self.benchmark_path:
            quality: Dict[str, Any] = {"name": "retrieval_quality", "component": "memory/controller.py::search (AI_AGENT)"}
            try:
                bench = load_frozen_benchmark(self.benchmark_path)
                result = evaluate(self.controller, bench["cases"], k=self.k)
                quality.update(status="COMPLETED", output={
                    "benchmark": {"name": bench["name"], "file": bench["path"], "digest": bench["digest"]},
                    **result,
                })
            except Exception as exc:
                quality.update(status="FAILED", error=_error(exc))
            stages.append(quality)
        return stages

    def _write(self, path: Path, record: Dict[str, Any]) -> None:
        self.output_dir.mkdir(parents=True, exist_ok=True)
        tmp = path.with_suffix(".json.tmp")
        tmp.write_text(json.dumps(record, ensure_ascii=False, indent=2, sort_keys=True, default=str), encoding="utf-8")
        os.replace(tmp, path)
