/**
 * Comprehensive Test Suite for VoiceEngine (Node.js test harness)
 */
import assert from 'node:assert';
import { VoiceEngine, WAKE_WORD_REGEX, ROMANIAN_TOKENS, ENGLISH_TOKENS } from '../../projects/jarvis_web/js/voice_engine.js';

console.log('--- STARTING VOICE ENGINE TESTS ---');

// 1. Wake Word Detection Tests
console.log('Testing Wake Word Detection...');
const engine = new VoiceEngine();

const wakeWordTestCases = [
  { input: 'Jarvis', expectedMatch: true, expectedCommand: '' },
  { input: 'Hey Jarvis', expectedMatch: true, expectedCommand: '' },
  { input: 'Salut Jarvis', expectedMatch: true, expectedCommand: '' },
  { input: 'OK Jarvis', expectedMatch: true, expectedCommand: '' },
  { input: 'Hei Jarvis', expectedMatch: true, expectedCommand: '' },
  { input: 'Hey Jarvis, what is the status of the vault?', expectedMatch: true, expectedCommand: 'what is the status of the vault?' },
  { input: 'Salut Jarvis, arată-mi regulile de sistem', expectedMatch: true, expectedCommand: 'arată-mi regulile de sistem' },
  { input: 'Hei Jarvis caută proiectele active', expectedMatch: true, expectedCommand: 'caută proiectele active' },
  { input: 'OK Jarvis: explain the P0-P18 trust boundaries', expectedMatch: true, expectedCommand: 'explain the P0-P18 trust boundaries' },
  { input: 'bună ziua jarvis, ce mai faci?', expectedMatch: true, expectedCommand: 'ce mai faci?' },
  { input: 'Just random text without wake trigger', expectedMatch: false, expectedCommand: '' },
  { input: 'Hey there friend', expectedMatch: false, expectedCommand: '' }
];

for (const testCase of wakeWordTestCases) {
  const res = engine.isWakeWord(testCase.input);
  assert.strictEqual(
    res.isMatch,
    testCase.expectedMatch,
    `Wake word match failed for "${testCase.input}": got ${res.isMatch}, expected ${testCase.expectedMatch}`
  );
  if (testCase.expectedMatch) {
    assert.strictEqual(
      res.commandText,
      testCase.expectedCommand,
      `Command text mismatch for "${testCase.input}": got "${res.commandText}", expected "${testCase.expectedCommand}"`
    );
  }
}
console.log('✓ Wake Word Detection passed.');

// 2. Bilingual Language Classifier Tests
console.log('Testing Bilingual Classifier...');
const langTestCases = [
  { text: 'Salut, caută în memorie despre reguli și proiecte active', expectedLang: 'ro-RO' },
  { text: 'Ce este sistemul exo-cortex și cum funcționează?', expectedLang: 'ro-RO' },
  { text: 'Unde sunt fișierele de configurare?', expectedLang: 'ro-RO' },
  { text: 'Arată-mi toate procedurile de recuperare', expectedLang: 'ro-RO' },
  { text: 'Hello, what are the core invariants and security rules?', expectedLang: 'en-US' },
  { text: 'Search the memory vault for active projects', expectedLang: 'en-US' },
  { text: 'How do I run the automated test suite in node?', expectedLang: 'en-US' },
  { text: 'Where is the knowledge catalog for subagents?', expectedLang: 'en-US' }
];

for (const tc of langTestCases) {
  const res = engine.detectLanguage(tc.text);
  assert.strictEqual(
    res.lang,
    tc.expectedLang,
    `Language detection failed for "${tc.text}": got ${res.lang}, expected ${tc.expectedLang}`
  );
  assert(res.confidence >= 0.5, `Confidence too low: ${res.confidence}`);
}
console.log('✓ Bilingual Classifier passed.');

