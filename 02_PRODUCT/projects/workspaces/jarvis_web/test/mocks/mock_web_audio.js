/**
 * mock_web_audio.js - Standalone high-fidelity test double for Web Audio API
 * Implements AudioContext, OscillatorNode, GainNode, BiquadFilterNode, AnalyserNode
 * and parametric automation curves.
 */

export class MockAudioParam {
  constructor(defaultValue = 1.0, minValue = -3.4028235e38, maxValue = 3.4028235e38) {
    this._value = defaultValue;
    this.defaultValue = defaultValue;
    this.minValue = minValue;
    this.maxValue = maxValue;
    this.scheduledEvents = [];
  }

  get value() {
    return this._value;
  }

  set value(v) {
    this._value = Number(v);
  }

  setValueAtTime(value, startTime) {
    this._value = Number(value);
    this.scheduledEvents.push({ type: 'setValue', value, time: startTime });
    return this;
  }

  linearRampToValueAtTime(value, endTime) {
    this._value = Number(value);
    this.scheduledEvents.push({ type: 'linearRamp', value, time: endTime });
    return this;
  }

  exponentialRampToValueAtTime(value, endTime) {
    this._value = Number(value);
    this.scheduledEvents.push({ type: 'exponentialRamp', value, time: endTime });
    return this;
  }

  setTargetAtTime(target, startTime, timeConstant) {
    this._value = Number(target);
    this.scheduledEvents.push({ type: 'setTarget', target, startTime, timeConstant });
    return this;
  }

  setValueCurveAtTime(values, startTime, duration) {
    if (values && values.length > 0) {
      this._value = Number(values[values.length - 1]);
    }
    this.scheduledEvents.push({ type: 'setValueCurve', values, startTime, duration });
    return this;
  }

  cancelScheduledValues(startTime) {
    this.scheduledEvents = this.scheduledEvents.filter(e => e.time < startTime);
    return this;
  }

  cancelAndHoldAtTime(cancelTime) {
    this.scheduledEvents = this.scheduledEvents.filter(e => e.time < cancelTime);
    return this;
  }
}

export class MockAudioNode {
  constructor(context, numberOfInputs = 1, numberOfOutputs = 1) {
    this.context = context;
    this.numberOfInputs = numberOfInputs;
    this.numberOfOutputs = numberOfOutputs;
    this.channelCount = 2;
    this.channelCountMode = 'max';
    this.channelInterpretation = 'speakers';
    this._connectedTo = new Set();
  }

  connect(destinationNode, outputIndex = 0, inputIndex = 0) {
    this._connectedTo.add(destinationNode);
    return destinationNode;
  }

  disconnect(destinationNode) {
    if (destinationNode) {
      this._connectedTo.delete(destinationNode);
    } else {
      this._connectedTo.clear();
    }
  }
}

export class MockOscillatorNode extends MockAudioNode {
  constructor(context) {
    super(context, 0, 1);
    this.type = 'sine';
    this.frequency = new MockAudioParam(440, 0, 24000);
    this.detune = new MockAudioParam(0, -153600, 153600);
    this.started = false;
    this.stopped = false;
    this.startTime = null;
    this.stopTime = null;
    this.onended = null;
  }

  start(when = 0) {
    if (this.started) {
      throw new Error('InvalidStateError: OscillatorNode cannot be restarted');
    }
    this.started = true;
    this.startTime = when;
  }

  stop(when = 0) {
    if (!this.started) {
      throw new Error('InvalidStateError: OscillatorNode has not been started');
    }
    if (this.stopped) {
      throw new Error('InvalidStateError: OscillatorNode cannot be stopped more than once');
    }
    this.stopped = true;
    this.stopTime = when;
    queueMicrotask(() => {
      if (typeof this.onended === 'function') {
        this.onended.call(this, { type: 'ended', target: this });
      }
    });
  }
}

export class MockGainNode extends MockAudioNode {
  constructor(context) {
    super(context, 1, 1);
    this.gain = new MockAudioParam(1.0, -3.4028235e38, 3.4028235e38);
  }
}

export class MockBiquadFilterNode extends MockAudioNode {
  constructor(context) {
    super(context, 1, 1);
    this.type = 'lowpass';
    this.frequency = new MockAudioParam(350, 0, 24000);
    this.detune = new MockAudioParam(0, -153600, 153600);
    this.Q = new MockAudioParam(1, 0.0001, 1000);
    this.gain = new MockAudioParam(0, -40, 40);
  }
}

export class MockAnalyserNode extends MockAudioNode {
  constructor(context) {
    super(context, 1, 1);
    this._fftSize = 2048;
    this.minDecibels = -100;
    this.maxDecibels = -30;
    this.smoothingTimeConstant = 0.8;
    this._customFrequencyData = null;
  }

  get fftSize() {
    return this._fftSize;
  }

  set fftSize(val) {
    const valid = [32, 64, 128, 256, 512, 1024, 2048, 4096, 8192, 16384, 32768];
    if (!valid.includes(val)) {
      throw new RangeError(`Invalid fftSize: ${val}`);
    }
    this._fftSize = val;
  }

  get frequencyBinCount() {
    return this._fftSize / 2;
  }

  setMockFrequencyData(data) {
    this._customFrequencyData = data ? new Uint8Array(data) : null;
  }

