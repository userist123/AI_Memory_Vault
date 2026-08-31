/**
 * mock_web_speech.js - Standalone high-fidelity test double for Web Speech API
 * Implements W3C SpeechRecognition and SpeechSynthesis specifications with event simulation.
 */

export class MockSpeechRecognitionEvent {
  constructor(type, { results = [], resultIndex = 0 } = {}) {
    this.type = type;
    this.results = results;
    this.resultIndex = resultIndex;
    this.emma = null;
    this.interpretation = null;
  }
}

export class MockSpeechRecognitionErrorEvent {
  constructor(type, { error = 'unknown', message = '' } = {}) {
    this.type = type;
    this.error = error;
    this.message = message;
  }
}

export class MockSpeechRecognition {
  constructor() {
    this.continuous = false;
    this.interimResults = false;
    this.lang = 'en-US';
    this.maxAlternatives = 1;
    this.isListening = false;
    this.aborted = false;

    // Event Handlers
    this.onstart = null;
    this.onresult = null;
    this.onerror = null;
    this.onend = null;
    this.onspeechstart = null;
    this.onspeechend = null;
    this.onaudiostart = null;
    this.onaudioend = null;
    this.onnomatch = null;

    this._listeners = new Map();
  }

  addEventListener(type, listener) {
    if (!this._listeners.has(type)) {
      this._listeners.set(type, new Set());
    }
    this._listeners.get(type).add(listener);
  }

  removeEventListener(type, listener) {
    if (this._listeners.has(type)) {
      this._listeners.get(type).delete(listener);
    }
  }

  dispatchEvent(event) {
    const handler = this[`on${event.type}`];
    if (typeof handler === 'function') {
      try {
        handler.call(this, event);
      } catch (err) {
        console.error(`Error in on${event.type} handler:`, err);
      }
    }
    const listeners = this._listeners.get(event.type);
    if (listeners) {
      for (const listener of listeners) {
        try {
          listener.call(this, event);
        } catch (err) {
          console.error(`Error in ${event.type} listener:`, err);
        }
      }
    }
    return true;
  }

  start() {
    if (this.isListening) {
      throw new Error('InvalidStateError: SpeechRecognition has already started.');
    }
    this.isListening = true;
    this.aborted = false;
    queueMicrotask(() => {
      if (this.isListening) {
        this.dispatchEvent({ type: 'start', target: this });
        this.dispatchEvent({ type: 'audiostart', target: this });
      }
    });
  }

  stop() {
    if (!this.isListening) return;
    this.isListening = false;
    queueMicrotask(() => {
      this.dispatchEvent({ type: 'speechend', target: this });
      this.dispatchEvent({ type: 'audioend', target: this });
      this.dispatchEvent({ type: 'end', target: this });
    });
  }

  abort() {
    if (!this.isListening) return;
    this.isListening = false;
    this.aborted = true;
    queueMicrotask(() => {
      this.dispatchEvent({ type: 'end', target: this });
    });
  }

  // --- Test Simulation Controls ---

  simulateResult(transcript, isFinal = true, confidence = 0.95) {
    const item = {
      transcript,
      confidence
    };
    const alternativeList = [item];
    alternativeList.item = (i) => alternativeList[i];
    alternativeList.isFinal = isFinal;

    const resultsList = [alternativeList];
    resultsList.item = (i) => resultsList[i];
    resultsList.length = 1;

    const event = new MockSpeechRecognitionEvent('result', {
      results: resultsList,
      resultIndex: 0
    });

    this.dispatchEvent(event);
  }

  simulateInterimResult(transcript) {
    this.simulateResult(transcript, false, 0.70);
  }

  simulateFinalResult(transcript) {
    this.simulateResult(transcript, true, 0.98);
  }

  simulateError(errorType, message = '') {
    const event = new MockSpeechRecognitionErrorEvent('error', {
      error: errorType,
      message
    });
    this.dispatchEvent(event);
  }

  simulateEnd() {
    this.isListening = false;
    this.dispatchEvent({ type: 'end', target: this });
  }

  simulateSpeechStart() {
    this.dispatchEvent({ type: 'speechstart', target: this });
  }

  simulateSpeechEnd() {
    this.dispatchEvent({ type: 'speechend', target: this });
  }
}

