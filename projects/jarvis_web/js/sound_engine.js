/**
 * JARVIS Web Ecosystem — Tactical Sound Synthesis Engine
 * Module: projects/jarvis_web/js/sound_engine.js
 * 
 * Features:
 * - 100% Free, zero-external-audio-file procedural sound synthesizer
 * - Built natively with Web Audio API (AudioContext / webkitAudioContext)
 * - Pure mathematical oscillators, biquad filters, and exponential ADSR envelopes
 * - Seamless browser autoplay unlock management (click/touch/keydown hooks)
 * - 6 Tactical sci-fi audio effects:
 *   1. playWakeChime()       — Ascending triad crystal chime (Jarvis online)
 *   2. playListeningBeep()   — Dual ascending tech blip (listening engaged)
 *   3. startThinkingDrone()  — Sub-bass AM drone loop (computational thinking)
 *   4. stopThinkingDrone()   — Smooth fade-out of thinking drone
 *   5. playSuccessChime()    — Upbeat harmonic chord (task success)
 *   6. playErrorAlert()      — Dissonant alert buzz (error / rejection)
 *   7. playStandbyChirp()    — Soft descending standby tone (idle / sleep)
 *   8. playCitationPulse()   — High-frequency telemetry packet chirp
 *   9. playClickFeedback()   — Micro UI interaction blip
 */

export class TacticalAudio {
  constructor(options = {}) {
    this.options = options;
    this.ctx = null;
    this.masterGain = null;
    this.isMuted = false;
    this.isUnlocked = false;

    // Thinking Drone Audio Nodes
    this.droneOsc = null;
    this.droneSubOsc = null;
    this.droneLfo = null;
    this.droneLfoGain = null;
    this.droneFilter = null;
    this.droneGain = null;
    this.isDroneActive = false;

    this.onUserGestureBound = this.onUserGesture.bind(this);
    this.setupAutoplayUnlock();
  }

  /**
   * Initializes the Web Audio context and master gain stage.
   */
  init() {
    if (this.ctx) return;

    const AudioContextClass =
      (typeof window !== 'undefined' && (window.AudioContext || window.webkitAudioContext)) ||
      (typeof globalThis !== 'undefined' && (globalThis.AudioContext || globalThis.webkitAudioContext)) ||
      null;

    if (!AudioContextClass) {
      console.warn('TacticalAudio: Web Audio API is not supported in this environment.');
      return;
    }

    try {
      this.ctx = new AudioContextClass();
      this.masterGain = this.ctx.createGain();
      this.masterGain.gain.setValueAtTime(this.isMuted ? 0.0 : 0.85, this.ctx.currentTime);
      this.masterGain.connect(this.ctx.destination);

      if (this.ctx.state === 'running') {
        this.isUnlocked = true;
      }
    } catch (err) {
      console.warn('TacticalAudio: Error creating AudioContext:', err);
    }
  }

  /**
   * Sets up auto-unlock event listeners on window for the browser autoplay policy.
   */
  setupAutoplayUnlock() {
    if (typeof window === 'undefined') return;

    const unlockEvents = ['click', 'touchstart', 'keydown', 'mousedown'];
    const unlockHandler = () => {
      this.unlockAudioContext().then(() => {
        unlockEvents.forEach(evt => window.removeEventListener(evt, unlockHandler, { capture: true }));
      }).catch(() => {});
    };

    unlockEvents.forEach(evt => window.addEventListener(evt, unlockHandler, { capture: true, passive: true }));
  }

  onUserGesture() {
    this.unlockAudioContext();
  }

  /**
   * Unlocks the AudioContext if it is suspended by the browser.
   * @returns {Promise<void>}
   */
  async unlockAudioContext() {
    if (!this.ctx) {
      this.init();
    }
    if (this.ctx && this.ctx.state === 'suspended') {
      try {
        await this.ctx.resume();
        this.isUnlocked = true;
      } catch (err) {
        console.warn('TacticalAudio: Failed to resume AudioContext:', err);
      }
    } else if (this.ctx && this.ctx.state === 'running') {
      this.isUnlocked = true;
    }
  }

  /**
   * Sets the mute state of the audio engine.
   * @param {boolean} muted 
   */
  setMuted(muted) {
    this.isMuted = !!muted;
    if (this.masterGain && this.ctx) {
      const targetGain = this.isMuted ? 0.0 : 0.85;
      const t = this.ctx.currentTime;
      this.masterGain.gain.cancelScheduledValues(t);
      this.masterGain.gain.setTargetAtTime(targetGain, t, 0.02);
    }
  }

  /**
   * Returns current mute state.
   * @returns {boolean}
   */
  isMutedState() {
    return this.isMuted;
  }

  /**
   * Returns the underlying AudioContext instance.
   * @returns {AudioContext | null}
   */
  getAudioContext() {
    return this.ctx;
  }

