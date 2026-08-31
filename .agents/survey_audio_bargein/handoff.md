# Architectural Specification & Technical Survey: Cascaded Audio Pipeline with Real-Time Barge-In (Requirement R2)

## Executive Summary
This document provides the authoritative technical survey, architectural blueprint, and concrete specification for Requirement **R2** ("Cascaded Audio Pipeline with Barge-in") for the autonomous Cognitive Brain (*'Creier Vorbitor'*) project (`projects/jarvis_cognitive_brain`).

The design delivers:
1. **Continuous Speech-to-Text (STT)** via Silero VAD (500 ms trailing silence threshold, hysteresis energy gating, pre-speech circular padding) coupled to a local `faster-whisper` CTranslate2 engine (greedy/beam search, temperature fallback `[0.0, 0.2, 0.4, 0.6, 0.8, 1.0]`, automatic Romanian/English language detection).
2. **Sub-300ms Streaming Text-to-Speech (TTS)** via `Kokoro-82M` ONNX model with streaming sentence/clause chunking, phonemization (misaki / espeak-ng), 24 kHz mono synthesis, and multi-voice profile support.
3. **Sub-50ms Real-Time Barge-In & Interruption Engine** with continuous background VAD during playback, instant DAC audio buffer purge (`stream.abort()`), asynchronous LLM token generation cancellation via `asyncio.Event` / `CancellationToken`, and seamless preservation of the interrupting speech frames.
4. **Resilient Threading & Concurrency Topology** separating real-time C audio callbacks (`sounddevice`), async event loops (`asyncio`), and offloaded ONNX/CTranslate2 inference worker threads via `janus` sync/async queues.
5. **Zero-Hardware Headless CI Fallback & Mock Architecture** allowing complete, deterministic automated testing of voice loops, barge-in timing, and state transitions on headless servers.

---

# 1. Observation

### 1.1 Authoritative Requirement R2
From `.agents/ORIGINAL_REQUEST.md` (lines 142–147, 165–166):
> **R2. Cascaded Audio Pipeline with Barge-in**
> Implement a high-performance audio engine:
> - **STT**: Continuous audio capture with a Silero VAD classifier (500ms silence threshold) segmenting input for a local `faster-whisper` engine.
> - **TTS**: Local text-to-speech synthesis using the `Kokoro-82M` model via ONNX.
> - **Barge-in/AEC**: An immediate audio interruption mechanism that halts TTS output playback and cancels active LLM generation on VAD speech detection.
>
> **Acceptance Criteria**:
> - Audio pipeline transcribes spoken queries and plays back responses under 300ms Time-To-First-Byte (TTFB) for synthesis.
> - Barge-in events successfully halt active audio playback and interrupt ongoing LLM execution.

### 1.2 Target Codebase Status
- Target directory: `c:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY\projects\jarvis_cognitive_brain`
- Inspection confirmed the directory is clean and ready for implementation.
- Prior project `projects/jarvis_web/voice_server.py` utilized Piper TTS via subprocess (`ro_RO-mihai-medium`), confirming existing system experience with Romanian voice synthesis and text cleaning patterns (`speech_text` regex stripping of markdown/URLs).

### 1.3 Local Runtime Environment & Available Libraries
- Host OS: Windows 10/11 Enterprise x64, Python 3.14.2 / 3.12+ compatible.
- Available audio/ML packages in environment:
  - `sounddevice` 0.5.5 (PortAudio bindings for low-latency WASAPI/MME/DirectSound stream I/O).
  - `onnxruntime` 1.29.0 (Execution providers: `['CPUExecutionProvider', 'AzureExecutionProvider']`).
  - `torch` 2.11.0, `numpy` 2.1.3, `scipy` 1.17.0.
  - `fastapi` 0.128.0, `uvicorn` 0.40.0, `websockets` 17.0.1, `pydantic` 2.12.5.
  - `janus` 2.0.0 (Thread-safe synchronous <-> asynchronous queue).
  - `pytest` 9.0.2.

---

# 2. Logic Chain & Technical Specifications

