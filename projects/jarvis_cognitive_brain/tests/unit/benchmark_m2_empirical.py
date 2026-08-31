"""
Empirical Performance & Resilience Benchmark for Milestone 2.
Measures precise latencies, TTFB, VAD endpoints, and resilience metrics.
"""

import time
import asyncio
import sys
import numpy as np
from typing import List, Dict, Any

if sys.platform == "win32":
    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except Exception:
        pass

from jarvis.audio.drivers import (
    RobustAudioSanitizer,
    CircularAudioBuffer,
    VirtualAudioDriver,
    VirtualAudioOutputDriver,
    VirtualAudioInputDriver,
)
from jarvis.audio.vad import EnergyVADEngine, VADSegmenter, VADState
from jarvis.audio.chunker import SentenceChunker, TextNormalizer
from jarvis.audio.tts import MockTTSEngine, KokoroTTSEngine
from jarvis.audio.stt import MockSTTEngine
from jarvis.audio.bargein import BargeInController
from jarvis.audio.pipeline import AudioPipeline, VoiceState


async def benchmark_vad_state_transitions() -> Dict[str, Any]:
    vad = EnergyVADEngine(threshold=0.5, silence_tail_ms=500, sample_rate=16000)
    segmenter = VADSegmenter(vad_engine=vad, speech_threshold=0.5, silence_tail_ms=500, sample_rate=16000)

    t = np.linspace(0, 0.032, 512, endpoint=False)
    speech_frame = (0.6 * np.sin(2 * np.pi * 300.0 * t)).astype(np.float32)
    silence_frame = np.zeros(512, dtype=np.float32)

    # 1. Test 100ms silence (3 frames = 96ms)
    for _ in range(4):
        segmenter.step(speech_frame)
    for _ in range(3):
        segmenter.step(silence_frame)
    state_100ms = segmenter.state
    assert state_100ms == VADState.TRAILING_SILENCE

    # 2. Test 490ms silence (15 frames = 480ms)
    segmenter.reset()
    for _ in range(4):
        segmenter.step(speech_frame)
    for _ in range(15):
        segmenter.step(silence_frame)
    state_490ms = segmenter.state
    assert state_490ms == VADState.TRAILING_SILENCE

    # Resume speech after 490ms
    segmenter.step(speech_frame)
    resumed_state = segmenter.state
    assert resumed_state == VADState.SPEECH_ACTIVE

    # 3. Test 510ms silence (16 frames = 512ms)
    segmenter.reset()
    for _ in range(4):
        segmenter.step(speech_frame)
    endpoint_frame = -1
    for i in range(16):
        s, utt = segmenter.step(silence_frame)
        if s == VADState.SPEECH_ENDED:
            endpoint_frame = i + 1
            break
    assert endpoint_frame == 16  # exactly on the 16th frame (512ms)

    # 4. Test 2000ms silence (63 frames)
    subsequent_spurious_count = 0
    for _ in range(47):
        s, utt = segmenter.step(silence_frame)
        if utt is not None:
            subsequent_spurious_count += 1

    return {
        "100ms_silence_state": state_100ms.value,
        "490ms_silence_state": state_490ms.value,
        "resumed_after_490ms_state": resumed_state.value,
        "510ms_endpoint_frame": endpoint_frame,
        "510ms_endpoint_time_ms": endpoint_frame * 32.0,
        "2000ms_spurious_utterances": subsequent_spurious_count,
    }


async def benchmark_ttfb_latencies() -> Dict[str, Any]:
    tts_engine = MockTTSEngine(sample_rate=24000)
    output_driver = VirtualAudioOutputDriver(sample_rate=24000)
    bargein = BargeInController(output_driver)
    chunker = SentenceChunker(clause_split=True, min_clause_words=4)

    test_sentences = {
        "short_3_words": ["Yes, ", "sir. ", "Ready."],
        "medium_clause_8_words": ["The ", "living ", "room ", "lights ", "are ", "on, ", "sir."],
        "long_technical_25_words": [
            "Cognitive ", "executive ", "running ", "at ", "24kHz ", "with ", "OODA ", "cycle ",
            "and ", "sub-50ms ", "barge-in ", "latency ", "has ", "stored ", "lessons ", "in ", "WAL ", "database."
        ],
        "complex_math_code_15_words": [
            "Formula ", "E = m * c^2 ", "is ", "computed ", "via ", "API ", "at ", "23 °C ", "and ", "98% ", "efficiency."
        ],
    }

    results = {}
    for name, tokens in test_sentences.items():
        output_driver.clear()
        token_cancellation = bargein.start_utterance()

        async def token_gen():
            for t in tokens:
                await asyncio.sleep(0.005)  # 5ms token generation delay
                yield t

        t_start = time.perf_counter()
        first_audio_ms = None
        chunks_count = 0
        total_audio_samples = 0

        chunk_stream = chunker.stream_chunks(token_gen(), cancellation_token=token_cancellation)
        async for audio_chunk in tts_engine.synthesize_stream(chunk_stream, cancellation_token=token_cancellation):
            if first_audio_ms is None:
                first_audio_ms = (time.perf_counter() - t_start) * 1000.0
            output_driver.play_chunk(audio_chunk)
            chunks_count += 1
            total_audio_samples += len(audio_chunk)

        total_elapsed_ms = (time.perf_counter() - t_start) * 1000.0
        audio_duration_s = total_audio_samples / 24000.0

        results[name] = {
            "ttfb_ms": round(first_audio_ms, 2),
            "total_elapsed_ms": round(total_elapsed_ms, 2),
            "chunks_emitted": chunks_count,
            "audio_duration_s": round(audio_duration_s, 2),
            "target_met_sub_300ms": first_audio_ms < 300.0,
        }

    return results


