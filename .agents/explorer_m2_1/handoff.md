# Milestone 2: Cascaded Audio Pipeline ("Creier Vorbitor") — Handoff Report

## 1. Observation

### Codebase Health & Test Suite Execution
- Running the complete test suite across `projects/jarvis_cognitive_brain` via `pytest` and `StructuredE2ERunner`:
  - Command: `python tests/e2e/test_runner.py`
  - Result: **167 passed in 2.31s** (100% Pass Rate).
  - Breakdown:
    - Tier 1 Feature Coverage (`tests/e2e/tier1_features`): 50 passed
    - Tier 2 Boundaries & Invariants (`tests/e2e/tier2_boundaries`): 25 passed
    - Tier 3 Pairwise Combinations (`tests/e2e/tier3_combinations`): 20 passed
    - Tier 4 Real-World Workloads (`tests/e2e/tier4_workloads`): 10 passed
    - Unit Tests (`tests/unit`): 54 passed (including adversarial concurrency, SQL WAL, lineage CTE cycles, and invariant checks)
  - Python Environment: Python 3.14.2 on Windows.

### Environment & Dependency Inspection
Direct package availability probe (`python -c "...import..."`):
- `numpy`: **AVAILABLE** (Used for all array manipulations, float32 audio buffers, and sanitization).
- `onnxruntime`: **AVAILABLE** (Used for Silero VAD ONNX and Kokoro-82M ONNX models).
- `sounddevice`: **AVAILABLE** (Used for physical microphone capture and speaker DAC output).
- `scipy`: **AVAILABLE** (Used for resampling, signal filters, and audio math).
- `torch`: **AVAILABLE** (Used for PyTorch tensor manipulation if needed).
- `faster_whisper`: **NOT AVAILABLE** (`No module named 'faster_whisper'`).
- `kokoro`: **NOT AVAILABLE** (`No module named 'kokoro'`).
- `pydantic` & `pydantic_settings`: **AVAILABLE**.

### Existing Codebase Connections & Touchpoints
1. `jarvis/config.py`:
   - Configures `audio_sample_rate` (16000), `tts_sample_rate` (24000), and `vad_silence_threshold_ms` (500).
   - Needs extension with audio driver mode (`auto`, `sounddevice`, `virtual`, `headless`), VAD sensitivity parameters (`vad_threshold: 0.5`, `vad_frame_size: 512`), STT engine settings (`stt_model_size`, `stt_device`, `stt_compute_type`), and TTS voice IDs (`tts_voice: "af_bella"`).
2. `jarvis/core/executive.py`:
   - `CognitiveExecutive.process_utterance(text, source="voice", principal=Principal.AI_AGENT)` is the primary cognitive entry point for audio transcription events.
   - Manages atomic working memory and active plan checkpointing (`.checkpoints/wm.json`, `.checkpoints/plan.json`).
3. `jarvis/core/ooda.py`:
   - `OODACognitiveEngine` implements Observe, Retrieve, Reason/Plan, Act, Reflect, Consolidate.
   - `BaseLLMProvider.stream()` yields token deltas used for streaming TTS synthesis with `CancellationToken`.
4. `jarvis/llm/base.py`:
   - Contains `CancellationToken` and `CancellationError`, featuring thread-safe callback registration (`register_callback()`) and cancellation checks.

---

## 2. Logic Chain

1. **Step 1 — Foundation Verification**:
   The existing M1 implementation (Cognitive OODA Loop, Multi-Signal Recall, SQLite WAL engine, Invariants P0-P18, and LLM Providers) is 100% operational with 167 green tests.

2. **Step 2 — Audio Pipeline Requirements**:
   Per requirements R2, Milestone 2 requires:
   - Continuous audio capture at 16kHz float32 mono.
   - Silero VAD speech detection (500ms trailing silence threshold) with circular ring buffering to preserve pre-trigger speech frames.
   - Faster-Whisper STT with language auto-detection (English/Romanian).
   - Kokoro-82M ONNX 24kHz TTS synthesis streaming with clause/sentence chunking achieving <300ms Time-To-First-Byte (TTFB).
   - Sub-50ms Barge-In Interruption mechanism immediately halting DAC playback, clearing TTS queues, and triggering LLM `CancellationToken.cancel()`.