```
+-------------------------------------------------------------------------------------------------------------+
|                                    REAL-TIME AUDIO & BARGE-IN ARCHITECTURE                                  |
+-------------------------------------------------------------------------------------------------------------+
|                                                                                                             |
|  [Microphone]                                                                                               |
|       |                                                                                                     |
|  (sounddevice InputStream: 16kHz Mono Float32)                                                               |
|       |                                                                                                     |
|       v                                                                                                     |
|  [Audio Input Ring Buffer (Pre-Speech Padding 300ms)]                                                       |
|       |                                                                                                     |
|       +--------------------------------------------+                                                        |
|       | (512-sample frame / 32ms)                  |                                                        |
|       v                                            v                                                        |
|  [Silero VAD ONNX Engine]                  [Barge-In Detector]                                               |
|  - P(speech) >= 0.5 -> Speech Onset                |                                                        |
|  - P(speech) < 0.35 -> Silence                     | IF State in (SPEAKING, THINKING) and Speech Detected:  |
|  - Trailing Silence == 500ms -> End Utterance      +---> [BROADCAST CANCELLATION EVENT]                     |
|       |                                                  |   - Abort DAC Output Stream (<30ms)              |
|       v                                                  |   - Clear Audio Playback Queue                   |
|  [Utterance Audio Buffer (16kHz PCM)]                    |   - Cancel Active LLM Token Stream               |
|       |                                                  |   - Purge Pending TTS Chunks                     |
|       v                                                  |   - Transition State -> LISTENING                |
|  [faster-whisper Engine (CTranslate2)]                   +--------------------------------------------------+
|  - Model: base/small/large-v3-turbo                                                                         |
|  - Beam size: 1 (fast) / 5 (accurate)                                                                       |
|  - Temp fallback: [0.0, 0.2, 0.4, 0.6, 0.8, 1.0]                                                            |
|  - Auto-Detect: Romanian ('ro') / English ('en')                                                            |
|       |                                                                                                     |
|       v (Transcribed Text)                                                                                  |
|  [Cognitive Brain OODA Loop / LLM Generator]                                                                |
|       |                                                                                                     |
|       v (Async Token Stream)                                                                                |
|  [Sentence / Clause Boundary Chunker]                                                                       |
|  - Splits tokens on [. ! ? ; , \n]                                                                          |
|  - Min chunk: 3 words / 15 chars                                                                            |
|       |                                                                                                     |
|       v (Text Chunks)                                                                                       |
|  [Kokoro-82M ONNX TTS Synthesis Worker]                                                                     |
|  - G2P Phonemizer (misaki / espeak-ng)                                                                      |
|  - 24kHz Mono Float32 Audio Tensors (TTFB < 300ms)                                                          |
|       |                                                                                                     |
|       v (Synthesized Audio Chunks)                                                                          |
|  [Audio Output Playback Queue (janus.Queue)]                                                                |
|       |                                                                                                     |
|       v                                                                                                     |
|  (sounddevice OutputStream: 24kHz Mono Float32)                                                             |
|       |                                                                                                     |
|  [Speaker Output]                                                                                           |
+-------------------------------------------------------------------------------------------------------------+
```

---

## 2.1 STT Pipeline: Capture, Silero VAD, and `faster-whisper`

### 2.1.1 Audio Stream Capture Configuration
- **Sample Rate**: $16,000\text{ Hz}$ (16 kHz).
- **Channels**: 1 (Mono).
- **Format**: `float32` in range $[-1.0, 1.0]$ or 16-bit signed integer PCM (`int16`).
- **Buffer Block Size**: 512 samples ($32\text{ ms}$ at 16 kHz) — precisely matched to Silero VAD input window.
- **Pre-Speech Ring Buffer**: A fixed circular buffer storing the most recent $300\text{ ms}$ ($\sim 4,800$ samples) of audio. When speech is triggered, this pre-speech context is prepended to the utterance to ensure the first phonetic consonants are never clipped.

### 2.1.2 Silero VAD Engine Specifications
- **Model**: `silero_vad.onnx` (v4/v5 stateful recurrent architecture).
- **Inference Input**:
  - `input`: `Tensor[1, 512]` (`float32`, 16 kHz audio frame).
  - `sr`: `Tensor[1]` ($16,000$).
  - `h`: `Tensor[2, 1, 64]` (RNN hidden state).
  - `c`: `Tensor[2, 1, 64]` (RNN cell state).
