const TTS_API = typeof window !== 'undefined' ? '' : 'http://127.0.0.1:8002';

const ROMANIAN_WORDS = [
  'arată', 'arata', 'arată-mi', 'arata-mi', 'ajută', 'ajuta', 'caută', 'cauta',
  'ce', 'cum', 'din', 'este', 'în', 'in', 'lumina', 'luminile', 'memorie',
  'memoria', 'pentru', 'proceduri', 'reguli', 'regulile', 'salut', 'spune',
  'stare', 'starea', 'știi', 'stii', 'și', 'si', 'temperatură', 'temperatura',
  'toate', 'vreau', 'deschide', 'proiectul', 'oprește', 'opreste', 'aprinde',
  'stinge'
];

const ENGLISH_WORDS = [
  'a', 'active', 'all', 'are', 'architecture', 'ask', 'find', 'for', 'how',
  'in', 'is', 'memory', 'of', 'please', 'protocol', 'rules', 'search', 'show',
  'status', 'system', 'tell', 'the', 'this', 'what', 'where', 'who', 'why',
  'with', 'turn', 'on', 'off', 'lights', 'open', 'project'
];

export const ROMANIAN_TOKENS = new Set(ROMANIAN_WORDS);
export const ENGLISH_TOKENS = new Set(ENGLISH_WORDS);
export const WAKE_WORD_REGEX = /^(?:\s*(?:(?:hey|hei|salut)\s+)?jarvis\b[,:;!?-]?\s*)(.*)$/i;

let currentAudio = null;
let speechSession = 0;

function globalScope() {
  if (typeof window !== 'undefined') return window;
  return typeof globalThis !== 'undefined' ? globalThis : {};
}

function stripDiacritics(value) {
  return String(value ?? '').normalize('NFD').replace(/[\u0300-\u036f]/g, '');
}

function tokenize(value) {
  return stripDiacritics(value).toLowerCase().match(/[a-zăâîșț-]+/gi) || [];
}

function normalizeLanguage(language) {
  if (!language || language === 'auto') return 'ro-RO';
  return String(language).toLowerCase().startsWith('ro') ? 'ro-RO' : 'en-US';
}

function clamp(value, minimum, maximum, fallback) {
  const numeric = Number(value);
  return Number.isFinite(numeric) ? Math.min(maximum, Math.max(minimum, numeric)) : fallback;
}

export class VoiceEngine {
  constructor(options = {}) {
    this.lang = options.lang || 'auto';
    this.wakeWord = String(options.wakeWord || 'jarvis').toLowerCase();
    this.requireWakeWord = options.requireWakeWord ?? true;
    this.continuous = options.continuous ?? true;
    this.interimResults = options.interimResults ?? true;
    this.maxAlternatives = options.maxAlternatives ?? 1;
    this.rate = options.rate ?? 1;
    this.pitch = options.pitch ?? 1;
    this.volume = options.volume ?? 1;
    this.isMuted = false;
    this.isSpeaking = false;
    this.isListeningDesired = false;
    this.permissionGranted = true;
    this.recognition = null;
    this._recognitionActive = false;
    this._voices = [];
    this._synthesis = null;
    this._speechQueue = [];
    this._speechIndex = 0;
    this._speechCallback = null;
    this._speechErrorCallback = null;
    this._lastAudioLevel = 0;
    this.onTranscript = options.onTranscript || null;
    this.onWakeWordDetected = options.onWakeWordDetected || null;
    this.onCommand = options.onCommand || null;
    this.onState = options.onState || null;
    this.onError = options.onError || null;
    this._setupSynthesis();
  }

  _setupSynthesis() {
    const scope = globalScope();
    this._synthesis = scope.speechSynthesis || (typeof globalThis !== 'undefined' ? globalThis.speechSynthesis : null);
    if (!this._synthesis) return;
    this._refreshVoices = () => { this._voices = this._readVoices(); };
    if (typeof this._synthesis.addEventListener === 'function') {
      this._synthesis.addEventListener('voiceschanged', this._refreshVoices);
    }
    if ('onvoiceschanged' in this._synthesis) this._synthesis.onvoiceschanged = this._refreshVoices;
    this._refreshVoices();
  }

  _readVoices() {
    try {
      const voices = this._synthesis?.getVoices?.();
      return Array.isArray(voices) ? [...voices] : [];
    } catch {
      return [];
    }
  }

  _recognitionConstructor() {
    const scope = globalScope();
    return scope.SpeechRecognition || scope.webkitSpeechRecognition ||
      (typeof globalThis !== 'undefined' && (globalThis.SpeechRecognition || globalThis.webkitSpeechRecognition));
  }

  _createRecognition() {
    const Recognition = this._recognitionConstructor();
    if (!Recognition) return null;
    const recognition = new Recognition();
    recognition.continuous = this.continuous;
    recognition.interimResults = this.interimResults;
    recognition.maxAlternatives = this.maxAlternatives;
    recognition.lang = normalizeLanguage(this.lang);
    recognition.onstart = () => {
      this._recognitionActive = true;
      this.onState?.('LISTENING');
    };
    recognition.onend = () => {
      this._recognitionActive = false;
      this.onState?.('READY');
      if (this.isListeningDesired && this.permissionGranted) this._restartRecognition();
    };
    recognition.onerror = (event) => this._handleRecognitionError(event);
    recognition.onresult = (event) => this._handleRecognitionResult(event);
    return recognition;
  }

