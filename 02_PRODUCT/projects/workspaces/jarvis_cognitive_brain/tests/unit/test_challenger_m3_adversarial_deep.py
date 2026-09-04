"""
Milestone 3 Deep Adversarial Stress & Empirical Challenge Suite.
Covers heavy concurrency saturation, priority preemption under load, mid-execution
cancellation storms, cascading worker crashes, retry isolation, proxy RBAC attacks,
and shutdown drain invariants.
"""

import pytest
import asyncio
import time
import uuid
import random
from typing import Dict, Any, List

from jarvis.memory.invariants import Principal, Lifecycle, NoteType
from jarvis.memory.sqlite_engine import SQLiteStorageEngine
from jarvis.llm.base import CancellationToken
from jarvis.llm.mock_provider import MockLLMProvider
from jarvis.agents import (
    AgentRole,
    TaskPriority,
    TaskStatus,
    AgentTask,
    TaskResult,
    MultiAgentSupervisor,
    RouterAgent,
    RetrievalAgent,
    VerifierAgent,
    ConsolidatorAgent,
    CriticAgent,
)


@pytest.mark.asyncio
async def test_deep_concurrency_saturation_and_semaphore_invariance(sqlite_storage: SQLiteStorageEngine, mock_llm: MockLLMProvider):
    """
    Submits 100 concurrent tasks with variable execution latency (10-30ms) to a supervisor with max_workers=4.
    Monitors active worker count continuously and verifies it never exceeds 4 while all 100 complete cleanly.
    """
    supervisor = MultiAgentSupervisor(storage=sqlite_storage, llm=mock_llm, max_concurrent_workers=4)
    await supervisor.start()

    peak_concurrency = 0
    monitoring = True

    async def concurrency_monitor():
        nonlocal peak_concurrency
        while monitoring:
            curr = supervisor.active_worker_count
            if curr > peak_concurrency:
                peak_concurrency = curr
            await asyncio.sleep(0.002)

    # Monkeypatch critic to simulate realistic async I/O delay
    async def variable_latency_execute(payload, cancellation_token=None):
        delay = random.uniform(0.01, 0.03)
        await asyncio.sleep(delay)
        return {"status": "ok", "delay": delay}

    supervisor.critic.execute = variable_latency_execute

    monitor_task = asyncio.create_task(concurrency_monitor())

    futures = []
    for i in range(100):
        t = AgentTask(
            task_id=f"sat-{i}",
            priority=(i % 5) + 1,
            role=AgentRole.CRITIC,
            payload={"index": i},
        )
        futures.append(supervisor.submit_task(t))

    results = await asyncio.gather(*futures)
    monitoring = False
    await monitor_task

    assert len(results) == 100
    assert all(r.status == TaskStatus.COMPLETED for r in results)
    assert peak_concurrency <= 4, f"Concurrency limit breached! Observed {peak_concurrency} > 4"

    await supervisor.shutdown(wait=True, timeout=3.0)
    assert len(supervisor.completed_tasks) == 100


