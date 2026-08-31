/**
 * index.js - Central mock environment installer for JARVIS Web test suite.
 */

import { installWebSpeechMocks, MockSpeechRecognition, MockSpeechSynthesis, MockSpeechSynthesisUtterance } from './mock_web_speech.js';
import { installWebAudioMocks, MockAudioContext, MockAudioParam, MockOscillatorNode, MockGainNode, MockAnalyserNode } from './mock_web_audio.js';
import { installWebGLMocks, MockHTMLCanvasElement, MockWebGLRenderingContext } from './mock_webgl.js';
import { installFetchMock, MockFetchClient, MOCK_VAULT_KNOWLEDGE_BASE } from './mock_fetch.js';
import { installDOMMocks, MockWindow, MockDocument, MockHTMLElement } from './mock_dom.js';

export function setupTestEnvironment(target = globalThis) {
  const dom = installDOMMocks(target);
  const speech = installWebSpeechMocks(target);
  const audio = installWebAudioMocks(target);
  const webgl = installWebGLMocks(target);
  const fetchMock = installFetchMock(target);

  if (target.window) {
    installWebSpeechMocks(target.window);
    installWebAudioMocks(target.window);
    installWebGLMocks(target.window);
    installFetchMock(target.window);
  }

  return {
    dom,
    speech,
    audio,
    webgl,
    fetchMock,
    cleanup() {
      if (globalThis.fetch && globalThis.fetch.setOffline) {
        globalThis.fetch.setOffline(false);
        globalThis.fetch.clearRoutes();
      }
    }
  };
}

export {
  MockSpeechRecognition,
  MockSpeechSynthesis,
  MockSpeechSynthesisUtterance,
  MockAudioContext,
  MockAudioParam,
  MockOscillatorNode,
  MockGainNode,
  MockAnalyserNode,
  MockHTMLCanvasElement,
  MockWebGLRenderingContext,
  MockFetchClient,
  MOCK_VAULT_KNOWLEDGE_BASE,
  MockWindow,
  MockDocument,
  MockHTMLElement
};