export class MockSpeechSynthesisVoice {
  constructor({ name, lang, voiceURI, localService = true, isDefault = false }) {
    this.name = name;
    this.lang = lang;
    this.voiceURI = voiceURI || name;
    this.localService = localService;
    this.default = isDefault;
  }
}

export class MockSpeechSynthesisUtterance {
  constructor(text = '') {
    this.text = text;
    this.lang = 'en-US';
    this.voice = null;
    this.volume = 1;
    this.rate = 1;
    this.pitch = 1;

    this.onstart = null;
    this.onend = null;
    this.onerror = null;
    this.onpause = null;
    this.onresume = null;
    this.onboundary = null;
    this.onmark = null;

    this._listeners = new Map();
  }

  addEventListener(type, listener) {
    if (!this._listeners.has(type)) {
      this._listeners.set(type, new Set());
    }
    this._listeners.get(type).add(listener);
  }

  removeEventListener(type, listener) {
    if (this._listeners.has(type)) {
      this._listeners.get(type).delete(listener);
    }
  }

  dispatchEvent(event) {
    const handler = this[`on${event.type}`];
    if (typeof handler === 'function') {
      try {
        handler.call(this, event);
      } catch (err) {
        console.error(`Error in utterance on${event.type}:`, err);
      }
    }
    const listeners = this._listeners.get(event.type);
    if (listeners) {
      for (const listener of listeners) {
        try {
          listener.call(this, event);
        } catch (err) {
          console.error(`Error in utterance ${event.type} listener:`, err);
        }
      }
    }
    return true;
  }
}

export const DEFAULT_MOCK_VOICES = [
  new MockSpeechSynthesisVoice({
    name: 'Microsoft Andrei Online (Natural) - Romanian (Romania)',
    lang: 'ro-RO',
    voiceURI: 'Microsoft Andrei Online (Natural) - Romanian (Romania)',
    isDefault: true
  }),
  new MockSpeechSynthesisVoice({
    name: 'Microsoft Emil Online (Natural) - Romanian (Romania)',
    lang: 'ro-RO',
    voiceURI: 'Microsoft Emil Online (Natural) - Romanian (Romania)',
    isDefault: false
  }),
  new MockSpeechSynthesisVoice({
    name: 'Microsoft Christopher Online (Natural) - English (United States)',
    lang: 'en-US',
    voiceURI: 'Microsoft Christopher Online (Natural) - English (United States)',
    isDefault: true
  }),
  new MockSpeechSynthesisVoice({
    name: 'Microsoft Guy Online (Natural) - English (United States)',
    lang: 'en-US',
    voiceURI: 'Microsoft Guy Online (Natural) - English (United States)',
    isDefault: false
  }),
  new MockSpeechSynthesisVoice({
    name: 'Google UK English Male',
    lang: 'en-GB',
    voiceURI: 'Google UK English Male',
    isDefault: false
  }),
  new MockSpeechSynthesisVoice({
    name: 'Google română',
    lang: 'ro',
    voiceURI: 'Google română',
    isDefault: false
  })
];

export class MockSpeechSynthesis {
  constructor(voices = DEFAULT_MOCK_VOICES) {
    this.pending = false;
    this.speaking = false;
    this.paused = false;
    this.queue = [];
    this.currentUtterance = null;
    this.onvoiceschanged = null;
    this.voices = [...voices];
    this.autoFinish = false;
    this.autoFinishDelayMs = 10;
    this._listeners = new Map();
  }

  getVoices() {
    return [...this.voices];
  }

  setVoices(newVoices) {
    this.voices = [...newVoices];
    this.dispatchEvent({ type: 'voiceschanged', target: this });
  }

  addEventListener(type, listener) {
    if (!this._listeners.has(type)) {
      this._listeners.set(type, new Set());
    }
    this._listeners.get(type).add(listener);
  }

  removeEventListener(type, listener) {
    if (this._listeners.has(type)) {
      this._listeners.get(type).delete(listener);
    }
  }

  dispatchEvent(event) {
    const handler = this[`on${event.type}`];
    if (typeof handler === 'function') {
      try {
        handler.call(this, event);
      } catch (err) {
        console.error(`Error in speechSynthesis on${event.type}:`, err);
      }
    }
    const listeners = this._listeners.get(event.type);
    if (listeners) {
      for (const listener of listeners) {
        try {
          listener.call(this, event);
        } catch (err) {
          console.error(`Error in speechSynthesis ${event.type} listener:`, err);
        }
      }
    }
    return true;
  }

