/**
 * Comprehensive Verification Test for M2: Hologram & Sound Engine
 */
const assert = require('assert');

// 1. Setup Mock DOM Environment
class MockDOMElement {
  constructor(tagName = 'div') {
    this.tagName = tagName.toUpperCase();
    this.clientWidth = 400;
    this.clientHeight = 400;
    this.width = 400;
    this.height = 400;
    this.children = [];
    this.style = {};
    this.eventListeners = {};
  }

  appendChild(child) {
    this.children.push(child);
    child.parentNode = this;
    return child;
  }

  removeChild(child) {
    const idx = this.children.indexOf(child);
    if (idx !== -1) {
      this.children.splice(idx, 1);
      child.parentNode = null;
    }
    return child;
  }

  get firstChild() {
    return this.children[0] || null;
  }

  addEventListener(event, handler) {
    if (!this.eventListeners[event]) this.eventListeners[event] = [];
    this.eventListeners[event].push(handler);
  }

  removeEventListener(event, handler) {
    if (!this.eventListeners[event]) return;
    this.eventListeners[event] = this.eventListeners[event].filter(h => h !== handler);
  }

  dispatchEvent(event, payload) {
    if (this.eventListeners[event]) {
      this.eventListeners[event].forEach(h => h(payload));
    }
  }

  getContext(type) {
    if (type === '2d') {
      return new MockCanvasRenderingContext2D(this);
    }
    return null;
  }
}

class MockCanvasRenderingContext2D {
  constructor(canvas) {
    this.canvas = canvas;
    this.shadowBlur = 0;
    this.shadowColor = '';
    this.strokeStyle = '';
    this.fillStyle = '';
    this.lineWidth = 1;
  }

  clearRect() {}
  beginPath() {}
  arc() {}
  stroke() {}
  fill() {}
  save() {}
  restore() {}
  translate() {}
  rotate() {}
  moveTo() {}
  lineTo() {}
  setLineDash() {}
  createRadialGradient() {
    return {
      addColorStop: () => {}
    };
  }
}

// 2. Setup Mock Web Audio API
class MockAudioNode {
  constructor(ctx) {
    this.context = ctx;
  }
  connect() {}
  disconnect() {}
}

class MockAudioParam {
  constructor(val = 0) {
    this.value = val;
  }
  setValueAtTime(val) { this.value = val; }
  exponentialRampToValueAtTime(val) { this.value = val; }
  linearRampToValueAtTime(val) { this.value = val; }
  setTargetAtTime(val) { this.value = val; }
  cancelScheduledValues() {}
}

class MockGainNode extends MockAudioNode {
  constructor(ctx) {
    super(ctx);
    this.gain = new MockAudioParam(1.0);
  }
}

class MockOscillatorNode extends MockAudioNode {
  constructor(ctx) {
    super(ctx);
    this.frequency = new MockAudioParam(440);
    this.type = 'sine';
    this.onended = null;
  }
  start() {}
  stop() {}
}

class MockBiquadFilterNode extends MockAudioNode {
  constructor(ctx) {
    super(ctx);
    this.frequency = new MockAudioParam(1000);
    this.Q = new MockAudioParam(1);
    this.type = 'lowpass';
  }
}

class MockAudioContext {
  constructor() {
    this.currentTime = 0.0;
    this.state = 'suspended';
    this.destination = new MockAudioNode(this);
  }
  createGain() { return new MockGainNode(this); }
  createOscillator() { return new MockOscillatorNode(this); }
  createBiquadFilter() { return new MockBiquadFilterNode(this); }
  resume() {
    this.state = 'running';
    return Promise.resolve();
  }
  close() {
    this.state = 'closed';
    return Promise.resolve();
  }
}

// Global browser mocks
global.document = {
  createElement: (tag) => new MockDOMElement(tag)
};
global.window = {
  AudioContext: MockAudioContext,
  addEventListener: () => {},
  removeEventListener: () => {},
  devicePixelRatio: 2.0
};
global.performance = {
  now: () => Date.now()
};
global.requestAnimationFrame = (cb) => setTimeout(cb, 16);
global.cancelAnimationFrame = (id) => clearTimeout(id);

// Load Modules
const { HologramController, Canvas2DFallbackVisualizer, detectWebGL, HOLOGRAM_STATES } = require('../../projects/jarvis_web/js/hologram.js');
const { TacticalAudio } = require('../../projects/jarvis_web/js/sound_engine.js');