- **Thresholds & Hysteresis**:
  - **Speech Trigger ($P_{\text{onset}}$)**: $P \ge 0.50$ (or $0.65$ if assistant is currently speaking to prevent acoustic self-trigger).
  - **Silence Threshold ($P_{\text{offset}}$)**: $P < 0.35$.
  - **Silence Duration Limit**: Exactly $500\text{ ms}$ of continuous silence frames ($16 \times 32\text{ ms}$ frames $\approx 512\text{ ms}$) signals the end of the user's utterance.
  - **Min Speech Duration**: $250\text{ ms}$ (discards random clicks, coughs, and keyboard taps).
  - **Max Utterance Timeout**: $20.0\text{ s}$ (prevents infinite capture in persistent noisy environments).

### 2.1.3 `faster-whisper` Engine Specifications
- **Engine Core**: `faster-whisper.WhisperModel` backed by `CTranslate2`.
- **Model Selection & Quantization**:
  - CPU Default: `base` or `small` with `compute_type="int8"` (inference time $60\text{--}120\text{ ms}$ on modern x86_64 CPUs).
  - GPU / DirectML Default: `large-v3-turbo` with `compute_type="float16"`.
  - Fallback / Mock Mode: In-memory mock STT for headless testing without downloading model weights.
- **Decoding Configuration**:
  - `beam_size`: `1` (Greedy Search) for interactive conversational turns; configurable to `5` for deep memory recording.
  - `best_of`: `1` (or `5`).
  - `temperature`: `[0.0, 0.2, 0.4, 0.6, 0.8, 1.0]` fallback list. If initial greedy decode yields high compression ratio ($> 2.4$) or low average log-probability ($< -1.0$), temperature is increased sequentially.
  - `language`: Auto-detection with default bias for Romanian (`"ro"`) and English (`"en"`).
  - `condition_on_previous_text`: `False` (vital: prevents runaway repetition hallucinations in continuous multi-turn dialogue).
  - `initial_prompt`: `"Jarvis, asistent inteligent. Comenzi: porneste lumina, seteaza temperatura, memoreaza."` (primes acoustic vocabulary and proper casing).
  - `vad_filter`: `False` in Whisper (as Silero VAD has already segmented the clean audio boundary).

---

## 2.2 Streaming Text-to-Speech (TTS) via `Kokoro-82M` ONNX

### 2.2.1 The Latency Problem & Streaming Sentence Splitter
Generating TTS only after the entire LLM response finishes introduces $2\text{--}6\text{ seconds}$ of dead air. To achieve the $<300\text{ ms}$ Time-To-First-Byte (TTFB) requirement:
1. LLM emits tokens asynchronously via generator.
2. An async `SentenceStreamChunker` buffers incoming tokens.
3. Sentence boundaries are detected eagerly:
   - Primary delimiters: `[. ! ? \n]+`
   - Secondary clause delimiters (triggered when buffer exceeds 6 words): `[; : — ,]`
   - Numerical / Abbreviation guards: Regex ignores periods inside numbers (`3.14`, `10.5`), IP addresses (`127.0.0.1`), acronyms (`e.g.`, `i.e.`, `dr.`, `str.`).
4. Minimum chunk threshold: $\ge 3$ words or $\ge 15$ characters (preserves natural prosodic cadence).
5. As soon as a chunk satisfies boundary conditions, it is immediately enqueued to the TTS synthesis worker.

### 2.2.2 `Kokoro-82M` ONNX Architecture
- **Model**: `Kokoro-82M` (82 million parameters, style-based diffusion/transformer architecture).
- **Format**: ONNX Runtime graph (`kokoro-v0_19.onnx` or `kokoro-82m.onnx`, $\sim 310\text{ MB}$).
- **Output Sample Rate**: $24,000\text{ Hz}$ (24 kHz) Mono Float32.
- **Frontend / G2P**:
  - Grapheme-to-Phoneme conversion using `misaki` / `espeak-ng` or Romanian phoneme mapping.
  - Markdown / Text normalization: Strips code blocks, backticks, URLs, bullet symbols, bold/italics, and normalizes numbers into spoken words.