// 3. Sentence Chunking (Anti-Chrome 15s freeze)
console.log('Testing Sentence Chunking...');
const shortText = 'Salut. Sunt Jarvis.';
const shortChunks = engine.chunkText(shortText);
assert.deepStrictEqual(shortChunks, ['Salut.', 'Sunt Jarvis.']);

const longSentence = 'Aceasta este o propoziție extrem de lungă destinată testării algoritmului de fragmentare a vorbirii în fragmente mai mici de o sută șaizeci de caractere pentru a preveni înghețarea motorului de sinteză vocală din browserul Chromium, care se oprește automat după cincisprezece secunde dacă nu primește date fragmentate corespunzător.';
const longChunks = engine.chunkText(longSentence);
assert(longChunks.length > 1, 'Long sentence should be chunked into multiple pieces');
for (const ch of longChunks) {
  assert(ch.length <= 160, `Chunk length exceeded 160 chars: ${ch.length} -> "${ch}"`);
}
console.log('✓ Sentence Chunking passed.');

// 4. Voice Selection Priority
console.log('Testing Natural Voice Selector...');
// Mock voices list
const mockVoices = [
  { name: 'Microsoft David Desktop', lang: 'en-US' },
  { name: 'Microsoft Zira Desktop', lang: 'en-US' },
  { name: 'Microsoft Christopher Online (Natural)', lang: 'en-US' },
  { name: 'Microsoft Andrei', lang: 'ro-RO' },
  { name: 'Microsoft Emil Online (Natural)', lang: 'ro-RO' },
  { name: 'Google română', lang: 'ro-RO' }
];

globalThis.speechSynthesis = {
  getVoices: () => mockVoices,
  onvoiceschanged: null,
  speak: () => {},
  cancel: () => {}
};

engine._refreshVoices();
const bestRo = engine.getBestVoice('ro-RO');
assert.strictEqual(bestRo.name, 'Microsoft Emil Online (Natural)', `Expected Emil for RO, got ${bestRo?.name}`);

const bestEn = engine.getBestVoice('en-US');
assert.strictEqual(bestEn.name, 'Microsoft Christopher Online (Natural)', `Expected Christopher for EN, got ${bestEn?.name}`);
console.log('✓ Natural Voice Selector passed.');

// 5. Mute/Unmute & State Management
console.log('Testing Mute & State Management...');
let lastState = null;
engine.onStateChange = (state) => { lastState = state; };

assert.strictEqual(engine.isMuted, false);
const mutedState = engine.mute();
assert.strictEqual(mutedState, true);
assert.strictEqual(engine.isMuted, true);
assert.strictEqual(lastState, 'MUTED');

const unmutedState = engine.unmute();
assert.strictEqual(unmutedState, false);
assert.strictEqual(engine.isMuted, false);
assert.strictEqual(lastState, 'IDLE');

const toggledMuted = engine.toggleMute();
assert.strictEqual(toggledMuted, true);
assert.strictEqual(engine.isMuted, true);

const toggledUnmuted = engine.toggleMute();
assert.strictEqual(toggledUnmuted, false);
assert.strictEqual(engine.isMuted, false);
console.log('✓ Mute & State Management passed.');

// 6. SpeechRecognition Event Mocking & Auto-Restart Debounce
console.log('Testing SpeechRecognition Event Lifecycle...');
class MockSpeechRecognition {
  constructor() {
    this.continuous = false;
    this.interimResults = false;
    this.lang = '';
    this.onstart = null;
    this.onresult = null;
    this.onerror = null;
    this.onend = null;
    this.started = false;
  }
  start() {
    this.started = true;
    if (this.onstart) this.onstart();
  }
  stop() {
    this.started = false;
    if (this.onend) this.onend();
  }
}

globalThis.SpeechRecognition = MockSpeechRecognition;

const engineWithSTT = new VoiceEngine();
let transcriptReceived = null;
let wakeWordReceived = null;