  speak(utterance) {
    if (!(utterance instanceof MockSpeechSynthesisUtterance)) {
      throw new TypeError('Failed to execute speak: parameter 1 is not of type SpeechSynthesisUtterance');
    }
    this.queue.push(utterance);
    this.pending = this.queue.length > 1;

    if (!this.speaking && !this.paused) {
      this._processNext();
    }
  }

  _processNext() {
    if (this.queue.length === 0) {
      this.speaking = false;
      this.pending = false;
      this.currentUtterance = null;
      return;
    }

    this.currentUtterance = this.queue.shift();
    this.speaking = true;
    this.pending = this.queue.length > 0;

    queueMicrotask(() => {
      if (this.currentUtterance) {
        this.currentUtterance.dispatchEvent({ type: 'start', target: this.currentUtterance });
        if (this.autoFinish) {
          setTimeout(() => {
            this.finishSpeaking();
          }, this.autoFinishDelayMs);
        }
      }
    });
  }

  cancel() {
    const cancelledUtterances = [this.currentUtterance, ...this.queue].filter(Boolean);
    this.queue = [];
    this.speaking = false;
    this.paused = false;
    this.pending = false;
    this.currentUtterance = null;

    for (const utt of cancelledUtterances) {
      utt.dispatchEvent({ type: 'error', error: 'canceled', target: utt });
    }
  }

  pause() {
    if (this.speaking && !this.paused) {
      this.paused = true;
      if (this.currentUtterance) {
        this.currentUtterance.dispatchEvent({ type: 'pause', target: this.currentUtterance });
      }
    }
  }

  resume() {
    if (this.paused) {
      this.paused = false;
      if (this.currentUtterance) {
        this.currentUtterance.dispatchEvent({ type: 'resume', target: this.currentUtterance });
      } else if (this.queue.length > 0) {
        this._processNext();
      }
    }
  }

  finishSpeaking() {
    if (!this.speaking || !this.currentUtterance) return;
    const completed = this.currentUtterance;
    this.currentUtterance = null;
    completed.dispatchEvent({ type: 'end', target: completed });
    this._processNext();
  }

  simulateVoiceError(errorType = 'canceled') {
    if (!this.speaking || !this.currentUtterance) return;
    const failed = this.currentUtterance;
    this.currentUtterance = null;
    failed.dispatchEvent({ type: 'error', error: errorType, target: failed });
    this._processNext();
  }
}

function safeDefine(target, prop, value) {
  try {
    target[prop] = value;
  } catch (e) {
    try {
      Object.defineProperty(target, prop, { value, configurable: true, writable: true });
    } catch (e2) {
      // Best effort
    }
  }
}

export function installWebSpeechMocks(target = globalThis) {
  safeDefine(target, 'SpeechRecognition', MockSpeechRecognition);
  safeDefine(target, 'webkitSpeechRecognition', MockSpeechRecognition);
  safeDefine(target, 'SpeechSynthesisUtterance', MockSpeechSynthesisUtterance);
  safeDefine(target, 'SpeechSynthesisVoice', MockSpeechSynthesisVoice);
  const synth = (target.speechSynthesis instanceof MockSpeechSynthesis) ? target.speechSynthesis : new MockSpeechSynthesis();
  safeDefine(target, 'speechSynthesis', synth);

  if (target.window && target.window !== target) {
    safeDefine(target.window, 'SpeechRecognition', MockSpeechRecognition);
    safeDefine(target.window, 'webkitSpeechRecognition', MockSpeechRecognition);
    safeDefine(target.window, 'SpeechSynthesisUtterance', MockSpeechSynthesisUtterance);
    safeDefine(target.window, 'SpeechSynthesisVoice', MockSpeechSynthesisVoice);
    safeDefine(target.window, 'speechSynthesis', synth);
  }

  return {
    SpeechRecognition: MockSpeechRecognition,
    speechSynthesis: synth,
    SpeechSynthesisUtterance: MockSpeechSynthesisUtterance
  };
}