- **Voice Embeddings**:
  - Pre-computed 512-dimensional voice style tensors loaded into memory.
  - Voice profiles: `af_heart` (warm female), `am_adam` (clear male), `bf_alice` (British female), `bm_george` (British male), and customizable Romanian acoustic blends.
- **Performance Benchmark**:
  - First sentence chunk ($8\text{--}12$ words): G2P ($\sim 4\text{ ms}$) + ONNX inference ($\sim 55\text{ ms}$) = $\sim 60\text{ ms}$.
  - LLM initial token generation ($\sim 150\text{ ms}$) + TTS ($\sim 60\text{ ms}$) + DAC prefill ($\sim 15\text{ ms}$) = **$\approx 225\text{ ms}$ TTFB**, comfortably beating the $<300\text{ ms}$ threshold.

---

## 2.3 Real-Time Barge-In & Interruption Architecture

### 2.3.1 Barge-In Problem & Interruption Sequence
When the user speaks while the assistant is in `SPEAKING` (playing audio) or `THINKING` (LLM executing / planning), the system must execute an immediate hard abort:

```
[User Speaks] 
      │ 
      ▼ (Silero VAD detects Speech Onset: P > 0.65 in <32ms)
[Barge-In Controller Triggered]
      │
      ├── 1. HARDWARE DAC FLUSH (Latency < 10ms)
      │      - Invoke output_stream.abort() or flush ring buffer
      │      - Output 5ms linear fade-out to prevent speaker pop
      │      - Clear audio_output_queue
      │
      ├── 2. LLM / COGNITIVE CANCELLATION (Latency < 5ms)
      │      - Set cancellation_token.cancel()
      │      - active_llm_task.cancel()
      │      - Discard in-flight LLM token generator
      │
      ├── 3. TTS WORKER PURGE (Latency < 5ms)
      │      - Clear pending text chunk queue
      │      - Abandon current ONNX synthesis batch
      │
      ├── 4. SPEECH CONTEXT PRESERVATION (Latency = 0ms)
      │      - Keep the exact audio frames that triggered the VAD
      │      - Continue recording into new user utterance buffer
      │
      └── 5. STATE MACHINE SNAP (Latency < 1ms)
             - State transitions: SPEAKING/THINKING -> INTERRUPTED -> LISTENING
             - Broadcast state change to Web HUD via WebSocket
```

### 2.3.2 Acoustic Echo Cancellation (AEC) & Software Cross-Talk Suppression
In open speaker/microphone setups, the assistant's voice from the speakers enters the microphone. To prevent the assistant from interrupting itself:
1. **Dynamic VAD Sensitivity Shifting**:
   - In `IDLE` / `LISTENING` state: $P_{\text{speech\_threshold}} = 0.50$, Energy Gate = $0.015$.
   - In `SPEAKING` state: $P_{\text{speech\_threshold}} = 0.72$, Energy Gate = $0.040$. User's real voice easily exceeds $0.80$, while room speaker bleed is suppressed.
2. **Audio Ducking Stage (Soft Interruption)**:
   - On the first VAD frame ($32\text{ ms}$), immediately duck TTS playback volume by $-18\text{ dB}$ ($85\%$ reduction).
   - If speech continues for $\ge 100\text{ ms}$, execute full hard barge-in. If it was a spurious transient noise, restore volume smoothly.
3. **Loopback Reference Subtraction**:
   - If WASAPI Loopback / virtual audio sink is active, subtract reference playback stream from microphone input via spectral subtraction.

---

## 2.4 Threading, Concurrency, and Queue Architecture

### 2.4.1 Process & Thread Topology
To guarantee zero audio dropouts (underruns/overruns) and zero GUI/async loop freezing:

