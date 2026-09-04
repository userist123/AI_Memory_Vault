"""
Audio Hardware Drivers and In-Memory Virtual Streams for Jarvis Cognitive Brain.
Provides hardware abstractions for physical sounddevice I/O and deterministic virtual streams.
"""

from abc import ABC, abstractmethod
from enum import Enum
import queue
import time
import threading
from typing import Optional, Callable, List, Dict, Any, Union
import numpy as np


class AudioDriverState(str, Enum):
    """Lifecycle state of an audio driver."""
    UNINITIALIZED = "uninitialized"
    RUNNING = "running"
    STOPPED = "stopped"
    ERROR = "error"


class AudioDriverError(Exception):
    """Base exception for audio driver operations."""
    pass


class AudioDeviceNotFoundError(AudioDriverError):
    """Raised when hardware audio devices cannot be located or opened."""
    pass


class AudioBufferOverflowError(AudioDriverError):
    """Raised when audio ring buffers or driver queues overflow."""
    pass


class RobustAudioSanitizer:
    """
    Sanitizes raw audio frames before passing to VAD, Whisper, or DAC hardware.
    Enforces 1D float32 representation, removes NaNs and Infs, and clamps amplitudes.
    """

    @staticmethod
    def sanitize(frame: Optional[Any]) -> np.ndarray:
        """
        Sanitize an incoming audio buffer.
        - None or empty -> returns empty float32 array
        - NaN / Inf -> replaced with 0.0 (silence)
        - Clipping -> hard clamped strictly to [-1.0, 1.0]
        """
        if frame is None:
            return np.zeros(0, dtype=np.float32)

        try:
            sanitized = np.asarray(frame, dtype=np.float32)
        except Exception:
            return np.zeros(0, dtype=np.float32)

        if sanitized.size == 0:
            return np.zeros(0, dtype=np.float32)

        sanitized = np.atleast_1d(sanitized)
        if sanitized.ndim > 1:
            sanitized = sanitized.flatten()

        # Replace non-finite samples (NaN, Inf, -Inf) with silence
        invalid_mask = ~np.isfinite(sanitized)
        if np.any(invalid_mask):
            sanitized = sanitized.copy()
            sanitized[invalid_mask] = 0.0

        # Hard clamp between -1.0 and 1.0 to protect speakers and prevent overflow
        sanitized = np.clip(sanitized, -1.0, 1.0)
        return np.ascontiguousarray(sanitized, dtype=np.float32)


class CircularAudioBuffer:
    """
    Thread-safe circular ring buffer preserving pre-trigger speech frames
    and preventing memory leaks during continuous streaming.
    """

    def __init__(self, max_seconds: float = 30.0, sample_rate: int = 16000):
        self.max_seconds = max_seconds
        self.sample_rate = sample_rate
        self.max_samples = int(max_seconds * sample_rate)
        self.buffer = np.zeros(self.max_samples, dtype=np.float32)
        self.write_pos: int = 0
        self.total_written: int = 0
        self._lock = threading.Lock()

    def write(self, chunk: np.ndarray) -> None:
        """Write a chunk of float32 audio samples into the circular ring buffer."""
        sanitized = RobustAudioSanitizer.sanitize(chunk)
        chunk_len = len(sanitized)
        if chunk_len == 0:
            return

        with self._lock:
            if chunk_len >= self.max_samples:
                # Overwrite entire buffer with tail of chunk
                self.buffer[:] = sanitized[-self.max_samples:]
                self.write_pos = 0
                self.total_written += chunk_len
                return

            end_pos = (self.write_pos + chunk_len) % self.max_samples
            if self.write_pos + chunk_len <= self.max_samples:
                self.buffer[self.write_pos : self.write_pos + chunk_len] = sanitized
            else:
                first_part = self.max_samples - self.write_pos
                self.buffer[self.write_pos :] = sanitized[:first_part]
                self.buffer[:end_pos] = sanitized[first_part:]

            self.write_pos = end_pos
            self.total_written += chunk_len

    def get_recent(self, num_samples: int) -> np.ndarray:
        """Retrieve the most recent `num_samples` contiguous audio slice."""
        with self._lock:
            if self.total_written == 0 or num_samples <= 0:
                return np.empty((0,), dtype=np.float32)
            num = min(num_samples, self.max_samples, self.total_written)
            start_pos = (self.write_pos - num) % self.max_samples
            if start_pos + num <= self.max_samples:
                return self.buffer[start_pos : start_pos + num].copy()
            else:
                part1 = self.buffer[start_pos:]
                part2 = self.buffer[: (start_pos + num) % self.max_samples]
                return np.concatenate([part1, part2])

    def clear(self) -> None:
        """Flush and zero the circular buffer."""
        with self._lock:
            self.buffer.fill(0.0)
            self.write_pos = 0
            self.total_written = 0

    @property
    def capacity(self) -> int:
        return self.max_samples