engineWithSTT.onStateChange = (state) => { lastState = state; };
engineWithSTT.onTranscript = (payload) => { transcriptReceived = payload; };
engineWithSTT.onWakeWordDetected = (payload) => { wakeWordReceived = payload; };

engineWithSTT.startListening();
assert.strictEqual(engineWithSTT.isListening, true);
assert.strictEqual(lastState, 'LISTENING');

// Simulate speech result with wake word
const mockEvent = {
  resultIndex: 0,
  results: [
    {
      0: { transcript: 'Hey Jarvis what are the security rules' },
      isFinal: true,
      length: 1
    }
  ]
};

engineWithSTT.recognition.onresult(mockEvent);
assert(transcriptReceived !== null, 'Transcript callback should have fired');
assert.strictEqual(transcriptReceived.text, 'Hey Jarvis what are the security rules');
assert.strictEqual(transcriptReceived.isFinal, true);
assert.strictEqual(transcriptReceived.lang, 'en-US');

assert(wakeWordReceived !== null, 'Wake word callback should have fired');
assert.strictEqual(wakeWordReceived.commandText, 'what are the security rules');
assert.strictEqual(wakeWordReceived.lang, 'en-US');

// Simulate benign no-speech error
engineWithSTT.recognition.onerror({ error: 'no-speech' });
// Should not disable listening desired
assert.strictEqual(engineWithSTT.isListeningDesired, true);

// Simulate permission denied error
let errorMsg = null;
engineWithSTT.onError = (msg) => { errorMsg = msg; };
engineWithSTT.recognition.onerror({ error: 'not-allowed' });
assert.strictEqual(engineWithSTT.isListeningDesired, false);
assert.strictEqual(engineWithSTT.permissionGranted, false);
assert(errorMsg.includes('not-allowed') || errorMsg.includes('permission denied'));

console.log('✓ SpeechRecognition Lifecycle passed.');

// 7. Audio Level & Visualizer Hook Simulation
console.log('Testing Audio Level & Visualizer...');
const idleLevel = engine.getAudioLevel();
assert.strictEqual(idleLevel, 0.0);

engine.isSpeaking = true;
const speakingLevel = engine.getAudioLevel();
assert(speakingLevel >= 0.15 && speakingLevel <= 1.0, `Speaking level out of range: ${speakingLevel}`);

engine.isSpeaking = false;
engine.isListening = true;
const listeningLevel = engine.getAudioLevel();
assert(listeningLevel >= 0.01 && listeningLevel <= 0.2, `Listening level out of range: ${listeningLevel}`);

engine.isListening = false;
const freqData = engine.getFrequencyData();
assert.strictEqual(freqData.length, 64);
console.log('✓ Audio Level & Visualizer passed.');

// 8. TTS Queue & GC Protection
console.log('Testing TTS Speech Synthesis & Barge-in...');
let speechStarted = false;
let speechEnded = false;
const spokenUtterances = [];

globalThis.SpeechSynthesisUtterance = class {
  constructor(text) {
    this.text = text;
    this.lang = '';
    this.rate = 1.0;
    this.pitch = 1.0;
    this.volume = 1.0;
    this.voice = null;
    this.onend = null;
    this.onerror = null;
  }
};

let cancelCalled = false;
globalThis.speechSynthesis = {
  getVoices: () => mockVoices,
  onvoiceschanged: null,
  speak: (utt) => {
    spokenUtterances.push(utt);
    setTimeout(() => {
      if (utt.onend) utt.onend();
    }, 5);
  },
  cancel: () => {
    cancelCalled = true;
  }
};

const ttsEngine = new VoiceEngine();
ttsEngine.onSpeechStart = () => { speechStarted = true; };
ttsEngine.onSpeechEnd = () => { speechEnded = true; };

let finishedCallbackCalled = false;
ttsEngine.speak('Prima propoziție. A doua propoziție.', 'ro-RO', () => {
  finishedCallbackCalled = true;
});

