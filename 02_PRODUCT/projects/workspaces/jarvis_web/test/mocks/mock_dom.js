/**
 * mock_dom.js - Lightweight DOM & Browser Environment Mock for Node.js test runner
 * Implements HTML elements, EventTarget, query selectors, document, window, requestAnimationFrame, and localStorage.
 */

import { MockHTMLCanvasElement } from './mock_webgl.js';

export class MockDOMTokenList {
  constructor() {
    this._tokens = new Set();
  }

  add(...tokens) {
    for (const t of tokens) if (t) this._tokens.add(t);
  }

  remove(...tokens) {
    for (const t of tokens) this._tokens.delete(t);
  }

  contains(token) {
    return this._tokens.has(token);
  }

  toggle(token, force) {
    if (force !== undefined) {
      if (force) this.add(token);
      else this.remove(token);
      return force;
    }
    if (this.contains(token)) {
      this.remove(token);
      return false;
    }
    this.add(token);
    return true;
  }

  toString() {
    return Array.from(this._tokens).join(' ');
  }

  get value() {
    return this.toString();
  }

  set value(v) {
    this._tokens.clear();
    if (v) v.split(/\s+/).filter(Boolean).forEach(t => this._tokens.add(t));
  }
}

export class MockHTMLElement {
  constructor(tagName = 'DIV', document = null) {
    this.tagName = tagName.toUpperCase();
    this.nodeName = this.tagName;
    this.nodeType = 1;
    this.ownerDocument = document;
    this.id = '';
    this._className = '';
    this.classList = new MockDOMTokenList();
    this.style = {};
    this.attributes = new Map();
    this.children = [];
    this.childNodes = this.children;
    this.parentNode = null;
    this.parentElement = null;
    this.dataset = {};

    this._innerHTML = '';
    this._textContent = '';
    this.value = '';
    this.disabled = false;
    this.placeholder = '';
    this.scrollTop = 0;
    this.scrollHeight = 100;

    this._listeners = new Map();
  }

  get className() {
    return this.classList.toString();
  }

  set className(val) {
    this._className = val || '';
    this.classList.value = this._className;
  }

  get innerHTML() {
    return this._innerHTML;
  }

  set innerHTML(html) {
    this._innerHTML = String(html);
    this._textContent = String(html).replace(/<[^>]*>?/gm, '');
    this.children = [];

    const tagRegex = /<([a-zA-Z0-9_-]+)([^>]*)>([\s\S]*?)<\/\1>|<([a-zA-Z0-9_-]+)([^>]*)\/?>/g;
    let match;
    while ((match = tagRegex.exec(html)) !== null) {
      const tagName = match[1] || match[4];
      const attrStr = match[2] || match[5] || '';
      const innerContent = match[3] || '';

      const child = new MockHTMLElement(tagName, this.ownerDocument);
      const idMatch = attrStr.match(/id=["']([^"']*)["']/);
      if (idMatch) child.id = idMatch[1];

      const classMatch = attrStr.match(/class=["']([^"']*)["']/);
      if (classMatch) child.className = classMatch[1];

      if (innerContent) {
        child.innerHTML = innerContent;
      }
      child.parentNode = this;
      child.parentElement = this;
      this.children.push(child);
    }
  }

  get textContent() {
    return this._textContent;
  }

  set textContent(text) {
    this._textContent = String(text);
    this._innerHTML = String(text);
  }

  get innerText() {
    return this.textContent;
  }

  set innerText(text) {
    this.textContent = text;
  }

  setAttribute(name, value) {
    this.attributes.set(name, String(value));
    if (name === 'id') this.id = String(value);
    if (name === 'class') this.className = String(value);
    if (name === 'disabled') this.disabled = true;
    if (name.startsWith('data-')) {
      const key = name.slice(5).replace(/-([a-z])/g, (_, c) => c.toUpperCase());
      this.dataset[key] = String(value);
    }
  }

  getAttribute(name) {
    if (name === 'id') return this.id || null;
    if (name === 'class') return this.className || null;
    return this.attributes.has(name) ? this.attributes.get(name) : null;
  }

  removeAttribute(name) {
    this.attributes.delete(name);
    if (name === 'id') this.id = '';
    if (name === 'class') this.className = '';
    if (name === 'disabled') this.disabled = false;
  }

  hasAttribute(name) {
    return this.attributes.has(name);
  }

  appendChild(child) {
    if (!child) return null;
    child.parentNode = this;
    child.parentElement = this;
    child.ownerDocument = this.ownerDocument;
    this.children.push(child);
    return child;
  }

  removeChild(child) {
    const idx = this.children.indexOf(child);
    if (idx !== -1) {
      this.children.splice(idx, 1);
      child.parentNode = null;
      child.parentElement = null;
      return child;
    }
    return null;
  }

  remove() {
    if (this.parentNode) {
      this.parentNode.removeChild(this);
    }
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
    if (!event.target) event.target = this;
    event.currentTarget = this;
    const listeners = this._listeners.get(event.type);
    if (listeners) {
      for (const listener of listeners) {
        try {
          listener.call(this, event);
        } catch (err) {
          console.error(`Error in event ${event.type} on ${this.tagName}:`, err);
        }
      }
    }
    const handler = this[`on${event.type}`];
    if (typeof handler === 'function') {
      try {
        handler.call(this, event);
      } catch (err) {
        console.error(`Error in inline on${event.type}:`, err);
      }
    }
    return !event.defaultPrevented;
  }

  click() {
    this.dispatchEvent({ type: 'click', target: this, defaultPrevented: false, preventDefault: () => {} });
  }

  focus() {
    this.dispatchEvent({ type: 'focus', target: this });
  }

  blur() {
    this.dispatchEvent({ type: 'blur', target: this });
  }