class BaseAudioInputDriver(ABC):
    """Abstract interface for audio capture devices."""

    def __init__(self, sample_rate: int = 16000, channels: int = 1, chunk_size: int = 512):
        self.sample_rate = sample_rate
        self.channels = channels
        self.chunk_size = chunk_size
        self.state = AudioDriverState.UNINITIALIZED

    @abstractmethod
    def start(self) -> None:
        """Start capturing audio stream."""
        pass

    @abstractmethod
    def stop(self) -> None:
        """Stop capturing audio stream."""
        pass

    @abstractmethod
    def read_chunk(self, timeout: float = 0.5) -> Optional[np.ndarray]:
        """Read a single chunk (512 samples) of float32 mono audio."""
        pass

    @abstractmethod
    def register_callback(self, callback: Callable[[np.ndarray], None]) -> None:
        """Register a chunk listener callback."""
        pass

    def start_stream(self, callback: Optional[Callable[[np.ndarray], None]] = None) -> None:
        """Convenience method to register callback and start stream."""
        if callback:
            self.register_callback(callback)
        self.start()

    def stop_stream(self) -> None:
        """Convenience alias for stop()."""
        self.stop()

    @property
    def is_active(self) -> bool:
        return self.state == AudioDriverState.RUNNING


class SoundDeviceInputDriver(BaseAudioInputDriver):
    """Hardware microphone capture using sounddevice."""

    def __init__(
        self,
        sample_rate: int = 16000,
        channels: int = 1,
        chunk_size: int = 512,
        device_id: Optional[Union[int, str]] = None,
        max_queue_size: int = 200,
    ):
        super().__init__(sample_rate, channels, chunk_size)
        self.device_id = device_id
        self.max_queue_size = max_queue_size
        self._queue: queue.Queue[np.ndarray] = queue.Queue(maxsize=max_queue_size)
        self._callbacks: List[Callable[[np.ndarray], None]] = []
        self._stream = None
        self._lock = threading.Lock()
        self.dropped_frames: int = 0

    def _audio_callback(self, indata: np.ndarray, frames: int, time_info: Any, status: Any) -> None:
        if status:
            pass  # Overflow / underflow status flag
        chunk = RobustAudioSanitizer.sanitize(indata[:, 0] if indata.ndim > 1 else indata)
        try:
            self._queue.put_nowait(chunk)
        except queue.Full:
            self.dropped_frames += 1
            try:
                _ = self._queue.get_nowait()
                self._queue.put_nowait(chunk)
            except Exception:
                pass

        for cb in self._callbacks:
            try:
                cb(chunk)
            except Exception:
                pass

    def start(self) -> None:
        with self._lock:
            if self.state == AudioDriverState.RUNNING:
                return
            try:
                import sounddevice as sd
                self._stream = sd.InputStream(
                    samplerate=self.sample_rate,
                    channels=self.channels,
                    dtype="float32",
                    blocksize=self.chunk_size,
                    device=self.device_id,
                    callback=self._audio_callback,
                )
                self._stream.start()
                self.state = AudioDriverState.RUNNING
            except Exception as e:
                self.state = AudioDriverState.ERROR
                raise AudioDeviceNotFoundError(f"Failed to start sounddevice microphone: {e}") from e

    def stop(self) -> None:
        with self._lock:
            if self._stream is not None:
                try:
                    self._stream.stop()
                    self._stream.close()
                except Exception:
                    pass
                self._stream = None
            self.state = AudioDriverState.STOPPED

    def read_chunk(self, timeout: float = 0.5) -> Optional[np.ndarray]:
        try:
            return self._queue.get(timeout=timeout)
        except queue.Empty:
            return None

    def register_callback(self, callback: Callable[[np.ndarray], None]) -> None:
        if callback not in self._callbacks:
            self._callbacks.append(callback)


