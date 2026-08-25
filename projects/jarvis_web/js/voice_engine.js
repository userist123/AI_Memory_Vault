const SpeechRecognition = window.SpeechRecognition || window.webkitSpeechRecognition;

export function createVoiceEngine({ onText, onState } = {}) {
  if (!SpeechRecognition) return null;
  const recognition = new SpeechRecognition();
  recognition.lang = 'ro-RO'; recognition.interimResults = false; recognition.continuous = false;
  recognition.onstart = () => onState?.('LISTENING');
  recognition.onend = () => onState?.('READY');
  recognition.onerror = (event) => onState?.(`VOICE ERROR: ${event.error}`);
  recognition.onresult = (event) => {
    const text = event.results?.[0]?.[0]?.transcript?.trim();
    if (text && onText) onText(text);
  };
  return { start: () => recognition.start(), stop: () => recognition.stop(), supported: true };
}

export function speak(text, { lang = 'ro-RO', rate = 1.0, pitch = 0.92 } = {}) {
  if (!('speechSynthesis' in window)) return false;
  window.speechSynthesis.cancel();
  const utterance = new SpeechSynthesisUtterance(String(text).slice(0, 4000));
  utterance.lang = lang; utterance.rate = rate; utterance.pitch = pitch;
  window.speechSynthesis.speak(utterance);
  return true;
}
