const SpeechRecognition = window.SpeechRecognition || window.webkitSpeechRecognition;
const TTS_API = 'http://127.0.0.1:8002';
let currentAudio = null;
let speechSession = 0;

export function createVoiceEngine({ onText, onState } = {}) {
  if (!SpeechRecognition) return null;
  const recognition = new SpeechRecognition();
  recognition.lang = 'ro-RO';
  recognition.interimResults = false;
  recognition.continuous = false;
  recognition.maxAlternatives = 1;
  recognition.onstart = () => onState?.('LISTENING');
  recognition.onend = () => onState?.('READY');
  recognition.onerror = (event) => onState?.(`VOICE ERROR: ${event.error}`);
  recognition.onresult = (event) => {
    const text = event.results?.[0]?.[0]?.transcript?.trim();
    if (text && onText) onText(text);
  };
  return {
    start: () => recognition.start(),
    stop: () => recognition.stop(),
    supported: true
  };
}

export async function speak(text, { onStart, onEnd, onError } = {}) {
  const clean = String(text ?? '').trim();
  if (!clean) return false;
  const session = ++speechSession;
  stopSpeaking();
  try {
    const response = await fetch(`${TTS_API}/tts`, {
      method: 'POST',
      headers: {'Content-Type': 'application/json'},
      body: JSON.stringify({text: clean})
    });
    if (!response.ok) {
      let message = `TTS HTTP ${response.status}`;
      try { const data = await response.json(); if (data.error) message = data.error; } catch {}
      throw new Error(message);
    }
    const blob = await response.blob();
    if (session !== speechSession) return false;
    const url = URL.createObjectURL(blob);
    const audio = new Audio(url);
    currentAudio = audio;
    audio.preload = 'auto';
    audio.onplay = () => onStart?.();
    audio.onended = () => { URL.revokeObjectURL(url); if (currentAudio === audio) currentAudio = null; onEnd?.(); };
    audio.onerror = () => { URL.revokeObjectURL(url); if (currentAudio === audio) currentAudio = null; onError?.(new Error('JARVIS voice playback failed')); };
    await audio.play();
    return true;
  } catch (error) {
    onError?.(error);
    return false;
  }
}

export function stopSpeaking() {
  speechSession += 1;
  if (currentAudio) {
    currentAudio.pause();
    try { currentAudio.currentTime = 0; } catch {}
    currentAudio = null;
  }
}

export async function hasSpeechSynthesis() {
  try {
    const response = await fetch(`${TTS_API}/health`, {cache: 'no-store'});
    return response.ok;
  } catch {
    return false;
  }
}