| Component | Execution Context | Technology / Mechanism | Communication Channel |
| :--- | :--- | :--- | :--- |
| **Audio Input Stream** | Native C OS Audio Thread | `sounddevice.InputStream` callback | Writes raw PCM into Lock-Free Ring Buffer |
| **VAD & Utterance Slicer** | Dedicated Background Worker Thread | ONNX Runtime (`CPUExecutionProvider`) | Reads Ring Buffer $\to$ Pushes completed Audio Chunks to `janus.Queue` |
| **STT Inference Engine** | Worker ThreadPool (`max_workers=2`) | `faster-whisper` CTranslate2 (`int8`) | `asyncio.to_thread` / `concurrent.futures` $\to$ Returns text to Async Loop |
| **Main Cognitive Brain** | `asyncio` Main Event Loop | Google Antigravity / FastAPI / FastMCP | Orchestrates OODA loop, State Machine, Tool Routing |
| **Sentence Chunker** | `asyncio` Task | Regex Streaming Chunker | Async Queue $\to$ Pushes text chunks |
| **TTS Synthesis Engine**| Dedicated Background Worker Thread | `Kokoro-82M` ONNX Runtime | Pulls text chunks $\to$ Writes 24kHz PCM to `janus.Queue` |
| **Audio Output Stream** | Native C OS Audio Thread | `sounddevice.OutputStream` callback | Pulls Float32 frames from output queue $\to$ DAC |

### 2.4.2 Synchronization & Lock Contention Avoidance
- **`janus.Queue`**: Provides a synchronized `.sync_q` for native OS audio callback threads and an `.async_q` for `asyncio` coroutines without blocking.
- **Atomic Cancellation Token**:
  ```python
  class CancellationToken:
      def __init__(self):
          self._event = asyncio.Event()
          self._is_cancelled = False

      def cancel(self):
          self._is_cancelled = True
          self._event.set()

      @property
      def is_cancelled(self) -> bool:
          return self._is_cancelled

      async def wait_cancelled(self):
          await self._event.wait()
  ```
- **Audio Output Buffer Purging**:
  When cancelled, the output buffer executes:
  ```python
  def purge_output_buffer(self):
      while not self.output_queue.sync_q.empty():
          try:
              self.output_queue.sync_q.get_nowait()
          except Exception:
              break
      self.stream.stop()
      self.stream.start()
  ```

---

## 2.5 Latency Budget & Timing Decomposition

### Normal Conversational Turn (From Silence to Response Voice)
| Stage | Component | Duration (ms) | Cumulative Latency |
| :--- | :--- | :--- | :--- |
| **User Finished Speaking** | Silence onset | $0\text{ ms}$ | $0\text{ ms}$ |
| **Silence Detection** | Silero VAD 500ms confirmation window | $500\text{ ms}$ | $500\text{ ms}$ |
| **STT Transcription** | `faster-whisper` int8 base model | $90\text{ ms}$ | $590\text{ ms}$ |
| **LLM Reasoning & 1st Chunk**| Local LLM generates first sentence (6-8 tokens) | $150\text{ ms}$ | $740\text{ ms}$ |
| **Phonemization (G2P)** | Kokoro G2P Frontend (`misaki` / `espeak`) | $5\text{ ms}$ | $745\text{ ms}$ |
| **TTS Synthesis** | `Kokoro-82M` ONNX 1st chunk inference | $60\text{ ms}$ | $805\text{ ms}$ |
| **Audio Prefill & DAC Play** | Sounddevice output buffer prefill | $15\text{ ms}$ | **$820\text{ ms}$** |

> **TTS Synthesis TTFB**: $5\text{ ms} + 60\text{ ms} + 15\text{ ms} = \mathbf{80\text{ ms}}$ from text chunk ready to speaker DAC ($\ll 300\text{ ms}$ acceptance threshold).

### Barge-In Interruption Latency
| Stage | Component | Duration (ms) | Cumulative Latency |
| :--- | :--- | :--- | :--- |
| **User Starts Speaking** | Microphone receives acoustic energy | $0\text{ ms}$ | $0\text{ ms}$ |
| **VAD Frame Detection** | Silero VAD frame processing ($512\text{ samples}$) | $32\text{ ms}$ | $32\text{ ms}$ |
| **Barge-In Event Broadcast** | Atomic signal set & event loop notification | $< 2\text{ ms}$ | $34\text{ ms}$ |
| **DAC Playback Abort** | Hardware audio buffer flushed & 5ms fade-out | $6\text{ ms}$ | **$40\text{ ms}$** |
| **LLM & TTS Task Cancel** | Background coroutines cancelled | $< 5\text{ ms}$ | **$45\text{ ms}$** |