3. **Step 3 — Headless & Cross-Platform Resilience**:
   Because `faster_whisper` and `kokoro` Python packages may not be pre-installed in all development or CI environments, and headless runners lack physical audio hardware (causing `PortAudioError` in `sounddevice`), the architecture MUST enforce a strict Driver Abstraction Layer:
   - `AudioInputDriver` -> `SounddeviceInputDriver` (hardware) + `VirtualAudioInputDriver` (mock/headless).
   - `AudioOutputDriver` -> `SounddeviceOutputDriver` (hardware) + `VirtualAudioOutputDriver` (mock/headless).
   - `VADEngine` -> `SileroVADEngine` (ONNX) + `EnergyVADEngine` (RMS fallback).
   - `STTEngine` -> `FasterWhisperSTTEngine` (real) + `MockSTTEngine` (deterministic test).
   - `TTSEngine` -> `KokoroTTSEngine` (ONNX) + `MockTTSEngine` (synthetic wave).

4. **Step 4 — State & Telemetry Integration**:
   The audio pipeline maintains an internal state machine (`IDLE`, `LISTENING`, `PROCESSING`, `SPEAKING`, `INTERRUPTED`) and emits real-time events that link directly into `CognitiveExecutive` and the future Milestone 5 HUD WebSocket stream (`/ws/hud`).

---

## 3. Caveats

- **No Caveats on M1 Codebase**: Milestone 1 is completely verified and operational.
- **Hardware Dependency**: On systems without microphone/speakers, the audio subsystem must default cleanly to `virtual`/`headless` mode without raising uncaught exceptions.
- **Model Weight Files**: When ONNX model weights (`silero_vad.onnx`, `kokoro-82m.onnx`, `whisper-base`) are not downloaded locally, the engines must fall back gracefully to their synthetic/energy counterparts rather than crashing the daemon.

---

## 4. Conclusion & Milestone 2 Blueprint

### Target Code Layout (`jarvis/audio/` + Core Connections)

```
projects/jarvis_cognitive_brain/
├── jarvis/
│   ├── config.py                  # [UPDATE] Extended audio & VAD configuration settings
│   ├── core/
│   │   ├── context.py             # [NEW] AudioSessionContext, metrics, and dialogue state
│   │   ├── executive.py           # [INTEGRATION] Audio callback hooks & cycle dispatch
│   │   └── models.py              # [INTEGRATION] Perception metadata & audio state enums
│   └── audio/
│       ├── __init__.py            # Clean exports of audio public API
│       ├── drivers.py             # Audio I/O drivers (Sounddevice + Virtual/Headless), Sanitizer & Ring Buffer
│       ├── vad.py                 # Silero VAD (ONNX) + Energy VAD with 500ms trailing silence trigger
│       ├── stt.py                 # Faster-Whisper STT engine + Mock STT engine
│       ├── tts.py                 # Kokoro-82M ONNX TTS engine (24kHz) + Mock TTS engine
│       ├── chunker.py             # Sentence & clause streaming chunker + TextNormalizer
│       ├── bargein.py             # Sub-50ms BargeInController with DAC abort & CancellationToken
│       └── pipeline.py            # Master AudioPipeline coordinating I/O, VAD, STT, TTS, and Executive
└── tests/
    └── unit/
        ├── test_audio_pipeline.py # Comprehensive unit tests for AudioPipeline & drivers
        └── test_bargein.py        # Dedicated unit tests for sub-50ms barge-in and race conditions
```

### Detailed Component Specifications

#### 1. `jarvis/audio/drivers.py`
- **`RobustAudioSanitizer`**:
  - Validates `float32` 1D mono arrays.
  - Replaces `NaN` and `Inf` with `0.0`.
  - Clamps amplitudes strictly to `[-1.0, 1.0]` to protect DAC hardware and prevent distortion.
  - Safely handles empty arrays.
