"""Production runtime wiring the Jarvis subsystems into one assistant."""

from __future__ import annotations

import asyncio
import os
import re
from typing import Any, Dict, List, Optional

from jarvis.audio.drivers import (
    AudioDeviceNotFoundError,
    SoundDeviceInputDriver,
    SoundDeviceOutputDriver,
    VirtualAudioInputDriver,
    VirtualAudioOutputDriver,
)
from jarvis.audio.pipeline import AudioPipeline, VoiceState
from jarvis.audio.stt import FasterWhisperSTTEngine, MockSTTEngine
from jarvis.audio.tts import KokoroTTSEngine, MockTTSEngine
from jarvis.audio.vad import EnergyVADEngine, SileroONNXVADEngine
from jarvis.agents.models import AgentRole, AgentTask, TaskPriority
from jarvis.agents.supervisor import MultiAgentSupervisor
from jarvis.config import Settings, get_settings
from jarvis.core.executive import CognitiveExecutive
from jarvis.iot.fastmcp_server import FastMCPIoTServer
from jarvis.iot.ha_client import HomeAssistantClient
from jarvis.llm.base import BaseLLMProvider
from jarvis.llm.mock_provider import MockLLMProvider
from jarvis.llm.ollama_provider import OllamaProvider
from jarvis.memory.invariants import Principal
from jarvis.memory.markdown_sync import MarkdownSyncEngine
from jarvis.memory.session import SessionMemory
from jarvis.memory.sqlite_engine import SQLiteStorageEngine