  _restartRecognition() {
    if (!this.isListeningDesired || this.isMuted || this._recognitionActive) return;
    clearTimeout(this._restartTimer);
    this._restartTimer = setTimeout(() => {
      if (!this.isListeningDesired || this.isMuted || this._recognitionActive) return;
      try {
        this.recognition?.start();
      } catch {
        this._recognitionActive = false;
      }
    }, 50);
  }

  _handleRecognitionError(event) {
    const error = event?.error || 'unknown';
    if (error === 'not-allowed' || error === 'service-not-allowed') {
      this.permissionGranted = false;
      this.isListeningDesired = false;
    }
    const failure = new Error(event?.message || `Speech recognition error: ${error}`);
    failure.code = error;
    this.onError?.(failure);
    this.onState?.(`VOICE ERROR: ${error}`);
  }

  _handleRecognitionResult(event) {
    if (this.isMuted) return;
    const results = event?.results || [];
    const start = Number(event?.resultIndex || 0);
    for (let index = start; index < results.length; index += 1) {
      const result = results[index];
      const alternative = result?.[0] || result?.item?.(0);
      const text = String(alternative?.transcript || '').trim();
      if (!text) continue;
      const isFinal = Boolean(result?.isFinal);
      const classification = this.detectLanguage(text);
      this.onTranscript?.({
        text,
        isFinal,
        lang: classification.lang,
        confidence: Number(alternative?.confidence ?? 0),
        rawEvent: event
      });
      if (!isFinal) continue;
      const wakePayload = this.extractWakeWord(text);
      if (wakePayload) {
        if (this.isSpeaking) this.stopSpeaking();
        this.onWakeWordDetected?.(wakePayload);
      } else if (!this.requireWakeWord) {
        this.onCommand?.({ text, lang: classification.lang });
      }
    }
  }

  extractWakeWord(text) {
    const clean = String(text ?? '').trim();
    if (!clean) return null;
    if (this.wakeWord !== 'jarvis') {
      const custom = new RegExp(`^(?:\\\\s*(?:(?:hey|hei|salut)\\\\s+)?${this.wakeWord}\\\\b[,:;!?-]?\\\\s*)(.*)$`, 'i').exec(clean);
      if (!custom) return null;
      return { rawText: clean, commandText: custom[1].trim(), wakeWord: this.wakeWord };
    }
    const match = WAKE_WORD_REGEX.exec(clean);
    if (!match) return null;
    return { rawText: clean, commandText: match[1].trim(), wakeWord: this.wakeWord };
  }

  startListening() {
    this.isListeningDesired = true;
    if (!this.recognition) this.recognition = this._createRecognition();
    if (!this.recognition || this.isMuted || !this.permissionGranted) return false;
    if (this._recognitionActive || this.recognition.isListening) return true;
    try {
      this.recognition.lang = normalizeLanguage(this.lang);
      this.recognition.start();
      return true;
    } catch (error) {
      if (!String(error?.message || '').includes('InvalidStateError')) this.onError?.(error);
      return false;
    }
  }

  stopListening() {
    this.isListeningDesired = false;
    clearTimeout(this._restartTimer);
    if (!this.recognition) return;
    try { this.recognition.stop(); } catch {}
    this._recognitionActive = false;
  }

  getVoices() {
    const current = this._readVoices();
    if (current.length) this._voices = current;
    return [...this._voices];
  }

  getBestVoice(language = this.lang) {
    const voices = this.getVoices();
    if (!voices.length) return null;
    const requested = String(language || 'ro-RO').toLowerCase();
    const prefix = requested.split('-')[0];
    const candidates = voices.filter(voice => String(voice?.lang || '').toLowerCase().startsWith(prefix));
    const pool = candidates.length ? candidates : voices;
    const preferred = prefix === 'ro'
      ? ['andrei', 'emil', 'română', 'romana', 'romanian']
      : ['christopher', 'guy', 'english'];
    return pool.find(voice => preferred.some(token => String(voice?.name || '').toLowerCase().includes(token)))
      || pool.find(voice => voice?.default)
      || pool[0];
  }

  detectLanguage(text) {
    if (this.lang !== 'auto') {
      return { lang: normalizeLanguage(this.lang), romanian: 0, english: 0, confidence: 1 };
    }
    const tokens = tokenize(text);
    const romanian = tokens.filter(token => ROMANIAN_TOKENS.has(token) || ROMANIAN_TOKENS.has(stripDiacritics(token))).length;
    const english = tokens.filter(token => ENGLISH_TOKENS.has(token)).length;
    const lang = romanian >= english ? 'ro-RO' : 'en-US';
    const total = romanian + english;
    return { lang, romanian, english, confidence: total ? Math.max(romanian, english) / total : 0 };
  }

