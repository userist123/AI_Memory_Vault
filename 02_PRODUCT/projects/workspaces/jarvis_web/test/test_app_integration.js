/**
 * test_app_integration.js - Direct Integration Test for JarvisApp & StateMachine
 */

import { describe, it, beforeEach, afterEach } from 'node:test';
import assert from 'node:assert/strict';

import {
  setupTestEnvironment,
  MockSpeechRecognition,
  MockSpeechSynthesis,
  MockAudioContext,
  MockFetchClient
} from './mocks/index.js';

// Setup environment
const env = setupTestEnvironment(globalThis);

import { StateMachine } from '../js/state_machine.js';
import { JarvisApp } from '../js/app.js';

describe('M4 Integration: StateMachine & JarvisApp', () => {
  let app;

  beforeEach(() => {
    env.cleanup();

    // Create full DOM tree matching index.html
    document.body.innerHTML = `
      <div id="app-container">
        <div id="jarvis-hud"></div>
        <div id="hologram-container"></div>
        <div id="conversation-stream"></div>
        <div id="citation-inspector-panel"></div>
        <div id="citations-list"></div>
        <span id="citations-count-badge">0</span>
        <div id="council-telemetry">
          <div id="agent-router"><span class="status-indicator"></span><div class="agent-progress-fill"></div><span class="status-badge"></span></div>
          <div id="agent-retrieval"><span class="status-indicator"></span><div class="agent-progress-fill"></div><span class="status-badge"></span></div>
          <div id="agent-verifier"><span class="status-indicator"></span><div class="agent-progress-fill"></div><span class="status-badge"></span></div>
          <div id="agent-consolidator"><span class="status-indicator"></span><div class="agent-progress-fill"></div><span class="status-badge"></span></div>
          <div id="agent-critic"><span class="status-indicator"></span><div class="agent-progress-fill"></div><span class="status-badge"></span></div>
        </div>
        <div id="audio-visualizer-bars"></div>
        <span id="state-pill-val"></span>
        <span id="state-display-text"></span>
        <span id="voice-status"></span>
        <span id="vault-status"></span>
        <span id="fps-meter"></span>
        <span id="mic-status-val"></span>
        <div id="telemetry-meter"><span class="latency-val"></span></div>
        <span id="indexed-notes-count"></span>
        <span id="cache-hit-ratio"></span>
        <span id="active-voice-name"></span>
        <div id="live-speech-bubble" class="hidden"></div>
        <span id="live-transcript-text"></span>
        <form id="prompt-form"><input id="prompt-input" /><button id="submit-btn"></button></form>
        <select id="lang-select"><option value="auto">Auto</option><option value="ro-RO">RO</option><option value="en-US">EN</option></select>
        <span id="command-feedback"></span>
        <button id="mic-toggle-btn"></button>
        <button id="wake-btn"></button>
        <button id="propose-note-btn"></button>
        <button id="diagnostics-btn"></button>
        <button id="clear-logs-btn"></button>
        <button id="btn-quick-wake"></button>
        <button id="btn-quick-stop"></button>
        <button id="btn-quick-clear"></button>
        <button id="tab-btn-stream"></button>
        <button id="tab-btn-citations"></button>
        <div id="audio-unlock-modal" class="hidden"><button id="audio-unlock-btn"></button></div>
        <div id="proposal-modal" class="hidden">
          <form id="proposal-form">
            <input id="prop-title" />
            <input id="prop-summary" />
            <select id="prop-category"><option value="01_KNOWLEDGE">01_KNOWLEDGE</option></select>
            <select id="prop-confidence"><option value="medium">medium</option></select>
            <input id="prop-tags" />
            <textarea id="prop-content"></textarea>
            <button id="prop-cancel-btn"></button>
          </form>
          <button id="modal-close-btn"></button>
        </div>
        <div id="diagnostics-modal" class="hidden">
          <button id="diag-close-btn"></button>
          <button id="diag-done-btn"></button>
          <button id="diag-run-test-btn"></button>
          <div id="diag-content"></div>
        </div>
      </div>
    `;

    app = new JarvisApp();
  });

  afterEach(() => {
    if (app) {
      app.destroy();
    }
    env.cleanup();
  });

  it('StateMachine initializes, transitions, and broadcasts payloads correctly', () => {
    const sm = new StateMachine('IDLE');
    assert.strictEqual(sm.getState(), 'IDLE');

    let captured = null;
    sm.subscribe((state, prev, payload) => {
      captured = { state, prev, payload };
    });

    sm.setState('LISTENING', { source: 'wake_button' });
    assert.strictEqual(captured.state, 'LISTENING');
    assert.strictEqual(captured.prev, 'IDLE');
    assert.strictEqual(captured.payload.source, 'wake_button');
  });

  it('JarvisApp initializes subsystems and binds DOM without errors', async () => {
    await app.init();
    assert.strictEqual(app.isInitialized, true);
    assert.strictEqual(app.stateMachine.getState(), 'IDLE');
  });

  it('JarvisApp handles text prompt submission and searches memory bank', async () => {
    try {
      await app.init();
      app.dom.promptInput.value = 'cauta reguli de memorie';
      await app.handlePromptSubmit();

      assert.ok(app.dom.conversationStream.children.length >= 2, 'Should contain user message and assistant answer');
      assert.strictEqual(app.inputHistory.length, 1);
      assert.strictEqual(app.inputHistory[0], 'cauta reguli de memorie');
    } catch (err) {
      console.error('Test prompt failure:', err);
      throw err;
    }
  });

  it('JarvisApp handles manual wake and muting', async () => {
    try {
      await app.init();
      app.wakeJarvis();
      assert.strictEqual(app.stateMachine.getState(), 'LISTENING');
      assert.strictEqual(app.voice.isListeningDesired, true);

      app.toggleMicrophone();
      assert.strictEqual(app.stateMachine.getState(), 'MUTED');
      assert.strictEqual(app.voice.isMuted, true);
    } catch (err) {
      console.error('Test wake/mute failure:', err);
      throw err;
    }
  });

  it('JarvisApp opens and processes memory proposals into REVIEW lifecycle', async () => {
    try {
      await app.init();
      app.openProposalModal();
      assert.strictEqual(app.dom.proposalModal.classList.contains('hidden'), false);

      app.dom.propTitle.value = 'Procedure: Production Containerization';
      app.dom.propSummary.value = 'Idempotent Docker container lifecycle procedure';
      await app.submitNoteProposal();

      assert.strictEqual(app.dom.proposalModal.classList.contains('hidden'), true);
    } catch (err) {
      console.error('Test proposal failure:', err);
      throw err;
    }
  });

  it('JarvisApp diagnostics modal renders telemetry details', async () => {
    try {
      await app.init();
      app.openDiagnosticsModal();
      assert.strictEqual(app.dom.diagnosticsModal.classList.contains('hidden'), false);
      assert.ok(app.dom.diagContent.innerHTML.includes('Web Speech STT'));
      app.closeDiagnosticsModal();
      assert.strictEqual(app.dom.diagnosticsModal.classList.contains('hidden'), true);
    } catch (err) {
      console.error('Test diagnostics failure:', err);
      throw err;
    }
  });
});