assert.strictEqual(ttsEngine.isSpeaking, true);
assert(speechStarted, 'Speech start event should fire');
assert(ttsEngine.activeUtterances.size > 0, 'Utterance should be in active Set for GC safety');

// Test barge-in cancellation
ttsEngine.stopSpeaking();
assert.strictEqual(ttsEngine.isSpeaking, false);
assert.strictEqual(ttsEngine.activeUtterances.size, 0);
assert.strictEqual(cancelCalled, true);
console.log('✓ TTS Speech Synthesis & Barge-in passed.');

// 9. Auto-restart Debounce & Network Exponential Backoff
console.log('Testing Auto-restart Debounce & Exponential Backoff...');
const autoRestartEngine = new VoiceEngine();
autoRestartEngine.startListening();
assert.strictEqual(autoRestartEngine.isListeningDesired, true);

// Simulate network error
autoRestartEngine.recognition.onerror({ error: 'network' });
assert.strictEqual(autoRestartEngine.retryCount, 1);

// Simulate onend - should schedule restart
autoRestartEngine.recognition.onend();
assert(autoRestartEngine.restartTimer !== null, 'Restart timer should be active');
clearTimeout(autoRestartEngine.restartTimer);
console.log('✓ Auto-restart Debounce & Backoff passed.');

// 10. Web Audio Analyser Real RMS Computation
console.log('Testing Web Audio Analyser RMS computation...');
class MockAnalyser {
  constructor() {
    this.fftSize = 128;
    this.frequencyBinCount = 64;
    this.smoothingTimeConstant = 0.8;
  }
  getByteTimeDomainData(buffer) {
    // Fill with simulated sine wave (amplitude 40 around center 128)
    for (let i = 0; i < buffer.length; i++) {
      buffer[i] = Math.round(128 + 40 * Math.sin((i / buffer.length) * Math.PI * 4));
    }
  }
  getByteFrequencyData(buffer) {
    for (let i = 0; i < buffer.length; i++) {
      buffer[i] = Math.max(0, 200 - i * 3);
    }
  }
}

class MockAudioContext {
  constructor() {
    this.state = 'running';
  }
  createMediaStreamSource() {
    return { connect: () => {} };
  }
  createAnalyser() {
    return new MockAnalyser();
  }
  close() {
    this.state = 'closed';
  }
}

globalThis.AudioContext = MockAudioContext;
Object.defineProperty(globalThis, 'navigator', {
  value: {
    mediaDevices: {
      getUserMedia: async () => ({
        getTracks: () => [{ stop: () => {} }]
      })
    }
  },
  configurable: true,
  writable: true
});

const visualizerEngine = new VoiceEngine();
await visualizerEngine.initAudioVisualizer();
assert.strictEqual(visualizerEngine.visualizerInitialized, true);

const realRms = visualizerEngine.getAudioLevel();
assert(realRms > 0.0 && realRms <= 1.0, `Real RMS should be between 0 and 1, got ${realRms}`);

const realFreq = visualizerEngine.getFrequencyData();
assert.strictEqual(realFreq.length, 64);
assert(realFreq[0] > 0, 'First frequency bin should have energy');

visualizerEngine.destroy();
assert.strictEqual(visualizerEngine.visualizerInitialized, false);
console.log('✓ Web Audio Analyser RMS computation passed.');

// 11. Graceful degradation when APIs are missing
console.log('Testing Graceful Degradation in Empty Environment...');
const bareEngine = new VoiceEngine();
// Should not throw
bareEngine.startListening();
bareEngine.stopListening();
bareEngine.speak('');
bareEngine.stopSpeaking();
const level = bareEngine.getAudioLevel();
assert.strictEqual(level, 0.0);
console.log('✓ Graceful Degradation passed.');

console.log('\n==========================================');
console.log('ALL VOICE ENGINE UNIT TESTS PASSED (11/11)!');
console.log('==========================================');