class JarvisRuntime:
    """Own the lifetime and cross-subsystem wiring of the Jarvis assistant."""

    ENTITY_PATTERN = re.compile(
        r"\b(?:light|switch|climate|scene|fan|cover|media_player|lock|sensor)\.[a-z0-9_]+\b",
        re.IGNORECASE,
    )

    def __init__(self, settings: Optional[Settings] = None, llm_provider: Optional[BaseLLMProvider] = None) -> None:
        self.settings = settings or get_settings()
        self.storage = SQLiteStorageEngine(
            db_path=self.settings.sqlite_db_path,
            timeout=self.settings.sqlite_busy_timeout_ms / 1000,
        )
        self.markdown_sync = MarkdownSyncEngine(self.settings.vault_path)
        if os.getenv("JARVIS_SYNC_VAULT", "0").lower() in {"1", "true", "yes"}:
            self.markdown_sync.sync_vault_to_sqlite(self.storage)
        self.session = SessionMemory(
            self.settings.session_memory_path,
            self.settings.recap_dir,
            max_bytes=self.settings.session_memory_max_bytes,
        )
        self.llm = llm_provider or self._build_llm()
        self.home_assistant = HomeAssistantClient(
            base_url=self.settings.home_assistant_url,
            token=self.settings.home_assistant_token,
            timeout_s=5.0,
        )
        self.controls = FastMCPIoTServer(self.home_assistant)
        self.executive = CognitiveExecutive(
            llm_provider=self.llm,
            storage_engine=self.storage,
            checkpoint_dir=self.settings.checkpoint_dir,
            max_retries=3,
        )
        self.executive.load_checkpoint()
        self.executive.register_state_callback(self._on_executive_state)
        self.audio = self._build_audio()
        self.last_state: Dict[str, Any] = {}
        self.agent_supervisor = MultiAgentSupervisor(
            storage=self.storage,
            llm=self.llm,
            max_concurrent_workers=4,
            telemetry_callback=self._on_agent_telemetry,
        )
        self._running = False
        self._hardware_fallback_used = False

    def _build_llm(self) -> BaseLLMProvider:
        if self.settings.llm_provider == "mock":
            return MockLLMProvider()
        if self.settings.llm_provider == "ollama":
            return OllamaProvider(
                host=self.settings.ollama_url,
                model=self.settings.ollama_model,
                timeout=self.settings.ollama_timeout,
            )
        raise RuntimeError(
            f"LLM provider '{self.settings.llm_provider}' is not configured in this runtime; use Ollama or mock."
        )

    def _build_audio(self, force_virtual: bool = False) -> AudioPipeline:
        use_hardware = not force_virtual and self.settings.audio_driver in {"auto", "sounddevice"}
        if use_hardware:
            input_driver = SoundDeviceInputDriver(
                sample_rate=self.settings.audio_sample_rate,
                chunk_size=self.settings.vad_frame_size,
            )
            output_driver = SoundDeviceOutputDriver(sample_rate=self.settings.tts_sample_rate)
        else:
            input_driver = VirtualAudioInputDriver(
                sample_rate=self.settings.audio_sample_rate,
                chunk_size=self.settings.vad_frame_size,
            )
            output_driver = VirtualAudioOutputDriver(sample_rate=self.settings.tts_sample_rate)

        if self.settings.vad_model_path and self.settings.vad_model_path.exists():
            vad_engine = SileroONNXVADEngine(
                model_path=self.settings.vad_model_path,
                threshold=self.settings.vad_threshold,
                sample_rate=self.settings.audio_sample_rate,
            )
        else:
            vad_engine = EnergyVADEngine(
                threshold=self.settings.vad_threshold,
                silence_tail_ms=self.settings.vad_silence_threshold_ms,
                sample_rate=self.settings.audio_sample_rate,
            )

        if os.getenv("JARVIS_ENABLE_LOCAL_STT", "1").lower() in {"0", "false", "no"}:
            stt_engine = MockSTTEngine()
        else:
            stt_engine = FasterWhisperSTTEngine(
                model_size=self.settings.stt_model_size,
                device=self.settings.stt_device,
                compute_type=self.settings.stt_compute_type,
            )

        if os.getenv("JARVIS_ENABLE_LOCAL_TTS", "1").lower() in {"0", "false", "no"}:
            tts_engine = MockTTSEngine(sample_rate=self.settings.tts_sample_rate)
        else:
            tts_engine = KokoroTTSEngine(
                model_path=self.settings.tts_model_path,
                voices_dir=self.settings.tts_voices_dir,
                sample_rate=self.settings.tts_sample_rate,
                use_gpu=self.settings.stt_device == "cuda",
            )

        return AudioPipeline(
            input_driver=input_driver,
            output_driver=output_driver,
            vad_engine=vad_engine,
            stt_engine=stt_engine,
            tts_engine=tts_engine,
            executive=self.executive,
            settings=self.settings,
            on_state_change=self._on_audio_state,
        )

    def _on_audio_state(self, state: VoiceState) -> None:
        self.last_state["voice_state"] = state.value

    def _on_executive_state(self, state: Dict[str, Any]) -> None:
        self.last_state.update(state)

    def _on_agent_telemetry(self, event_type: str, data: Dict[str, Any]) -> None:
        self.last_state["agent_event"] = event_type
        self.last_state["agent_event_data"] = data

    async def start_agent_workers(self) -> None:
        """Start the non-blocking specialist worker pool."""
        await self.agent_supervisor.start()

    async def dispatch_background(
        self,
        role: AgentRole,
        action: str,
        payload: Dict[str, Any],
        priority: TaskPriority = TaskPriority.P3_STANDARD,
        timeout_seconds: float = 30.0,
        max_retries: int = 0,
    ) -> asyncio.Future:
        """Queue specialist work and return its awaitable result future."""
        await self.start_agent_workers()
        task = AgentTask(
            role=role,
            action=action,
            payload=payload,
            priority=priority,
            timeout_seconds=timeout_seconds,
            max_retries=max_retries,
        )
        return self.agent_supervisor.submit_task(task)

    async def run_council_review(
        self,
        query: str,
        draft: str = "",
        note: Optional[Dict[str, Any]] = None,
        context: Optional[List[Dict[str, Any]]] = None,
    ) -> Dict[str, Any]:
        """Run retrieval, verification, and critique concurrently."""
        context = context or []
        futures = [
            await self.dispatch_background(
                AgentRole.RETRIEVAL,
                "retrieve_context",
                {"query": query, "limit": 10},
                priority=TaskPriority.P2_INTERACTIVE,
            ),
            await self.dispatch_background(
                AgentRole.VERIFIER,
                "verify_note",
                {"note": note or {}, "principal": Principal.AI_AGENT.value},
                priority=TaskPriority.P2_INTERACTIVE,
            ),
            await self.dispatch_background(
                AgentRole.CRITIC,
                "critique_draft",
                {"draft": draft or query, "context": context, "is_voice": False},
                priority=TaskPriority.P2_INTERACTIVE,
            ),
        ]
        results = await asyncio.gather(*futures)
        return {
            "retrieval": results[0].result,
            "verification": results[1].result,
            "critique": results[2].result,
        }

    def _dispatch_iot_command(self, command: str) -> Any:
        entity_match = self.ENTITY_PATTERN.search(command)
        entity_id = entity_match.group(0).lower() if entity_match else None
        lowered = command.casefold()
        if not entity_id:
            return {
                "status": "needs_clarification",
                "command": command,
                "reason": "An explicit Home Assistant entity_id is required.",
            }

        if any(token in lowered for token in ("turn on", "turn-on", "aprinde", "porneste", "pornește")):
            return self.controls.call_tool("turn_on", {"entity_id": entity_id})
        if any(token in lowered for token in ("turn off", "turn-off", "stinge", "opreste", "oprește")):
            return self.controls.call_tool("turn_off", {"entity_id": entity_id})
        if any(token in lowered for token in ("toggle", "comuta", "comută")):
            return self.controls.call_tool("toggle", {"entity_id": entity_id})
        brightness = re.search(r"(?:brightness|luminozitate)\s*(?:to|la)?\s*(\d{1,3})", lowered)
        if brightness:
            return self.controls.call_tool(
                "set_brightness", {"entity_id": entity_id, "brightness": int(brightness.group(1))}
            )
        temperature = re.search(r"(?:temperature|temperatura)\s*(?:to|la)?\s*(-?\d+(?:\.\d+)?)", lowered)
        if temperature:
            return self.controls.call_tool(
                "set_temperature", {"entity_id": entity_id, "temperature": float(temperature.group(1))}
            )
        return {
            "status": "needs_clarification",
            "command": command,
            "entity_id": entity_id,
            "reason": "No safe Home Assistant action was recognized.",
        }

    def _tool_executor(self, action: str, arguments: Dict[str, Any]) -> Any:
        if action == "iot_call":
            return self._dispatch_iot_command(str(arguments.get("command", "")))
        raise ValueError(f"Unsupported runtime tool action: {action}")

    async def process_text(
        self,
        text: str,
        source: str = "text",
        principal: Principal = Principal.HUMAN,
    ) -> Any:
        clean = str(text or "").strip()
        if not clean:
            return None
        self.executive.engine.tool_executor = self._tool_executor
        result = await self.executive.process_utterance(clean, source=source, principal=principal)
        response = "Processed successfully."
        for step in reversed(result.step_results):
            if isinstance(step.result, dict) and step.result.get("answer"):
                response = str(step.result["answer"])
                break
        intent = getattr(result.intent.intent_type, "value", str(result.intent.intent_type))
        plan_id = result.active_plan.id if result.active_plan else ""
        self.session.record_turn(
            clean,
            response,
            intent=intent,
            active_plan_id=plan_id,
            working_memory=self.executive.working_memory.get_active_context(),
        )
        return result

    def health(self) -> Dict[str, Any]:
        return {
            "assistant": "Jarvis",
            "llm_provider": self.settings.llm_provider,
            "llm_model": getattr(self.llm, "model", "mock"),
            "audio_driver": type(self.audio.input_driver).__name__,
            "stt_model": getattr(self.audio.stt_engine, "model_size", type(self.audio.stt_engine).__name__),
            "tts_engine": type(self.audio.tts_engine).__name__,
            "agent_workers": self.agent_supervisor.max_workers,
            "agent_active_workers": self.agent_supervisor.active_worker_count,
            "agent_queue_depth": len(self.agent_supervisor.queue),
            "home_assistant": self.home_assistant.check_health(),
            "session_memory": str(self.session.working_memory_path),
            "voice_state": self.audio.state.value,
            **self.last_state,
        }

    def start(self) -> None:
        if self._running:
            return
        try:
            self.audio.start()
        except AudioDeviceNotFoundError:
            if self.settings.audio_driver != "auto":
                raise
            self.audio = self._build_audio(force_virtual=True)
            self.audio.start()
            self._hardware_fallback_used = True
        self._running = True
        self.session.append_recap(
            "runtime_started",
            voice_state=self.audio.state.value,
            agent_workers=self.agent_supervisor.max_workers,
            hardware_fallback=self._hardware_fallback_used,
        )

    def stop(self) -> None:
        if not self._running:
            return
        self.audio.stop()
        self._running = False
        self.session.append_recap("runtime_stopped", voice_state=self.audio.state.value)
        self.storage.close()

    async def run_forever(self) -> None:
        await self.start_agent_workers()
        try:
            self.start()
            await self.audio.run()
        finally:
            await self.agent_supervisor.shutdown(wait=True)
            self.stop()


def create_runtime(settings: Optional[Settings] = None) -> JarvisRuntime:
    return JarvisRuntime(settings=settings)


async def main() -> None:
    runtime = create_runtime()
    await runtime.run_forever()


if __name__ == "__main__":
    asyncio.run(main())




