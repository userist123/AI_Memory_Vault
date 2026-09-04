/**
 * mock_webgl.js - Standalone high-fidelity test double for WebGL & Canvas API
 * Implements HTMLCanvasElement, WebGLRenderingContext, WebGL2RenderingContext,
 * 2D Canvas fallback context, and WebGL Context Loss / Restore event simulation.
 */

export class MockWebGLRenderingContext {
  constructor(canvas, isWebGL2 = false) {
    this.canvas = canvas;
    this.isWebGL2 = isWebGL2;
    this._isContextLost = false;
    this.drawCalls = 0;
    this.viewportWidth = canvas.width || 800;
    this.viewportHeight = canvas.height || 600;

    // WebGL Constants
    this.COLOR_BUFFER_BIT = 0x00004000;
    this.DEPTH_BUFFER_BIT = 0x00000100;
    this.STENCIL_BUFFER_BIT = 0x00000400;
    this.TRIANGLES = 0x0004;
    this.TRIANGLE_STRIP = 0x0005;
    this.LINES = 0x0001;
    this.POINTS = 0x0000;
    this.FLOAT = 0x1406;
    this.UNSIGNED_BYTE = 0x1401;
    this.UNSIGNED_SHORT = 0x1403;
    this.ARRAY_BUFFER = 0x8892;
    this.ELEMENT_ARRAY_BUFFER = 0x8893;
    this.STATIC_DRAW = 0x88e4;
    this.DYNAMIC_DRAW = 0x88e8;
    this.VERTEX_SHADER = 0x8b31;
    this.FRAGMENT_SHADER = 0x8b30;
    this.COMPILE_STATUS = 0x8b81;
    this.LINK_STATUS = 0x8b82;
    this.BLEND = 0x0be2;
    this.DEPTH_TEST = 0x0b71;
    this.CULL_FACE = 0x0b44;
    this.SRC_ALPHA = 0x0302;
    this.ONE_MINUS_SRC_ALPHA = 0x0303;
    this.ONE = 1;
    this.ZERO = 0;
    this.TEXTURE_2D = 0x0de1;
    this.RGBA = 0x1908;
    this.RGB = 0x1907;

    // State tracking
    this.clearColorValue = [0, 0, 0, 1];
    this.enabledCapabilities = new Set();
    this.currentProgram = null;
    this.boundBuffers = new Map();
    this.uniforms = new Map();
  }

  isContextLost() {
    return this._isContextLost;
  }

  viewport(x, y, width, height) {
    this.viewportWidth = width;
    this.viewportHeight = height;
  }

  clearColor(r, g, b, a) {
    this.clearColorValue = [r, g, b, a];
  }

  clear(mask) {
    // Clear simulation
  }

  enable(cap) {
    this.enabledCapabilities.add(cap);
  }

  disable(cap) {
    this.enabledCapabilities.delete(cap);
  }

  blendFunc(sfactor, dfactor) {}
  depthMask(flag) {}
  pixelStorei(pname, param) {}

  createBuffer() {
    return { id: Math.random().toString(36).substring(2), data: null };
  }

  bindBuffer(target, buffer) {
    this.boundBuffers.set(target, buffer);
  }

  bufferData(target, data, usage) {
    const buf = this.boundBuffers.get(target);
    if (buf) {
      buf.data = data;
      buf.usage = usage;
    }
  }

  createShader(type) {
    return { id: Math.random().toString(36).substring(2), type, source: '', compiled: true };
  }

  shaderSource(shader, source) {
    if (shader) shader.source = source;
  }

  compileShader(shader) {
    if (shader) shader.compiled = true;
  }

  getShaderParameter(shader, pname) {
    if (pname === this.COMPILE_STATUS) return true;
    return 1;
  }

  getShaderInfoLog(shader) {
    return '';
  }

  createProgram() {
    return { id: Math.random().toString(36).substring(2), shaders: [], linked: true };
  }

