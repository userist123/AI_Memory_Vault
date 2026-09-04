"""Memory Ablation Benchmark (TASK: MEMORY_ABLATION_01).

Implements a reproducible, paired control vs. treatment ablation experiment:
  - CONTROL: agent executes with NO retrieved memory (empty context).
  - TREATMENT: agent executes with memory retrieved via MemoryController.search().
  - MODEL: Real model (Ollama qwen2.5-coder:3b) under identical parameters.
  - REPRODUCIBILITY: Fixed 20-task benchmark suite, randomized alternating order,
    fresh isolated workspace per trial, persistent structured traces with experiment block.
"""
from __future__ import annotations

import hashlib
import json
import os
import shutil
import time
import uuid
from dataclasses import asdict, dataclass, field
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Callable, Dict, List, Optional, Tuple, Union

from cognitive_core.real_execution_harness import (
    AgentModelExecutor,
    AgentTask,
    RealAgentExecutionHarness,
)
from cognitive_core.orchestrator import AgentRole


@dataclass
class AblationTask:
    """Benchmark task definition for ablation experimentation."""
    task_id: str
    name: str
    category: str
    description: str
    target_file: str
    test_file: str
    test_code: str
    instructions: str
    memory_query: str


@dataclass
class TrialResult:
    """Outcome of an individual trial run."""
    task_id: str
    condition: str          # 'control' | 'treatment'
    trial_id: str
    order: int              # 1 or 2
    success: bool
    verification_passed: bool
    verification_exit_code: int
    execution_time_ms: float
    model_latency_ms: float
    tool_execution_time_ms: float
    retrieval_count: int
    memory_ids: List[str]
    commands_count: int
    files_changed: int
    failure_type: Optional[str]
    trace_id: str

    def to_dict(self) -> Dict[str, Any]:
        return asdict(self)


@dataclass
class PairedTaskResult:
    """Paired comparison for a single task."""
    task_id: str
    category: str
    control: TrialResult
    treatment: TrialResult
    delta: int              # +1 (treatment won), -1 (control won), 0 (tie)

    def to_dict(self) -> Dict[str, Any]:
        return {
            "task_id": self.task_id,
            "category": self.category,
            "control": self.control.to_dict(),
            "treatment": self.treatment.to_dict(),
            "delta": self.delta,
        }


@dataclass
class BenchmarkSummary:
    """Aggregated statistical outcome of the ablation experiment."""
    experiment_id: str
    benchmark_version: str
    benchmark_hash: str
    git_commit_sha: str
    provider: str
    model: str
    task_count: int
    control_trials: int
    control_successes: int
    control_failures: int
    control_success_rate: float
    treatment_trials: int
    treatment_successes: int
    treatment_failures: int
    treatment_success_rate: float
    absolute_delta: float
    relative_delta: float
    paired_counts: Dict[str, int]
    mean_control_latency_ms: float
    mean_treatment_latency_ms: float
    mean_control_execution_ms: float
    mean_treatment_execution_ms: float
    total_retrievals: int
    mean_retrievals_per_treatment: float
    failure_breakdown_control: Dict[str, int]
    failure_breakdown_treatment: Dict[str, int]
    conclusion_status: str

    def to_dict(self) -> Dict[str, Any]:
        return asdict(self)


def classify_failure(trace: Dict[str, Any], result: Dict[str, Any]) -> str:
    """Classifies a trial failure into the canonical taxonomy."""
    model_rec = trace.get("model", {})
    v_rec = trace.get("verification", {})
    status = model_rec.get("response_status")

    if status == "failed":
        err = str(model_rec.get("error_details", "")).lower()
        if "timeout" in err:
            return "TIMEOUT"
        return "PROVIDER_FAILURE"

    actions = trace.get("actions", [])
    if not actions:
        return "MODEL_OUTPUT_INVALID"

    for act in actions:
        if not act.get("validated", False):
            return "ACTION_UNAUTHORIZED"

    v_status = v_rec.get("status")
    v_exit = v_rec.get("exit_code")
    v_stdout = v_rec.get("stdout", "")
    v_stderr = v_rec.get("stderr", "")

    if v_status == "passed" and v_exit == 0:
        return "NONE"

    combined_out = f"{v_stdout}\n{v_stderr}"
    if "AssertionError" in combined_out or "FAILED" in combined_out:
        return "TEST_ASSERTION_FAILURE"
    if "SyntaxError" in combined_out or "ImportError" in combined_out or "NameError" in combined_out:
        return "TOOL_EXECUTION_FAILURE"
    if v_exit != 0:
        return "VERIFICATION_FAILURE"

    return "OTHER"