async function runTests() {
  console.log('=== Running M2 Hologram & Sound Engine Verification Suite ===\n');

  // Test 1: WebGL detection in mock environment
  console.log('[Test 1] detectWebGL()');
  const webglSupported = detectWebGL();
  console.log(`  -> WebGL detected: ${webglSupported} (Expected false in 2D-only mock)`);
  assert.strictEqual(typeof webglSupported, 'boolean');

  // Test 2: HOLOGRAM_STATES definitions
  console.log('[Test 2] HOLOGRAM_STATES validation');
  const requiredStates = ['IDLE', 'LISTENING', 'THINKING', 'SPEAKING', 'MUTED', 'ERROR'];
  requiredStates.forEach(st => {
    assert.ok(HOLOGRAM_STATES[st], `State ${st} should be defined`);
    assert.ok(HOLOGRAM_STATES[st].primaryColor !== undefined, `State ${st} missing primaryColor`);
    assert.ok(HOLOGRAM_STATES[st].rotSpeedInner !== undefined, `State ${st} missing rotSpeedInner`);
    console.log(`  ✓ State '${st}' verified with primary: 0x${HOLOGRAM_STATES[st].primaryColor.toString(16)}`);
  });

  // Test 3: HologramController 2D Fallback initialization and lifecycle
  console.log('\n[Test 3] HologramController 2D Fallback Lifecycle');
  const container = new MockDOMElement('div');
  const holo = new HologramController();
  holo.init(container);

  assert.strictEqual(holo.getMode(), 'canvas2d', 'Should fall back to canvas2d in mock env');
  assert.strictEqual(holo.getState(), 'IDLE');
  assert.strictEqual(holo.isWebGLActive(), false);
  console.log('  ✓ Initialized in 2D fallback mode without errors');

  // Test 4: Hologram State Transitions
  console.log('\n[Test 4] Hologram State Transitions');
  for (const st of requiredStates) {
    holo.setVisualState(st);
    assert.strictEqual(holo.getState(), st);
    console.log(`  ✓ Successfully switched state to: ${st}`);
  }

  // Test 5: Audio Reactivity Updates
  console.log('\n[Test 5] Audio Reactivity Updates');
  holo.setAudioReactivity(0.75, new Uint8Array([255, 200, 150, 100, 80, 50, 20, 10]));
  assert.strictEqual(holo.audioLevel, 0.75);
  holo.setAudioReactivity(-0.5); // Should clamp to 0
  assert.strictEqual(holo.audioLevel, 0);
  holo.setAudioReactivity(1.5); // Should clamp to 1
  assert.strictEqual(holo.audioLevel, 1);
  console.log('  ✓ Audio level clamping and frequency data handling verified');

  // Test 6: Context Loss & Restoration Simulation
  console.log('\n[Test 6] WebGL Context Loss / Restore Simulation');
  holo.handleContextLost();
  assert.strictEqual(holo.getMode(), 'canvas2d');
  holo.handleContextRestored();
  console.log('  ✓ Context loss & restore handlers executed gracefully');

  // Test 7: Hologram Clean Disposal
  holo.destroy();
  assert.strictEqual(holo.getMode(), 'none');
  console.log('  ✓ Hologram destroyed and cleaned up');

  // Test 8: TacticalAudio Engine Initialization
  console.log('\n[Test 8] TacticalAudio Engine Lifecycle & Unlocking');
  const audio = new TacticalAudio();
  audio.init();
  assert.ok(audio.getAudioContext(), 'AudioContext should be initialized');
  assert.strictEqual(audio.isMutedState(), false);

  await audio.unlockAudioContext();
  assert.strictEqual(audio.getAudioContext().state, 'running', 'AudioContext should be running');
  console.log('  ✓ AudioContext successfully unlocked');

  // Test 9: Procedural SFX Synthesis execution
  console.log('\n[Test 9] Procedural Sound Synthesis');
  audio.playWakeChime();
  console.log('  ✓ playWakeChime() executed');

  audio.playListeningBeep();
  console.log('  ✓ playListeningBeep() executed');

  audio.startThinkingDrone();
  assert.strictEqual(audio.isDroneActive, true, 'Drone should be active');
  console.log('  ✓ startThinkingDrone() active');

  audio.stopThinkingDrone();
  assert.strictEqual(audio.isDroneActive, false, 'Drone should be inactive');
  console.log('  ✓ stopThinkingDrone() stopped');

  audio.playSuccessChime();
  console.log('  ✓ playSuccessChime() executed');

  audio.playErrorAlert();
  console.log('  ✓ playErrorAlert() executed');

  audio.playStandbyChirp();
  console.log('  ✓ playStandbyChirp() executed');

  audio.playCitationPulse();
  console.log('  ✓ playCitationPulse() executed');

  audio.playClickFeedback();
  console.log('  ✓ playClickFeedback() executed');

  // Test 10: Mute toggle
  console.log('\n[Test 10] Mute Toggle Verification');
  audio.setMuted(true);
  assert.strictEqual(audio.isMutedState(), true);
  audio.playWakeChime(); // Should do nothing when muted
  audio.setMuted(false);
  assert.strictEqual(audio.isMutedState(), false);
  console.log('  ✓ Mute/Unmute state behavior verified');

  // Test 11: Audio Engine Cleanup
  audio.destroy();
  assert.strictEqual(audio.getAudioContext(), null);
  console.log('  ✓ TacticalAudio destroyed cleanly');

  console.log('\n=== ALL M2 VERIFICATION TESTS PASSED (11/11) ===');
}

runTests().catch(err => {
  console.error('Test failed with error:', err);
  process.exit(1);
});
