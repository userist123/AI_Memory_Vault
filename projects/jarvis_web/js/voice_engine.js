const SpeechRecognition = window.SpeechRecognition || window.webkitSpeechRecognition;

function pickRomanianVoice() {
  if (!('speechSynthesis' in window)) return null;
  const voices = window.speechSynthesis.getVoices();
  return voices.find(v => /^ro(-|_)?RO$/i.test(v.lang))
    || voices.find(v => /^ro/i.test(v.lang))
    || voices.find(v => /romania|romanian|romana/i.test(v.name));
}

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

export function speak(text, {
  lang = 'ro-RO',
  rate = 0.98,
  pitch = 0.9,
  volume = 1,
  onStart,
  onEnd,
  onError
} = {}) {
  if (!('speechSynthesis' in window)) return false;
  const clean = String(text ?? '').trim();
  if (!clean) return false;

  window.speechSynthesis.cancel();
  const utterance = new SpeechSynthesisUtterance(clean.slice(0, 6000));
  const voice = pickRomanianVoice();
  utterance.lang = voice?.lang || lang;
  if (voice) utterance.voice = voice;
  utterance.rate = rate;
  utterance.pitch = pitch;
  utterance.volume = volume;
  utterance.onstart = () => onStart?.();
  utterance.onend = () => onEnd?.();
  utterance.onerror = (event) => onError?.(event);
  window.speechSynthesis.speak(utterance);
  return true;
}

export function stopSpeaking() {
  if (!('speechSynthesis' in window)) return;
  window.speechSynthesis.cancel();
}

export function hasSpeechSynthesis() {
  return 'speechSynthesis' in window && 'SpeechSynthesisUtterance' in window;
}