class VirtualAudioInputDriver(BaseAudioInputDriver):
    """In-memory virtual audio input stream for automated testing."""

    def __init__(self, sample_rate: int = 16000, chunk_size: int = 512):
        super().__init__(sample_rate=sample_rate, channels=1, chunk_size=chunk_size)
        self._queue: queue.Queue[np.ndarray] = queue.Queue()
        self._callbacks: List[Callable[[np.ndarray], None]] = []
        self.recorded_frames: List[np.ndarray] = []

    def start(self) -> None:
        self.state = AudioDriverState.RUNNING

    def stop(self) -> None:
        self.state = AudioDriverState.STOPPED

    def push_audio(self, audio_data: np.ndarray) -> None:
        """Push raw float32 audio samples, automatically chunking into block sizes."""
        sanitized = RobustAudioSanitizer.sanitize(audio_data)
        if len(sanitized) == 0:
            return

        for i in range(0, len(sanitized), self.chunk_size):
            chunk = sanitized[i : i + self.chunk_size]
            if len(chunk) < self.chunk_size:
                chunk = np.pad(chunk, (0, self.chunk_size - len(chunk)))
            self._queue.put(chunk)
            self.recorded_frames.append(chunk)
            for cb in self._callbacks:
                try:
                    cb(chunk)
                except Exception:
                    pass

    def generate_sine_wave(self, duration_s: float, freq_hz: float = 440.0, amplitude: float = 0.5) -> np.ndarray:
        """Generate float32 sine wave."""
        num_samples = int(self.sample_rate * duration_s)
        t = np.linspace(0, duration_s, num_samples, endpoint=False)
        return (amplitude * np.sin(2 * np.pi * freq_hz * t)).astype(np.float32)

    def generate_silence(self, duration_s: float) -> np.ndarray:
        """Generate float32 silence frames."""
        num_samples = int(self.sample_rate * duration_s)
        return np.zeros(num_samples, dtype=np.float32)

    def generate_speech_utterance(self, duration_s: float = 1.5, silence_tail_s: float = 0.6) -> np.ndarray:
        """Generate synthetic speech followed by trailing silence."""
        speech = self.generate_sine_wave(duration_s, freq_hz=300.0, amplitude=0.4)
        silence = self.generate_silence(silence_tail_s)
        return np.concatenate([speech, silence])

    def push_sine_wave(self, duration_s: float, freq_hz: float = 440.0, amplitude: float = 0.5) -> None:
        self.push_audio(self.generate_sine_wave(duration_s, freq_hz, amplitude))

    def push_silence(self, duration_s: float) -> None:
        self.push_audio(self.generate_silence(duration_s))

    def read_chunk(self, timeout: float = 0.1) -> Optional[np.ndarray]:
        try:
            return self._queue.get(timeout=timeout)
        except queue.Empty:
            return None

    def register_callback(self, callback: Callable[[np.ndarray], None]) -> None:
        if callback not in self._callbacks:
            self._callbacks.append(callback)


class BaseAudioOutputDriver(ABC):
    """Abstract interface for audio playback devices."""

    def __init__(self, sample_rate: int = 24000, channels: int = 1):
        self.sample_rate = sample_rate
        self.channels = channels
        self.state = AudioDriverState.UNINITIALIZED

    @abstractmethod
    def start(self) -> None:
        pass

    @abstractmethod
    def stop(self) -> None:
        pass

    @abstractmethod
    def play_chunk(self, chunk: np.ndarray) -> None:
        pass

    def push_audio(self, chunk: np.ndarray) -> None:
        """Convenience alias for play_chunk()."""
        self.play_chunk(chunk)

    @abstractmethod
    def abort_playback(self) -> float:
        """Immediately abort DAC playback. Returns latency in milliseconds."""
        pass

    @abstractmethod
    def register_bargein_callback(self, cb: Callable[[], None]) -> None:
        pass

    @property
    @abstractmethod
    def is_playing(self) -> bool:
        pass


