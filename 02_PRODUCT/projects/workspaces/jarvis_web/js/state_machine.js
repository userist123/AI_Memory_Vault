/**
 * JARVIS Web Ecosystem — Finite State Controller
 * Module: projects/jarvis_web/js/state_machine.js
 * 
 * Central State Controller synchronizing:
 * - Voice STT / TTS states (IDLE, LISTENING, THINKING, SPEAKING, MUTED, ERROR, INIT)
 * - Three.js 3D WebGL Holographic Arc-Reactor visuals & color shaders
 * - Web Audio procedural tactical sound cues
 * - UI HUD glow accents, status pills, and agent telemetry meters
 * 
 * Complies with JARVIS Master Architecture, Interface Contracts, and Test Suite.
 */

// Universal global accessor
const getGlobalScope = () => {
  if (typeof window !== 'undefined') return window;
  if (typeof globalThis !== 'undefined') return globalThis;
  if (typeof global !== 'undefined') return global;
  return {};
};

export class StateMachine {
  /**
   * Standard supported operational states
   */
  static STATES = Object.freeze({
    INIT: 'INIT',
    IDLE: 'IDLE',
    LISTENING: 'LISTENING',
    THINKING: 'THINKING',
    SPEAKING: 'SPEAKING',
    MUTED: 'MUTED',
    ERROR: 'ERROR'
  });

  /**
   * Allowed state transitions map for strict state machine flow
   */
  static TRANSITIONS = Object.freeze({
    INIT: ['IDLE', 'ERROR', 'MUTED'],
    IDLE: ['LISTENING', 'THINKING', 'SPEAKING', 'MUTED', 'ERROR'],
    LISTENING: ['IDLE', 'THINKING', 'SPEAKING', 'MUTED', 'ERROR'],
    THINKING: ['IDLE', 'SPEAKING', 'LISTENING', 'MUTED', 'ERROR'],
    SPEAKING: ['IDLE', 'LISTENING', 'THINKING', 'MUTED', 'ERROR'],
    MUTED: ['IDLE', 'LISTENING', 'ERROR'],
    ERROR: ['IDLE', 'LISTENING', 'MUTED']
  });

  /**
   * @param {string} [initialState='IDLE'] Starting state
   * @param {Object} [options={}] Configuration options
   * @param {boolean} [options.strict=false] If true, enforces TRANSITIONS table strictly
   * @param {Object} [options.timeouts] Automatic timeout recovery per state (in ms)
   * @param {Function} [options.onStateChange] Optional global state callback
   */
  constructor(initialState = StateMachine.STATES.IDLE, options = {}) {
    const normInitial = (initialState || StateMachine.STATES.IDLE).toUpperCase();
    this._state = Object.values(StateMachine.STATES).includes(normInitial)
      ? normInitial
      : StateMachine.STATES.IDLE;
    
    this._prevState = null;
    this._listeners = new Set();
    this._options = {
      strict: !!options.strict,
      timeouts: {
        THINKING: 30000,  // Max 30s in thinking before recovering to IDLE
        LISTENING: 60000, // Max 60s in listening before timeout
        ERROR: 15000,     // Max 15s in error before recovering to IDLE
        ...(options.timeouts || {})
      },
      ...options
    };

    this._stateTimer = null;
    this._history = [{ state: this._state, timestamp: Date.now(), payload: null }];

    if (options.onStateChange && typeof options.onStateChange === 'function') {
      this.subscribe(options.onStateChange);
    }

    this._scheduleStateTimeout(this._state);
  }

  /**
   * Returns current active state
   * @returns {string}
   */
  getState() {
    return this._state;
  }

  /**
   * Returns immediate previous state
   * @returns {string|null}
   */
  getPreviousState() {
    return this._prevState;
  }

  /**
   * Returns state transition history
   * @returns {Array<{ state: string, timestamp: number, payload: any }>}
   */
  getHistory() {
    return [...this._history];
  }