  /**
   * Sound 1: Ascending Triad Harmonic Crystal Chime
   * Plays when wake-word ("Jarvis") is detected or system comes online.
   */
  playWakeChime() {
    if (this.isMuted) return;
    this.unlockAudioContext();
    if (!this.ctx) return;

    const t = this.ctx.currentTime;
    const notes = [
      { freq: 739.99, time: t + 0.00, dur: 0.28 }, // F#5
      { freq: 1108.73, time: t + 0.08, dur: 0.32 }, // C#6
      { freq: 1479.98, time: t + 0.16, dur: 0.45 }  // F#6
    ];

    notes.forEach(({ freq, time, dur }) => {
      try {
        const osc = this.ctx.createOscillator();
        const gain = this.ctx.createGain();
        const filter = this.ctx.createBiquadFilter();

        osc.type = 'sine';
        osc.frequency.setValueAtTime(freq, time);

        filter.type = 'bandpass';
        filter.frequency.setValueAtTime(freq, time);
        filter.Q.setValueAtTime(4.0, time);

        gain.gain.setValueAtTime(0.0001, time);
        gain.gain.exponentialRampToValueAtTime(0.35, time + 0.015);
        gain.gain.exponentialRampToValueAtTime(0.0001, time + dur);

        osc.connect(filter);
        filter.connect(gain);
        gain.connect(this.masterGain);

        osc.start(time);
        osc.stop(time + dur);
        osc.onended = () => {
          osc.disconnect();
          filter.disconnect();
          gain.disconnect();
        };
      } catch (e) {}
    });
  }

  /**
   * Sound 2: Dual Ascending Tech Blip
   * Plays when entering listening state.
   */
  playListeningBeep() {
    if (this.isMuted) return;
    this.unlockAudioContext();
    if (!this.ctx) return;

    const t = this.ctx.currentTime;

    // Blip 1 (Triangle wave sweep 440 -> 880 Hz)
    try {
      const osc1 = this.ctx.createOscillator();
      const gain1 = this.ctx.createGain();

      osc1.type = 'triangle';
      osc1.frequency.setValueAtTime(440, t);
      osc1.frequency.exponentialRampToValueAtTime(880, t + 0.04);

      gain1.gain.setValueAtTime(0.0001, t);
      gain1.gain.exponentialRampToValueAtTime(0.24, t + 0.008);
      gain1.gain.exponentialRampToValueAtTime(0.0001, t + 0.045);

      osc1.connect(gain1);
      gain1.connect(this.masterGain);

      osc1.start(t);
      osc1.stop(t + 0.05);
      osc1.onended = () => {
        osc1.disconnect();
        gain1.disconnect();
      };
    } catch (e) {}

    // Blip 2 (Sine wave confirmation 880 -> 1320 Hz)
    try {
      const osc2 = this.ctx.createOscillator();
      const gain2 = this.ctx.createGain();
      const t2 = t + 0.045;

      osc2.type = 'sine';
      osc2.frequency.setValueAtTime(880, t2);
      osc2.frequency.exponentialRampToValueAtTime(1320, t2 + 0.035);

      gain2.gain.setValueAtTime(0.0001, t2);
      gain2.gain.exponentialRampToValueAtTime(0.20, t2 + 0.006);
      gain2.gain.exponentialRampToValueAtTime(0.0001, t2 + 0.040);

      osc2.connect(gain2);
      gain2.connect(this.masterGain);

      osc2.start(t2);
      osc2.stop(t2 + 0.045);
      osc2.onended = () => {
        osc2.disconnect();
        gain2.disconnect();
      };
    } catch (e) {}
  }