  chunkText(text, maximum = 220) {
    const clean = String(text ?? '').trim();
    if (!clean) return [];
    const sentences = clean.match(/[^.!?]+[.!?]+|[^.!?]+$/g) || [clean];
    const chunks = [];
    for (const sentence of sentences) {
      const value = sentence.trim();
      if (!value) continue;
      for (let offset = 0; offset < value.length; offset += maximum) {
        chunks.push(value.slice(offset, offset + maximum).trim());
      }
    }
    return chunks.filter(Boolean);
  }

  speak(text, language = this.lang, onEnd = null, onError = null) {
    const clean = String(text ?? '').trim();
    if (!clean || !this._synthesis) return false;
    if (typeof language === 'function') {
      onError = onEnd;
      onEnd = language;
      language = this.lang;
    }
    this.stopSpeaking();
    const Utterance = globalScope().SpeechSynthesisUtterance || globalThis.SpeechSynthesisUtterance;
    if (!Utterance) return false;
    this._speechQueue = this.chunkText(clean);
    this._speechIndex = 0;
    this._speechCallback = typeof onEnd === 'function' ? onEnd : null;
    this._speechErrorCallback = typeof onError === 'function' ? onError : null;
    this._speechLanguage = normalizeLanguage(language === 'auto' ? this.detectLanguage(clean).lang : language);
    this._speakNext(Utterance);
    return true;
  }

  _speakNext(Utterance) {
    const text = this._speechQueue[this._speechIndex];
    if (!text) {
      this.isSpeaking = false;
      this._lastAudioLevel = 0;
      this._speechCallback?.();
      this._speechCallback = null;
      return;
    }
    const utterance = new Utterance(text);
    utterance.lang = this._speechLanguage;
    utterance.voice = this.getBestVoice(this._speechLanguage);
    utterance.rate = clamp(this.rate, 0.1, 4, 1);
    utterance.pitch = clamp(this.pitch, 0, 2, 1);
    utterance.volume = clamp(this.volume, 0, 1, 1);
    utterance.onstart = () => {
      this.isSpeaking = true;
      this._lastAudioLevel = 0.55;
    };
    utterance.onend = () => {
      this._speechIndex += 1;
      if (this._speechIndex < this._speechQueue.length) this._speakNext(Utterance);
      else {
        this.isSpeaking = false;
        this._lastAudioLevel = 0;
        this._speechCallback?.();
        this._speechCallback = null;
      }
    };
    utterance.onerror = (event) => {
      this.isSpeaking = false;
      this._lastAudioLevel = 0;
      const error = new Error(event?.error || 'Speech synthesis failed');
      this._speechErrorCallback?.(error);
      this._speechErrorCallback = null;
    };
    this.isSpeaking = true;
    this._lastAudioLevel = 0.35;
    try {
      this._synthesis.speak(utterance);
    } catch (error) {
      this.isSpeaking = false;
      this._speechErrorCallback?.(error);
    }
  }

  stopSpeaking() {
    try { this._synthesis?.cancel?.(); } catch {}
    this._speechQueue = [];
    this._speechIndex = 0;
    this.isSpeaking = false;
    this._lastAudioLevel = 0;
    this._speechCallback = null;
    this._speechErrorCallback = null;
  }

  getAudioLevel() {
    return clamp(this._lastAudioLevel, 0, 1, 0);
  }

  mute() {
    this.isMuted = true;
    this.stopSpeaking();
    return this.isMuted;
  }

  unmute() {
    this.isMuted = false;
    if (this.isListeningDesired) this.startListening();
    return this.isMuted;
  }

  toggleMute() {
    return this.isMuted ? this.unmute() : this.mute();
  }

  destroy() {
    this.stopListening();
    this.stopSpeaking();
    if (this._synthesis?.removeEventListener && this._refreshVoices) {
      this._synthesis.removeEventListener('voiceschanged', this._refreshVoices);
    }
  }

  start() { return this.startListening(); }
  stop() { return this.stopListening(); }
}

export function createVoiceEngine({ onText, onState, requireWakeWord = true, ...options } = {}) {
  const engine = new VoiceEngine({ ...options, requireWakeWord });
  if (!engine._recognitionConstructor()) return null;
  engine.onState = onState;
  engine.onWakeWordDetected = (payload) => {
    onState?.('LISTENING');
    if (payload.commandText && onText) onText(payload.commandText);
  };
  engine.onCommand = ({ text }) => onText?.(text);
  engine.supported = true;
  return engine;
}

export async function speak(text, { onStart, onEnd, onError } = {}) {
  const clean = String(text ?? '').trim();
  if (!clean) return false;
  const session = ++speechSession;
  stopSpeaking();
  try {
    const response = await fetch(`${TTS_API}/tts`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ text: clean })
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
    const response = await fetch(`${TTS_API}/health`, { cache: 'no-store' });
    return response.ok;
  } catch {
    return false;
  }
}

if (typeof globalThis !== 'undefined') globalThis.VoiceEngine = VoiceEngine;