  attachShader(program, shader) {
    if (program && shader) program.shaders.push(shader);
  }

  linkProgram(program) {
    if (program) program.linked = true;
  }

  getProgramParameter(program, pname) {
    if (pname === this.LINK_STATUS) return true;
    return 1;
  }

  getProgramInfoLog(program) {
    return '';
  }

  useProgram(program) {
    this.currentProgram = program;
  }

  getUniformLocation(program, name) {
    return { name, programId: program ? program.id : null };
  }

  uniform1f(loc, v) { if (loc) this.uniforms.set(loc.name, v); }
  uniform2f(loc, x, y) { if (loc) this.uniforms.set(loc.name, [x, y]); }
  uniform3f(loc, x, y, z) { if (loc) this.uniforms.set(loc.name, [x, y, z]); }
  uniform4f(loc, x, y, z, w) { if (loc) this.uniforms.set(loc.name, [x, y, z, w]); }
  uniformMatrix4fv(loc, transpose, value) { if (loc) this.uniforms.set(loc.name, value); }

  getAttribLocation(program, name) {
    return 0;
  }

  enableVertexAttribArray(index) {}
  disableVertexAttribArray(index) {}
  vertexAttribPointer(index, size, type, normalized, stride, offset) {}

  drawArrays(mode, first, count) {
    this.drawCalls++;
  }

  drawElements(mode, count, type, offset) {
    this.drawCalls++;
  }

  createTexture() {
    return { id: Math.random().toString(36).substring(2) };
  }

  bindTexture(target, texture) {}
  texParameteri(target, pname, param) {}
  texImage2D() {}

  getExtension(name) {
    if (name === 'OES_texture_float' || name === 'ANGLE_instanced_arrays' || name === 'WEBGL_lose_context') {
      return {
        loseContext: () => this.canvas.simulateContextLost(),
        restoreContext: () => this.canvas.simulateContextRestored()
      };
    }
    return null;
  }
}

export class MockCanvasRenderingContext2D {
  constructor(canvas) {
    this.canvas = canvas;
    this.fillStyle = '#000000';
    this.strokeStyle = '#000000';
    this.lineWidth = 1;
    this.font = '10px sans-serif';
    this.textAlign = 'start';
    this.textBaseline = 'alphabetic';
    this.globalAlpha = 1.0;
    this.operations = [];
  }

  save() { this.operations.push(['save']); }
  restore() { this.operations.push(['restore']); }
  translate(x, y) { this.operations.push(['translate', x, y]); }
  rotate(angle) { this.operations.push(['rotate', angle]); }
  scale(x, y) { this.operations.push(['scale', x, y]); }
  setLineDash(segments) { this.operations.push(['setLineDash', segments]); }
  beginPath() { this.operations.push(['beginPath']); }
  closePath() { this.operations.push(['closePath']); }
  moveTo(x, y) { this.operations.push(['moveTo', x, y]); }
  lineTo(x, y) { this.operations.push(['lineTo', x, y]); }
  arc(x, y, r, sa, ea) { this.operations.push(['arc', x, y, r, sa, ea]); }
  rect(x, y, w, h) { this.operations.push(['rect', x, y, w, h]); }
  fill() { this.operations.push(['fill', this.fillStyle]); }
  stroke() { this.operations.push(['stroke', this.strokeStyle]); }
  fillRect(x, y, w, h) { this.operations.push(['fillRect', x, y, w, h, this.fillStyle]); }
  strokeRect(x, y, w, h) { this.operations.push(['strokeRect', x, y, w, h, this.strokeStyle]); }
  clearRect(x, y, w, h) { this.operations.push(['clearRect', x, y, w, h]); }
  fillText(text, x, y) { this.operations.push(['fillText', text, x, y]); }
  strokeText(text, x, y) { this.operations.push(['strokeText', text, x, y]); }