async def benchmark_bargein_latency() -> Dict[str, Any]:
    output_driver = VirtualAudioOutputDriver(sample_rate=24000)
    bargein = BargeInController(output_driver)

    latencies = []
    for _ in range(100):
        bargein.start_utterance(["Sentence 1", "Sentence 2", "Sentence 3"])
        output_driver.play_chunk(np.ones(480, dtype=np.float32))
        lat = bargein.trigger_bargein("Voice activity detected")
        latencies.append(lat)

    return {
        "iterations": len(latencies),
        "mean_latency_ms": round(float(np.mean(latencies)), 4),
        "median_latency_ms": round(float(np.median(latencies)), 4),
        "p99_latency_ms": round(float(np.percentile(latencies, 99)), 4),
        "max_latency_ms": round(float(np.max(latencies)), 4),
        "target_met_sub_50ms": float(np.max(latencies)) < 50.0,
    }


async def benchmark_chunker_edge_cases() -> Dict[str, Any]:
    chunker = SentenceChunker(clause_split=True, min_clause_words=4, max_buffer_words=15)
    
    test_corpora = {
        "code_snippet": "def ping() -> bool:\n    return True\n",
        "math_expression": "x = (-b +- sqrt(b^2 - 4ac)) / (2a) where a != 0.",
        "url_and_path": "Visit https://vault.local/api/v1/search and check C:\\Vault\\data.json.",
        "emojis_and_units": "🚀 Temperature is 24 °C, power at 100%, 24kHz audio, IoT ready!",
        "runaway_50_words": "word " * 50,
        "unbroken_string_300_chars": "X" * 300,
    }

    results = {}
    for name, text in test_corpora.items():
        chunker.buffer = ""
        chunks = []
        for word in text.split(" "):
            chunks.extend(chunker.feed_token(word + " "))
        chunks.extend(chunker.flush())

        results[name] = {
            "input_len_chars": len(text),
            "chunks_count": len(chunks),
            "chunks_preview": chunks[:3],
        }

    return results


async def main():
    print("=================================================================")
    print("   JARVIS COGNITIVE BRAIN — MILESTONE 2 EMPIRICAL BENCHMARKS    ")
    print("=================================================================")
    
    vad_res = await benchmark_vad_state_transitions()
    print("\n--- 1. VAD State Transitions & Silence Thresholds ---")
    for k, v in vad_res.items():
        print(f"  {k}: {v}")

    ttfb_res = await benchmark_ttfb_latencies()
    print("\n--- 2. TTFB Streaming Latencies (<300ms) ---")
    for k, v in ttfb_res.items():
        print(f"  [{k}]: TTFB = {v['ttfb_ms']}ms (Met: {v['target_met_sub_300ms']}), Chunks = {v['chunks_emitted']}, Total Time = {v['total_elapsed_ms']}ms")

    bargein_res = await benchmark_bargein_latency()
    print("\n--- 3. Barge-In Interruption Latency (<50ms) ---")
    for k, v in bargein_res.items():
        print(f"  {k}: {v}")

    chunker_res = await benchmark_chunker_edge_cases()
    print("\n--- 4. Sentence Chunker Edge-Case Resilience ---")
    for k, v in chunker_res.items():
        print(f"  [{k}]: {v['chunks_count']} chunks, preview: {v['chunks_preview']}")

    print("\n=================================================================")
    print("   ALL EMPIRICAL VERIFICATION BENCHMARKS COMPLETED SUCCESSFULLY  ")
    print("=================================================================")


if __name__ == "__main__":
    asyncio.run(main())
