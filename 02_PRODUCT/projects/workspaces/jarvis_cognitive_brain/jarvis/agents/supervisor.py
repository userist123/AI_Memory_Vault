"""
Milestone 3: Multi-Agent Supervisor Coordinator (Priority Queue, Worker Pool, Error Isolation).
"""

import asyncio
import heapq
import time
import uuid
import logging
from typing import Dict, Any, List, Optional, Callable, Set, Union, Tuple

from jarvis.memory.sqlite_engine import SQLiteStorageEngine
from jarvis.llm.base import BaseLLMProvider, CancellationToken
from jarvis.agents.models import (
    AgentRole,
    TaskPriority,
    TaskStatus,
    AgentTask,
    TaskResult,
    AgentTaskResult,
)
from jarvis.agents.base import ScopedStorageProxy
from jarvis.agents.router import RouterAgent
from jarvis.agents.retrieval import RetrievalAgent
from jarvis.agents.verifier import VerifierAgent
from jarvis.agents.consolidator import ConsolidatorAgent
from jarvis.agents.critic import CriticAgent

logger = logging.getLogger(__name__)


class MultiAgentSupervisor:
    """
    Coordinates specialized agent workers using a prioritized asynchronous task queue.
    Provides complete error isolation, worker pool lifecycle management, retry policies,
    and non-blocking background execution.
    """

    def __init__(
        self,
        storage: Optional[SQLiteStorageEngine] = None,
        llm: Optional[BaseLLMProvider] = None,
        max_concurrent_workers: int = 4,
        max_workers: Optional[int] = None,
        default_timeout_s: float = 30.0,
        telemetry_callback: Optional[Callable[[str, Dict[str, Any]], Any]] = None,
        on_task_complete: Optional[Callable[[TaskResult], Any]] = None,
    ):
        self.storage = storage
        self.llm = llm
        self.max_workers = max_workers or max_concurrent_workers
        self.default_timeout_s = default_timeout_s
        self.telemetry_callback = telemetry_callback
        self.on_task_complete = on_task_complete

        # Heap queue for synchronous access & PriorityQueue for async workers
        self.queue: List[AgentTask] = []
        self._async_queue: asyncio.PriorityQueue[Tuple[int, int, AgentTask]] = asyncio.PriorityQueue()
        self._seq = 0
        self._running = False
        self._workers: List[asyncio.Task] = []
        self._semaphore = asyncio.Semaphore(self.max_workers)
        self._active_workers = 0

        self._active_tasks: Dict[str, AgentTask] = {}
        self._cancelled_task_ids: Set[str] = set()
        self._task_futures: Dict[str, asyncio.Future] = {}
        self.completed_tasks: List[Dict[str, Any]] = []
        self.failed_tasks: List[Dict[str, Any]] = []

        # Instantiate specialized worker agents with scoped proxies
        self.router = RouterAgent(
            storage=ScopedStorageProxy(storage, AgentRole.ROUTER) if storage else None,
            llm=llm,
        )
        self.retrieval = RetrievalAgent(
            storage=ScopedStorageProxy(storage, AgentRole.RETRIEVAL) if storage else None,
            llm=llm,
        )
        self.verifier = VerifierAgent(
            storage=ScopedStorageProxy(storage, AgentRole.VERIFIER) if storage else None,
            llm=llm,
        )
        self.consolidator = ConsolidatorAgent(
            storage=ScopedStorageProxy(storage, AgentRole.CONSOLIDATOR) if storage else None,
            llm=llm,
        )
        self.critic = CriticAgent(
            storage=ScopedStorageProxy(storage, AgentRole.CRITIC) if storage else None,
            llm=llm,
        )

    @property
    def active_worker_count(self) -> int:
        return self._active_workers

    def _emit_telemetry(self, event_type: str, data: Dict[str, Any]) -> None:
        if self.telemetry_callback:
            try:
                self.telemetry_callback(event_type, data)
            except Exception:
                pass

    def submit_task(self, task: Union[AgentTask, Dict[str, Any]]) -> asyncio.Future:
        """
        Submit a task to the priority queue and return an awaitable Future.
        Accepts AgentTask or dictionary.
        """
        if isinstance(task, dict):
            task_obj = AgentTask(**task)
        else:
            task_obj = task

        self._seq += 1
        heapq.heappush(self.queue, task_obj)

        try:
            loop = asyncio.get_running_loop()
            fut = loop.create_future()
        except RuntimeError:
            loop = asyncio.new_event_loop()
            fut = loop.create_future()

        self._task_futures[task_obj.task_id] = fut
        self._async_queue.put_nowait((task_obj.priority, self._seq, task_obj))

        self._emit_telemetry("task_submitted", {
            "task_id": task_obj.task_id,
            "role": str(task_obj.role),
            "priority": task_obj.priority,
        })
        return fut

    async def start(self) -> None:
        """Start background worker tasks."""
        if self._running:
            return
        self._running = True
        self._workers = [
            asyncio.create_task(self._worker_loop(f"agent-worker-{i}"))
            for i in range(self.max_workers)
        ]
        self._emit_telemetry("supervisor_started", {"workers": self.max_workers})

    async def stop(self) -> None:
        """Gracefully stop workers without draining remaining queue."""
        self._running = False
        for w in self._workers:
            w.cancel()
        if self._workers:
            await asyncio.gather(*self._workers, return_exceptions=True)
            self._workers.clear()
        self._emit_telemetry("supervisor_stopped", {})

    async def shutdown(self, wait: bool = True, timeout: float = 2.0) -> None:
        """Gracefully drain in-flight tasks and shut down worker pool."""
        t0 = time.time()

        if wait:
            # Wait for queue to drain or timeout while workers are still active
            while (not self._async_queue.empty() or self._active_tasks) and (time.time() - t0 < timeout):
                await asyncio.sleep(0.01)

        self._running = False
        await self.stop()

    async def _worker_loop(self, worker_id: str) -> None:
        """Background worker loop continuously pulling tasks by priority."""
        while self._running:
            try:
                # Wait for next task with short timeout for responsive cancellation
                try:
                    priority, seq, task = await asyncio.wait_for(self._async_queue.get(), timeout=0.1)
                except asyncio.TimeoutError:
                    continue

                async with self._semaphore:
                    self._active_workers += 1
                    try:
                        # Remove from sync heap queue if still present
                        if task in self.queue:
                            try:
                                self.queue.remove(task)
                                heapq.heapify(self.queue)
                            except ValueError:
                                pass
                        result = await self._dispatch(task)
                    finally:
                        self._active_workers -= 1
                        self._async_queue.task_done()

            except asyncio.CancelledError:
                break
            except Exception as exc:
                logger.error(f"Worker {worker_id} encountered unhandled exception: {exc}")

    async def execute_task_direct(self, task: Union[AgentTask, Dict[str, Any]]) -> TaskResult:
        """Direct, high-priority synchronous dispatch bypassing queue."""
        if isinstance(task, dict):
            task_obj = AgentTask(**task)
        else:
            task_obj = task
        return await self._dispatch(task_obj)

    async def run_task_immediate(self, task: Union[AgentTask, Dict[str, Any]]) -> Dict[str, Any]:
        """Execute task immediately and return dict result."""
        res = await self.execute_task_direct(task)
        return res.result if isinstance(res.result, dict) else {"result": res.result, "status": res.status}

    async def run_next_task(self) -> Optional[Dict[str, Any]]:
        """
        Pop and execute the highest-priority pending task from queue.
        Maintains backwards compatibility with single-step test execution.
        """
        if not self.queue and self._async_queue.empty():
            return None

        task: Optional[AgentTask] = None
        if self.queue:
            task = heapq.heappop(self.queue)
        elif not self._async_queue.empty():
            _, _, task = self._async_queue.get_nowait()
            self._async_queue.task_done()

        if not task:
            return None

        res = await self._dispatch(task)
        return res.result if isinstance(res.result, dict) else {"result": res.result, "status": res.status}

    async def _dispatch(self, task: AgentTask) -> TaskResult:
        """Internal dispatch engine wrapping worker calls in isolation boundaries."""
        t0 = time.time()
        self._active_tasks[task.task_id] = task
        self._emit_telemetry("task_started", {"task_id": task.task_id, "role": str(task.role)})

        try:
            # 1. Check Cancellation Token or Cancelled Status
            is_cancelled = (
                task.task_id in self._cancelled_task_ids
                or (task.cancellation_token and getattr(task.cancellation_token, "is_cancelled", False))
                or (self._task_futures.get(task.task_id) is not None and self._task_futures[task.task_id].cancelled())
            )
            if is_cancelled:
                exec_ms = (time.time() - t0) * 1000.0
                task_res = TaskResult(
                    task_id=task.task_id,
                    role=task.role,
                    action=task.action,
                    status=TaskStatus.CANCELLED,
                    error="Task cancelled before execution.",
                    execution_time_ms=exec_ms,
                )
                self._record_completion(task, task_res)
                return task_res

            # 2. Route with Timeout Guard
            timeout = task.timeout_seconds or self.default_timeout_s
            role = task.role if isinstance(task.role, AgentRole) else AgentRole(str(task.role).lower())

            async with asyncio.timeout(timeout):
                if role == AgentRole.ROUTER:
                    res = await self.router.execute(task.payload, task.cancellation_token)
                elif role == AgentRole.RETRIEVAL:
                    res = await self.retrieval.execute(task.payload, task.cancellation_token)
                elif role == AgentRole.VERIFIER:
                    res = await self.verifier.execute(task.payload, task.cancellation_token)
                elif role == AgentRole.CONSOLIDATOR:
                    res = await self.consolidator.execute(task.payload, task.cancellation_token)
                elif role == AgentRole.CRITIC:
                    res = await self.critic.execute(task.payload, task.cancellation_token)
                else:
                    raise ValueError(f"Unknown agent role '{task.role}'")

            exec_ms = (time.time() - t0) * 1000.0
            task_res = TaskResult(
                task_id=task.task_id,
                role=task.role,
                action=task.action,
                status=TaskStatus.COMPLETED,
                result=res,
                execution_time_ms=exec_ms,
            )
            self._record_completion(task, task_res)
            return task_res

        except asyncio.TimeoutError:
            exec_ms = (time.time() - t0) * 1000.0
            task_res = TaskResult(
                task_id=task.task_id,
                role=task.role,
                action=task.action,
                status=TaskStatus.TIMED_OUT,
                error=f"Task exceeded timeout of {task.timeout_seconds}s",
                execution_time_ms=exec_ms,
            )
            self._record_completion(task, task_res)
            return task_res

        except asyncio.CancelledError as exc:
            exec_ms = (time.time() - t0) * 1000.0
            task_res = TaskResult(
                task_id=task.task_id,
                role=task.role,
                action=task.action,
                status=TaskStatus.CANCELLED,
                error="Task execution cancelled" + (f": {exc}" if str(exc) else "."),
                execution_time_ms=exec_ms,
            )
            self._record_completion(task, task_res)
            return task_res

        except Exception as exc:
            exec_ms = (time.time() - t0) * 1000.0
            # Handle Retry Policy
            if task.retry_count < task.max_retries:
                task.retry_count += 1
                logger.warning(f"Retrying task {task.task_id} (Attempt {task.retry_count}/{task.max_retries}): {exc}")
                self._emit_telemetry("task_retry", {
                    "task_id": task.task_id,
                    "attempt": task.retry_count,
                    "error": str(exc),
                })
                return await self._dispatch(task)

            task_res = TaskResult(
                task_id=task.task_id,
                role=task.role,
                action=task.action,
                status=TaskStatus.FAILED,
                error=str(exc),
                execution_time_ms=exec_ms,
            )
            self.failed_tasks.append({"task_id": task.task_id, "error": str(exc), "task": task.model_dump()})
            self._record_completion(task, task_res)
            return task_res

        finally:
            self._active_tasks.pop(task.task_id, None)
            self._cancelled_task_ids.discard(task.task_id)

    def _record_completion(self, task: AgentTask, result: TaskResult) -> None:
        """Record completed task result, resolve future, and trigger callbacks."""
        self.completed_tasks.append({
            "task_id": task.task_id,
            "role": str(task.role),
            "status": str(result.status),
            "result": result.result,
            "error": result.error,
            "execution_time_ms": result.execution_time_ms,
        })

        fut = self._task_futures.pop(task.task_id, None)
        if fut and not fut.done():
            fut.set_result(result)

        if self.on_task_complete:
            try:
                self.on_task_complete(result)
            except Exception:
                pass

        self._emit_telemetry("task_completed", {
            "task_id": task.task_id,
            "status": str(result.status),
            "exec_ms": result.execution_time_ms,
        })

    def cancel_tasks_matching(self, predicate: Callable[[AgentTask], bool], reason: str = "cancelled") -> int:
        """Cancel matching active and pending tasks."""
        cancelled_count = 0
        cancelled_ids: Set[str] = set()

        # Cancel in-flight active tasks
        for task_id, task in list(self._active_tasks.items()):
            if predicate(task):
                self._cancelled_task_ids.add(task.task_id)
                cancelled_ids.add(task.task_id)
                if task.cancellation_token and hasattr(task.cancellation_token, "cancel"):
                    task.cancellation_token.cancel(reason=reason)
                cancelled_count += 1

        # Cancel pending futures
        for task in list(self.queue):
            if predicate(task):
                self._cancelled_task_ids.add(task.task_id)
                if task.task_id not in cancelled_ids:
                    cancelled_ids.add(task.task_id)
                    cancelled_count += 1
                if task.cancellation_token and hasattr(task.cancellation_token, "cancel"):
                    task.cancellation_token.cancel(reason=reason)
                fut = self._task_futures.get(task.task_id)
                if fut and not fut.done():
                    fut.cancel()
                try:
                    self.queue.remove(task)
                    heapq.heapify(self.queue)
                except ValueError:
                    pass

        return cancelled_count

    def get_completed_tasks(self) -> List[Dict[str, Any]]:
        """Return audit history of completed tasks."""
        return list(self.completed_tasks)

    # Backwards-compatible private runner methods
    async def _run_router(self, payload: Dict[str, Any]) -> Dict[str, Any]:
        return await self.router.execute(payload)

    def _run_verifier(self, payload: Dict[str, Any]) -> Dict[str, Any]:
        note = payload.get("note", payload)
        report = self.verifier.verify_note(note)
        return {"valid": report.is_valid, "missing": report.missing, "violations": [v.rule for v in report.violations]}

    def _run_retrieval(self, payload: Dict[str, Any]) -> Dict[str, Any]:
        query = payload.get("query", "")
        limit = payload.get("limit", 5)
        if self.storage:
            results = self.storage.search_bm25(query, limit=limit)
        else:
            results = []
        return {"matches": results, "count": len(results)}

    async def _run_critic(self, payload: Dict[str, Any]) -> Dict[str, Any]:
        return await self.critic.execute(payload)


SupervisorCoordinator = MultiAgentSupervisor