- **`CircularAudioBuffer`**:
  - Bounded ring buffer (`max_samples = sample_rate * max_seconds`).
  - Thread-safe `write(chunk: np.ndarray)`.
  - `get_recent(num_samples: int) -> np.ndarray` returning contiguous audio slice.
  - Zero-leak circular overwrite on overflow.
- **`AudioInputDriver` (ABC)**:
  - `SounddeviceInputDriver`: Real hardware microphone capture using `sounddevice.InputStream(samplerate=16000, channels=1, dtype='float32')`. Auto-falls back if sounddevice errors.
  - `VirtualAudioInputDriver`: Headless in-memory audio source with methods `feed_audio()`, `generate_sine_wave()`, `generate_speech_utterance()`, `generate_silence()`.
- **`AudioOutputDriver` (ABC)**:
  - `SounddeviceOutputDriver`: Real hardware speaker output using `sounddevice.OutputStream(samplerate=24000, channels=1, dtype='float32')`. Supports non-blocking chunk playback and instant `abort_playback()`.
  - `VirtualAudioOutputDriver`: Headless audio sink tracking `played_chunks`, `is_playing`, `abort_playback()`, and barge-in callback triggers.

#### 2. `jarvis/audio/vad.py`
- **`BaseVADEngine` (ABC)**:
  - `process_frame(frame: np.ndarray) -> float`
  - `is_speech(frame: np.ndarray) -> bool`
  - `should_trigger_endpoint() -> bool`
  - `reset() -> None`
- **`SileroVADEngine`**:
  - Implements Silero VAD v4/v5 ONNX inference via `onnxruntime.InferenceSession`.
  - Processes 512-sample (32ms at 16kHz) frames with persistent hidden state `(2, 1, 64)`.
  - Evaluates speech probability against `vad_threshold` (0.5).
  - Triggers endpointing when trailing silence reaches `vad_silence_threshold_ms` (500ms).
  - Falls back gracefully to `EnergyVADEngine` if ONNX runtime or model path is unavailable.
- **`EnergyVADEngine`**:
  - Deterministic RMS energy detector (`prob = clip(rms * 4.0, 0.0, 1.0)`) for fast headless tests and environments without ONNX models.

#### 3. `jarvis/audio/stt.py`
- **`BaseSTTEngine` (ABC)**:
  - `async def transcribe(audio_samples: np.ndarray, language: Optional[str] = None) -> str`
- **`FasterWhisperSTTEngine`**:
  - Loads `faster_whisper.WhisperModel(model_size, device=device, compute_type=compute_type)`.
  - Transcribes 16kHz float32 audio arrays with beam size 5 and auto-detection for English / Romanian.
  - Falls back to `MockSTTEngine` if `faster_whisper` is not installed.
- **`MockSTTEngine`**:
  - Configurable deterministic transcriber for unit tests.

#### 4. `jarvis/audio/chunker.py`
- **`SentenceChunker`**:
  - Streaming accumulator splitting token deltas into synthesizable text chunks.
  - Primary split on sentence terminals (`.`, `!`, `?`, `\n`).
  - Secondary split on clause delimiters (`,`, `;`, `:`) when accumulated word count >= 4.
  - Abbreviation protection (e.g. "Mr.", "Dr.", "e.g.", "1.5").
  - `flush() -> List[str]`.
- **`TextNormalizer`**:
  - Regex expansions for numbers, units, acronyms, and frequencies:
    - `"24 kHz"` -> `"twenty four kilohertz"`
    - `"16 kHz"` -> `"sixteen kilohertz"`
    - `"(\d+)%"` -> `"\1 percent"`
    - `"(\d+)\s*(?:°C|deg C)"` -> `"\1 degrees Celsius"`
    - `"IoT"` -> `"I o T"`, `"OODA"` -> `"O O D A"`, `"API"` -> `"A P I"`, `"TTS"` -> `"T T S"`, `"STT"` -> `"S T T"`

#### 5. `jarvis/audio/tts.py`
- **`BaseTTSEngine` (ABC)**:
  - `async def synthesize(text: str, cancellation_token: Optional[CancellationToken] = None) -> np.ndarray`
  - `async def synthesize_stream(text_stream: AsyncIterator[str], cancellation_token: Optional[CancellationToken] = None) -> AsyncIterator[np.ndarray]`
