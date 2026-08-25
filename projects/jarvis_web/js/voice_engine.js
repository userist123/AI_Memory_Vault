const SpeechRecognition = window.SpeechRecognition || window.webkitSpeechRecognition;

export function createVoiceEngine({ onText } = {}) {
  if (!SpeechRecognition) return null;
  const recognition = new SpeechRecognition();
  recognition.lang = 'ro-RO';
  recognition.interimResults = false;
  recognition.continuous = false;
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
