"""
Jarvis Cognitive Brain Central Settings Configuration.
"""

from pathlib import Path
from typing import Optional, Literal
from pydantic import Field
from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    """Application-wide configuration parameters."""

    model_config = SettingsConfigDict(
        env_prefix="JARVIS_",
        env_file=".env",
        env_file_encoding="utf-8",
        extra="ignore",
    )

    # LLM Provider Configuration
    llm_provider: Literal["ollama", "gemini", "claude", "mock"] = Field(
        default="ollama",
        description="Active LLM provider backend",
    )
    ollama_url: str = Field(
        default="http://localhost:11434",
        description="Ollama API base URL",
    )
    ollama_model: str = Field(
        default="qwen2.5-coder:7b",
        description="Local Ollama model name",
    )
    ollama_timeout: float = Field(
        default=30.0,
        description="HTTP timeout in seconds for Ollama calls",
    )
    gemini_api_key: Optional[str] = Field(
        default=None,
        description="Google Gemini / Antigravity API key",
    )
    claude_api_key: Optional[str] = Field(
        default=None,
        description="Anthropic Claude API key",
    )

    # Persistent Storage & Memory Configuration
    vault_path: Path = Field(
        default=Path("vault_notes"),
        description="Root directory for Obsidian-style markdown notes",
    )
    sqlite_db_path: Path = Field(
        default=Path("vault_memory.sqlite3"),
        description="SQLite database path for relational index and WAL storage",
    )
    sqlite_busy_timeout_ms: int = Field(
        default=5000,
        description="SQLite busy timeout in milliseconds",
    )
    checkpoint_dir: Path = Field(
        default=Path(".checkpoints"),
        description="Directory for atomic working memory and plan checkpoints",
    )
    audit_log_path: Path = Field(
        default=Path("audit_log.jsonl"),
        description="Path to tamper-evident SHA-256 chained audit log",
    )

    # Audio Engine & VAD Configuration (Milestone 2 integration hooks)
    audio_driver: Literal["auto", "sounddevice", "virtual", "mock"] = Field(
        default="auto",
        description="Audio I/O driver backend (auto detects hardware with virtual fallback)",
    )
    audio_sample_rate: int = Field(
        default=16000,
        description="Input microphone audio sampling rate (Hz)",
    )
    tts_sample_rate: int = Field(
        default=24000,
        description="Output TTS speech sampling rate (Hz)",
    )
    vad_silence_threshold_ms: int = Field(
        default=500,
        description="Silero VAD trailing silence threshold (ms) for utterance segmentation",
    )
    vad_threshold: float = Field(
        default=0.5,
        description="Speech probability threshold for VAD activation",
    )
    vad_frame_size: int = Field(
        default=512,
        description="Frame chunk size for VAD processing (512 samples = 32ms at 16kHz)",
    )
    vad_model_path: Optional[Path] = Field(
        default=None,
        description="Path to Silero VAD ONNX model file",
    )
    stt_model_size: str = Field(
        default="large-v3-turbo",
        description="Faster-Whisper STT model size; large-v3-turbo is the production default",
    )
    stt_device: str = Field(
        default="auto",
        description="Computation device for STT inference (auto, cpu, cuda)",
    )
    stt_compute_type: str = Field(
        default="int8",
        description="Quantization compute type for STT; int8 is the production default",
    )
    tts_voice: str = Field(
        default="default",
        description="Default Kokoro TTS speaker voice ID",
    )
    tts_speed: float = Field(
        default=1.0,
        description="Speech synthesis rate multiplier",
    )
    tts_model_path: Optional[Path] = Field(
        default=None,
        description="Path to Kokoro-82M ONNX model file",
    )
    tts_voices_dir: Optional[Path] = Field(
        default=None,
        description="Path to Kokoro voices embeddings directory",
    )

    # Session recovery and compact working memory
    session_memory_path: Path = Field(
        default=Path(".jarvis/JARVIS_MEMORY.md"),
        description="Compact working memory kept below the configured byte budget",
    )
    recap_dir: Path = Field(
        default=Path(".jarvis/recaps"),
        description="Daily append-only crash-recovery recap directory",
    )
    session_memory_max_bytes: int = Field(
        default=3072,
        description="Maximum size of compact working memory in bytes",
    )

    # IoT & Home Assistant Configuration (Milestone 4 integration hooks)
    home_assistant_url: str = Field(
        default="http://localhost:8123",
        description="Home Assistant REST API base URL",
    )
    home_assistant_token: Optional[str] = Field(
        default=None,
        description="Home Assistant Long-Lived Access Token",
    )


# Singleton instance accessor
_settings_instance: Optional[Settings] = None


def get_settings() -> Settings:
    """Retrieve or initialize the global Settings instance."""
    global _settings_instance
    if _settings_instance is None:
        _settings_instance = Settings()
    return _settings_instance


def reset_settings(new_settings: Optional[Settings] = None) -> Settings:
    """Reset or override global settings (useful for tests)."""
    global _settings_instance
    _settings_instance = new_settings or Settings()
    return _settings_instance