  /**
   * Sound 3: Continuous Sub-Bass AM Drone Loop
   * Loops during computational search, reasoning, and memory retrieval.
   */
  startThinkingDrone() {
    if (this.isMuted || this.isDroneActive) return;
    this.unlockAudioContext();
    if (!this.ctx) return;

    try {
      const t = this.ctx.currentTime;
      this.isDroneActive = true;

      // 1. Fundamental Oscillator (Sawtooth @ 55 Hz / A1)
      this.droneOsc = this.ctx.createOscillator();
      this.droneOsc.type = 'sawtooth';
      this.droneOsc.frequency.setValueAtTime(55, t);

      // 2. Sub-Bass Sine Oscillator (110 Hz / A2)
      this.droneSubOsc = this.ctx.createOscillator();
      this.droneSubOsc.type = 'sine';
      this.droneSubOsc.frequency.setValueAtTime(110, t);

      // 3. 4 Hz Low Frequency Oscillator (LFO) for AM pulsing modulation
      this.droneLfo = this.ctx.createOscillator();
      this.droneLfoGain = this.ctx.createGain();
      this.droneLfo.type = 'sine';
      this.droneLfo.frequency.setValueAtTime(4.0, t);
      this.droneLfoGain.gain.setValueAtTime(12, t);
      this.droneLfo.connect(this.droneOsc.frequency);

      // 4. Resonant Lowpass Filter (160 Hz, Q=3.2)
      this.droneFilter = this.ctx.createBiquadFilter();
      this.droneFilter.type = 'lowpass';
      this.droneFilter.frequency.setValueAtTime(160, t);
      this.droneFilter.Q.setValueAtTime(3.2, t);

      // 5. Drone Gain with smooth fade-in
      this.droneGain = this.ctx.createGain();
      this.droneGain.gain.setValueAtTime(0.0001, t);
      this.droneGain.gain.exponentialRampToValueAtTime(0.09, t + 0.35);

      this.droneOsc.connect(this.droneFilter);
      this.droneSubOsc.connect(this.droneFilter);
      this.droneFilter.connect(this.droneGain);
      this.droneGain.connect(this.masterGain);

      this.droneOsc.start(t);
      this.droneSubOsc.start(t);
      this.droneLfo.start(t);
    } catch (err) {
      console.warn('TacticalAudio: Failed to start thinking drone:', err);
    }
  }

  /**
   * Stops the thinking drone with a smooth exponential fade-out.
   */
  stopThinkingDrone() {
    if (!this.isDroneActive || !this.droneGain || !this.ctx) {
      this.isDroneActive = false;
      return;
    }

    try {
      const t = this.ctx.currentTime;
      this.droneGain.gain.cancelScheduledValues(t);
      this.droneGain.gain.setValueAtTime(this.droneGain.gain.value, t);
      this.droneGain.gain.exponentialRampToValueAtTime(0.0001, t + 0.20);

      const oldOsc = this.droneOsc;
      const oldSub = this.droneSubOsc;
      const oldLfo = this.droneLfo;
      const oldFilter = this.droneFilter;
      const oldGain = this.droneGain;

      this.droneOsc = null;
      this.droneSubOsc = null;
      this.droneLfo = null;
      this.droneLfoGain = null;
      this.droneFilter = null;
      this.droneGain = null;
      this.isDroneActive = false;

      setTimeout(() => {
        try {
          if (oldOsc) oldOsc.stop();
          if (oldSub) oldSub.stop();
          if (oldLfo) oldLfo.stop();
          if (oldOsc) oldOsc.disconnect();
          if (oldSub) oldSub.disconnect();
          if (oldFilter) oldFilter.disconnect();
          if (oldGain) oldGain.disconnect();
        } catch (e) {}
      }, 250);
    } catch (err) {
      this.isDroneActive = false;
    }
  }

  /**
   * Sound 4: Upbeat Harmonic Confirmation Chime
   * Plays when an action, memory search, or query successfully completes.
   */
  playSuccessChime() {
    if (this.isMuted) return;
    this.unlockAudioContext();
    if (!this.ctx) return;

    const t = this.ctx.currentTime;
    const notes = [
      { freq: 523.25, time: t + 0.00, dur: 0.35 }, // C5
      { freq: 659.25, time: t + 0.04, dur: 0.35 }, // E5
      { freq: 783.99, time: t + 0.08, dur: 0.38 }, // G5
      { freq: 1046.50, time: t + 0.12, dur: 0.48 } // C6
    ];

    notes.forEach(({ freq, time, dur }) => {
      try {
        const osc = this.ctx.createOscillator();
        const gain = this.ctx.createGain();
        const filter = this.ctx.createBiquadFilter();

        osc.type = 'sine';
        osc.frequency.setValueAtTime(freq, time);

        filter.type = 'lowpass';
        filter.frequency.setValueAtTime(2400, time);

        gain.gain.setValueAtTime(0.0001, time);
        gain.gain.exponentialRampToValueAtTime(0.28, time + 0.012);
        gain.gain.exponentialRampToValueAtTime(0.0001, time + dur);

        osc.connect(filter);
        filter.connect(gain);
        gain.connect(this.masterGain);

        osc.start(time);
        osc.stop(time + dur);
        osc.onended = () => {
          osc.disconnect();
          filter.disconnect();
          gain.disconnect();
        };
      } catch (e) {}
    });
  }