  /**
   * Check if state transition is valid
   * @param {string} targetState 
   * @returns {boolean}
   */
  canTransitionTo(targetState) {
    if (!targetState || typeof targetState !== 'string') return false;
    const norm = targetState.toUpperCase();
    const validStates = Object.values(StateMachine.STATES);
    if (!validStates.includes(norm)) return false;

    if (!this._options.strict) return true;

    const allowed = StateMachine.TRANSITIONS[this._state];
    return allowed ? allowed.includes(norm) : true;
  }

  /**
   * Transitions machine to new state and broadcasts to subscribers
   * @param {string} newState Target state name
   * @param {any} [payload=null] Optional metadata associated with event
   * @returns {boolean} True if state was transitioned or payload broadcasted
   */
  setState(newState, payload = null) {
    if (!newState || typeof newState !== 'string') {
      return false;
    }

    const normState = newState.toUpperCase();
    const validStates = Object.values(StateMachine.STATES);

    if (!validStates.includes(normState)) {
      return false;
    }

    // Check transition validity if in strict mode
    if (this._options.strict && !this.canTransitionTo(normState)) {
      console.warn(`StateMachine: Transition from ${this._state} to ${normState} is not allowed.`);
      return false;
    }

    // No-op if same state and no payload to broadcast
    if (this._state === normState && payload === null) {
      return false;
    }

    const oldState = this._state;
    this._prevState = oldState;
    this._state = normState;

    // Track history (cap to 50 entries)
    this._history.push({ state: normState, timestamp: Date.now(), payload });
    if (this._history.length > 50) {
      this._history.shift();
    }

    // Reset and schedule auto-recovery timeout
    this._scheduleStateTimeout(normState);

    // Notify all pub/sub listeners
    for (const listener of this._listeners) {
      try {
        listener(this._state, this._prevState, payload);
      } catch (err) {
        console.error('StateMachine: Error in subscriber listener:', err);
      }
    }

    return true;
  }

  /**
   * Subscribes a listener function to state changes
   * @param {Function} listener Callback receiving (newState, prevState, payload)
   * @returns {Function} Unsubscribe function
   */
  subscribe(listener) {
    if (typeof listener !== 'function') {
      throw new TypeError('StateMachine subscriber must be a function');
    }

    this._listeners.add(listener);

    return () => {
      this._listeners.delete(listener);
    };
  }

  /**
   * Schedule automatic state timeout recovery
   * @private
   */
  _scheduleStateTimeout(state) {
    if (this._stateTimer) {
      clearTimeout(this._stateTimer);
      this._stateTimer = null;
    }

    const timeoutMs = this._options.timeouts && this._options.timeouts[state];
    if (typeof timeoutMs === 'number' && timeoutMs > 0) {
      this._stateTimer = setTimeout(() => {
        // Auto-recover to IDLE on timeout
        if (this._state === state) {
          console.warn(`StateMachine: State ${state} timed out after ${timeoutMs}ms. Recovering to IDLE.`);
          this.setState(StateMachine.STATES.IDLE, { trigger: 'timeout_recovery', fromState: state });
        }
      }, timeoutMs);
    }
  }

  /**
   * Resets machine back to IDLE and clears all listeners
   */
  reset() {
    if (this._stateTimer) {
      clearTimeout(this._stateTimer);
      this._stateTimer = null;
    }
    this._prevState = this._state;
    this._state = StateMachine.STATES.IDLE;
    this._listeners.clear();
    this._history = [{ state: this._state, timestamp: Date.now(), payload: null }];
  }

  /**
   * Clean up timers and listener references
   */
  destroy() {
    this.reset();
  }
}

// Universal Global Scope Registration
const globalScope = getGlobalScope();
if (globalScope) {
  globalScope.StateMachine = StateMachine;
}

if (typeof module !== 'undefined' && module.exports) {
  module.exports = { StateMachine };
}

export default StateMachine;