  getByteFrequencyData(array) {
    if (!array || !(array instanceof Uint8Array)) {
      throw new TypeError('Parameter 1 must be a Uint8Array');
    }
    const len = Math.min(array.length, this.frequencyBinCount);
    if (this._customFrequencyData) {
      for (let i = 0; i < len; i++) {
        array[i] = i < this._customFrequencyData.length ? this._customFrequencyData[i] : 0;
      }
      return;
    }

    // Generate realistic dynamic audio spectrum (bass spike + harmonic taper)
    for (let i = 0; i < len; i++) {
      const normalizedFreq = i / len;
      const energy = Math.max(0, Math.floor(180 * Math.exp(-3 * normalizedFreq) + 20 * Math.sin(i * 0.5)));
      array[i] = Math.min(255, energy);
    }
  }

  getByteTimeDomainData(array) {
    if (!array || !(array instanceof Uint8Array)) {
      throw new TypeError('Parameter 1 must be a Uint8Array');
    }
    for (let i = 0; i < array.length; i++) {
      array[i] = 128 + Math.floor(40 * Math.sin((i / array.length) * Math.PI * 4));
    }
  }

  getFloatFrequencyData(array) {
    if (!array || !(array instanceof Float32Array)) {
      throw new TypeError('Parameter 1 must be a Float32Array');
    }
    for (let i = 0; i < array.length; i++) {
      array[i] = -70.0 + 30.0 * Math.exp(-i / 50);
    }
  }

  getFloatTimeDomainData(array) {
    if (!array || !(array instanceof Float32Array)) {
      throw new TypeError('Parameter 1 must be a Float32Array');
    }
    for (let i = 0; i < array.length; i++) {
      array[i] = 0.5 * Math.sin((i / array.length) * Math.PI * 4);
    }
  }
}

export class MockAudioDestinationNode extends MockAudioNode {
  constructor(context) {
    super(context, 1, 0);
    this.maxChannelCount = 2;
  }
}

export class MockAudioContext {
  constructor(options = {}) {
    this.sampleRate = options.sampleRate || 44100;
    this.state = 'suspended'; // Standard browser behavior before user interaction
    this.currentTime = 0;
    this.destination = new MockAudioDestinationNode(this);
    this.onstatechange = null;
    this.createdNodes = [];
    this._listeners = new Map();

    // Auto-advance time slightly on creation
    this.currentTime = 0.001;
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
        console.error(`Error in AudioContext on${event.type}:`, err);
      }
    }
    const listeners = this._listeners.get(event.type);
    if (listeners) {
      for (const listener of listeners) {
        try {
          listener.call(this, event);
        } catch (err) {
          console.error(`Error in AudioContext ${event.type} listener:`, err);
        }
      }
    }
    return true;
  }

  async resume() {
    this.state = 'running';
    this.advanceTime(0.01);
    this.dispatchEvent({ type: 'statechange', target: this });
    return undefined;
  }

  async suspend() {
    this.state = 'suspended';
    this.dispatchEvent({ type: 'statechange', target: this });
    return undefined;
  }

  async close() {
    this.state = 'closed';
    this.dispatchEvent({ type: 'statechange', target: this });
    return undefined;
  }

  advanceTime(seconds) {
    this.currentTime = Math.round((this.currentTime + seconds) * 1000) / 1000;
  }

  createOscillator() {
    const osc = new MockOscillatorNode(this);
    this.createdNodes.push(osc);
    return osc;
  }

  createGain() {
    const gain = new MockGainNode(this);
    this.createdNodes.push(gain);
    return gain;
  }

  createBiquadFilter() {
    const filter = new MockBiquadFilterNode(this);
    this.createdNodes.push(filter);
    return filter;
  }

  createAnalyser() {
    const analyser = new MockAnalyserNode(this);
    this.createdNodes.push(analyser);
    return analyser;
  }

  createBufferSource() {
    const src = new MockAudioNode(this, 0, 1);
    src.buffer = null;
    src.start = (when = 0) => {};
    src.stop = (when = 0) => {};
    this.createdNodes.push(src);
    return src;
  }

  createMediaStreamSource(stream) {
    const src = new MockAudioNode(this, 0, 1);
    src.mediaStream = stream;
    this.createdNodes.push(src);
    return src;
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

export function installWebAudioMocks(target = globalThis) {
  safeDefine(target, 'AudioContext', MockAudioContext);
  safeDefine(target, 'webkitAudioContext', MockAudioContext);
  safeDefine(target, 'AudioParam', MockAudioParam);
  safeDefine(target, 'AudioNode', MockAudioNode);
  safeDefine(target, 'OscillatorNode', MockOscillatorNode);
  safeDefine(target, 'GainNode', MockGainNode);
  safeDefine(target, 'BiquadFilterNode', MockBiquadFilterNode);
  safeDefine(target, 'AnalyserNode', MockAnalyserNode);

  if (target.window && target.window !== target) {
    safeDefine(target.window, 'AudioContext', MockAudioContext);
    safeDefine(target.window, 'webkitAudioContext', MockAudioContext);
    safeDefine(target.window, 'AudioParam', MockAudioParam);
    safeDefine(target.window, 'AudioNode', MockAudioNode);
    safeDefine(target.window, 'OscillatorNode', MockOscillatorNode);
    safeDefine(target.window, 'GainNode', MockGainNode);
    safeDefine(target.window, 'BiquadFilterNode', MockBiquadFilterNode);
    safeDefine(target.window, 'AnalyserNode', MockAnalyserNode);
  }

  return {
    AudioContext: MockAudioContext,
    MockAudioParam,
    MockOscillatorNode,
    MockGainNode,
    MockBiquadFilterNode,
    MockAnalyserNode
  };
}