  /**
   * Sound 5: Dissonant Alert Buzz
   * Plays when an error, command failure, or API rejection occurs.
   */
  playErrorAlert() {
    if (this.isMuted) return;
    this.unlockAudioContext();
    if (!this.ctx) return;

    const t = this.ctx.currentTime;
    const freqs = [440.00, 466.16]; // Minor second interval dissonance

    freqs.forEach(freq => {
      try {
        const osc = this.ctx.createOscillator();
        const gain = this.ctx.createGain();
        const filter = this.ctx.createBiquadFilter();

        osc.type = 'sawtooth';
        osc.frequency.setValueAtTime(freq, t);

        filter.type = 'lowpass';
        filter.frequency.setValueAtTime(900, t);
        filter.frequency.exponentialRampToValueAtTime(160, t + 0.28);
        filter.Q.setValueAtTime(4.5, t);

        gain.gain.setValueAtTime(0.0001, t);
        gain.gain.exponentialRampToValueAtTime(0.26, t + 0.015);
        gain.gain.exponentialRampToValueAtTime(0.0001, t + 0.30);

        osc.connect(filter);
        filter.connect(gain);
        gain.connect(this.masterGain);

        osc.start(t);
        osc.stop(t + 0.32);
        osc.onended = () => {
          osc.disconnect();
          filter.disconnect();
          gain.disconnect();
        };
      } catch (e) {}
    });
  }

  /**
   * Sound 6: Soft Descending Standby Tone
   * Plays when system enters idle / standby mode.
   */
  playStandbyChirp() {
    if (this.isMuted) return;
    this.unlockAudioContext();
    if (!this.ctx) return;

    const t = this.ctx.currentTime;
    try {
      const osc = this.ctx.createOscillator();
      const gain = this.ctx.createGain();
      const filter = this.ctx.createBiquadFilter();

      osc.type = 'sine';
      osc.frequency.setValueAtTime(880, t);
      osc.frequency.exponentialRampToValueAtTime(440, t + 0.12);

      filter.type = 'lowpass';
      filter.frequency.setValueAtTime(1200, t);

      gain.gain.setValueAtTime(0.0001, t);
      gain.gain.exponentialRampToValueAtTime(0.18, t + 0.010);
      gain.gain.exponentialRampToValueAtTime(0.0001, t + 0.14);

      osc.connect(filter);
      filter.connect(gain);
      gain.connect(this.masterGain);

      osc.start(t);
      osc.stop(t + 0.15);
      osc.onended = () => {
        osc.disconnect();
        filter.disconnect();
        gain.disconnect();
      };
    } catch (e) {}
  }

  /**
   * Sound 7: Telemetry Packet Triplet Chirp
   * Plays upon citation or memory chunk retrieval.
   */
  playCitationPulse() {
    if (this.isMuted) return;
    this.unlockAudioContext();
    if (!this.ctx) return;

    const t = this.ctx.currentTime;
    const freqs = [1200, 1600, 2400];

    freqs.forEach((freq, idx) => {
      try {
        const time = t + idx * 0.025;
        const osc = this.ctx.createOscillator();
        const gain = this.ctx.createGain();

        osc.type = 'sine';
        osc.frequency.setValueAtTime(freq, time);

        gain.gain.setValueAtTime(0.0001, time);
        gain.gain.exponentialRampToValueAtTime(0.16, time + 0.004);
        gain.gain.exponentialRampToValueAtTime(0.0001, time + 0.022);

        osc.connect(gain);
        gain.connect(this.masterGain);

        osc.start(time);
        osc.stop(time + 0.025);
        osc.onended = () => {
          osc.disconnect();
          gain.disconnect();
        };
      } catch (e) {}
    });
  }

  /**
   * Sound 8: Micro UI Interaction Click
   * Plays on button taps, toggle switches, or keystrokes.
   */
  playClickFeedback() {
    if (this.isMuted) return;
    this.unlockAudioContext();
    if (!this.ctx) return;

    const t = this.ctx.currentTime;
    try {
      const osc = this.ctx.createOscillator();
      const gain = this.ctx.createGain();

      osc.type = 'sine';
      osc.frequency.setValueAtTime(1400, t);
      osc.frequency.exponentialRampToValueAtTime(700, t + 0.008);

      gain.gain.setValueAtTime(0.0001, t);
      gain.gain.exponentialRampToValueAtTime(0.12, t + 0.002);
      gain.gain.exponentialRampToValueAtTime(0.0001, t + 0.010);

      osc.connect(gain);
      gain.connect(this.masterGain);

      osc.start(t);
      osc.stop(t + 0.012);
      osc.onended = () => {
        osc.disconnect();
        gain.disconnect();
      };
    } catch (e) {}
  }

  /**
   * Cleans up all active audio nodes and closes the AudioContext.
   */
  destroy() {
    this.stopThinkingDrone();
    if (this.masterGain) {
      this.masterGain.disconnect();
      this.masterGain = null;
    }
    if (this.ctx) {
      try {
        this.ctx.close();
      } catch (e) {}
      this.ctx = null;
    }
    this.isUnlocked = false;
  }
}

// Global browser and Node.js testing environment registration
if (typeof window !== 'undefined') {
  window.TacticalAudio = TacticalAudio;
}
if (typeof globalThis !== 'undefined') {
  globalThis.TacticalAudio = TacticalAudio;
}