- **`KokoroTTSEngine`**:
  - Generates 24kHz float32 mono speech using Kokoro-82M ONNX model.
  - Checks `cancellation_token.raise_if_cancelled()` before each chunk to allow instant abort.
  - Falls back to `MockTTSEngine` when ONNX model is absent.
- **`MockTTSEngine`**:
  - Generates synthetic 24kHz float32 harmonic audio proportional to text length for fast deterministic testing.

#### 6. `jarvis/audio/bargein.py`
- **`BargeInController`**:
  - Manages immediate audio cutoff with sub-50ms latency guarantee.
  - `trigger_bargein(reason: str = "speech_detected") -> float`:
    1. Immediately calls `audio_output_driver.abort_playback()`.
    2. Cancels active `CancellationToken` (stopping ongoing LLM streaming & synthesis).
    3. Clears queued TTS chunks.
    4. Invokes all registered barge-in callbacks.
    5. Measures and returns elapsed dispatch latency in milliseconds.
  - Fully idempotent (safe against double-cancel and idle triggers).

#### 7. `jarvis/audio/pipeline.py`
- **`AudioPipeline`**:
  - Master async coordinator tying together:
    - Input: `AudioInputDriver` + `RobustAudioSanitizer` + `CircularAudioBuffer` + `VADEngine`
    - Interruption: `BargeInController`
    - STT: `STTEngine`
    - Reasoning/Executive: `CognitiveExecutive.process_utterance()`
    - TTS: `SentenceChunker` + `TextNormalizer` + `TTSEngine` + `AudioOutputDriver`
  - Maintains state machine: `AudioState.IDLE`, `AudioState.LISTENING`, `AudioState.PROCESSING`, `AudioState.SPEAKING`, `AudioState.INTERRUPTED`.
  - Methods:
    - `async def start() -> None`
    - `async def stop() -> None`
    - `async def process_frame(frame: np.ndarray) -> None`
    - `async def process_utterance(audio_samples: np.ndarray) -> str`
    - `async def speak_text(text: str, cancellation_token: Optional[CancellationToken] = None) -> None`
    - `async def speak_stream(token_stream: AsyncIterator[str], cancellation_token: Optional[CancellationToken] = None) -> None`

#### 8. `jarvis/core/context.py`
- **`AudioSessionContext`**:
  - Tracks dialogue turns, audio state transitions, VAD energy levels, STT/TTS durations, and barge-in metrics.
  - Integrates session metrics into `PerceptionEvent.metadata` and working memory.

---

## 5. Verification Method

To verify the proposed implementation during and after Milestone 2 development:

1. **Unit Test Suite**:
   - `python -m pytest tests/unit/test_audio_pipeline.py -v`
   - `python -m pytest tests/unit/test_bargein.py -v`
   - `python -m pytest tests/unit -v`
2. **E2E Audio Test Suite**:
   - `python -m pytest tests/e2e/tier1_features/test_t1_audio_stt_vad.py -v`
   - `python -m pytest tests/e2e/tier1_features/test_t1_audio_tts_kokoro.py -v`
   - `python -m pytest tests/e2e/tier1_features/test_t1_audio_bargein.py -v`
   - `python -m pytest tests/e2e/tier2_boundaries/test_t2_audio_buffer_overflow_underrun.py -v`
   - `python -m pytest tests/e2e/tier2_boundaries/test_t2_bargein_rapid_interruption.py -v`
3. **Full Multi-Tier Regression Suite**:
   - `python tests/e2e/test_runner.py --tier all`
   - Must achieve **100% Pass Rate** across all tiers.

### Invalidation Conditions
- Any audio frame with `NaN` or `Inf` causes a crash or DAC glitch.
- Barge-In dispatch latency exceeds 50ms.
- TTS Time-To-First-Byte (TTFB) exceeds 300ms on streaming responses.
- Missing `faster_whisper` or physical sound hardware raises unhandled exceptions instead of falling back to virtual/mock drivers.