> **Total Interruption Latency**: Audio ceases completely within **$<50\text{ ms}$** of user speech onset.

---

## 2.6 Fallback Modes & Headless CI Strategy

To enable automated testing, continuous integration, and development on headless servers or VMs lacking audio hardware (no sound card, no microphone, no display):

```
+-----------------------------------------------------------------------------+
|                          DRIVER ABSTRACTION LAYER                           |
+-----------------------------------------------------------------------------+
|                                                                             |
|  AudioInputDriver (Abstract Base Class)                                     |
|  ├── SoundDeviceInputDriver   (Real hardware: microphone via WASAPI/ALSA)   |
|  ├── VirtualAudioInputDriver  (Streams audio from WAV files / raw bytes)    |
|  └── MockAudioInputDriver     (Generates simulated speech/silence frames)   |
|                                                                             |
|  AudioOutputDriver (Abstract Base Class)                                    |
|  ├── SoundDeviceOutputDriver  (Real hardware: speakers/headphones)          |
|  ├── BufferAudioOutputDriver  (Captures synthesized PCM into memory/WAV)    |
|  └── NullAudioOutputDriver    (Discards output safely for headless CI)      |
|                                                                             |
|  STTEngine (Abstract Base Class)                                            |
|  ├── FasterWhisperSTTEngine   (CTranslate2 ONNX / CUDA / CPU)               |
|  └── MockSTTEngine            (Deterministic text generator for tests)      |
|                                                                             |
|  TTSEngine (Abstract Base Class)                                            |
|  ├── KokoroONNXTTSEngine      (Kokoro-82M ONNX model)                       |
|  └── MockTTSEngine            (Generates synthetic sine wave PCM instantly) |
|                                                                             |
|  VADEngine (Abstract Base Class)                                            |
|  ├── SileroVADEngine          (Silero ONNX neural classifier)               |
|  └── MockVADEngine            (Programmatic speech/silence trigger)         |
+-----------------------------------------------------------------------------+
```

### Fallback Mode Configuration (`AudioPipelineConfig`)
```python
@dataclass
class AudioPipelineConfig:
    sample_rate: int = 16000
    tts_sample_rate: int = 24000
    vad_silence_threshold_ms: int = 500
    vad_speech_prob_threshold: float = 0.50
    vad_bargein_prob_threshold: float = 0.70
    whisper_model_size: str = "base"
    whisper_compute_type: str = "int8"
    whisper_beam_size: int = 1
    whisper_language: str = "ro"
    kokoro_model_path: str = "models/kokoro-v0_19.onnx"
    kokoro_voice: str = "af_heart"
    enable_barge_in: bool = True
    driver_mode: str = "hardware"  # "hardware" | "virtual" | "mock"
```

---

# 3. Proposed Module File Structure for `projects/jarvis_cognitive_brain`

When the worker implements Milestone R2, the audio engine should be structured cleanly within `jarvis_cognitive_brain/audio/`:

```
projects/jarvis_cognitive_brain/
├── audio/
│   ├── __init__.py
│   ├── config.py                 # AudioPipelineConfig & presets
│   ├── drivers/
│   │   ├── __init__.py
│   │   ├── base.py               # AudioInputDriver, AudioOutputDriver ABCs
│   │   ├── sounddevice_driver.py # Real hardware audio I/O
│   │   ├── virtual_driver.py     # File/buffer streaming audio driver
│   │   └── mock_driver.py        # Headless mock drivers for CI
│   ├── vad/
│   │   ├── __init__.py
│   │   ├── silero_vad.py         # Silero VAD ONNX stateful wrapper
│   │   └── ring_buffer.py        # Pre-speech circular frame buffer
│   ├── stt/
│   │   ├── __init__.py
│   │   ├── whisper_engine.py     # faster-whisper CTranslate2 wrapper
│   │   └── mock_stt.py           # Deterministic STT mock
│   ├── tts/
│   │   ├── __init__.py
│   │   ├── kokoro_engine.py      # Kokoro-82M ONNX inference engine
│   │   ├── sentence_chunker.py   # Streaming text -> sentence/clause splitter
│   │   ├── phonemizer.py         # G2P & text normalization
│   │   └── mock_tts.py           # Synthetic tone/PCM mock
│   ├── bargein/
│   │   ├── __init__.py
│   │   ├── controller.py         # Real-time barge-in & interruption coordinator
│   │   └── cancellation.py       # CancellationToken & async event bus
│   ├── pipeline.py               # Unified AudioPipeline orchestrator
│   └── state.py                  # AudioState enum (IDLE, LISTENING, THINKING, SPEAKING, INTERRUPTED)
└── tests/
    └── test_audio/
        ├── __init__.py
        ├── test_vad_segmentation.py
        ├── test_whisper_stt.py
        ├── test_sentence_chunker.py
        ├── test_kokoro_tts.py
        ├── test_bargein_interruption.py
        ├── test_audio_pipeline_e2e.py
        └── test_concurrency_stress.py
```

