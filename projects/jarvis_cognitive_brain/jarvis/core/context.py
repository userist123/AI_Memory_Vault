"""
Dialogue and Audio Session Context Tracking for Jarvis Cognitive Brain.
Maintains real-time dialogue metrics, voice states, speech latencies, and barge-in telemetry.
"""

from typing import List, Dict, Any, Optional
import time
import uuid
from pydantic import BaseModel, Field


class DialogueTurn(BaseModel):
    """Represents a single conversational turn (user prompt + assistant response)."""
    turn_id: str = Field(default_factory=lambda: str(uuid.uuid4()))
    turn_index: int = 0
    timestamp: float = Field(default_factory=time.time)
    user_transcription: str = ""
    detected_language: str = "en"
    stt_duration_s: float = 0.0
    stt_latency_ms: float = 0.0
    assistant_response: str = ""
    tts_chunks_count: int = 0
    tts_duration_s: float = 0.0
    ttfb_latency_ms: float = 0.0
    was_interrupted: bool = False
    interruption_latency_ms: float = 0.0


class AudioSessionContext(BaseModel):
    """
    Stateful audio session context tracking dialogue flow, VAD energy, and barge-in statistics.
    """
    session_id: str = Field(default_factory=lambda: str(uuid.uuid4()))
    started_at: float = Field(default_factory=time.time)
    current_turn_index: int = 0
    current_state: str = "idle"
    active_language: str = "en"
    turns: List[DialogueTurn] = Field(default_factory=list)
    state_transitions: List[Dict[str, Any]] = Field(default_factory=list)
    
    # Telemetry metrics
    last_vad_probability: float = 0.0
    total_speech_duration_s: float = 0.0
    total_synthesis_duration_s: float = 0.0
    total_bargeins: int = 0
    last_bargein_latency_ms: float = 0.0

    def transition_state(self, new_state: str, metadata: Optional[Dict[str, Any]] = None) -> None:
        """Record a state machine transition."""
        self.current_state = new_state
        self.state_transitions.append({
            "state": new_state,
            "timestamp": time.time(),
            "metadata": metadata or {},
        })

    def start_turn(self, user_transcription: str = "", language: str = "en") -> DialogueTurn:
        """Initialize a new dialogue turn."""
        turn = DialogueTurn(
            turn_index=self.current_turn_index,
            user_transcription=user_transcription,
            detected_language=language,
        )
        self.turns.append(turn)
        self.current_turn_index += 1
        self.active_language = language
        return turn

    def complete_turn(
        self,
        response_text: str,
        tts_chunks_count: int = 1,
        tts_duration_s: float = 0.0,
        ttfb_latency_ms: float = 0.0,
    ) -> Optional[DialogueTurn]:
        """Record completion of assistant speaking turn."""
        if not self.turns:
            return None
        current_turn = self.turns[-1]
        current_turn.assistant_response = response_text
        current_turn.tts_chunks_count = tts_chunks_count
        current_turn.tts_duration_s = tts_duration_s
        current_turn.ttfb_latency_ms = ttfb_latency_ms
        self.total_synthesis_duration_s += tts_duration_s
        return current_turn

    def record_bargein(self, latency_ms: float = 0.0) -> None:
        """Record a barge-in interruption event."""
        self.total_bargeins += 1
        self.last_bargein_latency_ms = latency_ms
        if self.turns:
            self.turns[-1].was_interrupted = True
            self.turns[-1].interruption_latency_ms = latency_ms

    def record_vad_metric(self, prob: float, is_speech: bool, duration_s: float = 0.0) -> None:
        """Record live VAD telemetry."""
        self.last_vad_probability = prob
        if is_speech and duration_s > 0.0:
            self.total_speech_duration_s += duration_s

    def export_telemetry(self) -> Dict[str, Any]:
        """Export session metrics for HUD / telemetry stream."""
        return {
            "session_id": self.session_id,
            "current_state": self.current_state,
            "turns_count": len(self.turns),
            "active_language": self.active_language,
            "last_vad_probability": self.last_vad_probability,
            "total_speech_duration_s": round(self.total_speech_duration_s, 2),
            "total_synthesis_duration_s": round(self.total_synthesis_duration_s, 2),
            "total_bargeins": self.total_bargeins,
            "last_bargein_latency_ms": round(self.last_bargein_latency_ms, 2),
        }
