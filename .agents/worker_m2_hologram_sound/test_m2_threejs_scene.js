/**
 * Three.js 3D Hologram Scene Verification Test
 */
const assert = require('assert');

// Mock Three.js
class MockVector3 {
  constructor(x = 0, y = 0, z = 0) {
    this.x = x; this.y = y; this.z = z;
  }
  set(x, y, z) { this.x = x; this.y = y; this.z = z; return this; }
}

class MockColor {
  constructor(hex = 0xffffff) {
    this.hex = hex;
  }
  set(hex) { this.hex = hex; return this; }
  lerp(target, factor) { return this; }
  copy(c) { this.hex = c.hex; return this; }
}

class MockObject3D {
  constructor() {
    this.position = new MockVector3();
    this.rotation = new MockVector3();
    this.scale = {
      x: 1, y: 1, z: 1,
      setScalar: (s) => { this.scale.x = s; this.scale.y = s; this.scale.z = s; }
    };
    this.children = [];
    this.visible = true;
    this.userData = {};
  }
  add(child) {
    this.children.push(child);
    child.parent = this;
    return this;
  }
  remove(child) {
    const idx = this.children.indexOf(child);
    if (idx !== -1) this.children.splice(idx, 1);
  }
  traverse(cb) {
    cb(this);
    this.children.forEach(c => c.traverse(cb));
  }
}

class MockScene extends MockObject3D {}
class MockGroup extends MockObject3D {}
class MockMesh extends MockObject3D {
  constructor(geo, mat) {
    super();
    this.geometry = geo;
    this.material = mat;
  }
}
class MockPoints extends MockObject3D {
  constructor(geo, mat) {
    super();
    this.geometry = geo;
    this.material = mat;
  }
}
class MockLine extends MockObject3D {
  constructor(geo, mat) {
    super();
    this.geometry = geo;
    this.material = mat;
  }
}

class MockBufferAttribute {
  constructor(arr, itemSize) {
    this.array = arr;
    this.itemSize = itemSize;
    this.count = arr.length / itemSize;
    this.needsUpdate = false;
  }
  getX(i) { return this.array[i * this.itemSize]; }
  getY(i) { return this.array[i * this.itemSize + 1]; }
  getZ(i) { return this.array[i * this.itemSize + 2]; }
  setXYZ(i, x, y, z) {
    this.array[i * this.itemSize] = x;
    this.array[i * this.itemSize + 1] = y;
    this.array[i * this.itemSize + 2] = z;
  }
}

class MockBufferGeometry {
  constructor() {
    this.attributes = {};
  }
  setAttribute(name, attr) { this.attributes[name] = attr; }
  setFromPoints(pts) {
    const arr = new Float32Array(pts.length * 3);
    this.attributes['position'] = new MockBufferAttribute(arr, 3);
    return this;
  }
  dispose() {}
}

class MockMaterial {
  constructor(opts = {}) {
    this.color = new MockColor(opts.color);
    this.opacity = opts.opacity || 1.0;
    this.transparent = !!opts.transparent;
  }
  dispose() {}
}

const mockTHREE = {
  Scene: MockScene,
  Group: MockGroup,
  Mesh: MockMesh,
  Points: MockPoints,
  Line: MockLine,
  Color: MockColor,
  Vector3: MockVector3,
  BufferGeometry: MockBufferGeometry,
  BufferAttribute: MockBufferAttribute,
  IcosahedronGeometry: class extends MockBufferGeometry { constructor() { super(); } },
  SphereGeometry: class extends MockBufferGeometry { constructor() { super(); } },
  TorusGeometry: class extends MockBufferGeometry { constructor() { super(); } },
  BoxGeometry: class extends MockBufferGeometry { constructor() { super(); } },
  RingGeometry: class extends MockBufferGeometry { constructor() { super(); } },
  MeshBasicMaterial: MockMaterial,
  LineBasicMaterial: MockMaterial,
  PointsMaterial: MockMaterial,
  AmbientLight: class extends MockObject3D {},
  PointLight: class extends MockObject3D { constructor(c, i, d) { super(); this.color = new MockColor(c); this.intensity = i; } },
  PerspectiveCamera: class extends MockObject3D {
    constructor() { super(); this.aspect = 1.0; }
    updateProjectionMatrix() {}
  },
  WebGLRenderer: class {
    constructor() {
      this.domElement = {
        style: {},
        addEventListener: () => {},
        removeEventListener: () => {},
        parentNode: null
      };
    }
    setSize() {}
    setPixelRatio() {}
    setClearColor() {}
    render() {}
    dispose() {}
  },
  CanvasTexture: class {},
  AdditiveBlending: 2,
  DoubleSide: 2
};