class SoundDeviceOutputDriver(BaseAudioOutputDriver):
    """Hardware speaker output using sounddevice."""

    def __init__(
        self,
        sample_rate: int = 24000,
        channels: int = 1,
        device_id: Optional[Union[int, str]] = None,
    ):
        super().__init__(sample_rate, channels)
        self.device_id = device_id
        self._stream = None
        self._queue: queue.Queue[np.ndarray] = queue.Queue(maxsize=100)
        self._callbacks: List[Callable[[], None]] = []
        self._is_playing = False
        self._lock = threading.Lock()

    def _audio_callback(self, outdata: np.ndarray, frames: int, time_info: Any, status: Any) -> None:
        try:
            chunk = self._queue.get_nowait()
            chunk_len = len(chunk)
            if chunk_len < frames:
                outdata[:chunk_len, 0] = chunk
                outdata[chunk_len:, 0] = 0.0
            else:
                outdata[:, 0] = chunk[:frames]
            self._is_playing = True
        except queue.Empty:
            outdata.fill(0.0)
            self._is_playing = False

    def start(self) -> None:
        with self._lock:
            if self.state == AudioDriverState.RUNNING:
                return
            try:
                import sounddevice as sd
                self._stream = sd.OutputStream(
                    samplerate=self.sample_rate,
                    channels=self.channels,
                    dtype="float32",
                    device=self.device_id,
                    callback=self._audio_callback,
                )
                self._stream.start()
                self.state = AudioDriverState.RUNNING
            except Exception as e:
                self.state = AudioDriverState.ERROR
                raise AudioDeviceNotFoundError(f"Failed to start sounddevice output: {e}") from e

    def stop(self) -> None:
        with self._lock:
            if self._stream is not None:
                try:
                    self._stream.stop()
                    self._stream.close()
                except Exception:
                    pass
                self._stream = None
            self._is_playing = False
            self.state = AudioDriverState.STOPPED

    def play_chunk(self, chunk: np.ndarray) -> None:
        sanitized = RobustAudioSanitizer.sanitize(chunk)
        if len(sanitized) > 0:
            self._queue.put(sanitized)
            self._is_playing = True

    def abort_playback(self) -> float:
        t0 = time.perf_counter()
        with self._lock:
            self._is_playing = False
            # Drain queue immediately
            while not self._queue.empty():
                try:
                    self._queue.get_nowait()
                except Exception:
                    break

            for cb in self._callbacks:
                try:
                    cb()
                except Exception:
                    pass

        return (time.perf_counter() - t0) * 1000.0

    def register_bargein_callback(self, cb: Callable[[], None]) -> None:
        if cb not in self._callbacks:
            self._callbacks.append(cb)

    @property
    def is_playing(self) -> bool:
        return self._is_playing


class VirtualAudioOutputDriver(BaseAudioOutputDriver):
    """Virtual audio output sink for automated tests."""

    def __init__(self, sample_rate: int = 24000):
        super().__init__(sample_rate=sample_rate, channels=1)
        self.played_chunks: List[np.ndarray] = []
        self._is_playing = False
        self.bargein_triggered = False
        self.bargein_callbacks: List[Callable[[], None]] = []

    def start(self) -> None:
        self.state = AudioDriverState.RUNNING

    def stop(self) -> None:
        self._is_playing = False
        self.state = AudioDriverState.STOPPED

    def play_chunk(self, chunk: np.ndarray) -> None:
        sanitized = RobustAudioSanitizer.sanitize(chunk)
        self.played_chunks.append(sanitized)
        self._is_playing = True

    def push_output_audio(self, chunk: np.ndarray) -> None:
        """Alias for play_chunk."""
        self.play_chunk(chunk)

    def abort_playback(self) -> float:
        t0 = time.perf_counter()
        self._is_playing = False
        self.bargein_triggered = True
        for cb in self.bargein_callbacks:
            try:
                cb()
            except Exception:
                pass
        return (time.perf_counter() - t0) * 1000.0

    def register_bargein_callback(self, cb: Callable[[], None]) -> None:
        if cb not in self.bargein_callbacks:
            self.bargein_callbacks.append(cb)

    @property
    def is_playing(self) -> bool:
        return self._is_playing

    @is_playing.setter
    def is_playing(self, val: bool) -> None:
        self._is_playing = val

    def clear(self) -> None:
        self.played_chunks.clear()
        self._is_playing = False
        self.bargein_triggered = False


class VirtualAudioDriver(VirtualAudioInputDriver, VirtualAudioOutputDriver):
    """
    Combined virtual audio input and output driver matching test fixture contracts.
    """

    def __init__(self, sample_rate_in: int = 16000, sample_rate_out: int = 24000):
        VirtualAudioInputDriver.__init__(self, sample_rate=sample_rate_in, chunk_size=512)
        VirtualAudioOutputDriver.__init__(self, sample_rate=sample_rate_out)
        self.sample_rate_in = sample_rate_in
        self.sample_rate_out = sample_rate_out
        self.is_recording = False

    def start_stream(self, callback: Optional[Callable[[np.ndarray], None]] = None) -> None:
        self.is_recording = True
        if callback:
            self.register_callback(callback)
        self.start()

    def stop_stream(self) -> None:
        self.is_recording = False
        self.stop()

    def clear(self) -> None:
        VirtualAudioInputDriver.stop(self)
        VirtualAudioOutputDriver.clear(self)
        self.recorded_frames.clear()
        self.is_recording = False
        self.bargein_triggered = False