@pytest.mark.asyncio
async def test_priority_preemption_under_heavy_queue_backlog(sqlite_storage: SQLiteStorageEngine, mock_llm: MockLLMProvider):
    """
    Floods queue with 40 low-priority (P5) tasks. While P5 tasks are queued/running,
    injects 5 urgent (P1) voice tasks. Verifies P1 tasks are executed ahead of remaining P5 tasks.
    """
    supervisor = MultiAgentSupervisor(storage=sqlite_storage, llm=mock_llm, max_concurrent_workers=2)
    await supervisor.start()

    execution_timestamps: Dict[str, float] = {}

    async def timed_execute(payload, cancellation_token=None):
        task_name = payload.get("name", "unknown")
        await asyncio.sleep(0.02)
        execution_timestamps[task_name] = time.time()
        return {"done": task_name}

    supervisor.critic.execute = timed_execute

    # 1. Enqueue 40 P5 background tasks
    p5_futures = []
    for i in range(40):
        t = AgentTask(
            task_id=f"p5-task-{i}",
            priority=5,
            role=AgentRole.CRITIC,
            payload={"name": f"p5-{i}"},
        )
        p5_futures.append(supervisor.submit_task(t))

    # Give worker loop a moment to pick up first 2 tasks
    await asyncio.sleep(0.01)

    # 2. Inject 5 P1 urgent tasks
    p1_futures = []
    p1_submit_time = time.time()
    for i in range(5):
        t = AgentTask(
            task_id=f"p1-urgent-{i}",
            priority=1,
            role=AgentRole.CRITIC,
            payload={"name": f"p1-{i}"},
        )
        p1_futures.append(supervisor.submit_task(t))

    all_results = await asyncio.gather(*(p5_futures + p1_futures))
    await supervisor.shutdown(wait=True, timeout=3.0)

    assert len(all_results) == 45
    assert all(r.status == TaskStatus.COMPLETED for r in all_results)

    # P1 tasks must have finished before the vast majority of P5 tasks
    p1_finish_times = [execution_timestamps[f"p1-{i}"] for i in range(5)]
    p5_finish_times = [execution_timestamps[f"p5-{i}"] for i in range(2, 40)] # Exclude the first 2 that started immediately

    max_p1_finish = max(p1_finish_times)
    # At least 30 of the remaining P5 tasks must finish AFTER the P1 tasks
    p5_after_p1 = [t for t in p5_finish_times if t > max_p1_finish]
    assert len(p5_after_p1) >= 30, f"Priority preemption failure: only {len(p5_after_p1)} P5 tasks ran after P1 batch."


@pytest.mark.asyncio
async def test_mid_execution_cancellation_with_in_flight_tasks(sqlite_storage: SQLiteStorageEngine, mock_llm: MockLLMProvider):
    """
    Submits tasks that sleep for 0.2s with active cancellation tokens.
    Cancels them mid-execution and verifies cancellation status is recorded cleanly.
    """
    supervisor = MultiAgentSupervisor(storage=sqlite_storage, llm=mock_llm, max_concurrent_workers=2)
    await supervisor.start()

    async def interruptible_execute(payload, cancellation_token=None):
        for _ in range(20):
            if cancellation_token and cancellation_token.is_cancelled:
                raise asyncio.CancelledError("Cancelled in worker loop")
            await asyncio.sleep(0.01)
        return {"status": "finished"}

    supervisor.critic.execute = interruptible_execute

    tokens = [CancellationToken() for _ in range(6)]
    futures = []
    for i in range(6):
        t = AgentTask(
            task_id=f"in-flight-{i}",
            priority=2,
            role=AgentRole.CRITIC,
            payload={"index": i},
            cancellation_token=tokens[i],
        )
        futures.append(supervisor.submit_task(t))

    # Let tasks start running
    await asyncio.sleep(0.04)

    # Cancel half of them mid-execution
    for i in range(3):
        tokens[i].cancel(reason="mid_flight_bargein")

    results = await asyncio.gather(*futures, return_exceptions=True)
    await supervisor.shutdown(wait=True, timeout=2.0)

    # Assert completed tasks have proper statuses
    cancelled_results = [r for r in results if isinstance(r, TaskResult) and r.status in [TaskStatus.CANCELLED, TaskStatus.FAILED]]
    assert len(cancelled_results) >= 3