def get_ablation_benchmark_tasks() -> List[AblationTask]:
    """Returns the canonical fixed 20-task benchmark suite."""
    tasks: List[AblationTask] = []

    # 1. Circuit Breaker
    tasks.append(AblationTask(
        task_id="task_ablation_001_circuit_breaker",
        name="Circuit Breaker State Machine",
        category="resilience",
        description="Implement a 3-state CircuitBreaker (CLOSED, OPEN, HALF_OPEN) with threshold recovery.",
        target_file="circuit_breaker.py",
        test_file="test_circuit_breaker.py",
        instructions=(
            "Write CircuitBreaker in circuit_breaker.py with states 'CLOSED', 'OPEN', 'HALF_OPEN'. "
            "Constructor __init__(self, failure_threshold: int = 3, recovery_threshold: int = 2). "
            "Methods: state property returning string, can_execute() -> bool, record_success() -> None, record_failure() -> None. "
            "In CLOSED: failure count increments on record_failure(); transitions to OPEN if failures >= failure_threshold. "
            "In OPEN: can_execute() is False. (Transition to HALF_OPEN can occur manually via set_half_open() or state setter). "
            "In HALF_OPEN: 1 failure immediately reverts to OPEN; recovery_threshold consecutive successes transition back to CLOSED."
        ),
        memory_query="circuit breaker pattern states CLOSED OPEN HALF_OPEN failure recovery threshold",
        test_code=(
            "from circuit_breaker import CircuitBreaker\n\n"
            "def test_initial_state_closed():\n"
            "    cb = CircuitBreaker(failure_threshold=3, recovery_threshold=2)\n"
            "    assert cb.state == 'CLOSED'\n"
            "    assert cb.can_execute() is True\n\n"
            "def test_opens_after_threshold_failures():\n"
            "    cb = CircuitBreaker(failure_threshold=3, recovery_threshold=2)\n"
            "    cb.record_failure()\n"
            "    cb.record_failure()\n"
            "    assert cb.state == 'CLOSED'\n"
            "    cb.record_failure()\n"
            "    assert cb.state == 'OPEN'\n"
            "    assert cb.can_execute() is False\n\n"
            "def test_half_open_recovery_and_fallback():\n"
            "    cb = CircuitBreaker(failure_threshold=2, recovery_threshold=2)\n"
            "    cb.record_failure()\n"
            "    cb.record_failure()\n"
            "    assert cb.state == 'OPEN'\n"
            "    if hasattr(cb, 'set_half_open'):\n"
            "        cb.set_half_open()\n"
            "    else:\n"
            "        cb.state = 'HALF_OPEN'\n"
            "    assert cb.state == 'HALF_OPEN'\n"
            "    assert cb.can_execute() is True\n"
            "    cb.record_success()\n"
            "    assert cb.state == 'HALF_OPEN'\n"
            "    cb.record_success()\n"
            "    assert cb.state == 'CLOSED'\n"
        ),
    ))

    # 2. Exponential Backoff
    tasks.append(AblationTask(
        task_id="task_ablation_002_exponential_backoff",
        name="Exponential Backoff Calculator",
        category="resilience",
        description="Compute bounded exponential backoff delay with factor and maximum cap.",
        target_file="backoff.py",
        test_file="test_backoff.py",
        instructions=(
            "Write compute_backoff(attempt: int, base_delay: float = 1.0, max_delay: float = 32.0, factor: float = 2.0) -> float "
            "in backoff.py. Formula is min(max_delay, base_delay * (factor ** attempt)). "
            "If attempt < 0, raise ValueError."
        ),
        memory_query="exponential backoff retry attempt factor max delay formula",
        test_code=(
            "import pytest\n"
            "from backoff import compute_backoff\n\n"
            "def test_backoff_progression():\n"
            "    assert compute_backoff(0, base_delay=1.0, max_delay=32.0, factor=2.0) == 1.0\n"
            "    assert compute_backoff(1, base_delay=1.0, max_delay=32.0, factor=2.0) == 2.0\n"
            "    assert compute_backoff(2, base_delay=1.0, max_delay=32.0, factor=2.0) == 4.0\n"
            "    assert compute_backoff(3, base_delay=1.0, max_delay=32.0, factor=2.0) == 8.0\n\n"
            "def test_backoff_max_cap():\n"
            "    assert compute_backoff(10, base_delay=1.0, max_delay=32.0, factor=2.0) == 32.0\n\n"
            "def test_invalid_attempt():\n"
            "    with pytest.raises(ValueError):\n"
            "        compute_backoff(-1)\n"
        ),
    ))

    # 3. Token Bucket
    tasks.append(AblationTask(
        task_id="task_ablation_003_token_bucket",
        name="Token Bucket Rate Limiter",
        category="resilience",
        description="Implement a TokenBucket rate limiter with capacity and continuous refill.",
        target_file="rate_limiter.py",
        test_file="test_rate_limiter.py",
        instructions=(
            "Write TokenBucket in rate_limiter.py with __init__(self, capacity: float, refill_rate: float, now: float = 0.0). "
            "refill_rate is tokens per second. "
            "Method consume(tokens: float = 1.0, now: float = None) -> bool. "
            "Refills elapsed * refill_rate capped at capacity before attempting consumption. "
            "Returns True and deducts tokens if available; returns False without deduction otherwise."
        ),
        memory_query="token bucket rate limiter capacity refill rate consumption elapsed time",
        test_code=(
            "from rate_limiter import TokenBucket\n\n"
            "def test_initial_consumption():\n"
            "    tb = TokenBucket(capacity=5.0, refill_rate=1.0, now=100.0)\n"
            "    assert tb.consume(3.0, now=100.0) is True\n"
            "    assert tb.consume(2.0, now=100.0) is True\n"
            "    assert tb.consume(1.0, now=100.0) is False\n\n"
            "def test_refill_over_time():\n"
            "    tb = TokenBucket(capacity=5.0, refill_rate=2.0, now=100.0)\n"
            "    assert tb.consume(5.0, now=100.0) is True\n"
            "    assert tb.consume(1.0, now=100.0) is False\n"
            "    assert tb.consume(2.0, now=101.0) is True\n"
        ),
    ))

    # 4. Sliding Window Rate Limiter
    tasks.append(AblationTask(
        task_id="task_ablation_004_sliding_window_log",
        name="Sliding Window Rate Limiter",
        category="resilience",
        description="Implement a SlidingWindowLimiter using timestamps log with pruning.",
        target_file="sliding_limiter.py",
        test_file="test_sliding_limiter.py",
        instructions=(
            "Write SlidingWindowLimiter in sliding_limiter.py with __init__(self, window_seconds: float, max_requests: int). "
            "Method allow_request(now: float) -> bool. "
            "Prunes requests older than (now - window_seconds). "
            "If remaining count < max_requests, records now and returns True; otherwise returns False."
        ),
        memory_query="sliding window rate limiter log timestamp prune max requests",
        test_code=(
            "from sliding_limiter import SlidingWindowLimiter\n\n"
            "def test_sliding_window_limit():\n"
            "    limiter = SlidingWindowLimiter(window_seconds=10.0, max_requests=2)\n"
            "    assert limiter.allow_request(100.0) is True\n"
            "    assert limiter.allow_request(105.0) is True\n"
            "    assert limiter.allow_request(108.0) is False\n"
            "    assert limiter.allow_request(111.0) is True\n"
        ),
    ))

    # 5. Bulkhead Limiter
    tasks.append(AblationTask(
        task_id="task_ablation_005_bulkhead_limiter",
        name="Bulkhead Concurrency Limiter",
        category="resilience",
        description="Implement BulkheadLimiter with active slot tracking and queue limit.",
        target_file="bulkhead.py",
        test_file="test_bulkhead.py",
        instructions=(
            "Write BulkheadLimiter in bulkhead.py with __init__(self, max_concurrent: int, max_queue: int = 0). "
            "Methods: acquire() -> bool, release() -> None, active_count: int property. "
            "If active_count < max_concurrent, increments active_count and returns True. "
            "If active_count >= max_concurrent and max_queue == 0, returns False. "
            "release() decrements active_count down to minimum 0."
        ),
        memory_query="bulkhead concurrency isolation limit active count release",
        test_code=(
            "from bulkhead import BulkheadLimiter\n\n"
            "def test_bulkhead_concurrency_slots():\n"
            "    bh = BulkheadLimiter(max_concurrent=2)\n"
            "    assert bh.acquire() is True\n"
            "    assert bh.acquire() is True\n"
            "    assert bh.acquire() is False\n"
            "    assert bh.active_count == 2\n"
            "    bh.release()\n"
            "    assert bh.active_count == 1\n"
            "    assert bh.acquire() is True\n"
            "    bh.release()\n"
            "    bh.release()\n"
            "    assert bh.active_count == 0\n"
        ),
    ))

    # 6. LRU Cache
    tasks.append(AblationTask(
        task_id="task_ablation_006_lru_cache",
        name="LRU Cache with Eviction Callback",
        category="caching_storage",
        description="Implement an LRU Cache with capacity bounding and optional on_evict hook.",
        target_file="lru_cache.py",
        test_file="test_lru_cache.py",
        instructions=(
            "Write LRUCache in lru_cache.py with __init__(self, capacity: int, on_evict=None). "
            "Methods: get(key, default=None), put(key, value), __len__() -> int. "
            "When capacity is exceeded upon put, the least recently accessed item is evicted, "
            "and on_evict(key, value) is invoked if provided."
        ),
        memory_query="lru cache least recently used eviction callback capacity ordered dict",
        test_code=(
            "from lru_cache import LRUCache\n\n"
            "def test_lru_eviction():\n"
            "    evicted = []\n"
            "    cache = LRUCache(capacity=2, on_evict=lambda k, v: evicted.append((k, v)))\n"
            "    cache.put('a', 1)\n"
            "    cache.put('b', 2)\n"
            "    assert cache.get('a') == 1\n"
            "    cache.put('c', 3)\n"
            "    assert cache.get('b') is None\n"
            "    assert evicted == [('b', 2)]\n"
            "    assert len(cache) == 2\n"
        ),
    ))

    # 7. TTL Cache
    tasks.append(AblationTask(
        task_id="task_ablation_007_ttl_cache",
        name="TTL Cache with Lazy Expiration",
        category="caching_storage",
        description="Implement TTLCache with key expiration and prune_expired method.",
        target_file="ttl_cache.py",
        test_file="test_ttl_cache.py",
        instructions=(
            "Write TTLCache in ttl_cache.py with __init__(self, default_ttl: float = 60.0). "
            "Methods: set(key, value, ttl: float = None, now: float = 0.0), "
            "get(key, default=None, now: float = 0.0), prune_expired(now: float) -> int. "
            "get returns default if expired. prune_expired removes all expired keys and returns count removed."
        ),
        memory_query="ttl cache expiration lazy eviction time to live timestamp",
        test_code=(
            "from ttl_cache import TTLCache\n\n"
            "def test_ttl_expiry():\n"
            "    cache = TTLCache(default_ttl=10.0)\n"
            "    cache.set('k1', 'v1', now=100.0)\n"
            "    cache.set('k2', 'v2', ttl=5.0, now=100.0)\n"
            "    assert cache.get('k1', now=108.0) == 'v1'\n"
            "    assert cache.get('k2', now=108.0) is None\n"
            "    removed = cache.prune_expired(now=115.0)\n"
            "    assert removed == 2\n"
        ),
    ))

    # 8. Atomic Write
    tasks.append(AblationTask(
        task_id="task_ablation_008_atomic_write",
        name="Atomic File Commit Protocol",
        category="caching_storage",
        description="Implement atomic_write writing to temp file and renaming via os.replace.",
        target_file="atomic_storage.py",
        test_file="test_atomic_storage.py",
        instructions=(
            "Write atomic_write(filepath: str, content: str, sync: bool = True) -> None in atomic_storage.py. "
            "Creates a temporary file in the same directory, writes content, calls flush() and os.fsync(fd) if sync=True, "
            "then replaces target file with os.replace."
        ),
        memory_query="atomic write temporary file os replace fsync crash consistency",
        test_code=(
            "import os\n"
            "from pathlib import Path\n"
            "from atomic_storage import atomic_write\n\n"
            "def test_atomic_file_write(tmp_path: Path):\n"
            "    target = tmp_path / 'data.txt'\n"
            "    atomic_write(str(target), 'hello atomic world', sync=True)\n"
            "    assert target.read_text(encoding='utf-8') == 'hello atomic world'\n"
            "    atomic_write(str(target), 'updated atomic world', sync=True)\n"
            "    assert target.read_text(encoding='utf-8') == 'updated atomic world'\n"
        ),
    ))

    # 9. WAL Journal Appender
    tasks.append(AblationTask(
        task_id="task_ablation_009_wal_journal",
        name="Write-Ahead Log Appender and Replay",
        category="caching_storage",
        description="Implement WALJournal with checksum verification and recovery replay.",
        target_file="wal_journal.py",
        test_file="test_wal_journal.py",
        instructions=(
            "Write WALJournal in wal_journal.py with __init__(self, log_path: str). "
            "Method append(seq: int, payload: str) -> None writes line: seq|payload|sha256(f'{seq}:{payload}'). "
            "Method replay() -> list: reads records, validates SHA-256 hash, and returns valid (seq, payload) tuples. "
            "Corrupted lines are skipped."
        ),
        memory_query="write ahead log wal sequence number checksum replay recovery integrity",
        test_code=(
            "from pathlib import Path\n"
            "from wal_journal import WALJournal\n\n"
            "def test_wal_append_and_replay(tmp_path: Path):\n"
            "    log_file = tmp_path / 'wal.log'\n"
            "    wal = WALJournal(str(log_file))\n"
            "    wal.append(1, 'insert_order')\n"
            "    wal.append(2, 'charge_card')\n"
            "    replayed = wal.replay()\n"
            "    assert replayed == [(1, 'insert_order'), (2, 'charge_card')]\n"
        ),
    ))

    # 10. Ring Buffer
    tasks.append(AblationTask(
        task_id="task_ablation_010_ring_buffer",
        name="Fixed-Capacity Circular Ring Buffer",
        category="caching_storage",
        description="Implement fixed-capacity RingBuffer with FIFO pop and overwrite on push.",
        target_file="ring_buffer.py",
        test_file="test_ring_buffer.py",
        instructions=(
            "Write RingBuffer in ring_buffer.py with __init__(self, capacity: int). "
            "Methods: push(item: Any), pop() -> Any, is_full() -> bool, is_empty() -> bool, __len__() -> int. "
            "If buffer is full, push overwrites oldest item. pop removes and returns oldest item in FIFO order. "
            "pop on empty raises IndexError."
        ),
        memory_query="ring buffer circular queue fixed capacity overwrite fifo",
        test_code=(
            "import pytest\n"
            "from ring_buffer import RingBuffer\n\n"
            "def test_ring_buffer_fifo_and_overwrite():\n"
            "    rb = RingBuffer(capacity=3)\n"
            "    assert rb.is_empty() is True\n"
            "    rb.push(1)\n"
            "    rb.push(2)\n"
            "    rb.push(3)\n"
            "    assert rb.is_full() is True\n"
            "    rb.push(4)\n"
            "    assert len(rb) == 3\n"
            "    assert rb.pop() == 2\n"
            "    assert rb.pop() == 3\n"
            "    assert rb.pop() == 4\n"
            "    assert rb.is_empty() is True\n"
            "    with pytest.raises(IndexError):\n"
            "        rb.pop()\n"
        ),
    ))

    # 11. Role Authorizer
    tasks.append(AblationTask(
        task_id="task_ablation_011_role_authorizer",
        name="Role-Based Least Privilege Authorizer",
        category="security_policy",
        description="Implement RoleAuthorizer verifying role permissions with PermissionError.",
        target_file="authorizer.py",
        test_file="test_authorizer.py",
        instructions=(
            "Write RoleAuthorizer in authorizer.py with __init__(self, permissions_map: dict). "
            "Method check_permission(role: str, action: str) -> bool: returns True if action in role's permissions, "
            "otherwise raises PermissionError(f'Role {role} unauthorized for {action}'). "
            "Method is_authorized(role: str, action: str) -> bool returns bool without raising."
        ),
        memory_query="role based access control rbac least privilege authorizer permissions",
        test_code=(
            "import pytest\n"
            "from authorizer import RoleAuthorizer\n\n"
            "def test_role_authorization():\n"
            "    auth = RoleAuthorizer({\n"
            "        'admin': {'read', 'write', 'delete'},\n"
            "        'viewer': {'read'}\n"
            "    })\n"
            "    assert auth.is_authorized('admin', 'write') is True\n"
            "    assert auth.is_authorized('viewer', 'write') is False\n"
            "    assert auth.check_permission('admin', 'read') is True\n"
            "    with pytest.raises(PermissionError):\n"
            "        auth.check_permission('viewer', 'delete')\n"
        ),
    ))

    # 12. IP Guard
    tasks.append(AblationTask(
        task_id="task_ablation_012_ip_ban_guard",
        name="IP Rate Guard with Progressive Ban",
        category="security_policy",
        description="Implement IPGuard tracking failed attempts and banning malicious IPs.",
        target_file="ip_guard.py",
        test_file="test_ip_guard.py",
        instructions=(
            "Write IPGuard in ip_guard.py with __init__(self, max_failures: int = 3, ban_duration: float = 300.0). "
            "Methods: record_failure(ip: str, now: float) -> bool (returns True if IP is now banned, False otherwise), "
            "is_banned(ip: str, now: float) -> bool, reset(ip: str) -> None."
        ),
        memory_query="ip rate limiter ban threshold brute force protection security",
        test_code=(
            "from ip_guard import IPGuard\n\n"
            "def test_ip_guard_ban():\n"
            "    guard = IPGuard(max_failures=3, ban_duration=100.0)\n"
            "    assert guard.record_failure('1.1.1.1', now=10.0) is False\n"
            "    assert guard.record_failure('1.1.1.1', now=11.0) is False\n"
            "    assert guard.record_failure('1.1.1.1', now=12.0) is True\n"
            "    assert guard.is_banned('1.1.1.1', now=50.0) is True\n"
            "    assert guard.is_banned('1.1.1.1', now=120.0) is False\n"
        ),
    ))

    # 13. Constant Time Comparator
    tasks.append(AblationTask(
        task_id="task_ablation_013_constant_time_compare",
        name="Timing Attack-Resistant Comparator",
        category="security_policy",
        description="Implement constant_time_compare preventing timing leaks in HMAC validation.",
        target_file="crypto_utils.py",
        test_file="test_crypto_utils.py",
        instructions=(
            "Write constant_time_compare(val_a: str, val_b: str) -> bool in crypto_utils.py. "
            "Compares strings byte-by-byte using bitwise XOR accumulation without early return. "
            "If lengths differ, still iterates through full length of val_a and returns False."
        ),
        memory_query="constant time string comparison timing attack prevention hmac xor",
        test_code=(
            "from crypto_utils import constant_time_compare\n\n"
            "def test_constant_time_compare():\n"
            "    assert constant_time_compare('secret_token_123', 'secret_token_123') is True\n"
            "    assert constant_time_compare('secret_token_123', 'secret_token_124') is False\n"
            "    assert constant_time_compare('secret', 'secret_token_123') is False\n"
            "    assert constant_time_compare('', '') is True\n"
        ),
    ))

    # 14. Audit Hash Chain
    tasks.append(AblationTask(
        task_id="task_ablation_014_audit_hash_chain",
        name="Tamper-Evident Audit Hash Chain",
        category="security_policy",
        description="Implement AuditHashChain linking events with SHA-256 previous hash chain.",
        target_file="audit_chain.py",
        test_file="test_audit_chain.py",
        instructions=(
            "Write AuditHashChain in audit_chain.py with __init__(self, genesis_hash: str = '0' * 64). "
            "Method append(event: dict) -> str: computes sha256(f'{prev_hash}:{json.dumps(event, sort_keys=True)}').hexdigest(), "
            "records (event, hash), updates prev_hash, and returns new hash. "
            "Method verify() -> bool: recomputes chain from genesis_hash and returns True if valid."
        ),
        memory_query="tamper evident audit log hash chain sha256 cryptographic verification",
        test_code=(
            "from audit_chain import AuditHashChain\n\n"
            "def test_hash_chain_verification():\n"
            "    chain = AuditHashChain()\n"
            "    h1 = chain.append({'action': 'login', 'user': 'alice'})\n"
            "    h2 = chain.append({'action': 'read', 'doc': 'report'})\n"
            "    assert chain.verify() is True\n"
            "    chain.records[0] = ({'action': 'tampered', 'user': 'alice'}, h1)\n"
            "    assert chain.verify() is False\n"
        ),
    ))

    # 15. Secret Sanitizer
    tasks.append(AblationTask(
        task_id="task_ablation_015_secret_sanitizer",
        name="Recursive Secret Redaction Sanitizer",
        category="security_policy",
        description="Implement sanitize_secrets recursively redacting sensitive keys and tokens.",
        target_file="sanitizer.py",
        test_file="test_sanitizer.py",
        instructions=(
            "Write sanitize_secrets(data: Any, sensitive_keys: set = None, mask: str = '[REDACTED]') -> Any in sanitizer.py. "
            "Default sensitive_keys includes {'api_key', 'secret', 'password', 'token'}. "
            "Recursively traverses dicts and lists: replaces values of sensitive keys with mask. "
            "Returns sanitized copy without mutating original."
        ),
        memory_query="secret redaction sanitizer credentials recursive mask privacy",
        test_code=(
            "from sanitizer import sanitize_secrets\n\n"
            "def test_secret_sanitization():\n"
            "    payload = {\n"
            "        'username': 'admin',\n"
            "        'password': 'supersecretpassword',\n"
            "        'nested': {\n"
            "            'api_key': 'sk-123456789',\n"
            "            'active': True\n"
            "        },\n"
            "        'tags': ['token', 'safe']\n"
            "    }\n"
            "    cleaned = sanitize_secrets(payload)\n"
            "    assert cleaned['password'] == '[REDACTED]'\n"
            "    assert cleaned['nested']['api_key'] == '[REDACTED]'\n"
            "    assert cleaned['nested']['active'] is True\n"
            "    assert payload['password'] == 'supersecretpassword'\n"
        ),
    ))

    # 16. Event Bus
    tasks.append(AblationTask(
        task_id="task_ablation_016_event_bus",
        name="Event Bus with Prefix Routing",
        category="coordination_consensus",
        description="Implement EventBus with publish/subscribe and exact/wildcard matching.",
        target_file="event_bus.py",
        test_file="test_event_bus.py",
        instructions=(
            "Write EventBus in event_bus.py. "
            "Methods: subscribe(topic: str, handler: Callable), unsubscribe(topic: str, handler: Callable), "
            "publish(topic: str, event: Any) -> int (returns number of handlers executed). "
            "Supports exact topic matching and '*' wildcard (matches any topic)."
        ),
        memory_query="event bus publish subscribe topic wildcard routing patterns",
        test_code=(
            "from event_bus import EventBus\n\n"
            "def test_event_bus_routing():\n"
            "    bus = EventBus()\n"
            "    logs = []\n"
            "    h1 = lambda e: logs.append(('order', e))\n"
            "    h2 = lambda e: logs.append(('all', e))\n"
            "    bus.subscribe('order.created', h1)\n"
            "    bus.subscribe('*', h2)\n"
            "    assert bus.publish('order.created', {'id': 1}) == 2\n"
            "    assert logs == [('order', {'id': 1}), ('all', {'id': 1})]\n"
            "    bus.unsubscribe('order.created', h1)\n"
            "    assert bus.publish('order.created', {'id': 2}) == 1\n"
        ),
    ))

    # 17. Two-Phase Commit Coordinator
    tasks.append(AblationTask(
        task_id="task_ablation_017_two_phase_commit",
        name="Two-Phase Commit Coordinator",
        category="coordination_consensus",
        description="Implement TwoPhaseCoordinator orchestrating prepare and commit/abort.",
        target_file="two_phase_commit.py",
        test_file="test_two_phase_commit.py",
        instructions=(
            "Write TwoPhaseCoordinator in two_phase_commit.py with __init__(self, participants: list). "
            "Each participant has prepare(txn_id) -> bool, commit(txn_id) -> None, rollback(txn_id) -> None. "
            "Method execute(txn_id: str) -> bool: Phase 1 sends prepare(txn_id) to all. "
            "If all return True, Phase 2 calls commit(txn_id) on all and returns True. "
            "If any returns False, calls rollback(txn_id) on all and returns False."
        ),
        memory_query="two phase commit 2pc coordinator prepare commit abort protocol",
        test_code=(
            "from two_phase_commit import TwoPhaseCoordinator\n\n"
            "class MockParticipant:\n"
            "    def __init__(self, can_commit=True):\n"
            "        self.can_commit = can_commit\n"
            "        self.committed = False\n"
            "        self.rolled_back = False\n"
            "    def prepare(self, txn_id):\n"
            "        return self.can_commit\n"
            "    def commit(self, txn_id):\n"
            "        self.committed = True\n"
            "    def rollback(self, txn_id):\n"
            "        self.rolled_back = True\n\n"
            "def test_2pc_success_and_rollback():\n"
            "    p1 = MockParticipant(True)\n"
            "    p2 = MockParticipant(True)\n"
            "    coord = TwoPhaseCoordinator([p1, p2])\n"
            "    assert coord.execute('tx1') is True\n"
            "    assert p1.committed and p2.committed\n\n"
            "    p3 = MockParticipant(True)\n"
            "    p4 = MockParticipant(False)\n"
            "    coord2 = TwoPhaseCoordinator([p3, p4])\n"
            "    assert coord2.execute('tx2') is False\n"
            "    assert p3.rolled_back and p4.rolled_back\n"
        ),
    ))

    # 18. Saga Orchestrator
    tasks.append(AblationTask(
        task_id="task_ablation_018_saga_orchestrator",
        name="Compensating Transaction Saga Orchestrator",
        category="coordination_consensus",
        description="Implement SagaOrchestrator rolling back completed steps on failure.",
        target_file="saga.py",
        test_file="test_saga.py",
        instructions=(
            "Write SagaOrchestrator in saga.py with method add_step(name: str, action: Callable, compensate: Callable). "
            "Method execute() -> bool: runs actions sequentially. If step i raises an Exception or returns False, "
            "it executes compensations for steps i-1 down to 0 in reverse order, then returns False. "
            "If all succeed, returns True."
        ),
        memory_query="saga pattern orchestrator compensating transactions rollback reverse",
        test_code=(
            "from saga import SagaOrchestrator\n\n"
            "def test_saga_rollback_on_failure():\n"
            "    saga = SagaOrchestrator()\n"
            "    actions = []\n"
            "    compensations = []\n"
            "    saga.add_step('step1', lambda: actions.append(1), lambda: compensations.append(1))\n"
            "    saga.add_step('step2', lambda: actions.append(2), lambda: compensations.append(2))\n"
            "    saga.add_step('step3', lambda: False, lambda: compensations.append(3))\n"
            "    assert saga.execute() is False\n"
            "    assert actions == [1, 2]\n"
            "    assert compensations == [2, 1]\n"
        ),
    ))

    # 19. Vector Clock
    tasks.append(AblationTask(
        task_id="task_ablation_019_vector_clock",
        name="Distributed Vector Clock",
        category="coordination_consensus",
        description="Implement VectorClock with increment, merge, and partial ordering comparison.",
        target_file="vector_clock.py",
        test_file="test_vector_clock.py",
        instructions=(
            "Write VectorClock in vector_clock.py with __init__(self, clock: dict = None). "
            "Methods: increment(node_id: str), merge(other: VectorClock), "
            "compare(other: VectorClock) -> str ('BEFORE', 'AFTER', 'EQUAL', 'CONCURRENT'). "
            "Self is BEFORE other if all self <= other and at least one self < other."
        ),
        memory_query="vector clock causal ordering distributed systems concurrent partial order",
        test_code=(
            "from vector_clock import VectorClock\n\n"
            "def test_vector_clock_ordering():\n"
            "    vc1 = VectorClock({'A': 1, 'B': 0})\n"
            "    vc2 = VectorClock({'A': 2, 'B': 0})\n"
            "    assert vc1.compare(vc2) == 'BEFORE'\n"
            "    assert vc2.compare(vc1) == 'AFTER'\n\n"
            "    vc3 = VectorClock({'A': 1, 'B': 1})\n"
            "    assert vc2.compare(vc3) == 'CONCURRENT'\n"
        ),
    ))

    # 20. Leader Lease
    tasks.append(AblationTask(
        task_id="task_ablation_020_leader_lease",
        name="Lease-Based Leader Heartbeat",
        category="coordination_consensus",
        description="Implement LeaderLease tracking monotonic lease ownership and expiration.",
        target_file="lease.py",
        test_file="test_lease.py",
        instructions=(
            "Write LeaderLease in lease.py with __init__(self, lease_duration: float = 10.0). "
            "Methods: acquire_or_renew(node_id: str, now: float) -> bool (succeeds if no leader, "
            "lease expired, or same node; extends expiration to now + lease_duration), "
            "get_current_leader(now: float) -> Optional[str], release(node_id: str) -> bool."
        ),
        memory_query="distributed leader election lease heartbeat timeout renewal consensus",
        test_code=(
            "from lease import LeaderLease\n\n"
            "def test_leader_lease_lifecycle():\n"
            "    lease = LeaderLease(lease_duration=10.0)\n"
            "    assert lease.acquire_or_renew('node_1', now=100.0) is True\n"
            "    assert lease.get_current_leader(now=105.0) == 'node_1'\n"
            "    assert lease.acquire_or_renew('node_2', now=105.0) is False\n"
            "    assert lease.acquire_or_renew('node_2', now=111.0) is True\n"
            "    assert lease.get_current_leader(now=111.0) == 'node_2'\n"
        ),
    ))

    return tasks