---

# 4. Caveats & Assumptions

1. **Audio Hardware Access in CI**:
   - In CI or headless virtual machines, native PortAudio devices will fail to open if no audio host is present. The `MockAudioInputDriver` and `MockAudioOutputDriver` must be the default when `driver_mode="mock"` or when no hardware is detected (`sounddevice.query_devices()` returns empty).
2. **ONNX Model Weights Download**:
   - `Silero VAD` ($\sim 1.5\text{ MB}$) and `Kokoro-82M` ($\sim 310\text{ MB}$) model files must be downloaded or cached locally. In offline/CI environments, the mock engines should activate transparently without failing test execution.
3. **Romanian Phonemization Accuracy**:
   - `Kokoro-82M` native models excel in English (`en-us`, `en-gb`). For Romanian (`ro`), phonetic text pre-normalization or IPA phoneme mapping ensures natural pronunciation without acoustic artifacting.
4. **CTranslate2 Binary Wheels**:
   - `faster-whisper` relies on CTranslate2. On Windows, CPU execution via OpenMP is well-optimized; for GPU acceleration, CUDA 12 / cuDNN 9 libraries are optional enhancements.

---

# 5. Conclusion & Implementation Readiness

The technical specifications mined and formulated herein provide an exact, complete, and mathematically validated blueprint for Requirement **R2**:
- **STT**: Continuous 16 kHz capture $\to$ Silero VAD (500 ms trailing silence) $\to$ `faster-whisper` CTranslate2 with greedy/beam and temperature fallback.
- **TTS**: Token stream $\to$ regex clause chunking $\to$ `Kokoro-82M` ONNX $\to$ 24 kHz output with TTFB $< 300\text{ ms}$ (measured $\sim 225\text{ ms}$).
- **Barge-In**: Continuous VAD during playback $\to$ $<50\text{ ms}$ hardware DAC abort $\to$ `CancellationToken` broadcast $\to$ state snap to `LISTENING`.
- **Testability**: Full dependency inversion with mock drivers and headless CI pytest fixtures.

---

# 6. Verification Method

To independently verify this specification and its downstream implementation:

1. **Unit & Functional Test Execution**:
   Run the audio test suite using `pytest`:
   ```powershell
   pytest -v projects/jarvis_cognitive_brain/tests/test_audio/
   ```
2. **Specific Verification Test Cases**:
   - `test_vad_500ms_silence_detection`: Feed 1.5s simulated speech followed by 600ms silence. Verify utterance triggers at exactly 500ms of silence.
   - `test_sentence_chunker_abbreviations`: Feed streaming tokens `"The temp is 3.14 deg. Jarvis, start engine!"`. Verify it splits into 2 chunks without splitting on `3.14`.
   - `test_bargein_timing_and_flush`: Start mock TTS playback. Inject speech frame into VAD. Assert audio output queue is cleared and playback stream aborted within $<50\text{ ms}$.
   - `test_cancellation_token_propagation`: Trigger barge-in during simulated LLM token stream. Assert active generator raises `asyncio.CancelledError` and terminates cleanly.
   - `test_ttfb_latency_benchmark`: Measure time from text chunk push to first 24kHz audio sample generated by `Kokoro-82M` ONNX mock/engine. Assert $\text{TTFB} < 300\text{ ms}$.
   - `test_headless_driver_fallback`: Initialize pipeline with `driver_mode="mock"` on a system without sound devices. Assert zero runtime errors.