@pytest.mark.asyncio
async def test_cascading_worker_exceptions_and_recovery(sqlite_storage: SQLiteStorageEngine, mock_llm: MockLLMProvider):
    """
    Simulates a storm of diverse exceptions (KeyError, ValueError, RuntimeError, MemoryError)
    across all specialized workers. Verifies supervisor isolates errors, records failure,
    and seamlessly executes subsequent valid tasks.
    """
    supervisor = MultiAgentSupervisor(storage=sqlite_storage, llm=mock_llm, max_concurrent_workers=3)
    await supervisor.start()

    exceptions_to_test = [
        (AgentRole.ROUTER, KeyError("Malformed intent schema")),
        (AgentRole.RETRIEVAL, RuntimeError("SQLite busy lock simulation")),
        (AgentRole.VERIFIER, ValueError("UUID format corruption")),
        (AgentRole.CRITIC, MemoryError("Simulated OOM condition")),
        (AgentRole.CONSOLIDATOR, TypeError("Invalid cluster payload")),
    ]

    async def failing_worker(role, exc):
        async def _fail(payload, cancellation_token=None):
            raise exc
        return _fail

    # Inject failures
    supervisor.router.execute = await failing_worker(AgentRole.ROUTER, KeyError("Malformed intent"))
    supervisor.retrieval.execute = await failing_worker(AgentRole.RETRIEVAL, RuntimeError("SQLite busy"))
    supervisor.verifier.execute = await failing_worker(AgentRole.VERIFIER, ValueError("UUID corruption"))
    supervisor.critic.execute = await failing_worker(AgentRole.CRITIC, MemoryError("Simulated OOM"))
    supervisor.consolidator.execute = await failing_worker(AgentRole.CONSOLIDATOR, TypeError("Invalid cluster"))

    fail_futures = []
    for i, (role, exc) in enumerate(exceptions_to_test):
        t = AgentTask(
            task_id=f"storm-{i}",
            priority=2,
            role=role,
            payload={"fail": True},
        )
        fail_futures.append(supervisor.submit_task(t))

    fail_results = await asyncio.gather(*fail_futures)
    assert len(fail_results) == len(exceptions_to_test)
    assert all(r.status == TaskStatus.FAILED for r in fail_results)
    assert len(supervisor.failed_tasks) == len(exceptions_to_test)

    # Now restore one worker and verify supervisor handles healthy tasks immediately
    async def healthy_execute(payload, cancellation_token=None):
        return {"status": "healthy_recovered"}

    supervisor.critic.execute = healthy_execute

    healthy_task = AgentTask(
        task_id="healthy-recovery",
        priority=1,
        role=AgentRole.CRITIC,
        payload={"draft": "recovered draft"},
    )
    res = await supervisor.submit_task(healthy_task)
    assert res.status == TaskStatus.COMPLETED
    assert res.result == {"status": "healthy_recovered"}

    await supervisor.shutdown(wait=True, timeout=2.0)