def compute_benchmark_hash(tasks: List[AblationTask]) -> str:
    """Computes deterministic SHA-256 fingerprint over the benchmark task suite."""
    raw = [
        {
            "id": t.task_id,
            "name": t.name,
            "category": t.category,
            "target": t.target_file,
            "test": t.test_file,
            "test_code": t.test_code.strip(),
            "instructions": t.instructions.strip(),
            "query": t.memory_query.strip(),
        }
        for t in tasks
    ]
    encoded = json.dumps(raw, sort_keys=True, ensure_ascii=False).encode("utf-8")
    return hashlib.sha256(encoded).hexdigest()


class MemoryAblationExperimentRunner:
    """Orchestrates paired control vs. treatment ablation execution."""

    def __init__(
        self,
        harness: RealAgentExecutionHarness,
        model_executor: AgentModelExecutor,
        experiment_id: Optional[str] = None,
        base_workspace_dir: Optional[Union[str, Path]] = None,
        tasks: Optional[List[AblationTask]] = None,
    ) -> None:
        self.harness = harness
        self.model_executor = model_executor
        self.experiment_id = experiment_id or f"ablation_{datetime.now(timezone.utc).strftime('%Y%m%d_%H%M%S')}"
        self.tasks = tasks or get_ablation_benchmark_tasks()
        self.base_ws = Path(base_workspace_dir or "telemetry/ablation_workspaces").resolve()
        self.base_ws.mkdir(parents=True, exist_ok=True)
        self.benchmark_version = "1.0.0"
        self.benchmark_hash = compute_benchmark_hash(self.tasks)

    def run_trial(
        self,
        task: AblationTask,
        condition: str,
        order: int,
    ) -> TrialResult:
        """Executes a single trial in a fresh isolated workspace."""
        trial_id = f"trial_{uuid.uuid4().hex[:8]}"
        trial_ws = self.base_ws / f"{task.task_id}_{condition}_{trial_id}"
        trial_ws.mkdir(parents=True, exist_ok=True)

        # Pre-populate test file in workspace
        test_path = trial_ws / task.test_file
        test_path.write_text(task.test_code.strip() + "\n", encoding="utf-8")

        agent_task = AgentTask(
            task_id=f"{task.task_id}_{condition}",
            description=f"{task.name} ({condition.upper()})",
            target_file=task.target_file,
            test_file=task.test_file,
            instructions=(
                f"{task.instructions}\n"
                f"Write the implementation to {task.target_file}. "
                f"The test file {task.test_file} is already present in your workspace. "
                f"Return structured JSON actions writing {task.target_file}."
            ),
        )

        experiment_meta = {
            "experiment_id": self.experiment_id,
            "task_id": task.task_id,
            "condition": condition,
            "trial_id": trial_id,
            "order": order,
        }

        enable_memory = (condition == "treatment")
        t0 = time.perf_counter()

        result, trace_ref = self.harness.execute(
            task=agent_task,
            agent_id=f"ablation_agent_{condition}",
            agent_role="synthesizer",
            workspace=trial_ws,
            memory_query=task.memory_query if enable_memory else "",
            model_executor=self.model_executor,
            enable_memory=enable_memory,
            experiment=experiment_meta,
        )
        total_exec_ms = round((time.perf_counter() - t0) * 1000.0, 2)

        trace = trace_ref["record"]
        model_meta = trace.get("model", {})
        mem_meta = trace.get("memory", {})
        v_meta = trace.get("verification", {})
        ws_meta = trace.get("workspace", {})

        failure_type = classify_failure(trace, result)
        success = (result.get("status") == "success" and v_meta.get("status") == "passed")

        files_changed = len(ws_meta.get("files_created", [])) + len(ws_meta.get("files_modified", []))

        return TrialResult(
            task_id=task.task_id,
            condition=condition,
            trial_id=trial_id,
            order=order,
            success=success,
            verification_passed=(v_meta.get("status") == "passed"),
            verification_exit_code=int(v_meta.get("exit_code", 1)),
            execution_time_ms=total_exec_ms,
            model_latency_ms=float(model_meta.get("latency_ms", 0.0)),
            tool_execution_time_ms=max(0.0, total_exec_ms - float(model_meta.get("latency_ms", 0.0))),
            retrieval_count=int(mem_meta.get("retrieval_count", 0)),
            memory_ids=mem_meta.get("memory_ids", []),
            commands_count=len(trace.get("execution", {}).get("commands", [])),
            files_changed=files_changed,
            failure_type=None if success else failure_type,
            trace_id=trace.get("trace_id", ""),
        )

    def run_paired_task(self, index: int, task: AblationTask) -> PairedTaskResult:
        """Runs a paired task with alternating condition order."""
        if index % 2 == 0:
            ctrl = self.run_trial(task, condition="control", order=1)
            treat = self.run_trial(task, condition="treatment", order=2)
        else:
            treat = self.run_trial(task, condition="treatment", order=1)
            ctrl = self.run_trial(task, condition="control", order=2)

        if treat.success and not ctrl.success:
            delta = 1
        elif ctrl.success and not treat.success:
            delta = -1
        else:
            delta = 0

        return PairedTaskResult(
            task_id=task.task_id,
            category=task.category,
            control=ctrl,
            treatment=treat,
            delta=delta,
        )

    def run_benchmark(self) -> Tuple[BenchmarkSummary, List[PairedTaskResult]]:
        """Executes full benchmark suite across all paired tasks."""
        paired_results: List[PairedTaskResult] = []

        for idx, task in enumerate(self.tasks):
            paired = self.run_paired_task(idx, task)
            paired_results.append(paired)

        summary = self.aggregate_results(paired_results)
        return summary, paired_results

    def aggregate_results(self, paired_results: List[PairedTaskResult]) -> BenchmarkSummary:
        """Aggregates trial results into statistical benchmark summary."""
        total_tasks = len(paired_results)
        ctrl_successes = sum(1 for p in paired_results if p.control.success)
        treat_successes = sum(1 for p in paired_results if p.treatment.success)

        ctrl_rate = round(ctrl_successes / total_tasks, 4) if total_tasks else 0.0
        treat_rate = round(treat_successes / total_tasks, 4) if total_tasks else 0.0

        abs_delta = round(treat_rate - ctrl_rate, 4)
        rel_delta = round((abs_delta / ctrl_rate) * 100.0, 2) if ctrl_rate > 0 else 0.0

        paired_counts = {
            "both_success": sum(1 for p in paired_results if p.control.success and p.treatment.success),
            "treatment_win": sum(1 for p in paired_results if not p.control.success and p.treatment.success),
            "control_win": sum(1 for p in paired_results if p.control.success and not p.treatment.success),
            "both_failure": sum(1 for p in paired_results if not p.control.success and not p.treatment.success),
        }

        ctrl_latencies = [p.control.model_latency_ms for p in paired_results]
        treat_latencies = [p.treatment.model_latency_ms for p in paired_results]
        ctrl_exec_times = [p.control.execution_time_ms for p in paired_results]
        treat_exec_times = [p.treatment.execution_time_ms for p in paired_results]

        mean_ctrl_lat = round(sum(ctrl_latencies) / len(ctrl_latencies), 2) if ctrl_latencies else 0.0
        mean_treat_lat = round(sum(treat_latencies) / len(treat_latencies), 2) if treat_latencies else 0.0
        mean_ctrl_exec = round(sum(ctrl_exec_times) / len(ctrl_exec_times), 2) if ctrl_exec_times else 0.0
        mean_treat_exec = round(sum(treat_exec_times) / len(treat_exec_times), 2) if treat_exec_times else 0.0

        total_retrievals = sum(p.treatment.retrieval_count for p in paired_results)
        mean_retrievals = round(total_retrievals / total_tasks, 2) if total_tasks else 0.0

        fail_ctrl: Dict[str, int] = {}
        for p in paired_results:
            if not p.control.success and p.control.failure_type:
                fail_ctrl[p.control.failure_type] = fail_ctrl.get(p.control.failure_type, 0) + 1

        fail_treat: Dict[str, int] = {}
        for p in paired_results:
            if not p.treatment.success and p.treatment.failure_type:
                fail_treat[p.treatment.failure_type] = fail_treat.get(p.treatment.failure_type, 0) + 1

        if abs_delta > 0:
            conclusion = "MEMORY_HELPFUL_UNDER_TESTED_CONDITIONS"
        elif abs_delta < 0:
            conclusion = "MEMORY_DEGRADATION_OBSERVED"
        else:
            conclusion = "NO_MEASURABLE_MEMORY_EFFECT_DETECTED"

        return BenchmarkSummary(
            experiment_id=self.experiment_id,
            benchmark_version=self.benchmark_version,
            benchmark_hash=self.benchmark_hash,
            git_commit_sha=os.getenv("GIT_COMMIT_SHA", "unknown"),
            provider=self.model_executor.provider_mode,
            model=self.model_executor.model_name or "default",
            task_count=total_tasks,
            control_trials=total_tasks,
            control_successes=ctrl_successes,
            control_failures=total_tasks - ctrl_successes,
            control_success_rate=ctrl_rate,
            treatment_trials=total_tasks,
            treatment_successes=treat_successes,
            treatment_failures=total_tasks - treat_successes,
            treatment_success_rate=treat_rate,
            absolute_delta=abs_delta,
            relative_delta=rel_delta,
            paired_counts=paired_counts,
            mean_control_latency_ms=mean_ctrl_lat,
            mean_treatment_latency_ms=mean_treat_lat,
            mean_control_execution_ms=mean_ctrl_exec,
            mean_treatment_execution_ms=mean_treat_exec,
            total_retrievals=total_retrievals,
            mean_retrievals_per_treatment=mean_retrievals,
            failure_breakdown_control=fail_ctrl,
            failure_breakdown_treatment=fail_treat,
            conclusion_status=conclusion,
        )