  scrollIntoView() {}

  getBoundingClientRect() {
    return { top: 0, left: 0, width: 100, height: 100, right: 100, bottom: 100, x: 0, y: 0 };
  }

  querySelector(selector) {
    return this._matchSelector(selector, this);
  }

  querySelectorAll(selector) {
    const matches = [];
    this._collectMatches(selector, this, matches);
    return matches;
  }

  _matchSelector(selector, root) {
    for (const child of root.children) {
      if (this._isMatch(child, selector)) return child;
      const found = this._matchSelector(selector, child);
      if (found) return found;
    }
    return null;
  }

  _collectMatches(selector, root, matches) {
    for (const child of root.children) {
      if (this._isMatch(child, selector)) matches.push(child);
      this._collectMatches(selector, child, matches);
    }
  }

  _isMatch(node, selector) {
    if (!selector) return false;
    if (selector.startsWith('#')) {
      return node.id === selector.slice(1);
    }
    if (selector.startsWith('.')) {
      return node.classList.contains(selector.slice(1));
    }
    if (selector.startsWith('[')) {
      const match = selector.match(/\[([a-zA-Z0-9_-]+)(?:=["']?([^"']*)["']?)?\]/);
      if (match) {
        const [, attr, val] = match;
        return val !== undefined ? node.getAttribute(attr) === val : node.hasAttribute(attr);
      }
    }
    return node.tagName.toLowerCase() === selector.toLowerCase();
  }
}

export class MockLocalStorage {
  constructor() {
    this._store = new Map();
  }

  getItem(key) {
    return this._store.has(String(key)) ? this._store.get(String(key)) : null;
  }

  setItem(key, value) {
    this._store.set(String(key), String(value));
  }

  removeItem(key) {
    this._store.delete(String(key));
  }

  clear() {
    this._store.clear();
  }

  get length() {
    return this._store.size;
  }

  key(index) {
    return Array.from(this._store.keys())[index] || null;
  }
}

export class MockDocument {
  constructor() {
    this.nodeType = 9;
    this.documentElement = new MockHTMLElement('HTML', this);
    this.head = new MockHTMLElement('HEAD', this);
    this.body = new MockHTMLElement('BODY', this);
    this.documentElement.appendChild(this.head);
    this.documentElement.appendChild(this.body);
    this._elementsById = new Map();
    this._listeners = new Map();
  }

  createElement(tagName) {
    const tagUpper = (tagName || 'DIV').toUpperCase();
    if (tagUpper === 'CANVAS') {
      const canvas = new MockHTMLCanvasElement(800, 600);
      canvas.ownerDocument = this;
      return canvas;
    }
    return new MockHTMLElement(tagUpper, this);
  }

  getElementById(id) {
    return this.documentElement.querySelector(`#${id}`);
  }

  querySelector(selector) {
    return this.documentElement.querySelector(selector);
  }

  querySelectorAll(selector) {
    return this.documentElement.querySelectorAll(selector);
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
    const listeners = this._listeners.get(event.type);
    if (listeners) {
      for (const listener of listeners) {
        listener.call(this, event);
      }
    }
    return true;
  }
}

export class MockWindow {
  constructor() {
    this.document = new MockDocument();
    this.localStorage = new MockLocalStorage();
    this.innerWidth = 1920;
    this.innerHeight = 1080;
    this.devicePixelRatio = 1;
    this.navigator = {
      userAgent: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) MockBrowser/1.0',
      language: 'ro-RO',
      languages: ['ro-RO', 'en-US', 'en'],
      mediaDevices: {
        getUserMedia: async () => ({
          getTracks: () => [{ stop: () => {} }]
        })
      }
    };

    this._listeners = new Map();
    this._rafId = 1;
    this._rafCallbacks = new Map();
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
    const listeners = this._listeners.get(event.type);
    if (listeners) {
      for (const listener of listeners) {
        listener.call(this, event);
      }
    }
    return true;
  }

  requestAnimationFrame(callback) {
    const id = this._rafId++;
    const timer = setImmediate(() => {
      this._rafCallbacks.delete(id);
      callback(Date.now());
    });
    this._rafCallbacks.set(id, timer);
    return id;
  }

  cancelAnimationFrame(id) {
    const timer = this._rafCallbacks.get(id);
    if (timer) {
      clearImmediate(timer);
      this._rafCallbacks.delete(id);
    }
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

export function installDOMMocks(target = globalThis) {
  const windowMock = new MockWindow();
  safeDefine(target, 'window', windowMock);
  safeDefine(target, 'document', windowMock.document);
  safeDefine(target, 'HTMLElement', MockHTMLElement);
  safeDefine(target, 'Element', MockHTMLElement);
  safeDefine(target, 'localStorage', windowMock.localStorage);
  safeDefine(target, 'navigator', windowMock.navigator);
  safeDefine(target, 'requestAnimationFrame', (cb) => windowMock.requestAnimationFrame(cb));
  safeDefine(target, 'cancelAnimationFrame', (id) => windowMock.cancelAnimationFrame(id));

  // Event constructor
  class EventMock {
    constructor(type, options = {}) {
      this.type = type;
      this.bubbles = Boolean(options.bubbles);
      this.cancelable = Boolean(options.cancelable);
      this.defaultPrevented = false;
    }
    preventDefault() {
      this.defaultPrevented = true;
    }
    stopPropagation() {}
  }

  class CustomEventMock extends EventMock {
    constructor(type, options = {}) {
      super(type, options);
      this.detail = options.detail || null;
    }
  }

  safeDefine(target, 'Event', EventMock);
  safeDefine(target, 'CustomEvent', CustomEventMock);

  return {
    window: windowMock,
    document: windowMock.document,
    localStorage: windowMock.localStorage
  };
}