  measureText(text) {
    return {
      width: (text || '').length * 8,
      actualBoundingBoxAscent: 10,
      actualBoundingBoxDescent: 2
    };
  }

  createLinearGradient(x0, y0, x1, y1) {
    return {
      addColorStop: (offset, color) => {}
    };
  }

  createRadialGradient(x0, y0, r0, x1, y1, r1) {
    return {
      addColorStop: (offset, color) => {}
    };
  }

  drawImage() { this.operations.push(['drawImage']); }
  getImageData(sx, sy, sw, sh) {
    return { data: new Uint8ClampedArray(sw * sh * 4), width: sw, height: sh };
  }
  putImageData() {}
}

export class MockHTMLCanvasElement {
  constructor(width = 800, height = 600) {
    this.width = width;
    this.height = height;
    this.style = {};
    this.clientWidth = width;
    this.clientHeight = height;
    this.tagName = 'CANVAS';
    this.nodeName = 'CANVAS';
    this.nodeType = 1;
    this.children = [];
    this.classList = {
      _classes: new Set(),
      add(c) { this._classes.add(c); },
      remove(c) { this._classes.delete(c); },
      contains(c) { return this._classes.has(c); },
      toggle(c) { if (this.contains(c)) { this.remove(c); return false; } else { this.add(c); return true; } }
    };

    this._listeners = new Map();
    this._webglContext = null;
    this._2dContext = null;
    this._forceWebGLFailure = false;
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
        try {
          listener.call(this, event);
        } catch (err) {
          console.error(`Error in canvas event ${event.type}:`, err);
        }
      }
    }
    return !event.defaultPrevented;
  }

  getContext(type, attributes = {}) {
    if (this._forceWebGLFailure && (type === 'webgl' || type === 'experimental-webgl' || type === 'webgl2')) {
      return null;
    }

    if (type === 'webgl' || type === 'experimental-webgl') {
      if (!this._webglContext) {
        this._webglContext = new MockWebGLRenderingContext(this, false);
      }
      return this._webglContext;
    }

    if (type === 'webgl2') {
      if (!this._webglContext) {
        this._webglContext = new MockWebGLRenderingContext(this, true);
      }
      return this._webglContext;
    }

    if (type === '2d') {
      if (!this._2dContext) {
        this._2dContext = new MockCanvasRenderingContext2D(this);
      }
      return this._2dContext;
    }

    return null;
  }

  getBoundingClientRect() {
    return {
      top: 0,
      left: 0,
      width: this.width,
      height: this.height,
      right: this.width,
      bottom: this.height,
      x: 0,
      y: 0
    };
  }

  simulateContextLost() {
    if (this._webglContext) {
      this._webglContext._isContextLost = true;
    }
    const event = {
      type: 'webglcontextlost',
      preventDefault: () => { event.defaultPrevented = true; },
      defaultPrevented: false
    };
    this.dispatchEvent(event);
  }

  simulateContextRestored() {
    if (this._webglContext) {
      this._webglContext._isContextLost = false;
    }
    const event = {
      type: 'webglcontextrestored',
      preventDefault: () => { event.defaultPrevented = true; },
      defaultPrevented: false
    };
    this.dispatchEvent(event);
  }

  setForceWebGLFailure(shouldFail) {
    this._forceWebGLFailure = Boolean(shouldFail);
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

export function installWebGLMocks(target = globalThis) {
  safeDefine(target, 'HTMLCanvasElement', MockHTMLCanvasElement);
  safeDefine(target, 'WebGLRenderingContext', MockWebGLRenderingContext);

  if (target.window && target.window !== target) {
    safeDefine(target.window, 'HTMLCanvasElement', MockHTMLCanvasElement);
    safeDefine(target.window, 'WebGLRenderingContext', MockWebGLRenderingContext);
  }

  return {
    HTMLCanvasElement: MockHTMLCanvasElement,
    WebGLRenderingContext: MockWebGLRenderingContext,
    MockCanvasRenderingContext2D
  };
}