@pytest.mark.asyncio
async def test_concurrent_proxy_rbac_invariant_bombardment(sqlite_storage: SQLiteStorageEngine, mock_llm: MockLLMProvider):
    """
    Simulates 50 concurrent adversarial tasks attempting illegal storage mutations across all agent roles:
    - ROUTER attempting propose
    - RETRIEVAL attempting update
    - VERIFIER attempting attest
    - CRITIC attempting archive
    - CONSOLIDATOR attempting privileged provenance
    Verifies all 50 attacks are rejected with PermissionError / ValueError and SQLite remains clean.
    """
    supervisor = MultiAgentSupervisor(storage=sqlite_storage, llm=mock_llm, max_concurrent_workers=4)
    await supervisor.start()

    attack_tasks = []
    for i in range(10):
        # 1. Router propose attack
        attack_tasks.append(AgentTask(
            task_id=f"atk-router-{i}", priority=2, role=AgentRole.ROUTER,
            payload={"query": "attack", "malicious_action": "propose"}
        ))
        # 2. Retrieval update attack
        attack_tasks.append(AgentTask(
            task_id=f"atk-retrieval-{i}", priority=2, role=AgentRole.RETRIEVAL,
            payload={"query": "attack", "malicious_action": "update"}
        ))
        # 3. Verifier attest attack
        attack_tasks.append(AgentTask(
            task_id=f"atk-verifier-{i}", priority=2, role=AgentRole.VERIFIER,
            payload={"malicious_action": "attest"}
        ))
        # 4. Critic archive attack
        attack_tasks.append(AgentTask(
            task_id=f"atk-critic-{i}", priority=2, role=AgentRole.CRITIC,
            payload={"draft": "attack", "malicious_action": "archive"}
        ))
        # 5. Consolidator privileged provenance attack
        attack_tasks.append(AgentTask(
            task_id=f"atk-consolidator-{i}", priority=2, role=AgentRole.CONSOLIDATOR,
            payload={"malicious_action": "spoofed_provenance"}
        ))

    async def execute_router_attack(payload, cancellation_token=None):
        supervisor.router.storage.propose({"id": str(uuid.uuid4()), "type": "knowledge"})

    async def execute_retrieval_attack(payload, cancellation_token=None):
        supervisor.retrieval.storage.update(str(uuid.uuid4()), {"content": "tampered"})

    async def execute_verifier_attack(payload, cancellation_token=None):
        supervisor.verifier.storage.attest(str(uuid.uuid4()), "fake")

    async def execute_critic_attack(payload, cancellation_token=None):
        supervisor.critic.storage.archive(str(uuid.uuid4()))

    async def execute_consolidator_attack(payload, cancellation_token=None):
        supervisor.consolidator.storage.propose({
            "id": str(uuid.uuid4()), "type": "knowledge", "lifecycle": "REVIEW", "category": "c",
            "provenance": {"source_type": "official", "source_ref": "spoofed"}
        })

    supervisor.router.execute = execute_router_attack
    supervisor.retrieval.execute = execute_retrieval_attack
    supervisor.verifier.execute = execute_verifier_attack
    supervisor.critic.execute = execute_critic_attack
    supervisor.consolidator.execute = execute_consolidator_attack

    futures = [supervisor.submit_task(t) for t in attack_tasks]
    results = await asyncio.gather(*futures)

    assert len(results) == 50
    # Every single attack must fail
    assert all(r.status == TaskStatus.FAILED for r in results)
    # Check that error mentions RBAC violation or permission or provenance
    for r in results:
        err = r.error or ""
        assert any(keyword in err.lower() for keyword in ["rbac", "permission", "not permitted", "provenance"])

    await supervisor.shutdown(wait=True, timeout=2.0)
    assert sqlite_storage.count() == 0  # No notes were illegally created


@pytest.mark.asyncio
async def test_supervisor_rapid_start_stop_cycles(sqlite_storage: SQLiteStorageEngine, mock_llm: MockLLMProvider):
    """
    Tests supervisor stability under rapid start() and stop() / shutdown() cycles with pending queue items.
    """
    supervisor = MultiAgentSupervisor(storage=sqlite_storage, llm=mock_llm, max_concurrent_workers=2)

    for cycle in range(5):
        await supervisor.start()
        # Submit tasks
        for i in range(5):
            t = AgentTask(task_id=f"cycle-{cycle}-{i}", priority=3, role=AgentRole.CRITIC, payload={"draft": "test"})
            supervisor.submit_task(t)
        await supervisor.stop()

    assert supervisor._running is False
    assert len(supervisor._workers) == 0


@pytest.mark.asyncio
async def test_supervisor_telemetry_callback_under_stress(sqlite_storage: SQLiteStorageEngine, mock_llm: MockLLMProvider):
    """
    Verifies that telemetry callbacks receive accurate event counts for submitted, started, completed,
    and retry events under high-volume dispatch.
    """
    events_received: List[str] = []

    def on_telemetry(event_type: str, data: Dict[str, Any]):
        events_received.append(event_type)

    supervisor = MultiAgentSupervisor(
        storage=sqlite_storage,
        llm=mock_llm,
        max_concurrent_workers=4,
        telemetry_callback=on_telemetry,
    )
    await supervisor.start()

    futures = []
    for i in range(25):
        t = AgentTask(task_id=f"telem-{i}", priority=2, role=AgentRole.CRITIC, payload={"draft": f"msg {i}"})
        futures.append(supervisor.submit_task(t))

    await asyncio.gather(*futures)
    await supervisor.shutdown(wait=True, timeout=2.0)

    assert "supervisor_started" in events_received
    assert events_received.count("task_submitted") == 25
    assert events_received.count("task_started") == 25
    assert events_received.count("task_completed") == 25
    assert "supervisor_stopped" in events_received