// Global setup
global.window = {
  THREE: mockTHREE,
  WebGLRenderingContext: class {},
  addEventListener: () => {},
  removeEventListener: () => {},
  devicePixelRatio: 2.0
};
global.document = {
  createElement: (tag) => {
    if (tag === 'canvas') {
      return {
        width: 64, height: 64,
        style: {},
        getContext: (type) => {
          if (type === 'webgl' || type === 'experimental-webgl') return {};
          if (type === '2d') return {
            createRadialGradient: () => ({ addColorStop: () => {} }),
            fillRect: () => {}
          };
          return null;
        },
        addEventListener: () => {},
        removeEventListener: () => {}
      };
    }
    return {
      clientWidth: 400,
      clientHeight: 400,
      children: [],
      style: {},
      appendChild: function(c) { this.children.push(c); c.parentNode = this; },
      removeChild: function(c) { const i = this.children.indexOf(c); if (i !== -1) this.children.splice(i, 1); }
    };
  }
};
global.performance = { now: () => Date.now() };
global.requestAnimationFrame = (cb) => setTimeout(cb, 16);
global.cancelAnimationFrame = (id) => clearTimeout(id);

const { HologramController, detectWebGL } = require('../../projects/jarvis_web/js/hologram.js');

async function testThreeHologram() {
  console.log('=== Running Three.js 3D WebGL Scene Construction Test ===\n');

  assert.strictEqual(detectWebGL(), true, 'detectWebGL should return true when WebGL mock is present');
  console.log('✓ detectWebGL() correctly detected mock WebGL context');

  const container = document.createElement('div');
  const holo = new HologramController();
  holo.init(container);

  assert.strictEqual(holo.getMode(), 'webgl', 'Mode should be webgl');
  assert.strictEqual(holo.isWebGLActive(), true, 'isWebGLActive should be true');
  console.log('✓ HologramController successfully initialized in WebGL mode');

  const viz = holo.visualizer;
  assert.ok(viz.coreMesh, 'Core mesh should be constructed');
  assert.ok(viz.coreGlowMesh, 'Core glow mesh should be constructed');
  assert.ok(viz.gimbalInner, 'Inner gimbal ring should be constructed');
  assert.ok(viz.gimbalMid, 'Middle gimbal ring should be constructed');
  assert.ok(viz.gimbalOuter, 'Outer gimbal ring should be constructed');
  assert.strictEqual(viz.energyArcs.length, 6, 'Should construct 6 energy arcs');
  assert.ok(viz.particleSystem, 'Particle swarm system should be constructed');
  assert.strictEqual(viz.shockwaveRings.length, 3, 'Should construct 3 shockwave rings');
  console.log('✓ All 3D Arc-Reactor scene graph components verified (Core, 3 Gimbal Rings, 6 Arcs, Particles, Shockwaves)');

  // Test 6 states in 3D
  const states = ['IDLE', 'LISTENING', 'THINKING', 'SPEAKING', 'MUTED', 'ERROR'];
  for (const st of states) {
    holo.setVisualState(st);
    assert.strictEqual(holo.getState(), st);
    holo.setAudioReactivity(0.8, new Uint8Array([250, 180, 120, 80, 50, 20]));
  }
  console.log('✓ Dynamic 6-state transitions & audio FFT modulation verified in 3D WebGL mode');

  // Test resize
  holo.resize();
  console.log('✓ Hologram resize handler executed');

  // Test cleanup
  holo.destroy();
  assert.strictEqual(holo.getMode(), 'none');
  console.log('✓ 3D WebGL scene disposed cleanly');

  console.log('\n=== ALL 3D WEBGL SCENE TESTS PASSED ===');
}

testThreeHologram().catch(err => {
  console.error('ThreeJS test failed:', err);
  process.exit(1);
});
