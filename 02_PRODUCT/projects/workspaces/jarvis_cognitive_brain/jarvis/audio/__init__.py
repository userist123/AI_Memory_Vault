"""
Audio Processing Subsystem for Jarvis Cognitive Brain ("Creier Vorbitor").
Provides real-time cascaded streaming STT, VAD segmentation, Kokoro-82M ONNX TTS,
Sentence/Clause chunking, sub-50ms Barge-In, and hardware/virtual audio drivers.
"""

from jarvis.audio.drivers import (
    AudioDriverState,
    AudioDriverError,
    AudioDeviceNotFoundError,
    AudioBufferOverflowError,
    RobustAudioSanitizer,
    CircularAudioBuffer,
    BaseAudioInputDriver,
    SoundDeviceInputDriver,
    VirtualAudioInputDriver,
    BaseAudioOutputDriver,
    SoundDeviceOutputDriver,
    VirtualAudioOutputDriver,
    VirtualAudioDriver,
)

from jarvis.audio.vad import (
    VADState,
    BaseVADEngine,
    EnergyVADEngine,
    SileroONNXVADEngine,
    VADSegmenter,
    SimulatedVADEngine,
)

from jarvis.audio.stt import (
    TranscriptionSegment,
    TranscriptionResult,
    BaseSTTEngine,
    FasterWhisperSTTEngine,
    MockSTTEngine,
)

from jarvis.audio.chunker import (
    TextNormalizer,
    SentenceChunker,
    SimulatedSentenceChunker,
    SimulatedTextNormalizer,
)

from jarvis.audio.tts import (
    BaseTTSEngine,
    KokoroTTSEngine,
    MockTTSEngine,
    SimulatedKokoroTTS,
)

from jarvis.audio.bargein import (
    BargeInController,
    SimulatedBargeInController,
)

from jarvis.audio.pipeline import (
    VoiceState,
    AudioPipeline,
)

__all__ = [
    # Drivers
    "AudioDriverState",
    "AudioDriverError",
    "AudioDeviceNotFoundError",
    "AudioBufferOverflowError",
    "RobustAudioSanitizer",
    "CircularAudioBuffer",
    "BaseAudioInputDriver",
    "SoundDeviceInputDriver",
    "VirtualAudioInputDriver",
    "BaseAudioOutputDriver",
    "SoundDeviceOutputDriver",
    "VirtualAudioOutputDriver",
    "VirtualAudioDriver",
    # VAD
    "VADState",
    "BaseVADEngine",
    "EnergyVADEngine",
    "SileroONNXVADEngine",
    "VADSegmenter",
    "SimulatedVADEngine",
    # STT
    "TranscriptionSegment",
    "TranscriptionResult",
    "BaseSTTEngine",
    "FasterWhisperSTTEngine",
    "MockSTTEngine",
    # Chunker
    "TextNormalizer",
    "SentenceChunker",
    "SimulatedSentenceChunker",
    "SimulatedTextNormalizer",
    # TTS
    "BaseTTSEngine",
    "KokoroTTSEngine",
    "MockTTSEngine",
    "SimulatedKokoroTTS",
    # Barge-In
    "BargeInController",
    "SimulatedBargeInController",
    # Pipeline
    "VoiceState",
    "AudioPipeline",
]