def export_ablation_artifacts(
    summary: BenchmarkSummary,
    paired_results: List[PairedTaskResult],
    output_dir: Union[str, Path] = "07_EVALUATION",
    date_slug: str = "2026-09",
) -> Tuple[Path, Path]:
    """Exports raw JSON and analytical Markdown reports."""
    out_path = Path(output_dir)
    out_path.mkdir(parents=True, exist_ok=True)

    json_file = out_path / f"memory_ablation_{date_slug}.json"
    md_file = out_path / f"memory_ablation_{date_slug}.md"

    raw_data = {
        "summary": summary.to_dict(),
        "paired_results": [p.to_dict() for p in paired_results],
        "generated_at": datetime.now(timezone.utc).isoformat(),
    }

    with open(json_file, "w", encoding="utf-8") as f:
        json.dump(raw_data, f, indent=2)

    all_fail_keys = sorted(set(list(summary.failure_breakdown_control.keys()) + list(summary.failure_breakdown_treatment.keys())))
    fail_table_rows = ""
    if not all_fail_keys:
        fail_table_rows = "| *None (100% pass)* | 0 | 0 |\n"
    else:
        for k in all_fail_keys:
            c_cnt = summary.failure_breakdown_control.get(k, 0)
            t_cnt = summary.failure_breakdown_treatment.get(k, 0)
            fail_table_rows += f"| `{k}` | {c_cnt} | {t_cnt} |\n"

    paired_table_rows = ""
    for p in paired_results:
        c_status = "PASS" if p.control.success else "FAIL"
        t_status = "PASS" if p.treatment.success else "FAIL"
        delta_sign = f"{p.delta:+d}"
        paired_table_rows += (
            f"| `{p.task_id}` | {p.category} | {c_status} | {t_status} | {delta_sign} | "
            f"{p.control.model_latency_ms:.0f} ms | {p.treatment.model_latency_ms:.0f} ms | {p.treatment.retrieval_count} |\n"
        )

    conclusion_desc = "relevant retrieved memory improved task success" if summary.absolute_delta > 0 else "no measurable difference was observed between control and treatment conditions"

    md_content = f"""# Controlled Memory Ablation Benchmark Report ({date_slug})

## 1. Executive Summary

| Parameter | Value |
|---|---|
| **Experiment ID** | `{summary.experiment_id}` |
| **Benchmark Version** | `{summary.benchmark_version}` |
| **Benchmark Hash** | `{summary.benchmark_hash}` |
| **Provider** | `{summary.provider}` |
| **Model** | `{summary.model}` |
| **Git Commit SHA** | `{summary.git_commit_sha}` |
| **Task Count (Paired)** | `{summary.task_count}` (Total Trials: {summary.task_count * 2}) |
| **Conclusion Status** | `{summary.conclusion_status}` |

---

## 2. Experimental Methodology

The memory ablation experiment empirically measures whether providing retrieved memory context via `MemoryController.search()` improves agent task performance compared to an identical zero-retrieval control condition.

### Conditions
- **CONTROL Condition**:
  - Memory retrieval disabled (`enable_memory=False`).
  - Execution context contains empty memory list (`retrieved_memories: []`).
  - Trace records `memory_ids: []` and `retrieval_count: 0`.
- **TREATMENT Condition**:
  - Secure retrieval enabled via `MemoryController.search()` under `Principal.AI_AGENT`.
  - Task-specific associative queries retrieve up to 5 canonical notes/snippets.
  - Retrieved memory injected into model context.
- **Controlled Invariants**:
  - Identical model (`{summary.model}`), temperature, and parameters.
  - Identical prompt instructions, system role, and verification test code.
  - Independent fresh workspace per trial to prevent cross-condition leakage.
  - Alternating execution order (Task 2i: Control -> Treatment; Task 2i+1: Treatment -> Control).

---

## 3. Primary Outcomes & Comparison

| Metric | Control (No Memory) | Treatment (With Memory) | Delta |
|---|---|---|---|
| **Trials** | {summary.control_trials} | {summary.treatment_trials} | - |
| **Successes** | {summary.control_successes} | {summary.treatment_successes} | {summary.treatment_successes - summary.control_successes:+d} |
| **Failures** | {summary.control_failures} | {summary.treatment_failures} | {summary.treatment_failures - summary.control_failures:+d} |
| **Success Rate** | {summary.control_success_rate * 100:.1f}% | {summary.treatment_success_rate * 100:.1f}% | **{summary.absolute_delta * 100:+.1f} pp** |
| **Relative Delta** | - | - | **{summary.relative_delta:+.1f}%** |

### Paired 2x2 Outcome Matrix
- **Both Succeeded (`control_success / treatment_success`)**: `{summary.paired_counts['both_success']}`
- **Treatment Won (`control_failure / treatment_success`)**: `{summary.paired_counts['treatment_win']}`
- **Control Won (`control_success / treatment_failure`)**: `{summary.paired_counts['control_win']}`
- **Both Failed (`control_failure / treatment_failure`)**: `{summary.paired_counts['both_failure']}`

---

## 4. Secondary Metrics

| Metric | Control | Treatment | Delta |
|---|---|---|---|
| **Mean Model Latency** | {summary.mean_control_latency_ms:.1f} ms | {summary.mean_treatment_latency_ms:.1f} ms | {summary.mean_treatment_latency_ms - summary.mean_control_latency_ms:+.1f} ms |
| **Mean Execution Time** | {summary.mean_control_execution_ms:.1f} ms | {summary.mean_treatment_execution_ms:.1f} ms | {summary.mean_treatment_execution_ms - summary.mean_control_execution_ms:+.1f} ms |
| **Total Memory Retrievals** | 0 | {summary.total_retrievals} | +{summary.total_retrievals} |
| **Mean Retrievals/Trial** | 0.0 | {summary.mean_retrievals_per_treatment:.1f} | +{summary.mean_retrievals_per_treatment:.1f} |

---

## 5. Failure Taxonomy Analysis

| Failure Type | Control Count | Treatment Count |
|---|---|---|
{fail_table_rows}
---

## 6. Granular Paired Trials Table

| Task ID | Category | Control Success | Treatment Success | Delta | Control Latency | Treatment Latency | Retr. Count |
|---|---|---|---|---|---|---|---|
{paired_table_rows}
---

## 7. Claim Boundary & Limitations

### Claim Boundary
{summary.conclusion_status}:
Under the tested benchmark tasks and execution constraints, {conclusion_desc}.

### Scientific Limitations
1. **Sample Size**: N = {summary.task_count} paired tasks provides empirical directional observation rather than asymptotic statistical significance.
2. **Model Specificity**: Findings are specific to `{summary.model}` and local inference execution.
3. **Retrieval Bound**: Memory was retrieved with `page_size=5` and bounded token snippets.
4. **Causality**: This trial establishes observed runtime linkage and differential verification pass rates under controlled conditions; it does NOT constitute proof of generalized cognitive reasoning or universal transfer.
"""

    with open(md_file, "w", encoding="utf-8") as f:
        f.write(md_content)

    return json_file, md_file
