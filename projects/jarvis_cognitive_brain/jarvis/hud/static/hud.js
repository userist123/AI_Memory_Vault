// ── Jarvis Cognitive Brain — Advanced HUD Controller ──
// Layer 0: Raw WebGL Laser Shader (webgl-laser)
// Layer 1: Three.js PBR 3D Brain Core (webgl-3d-object)
// Layer 2: Real-time WebSocket Telemetry & OODA State Machine

(function () {
  'use strict';

  // ─────────────────────────────────────────────────────────────
  // 1. Color Utilities & Design Tokens
  // ─────────────────────────────────────────────────────────────
  function hexToRgb01(hex) {
    const clean = hex.replace('#', '').trim();
    const val = clean.length === 3
      ? clean.split('').map(c => c + c).join('')
      : clean;
    return [
      parseInt(val.slice(0, 2), 16) / 255,
      parseInt(val.slice(2, 4), 16) / 255,
      parseInt(val.slice(4, 6), 16) / 255,
    ];
  }

  const ACCENT_HEX = '#00e5ff';
  const ACCENT_RGB = hexToRgb01(ACCENT_HEX);
  const REDUCE_MOTION = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

  // ─────────────────────────────────────────────────────────────
  // 2. Layer 0: Raw WebGL Laser Shader Background
  // ─────────────────────────────────────────────────────────────
  function initWebGLLaser(canvas) {
    if (!canvas) return () => {};

    const gl = canvas.getContext('webgl', {
      alpha: true,
      antialias: false,
      premultipliedAlpha: false,
    });
    if (!gl) return () => {};

    const laserVS = `
      attribute vec2 a_position;
      varying vec2 v_uv;
      void main() {
        v_uv = a_position * 0.5 + 0.5;
        gl_Position = vec4(a_position, 0.0, 1.0);
      }
    `;

    const laserFS = `
      precision highp float;
      uniform vec2 u_resolution;
      uniform float u_time;
      uniform vec3 u_color;
      uniform float u_xOffset;
      uniform float u_coreWidth;
      uniform float u_glowWidth;
      uniform float u_smokeDensity;
      varying vec2 v_uv;

      float hash(vec2 p) {
        p = fract(p * vec2(123.34, 456.21));
        p += dot(p, p + 45.32);
        return fract(p.x * p.y);
      }

      float noise(vec2 p) {
        vec2 i = floor(p);
        vec2 f = fract(p);
        vec2 u = f * f * (3.0 - 2.0 * f);
        float a = hash(i);
        float b = hash(i + vec2(1.0, 0.0));
        float c = hash(i + vec2(0.0, 1.0));
        float d = hash(i + vec2(1.0, 1.0));
        return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
      }

      float fbm(vec2 p) {
        float value = 0.0;
        float amplitude = 0.5;
        for (int i = 0; i < 5; i++) {
          value += amplitude * noise(p);
          p *= 2.02;
          amplitude *= 0.5;
        }
        return value;
      }

      void main() {
        vec2 aspect = vec2(u_resolution.x / u_resolution.y, 1.0);
        vec2 p = (v_uv - 0.5) * aspect;
        float x = p.x - u_xOffset;
        float distanceToBeam = abs(x);

        float core = exp(-pow(distanceToBeam / u_coreWidth, 2.0));
        float glow = exp(-pow(distanceToBeam / u_glowWidth, 1.45));
        float scatter = exp(-pow(distanceToBeam / (u_glowWidth * 5.5), 1.25));
        float pulse = 0.9 + 0.1 * sin(u_time * 1.15);

        vec2 fogUv = p * 3.1 + vec2(0.0, -u_time * 0.035);
        fogUv.x += sin(p.y * 3.5 + u_time * 0.11) * 0.14;
        float fogBase = fbm(fogUv);
        float fogFine = fbm(p * 8.0 + vec2(sin(u_time * 0.07) * 0.35, u_time * 0.05));
        float fog = smoothstep(0.30, 0.86, fogBase * 0.72 + fogFine * 0.28);
        float smoke = fog * scatter * u_smokeDensity;

        vec3 brand = clamp(u_color, 0.0, 1.0);
        vec3 haloColor = mix(brand, vec3(1.0), 0.16);
        vec3 smokeColor = mix(brand, vec3(0.55), 0.28) * 0.55;
        vec3 hotCore = vec3(1.0, 0.96, 0.90);

        vec3 color = vec3(0.006, 0.007, 0.010);
        color += smokeColor * smoke;
        color += haloColor * glow * 0.46 * pulse;
        color += hotCore * core * 1.35;

        float vignette = smoothstep(1.25, 0.18, length(p));
        color *= vignette;

        float alpha = clamp(smoke * 0.72 + glow * 0.68 + core, 0.0, 1.0);
        gl_FragColor = vec4(color, alpha);
      }
    `;

    function createShader(glCtx, type, source) {
      const shader = glCtx.createShader(type);
      glCtx.shaderSource(shader, source);
      glCtx.compileShader(shader);
      if (!glCtx.getShaderParameter(shader, glCtx.COMPILE_STATUS)) {
        console.warn('Laser shader compile error:', glCtx.getShaderInfoLog(shader));
      }
      return shader;
    }

    const program = gl.createProgram();
    gl.attachShader(program, createShader(gl, gl.VERTEX_SHADER, laserVS));
    gl.attachShader(program, createShader(gl, gl.FRAGMENT_SHADER, laserFS));
    gl.linkProgram(program);

    const positionBuffer = gl.createBuffer();
    const positions = new Float32Array([
      -1, -1,  1, -1, -1,  1,
      -1,  1,  1, -1,  1,  1,
    ]);

    gl.bindBuffer(gl.ARRAY_BUFFER, positionBuffer);
    gl.bufferData(gl.ARRAY_BUFFER, positions, gl.STATIC_DRAW);
    gl.useProgram(program);

    const positionLocation = gl.getAttribLocation(program, 'a_position');
    const uniforms = {
      resolution: gl.getUniformLocation(program, 'u_resolution'),
      time: gl.getUniformLocation(program, 'u_time'),
      color: gl.getUniformLocation(program, 'u_color'),
      xOffset: gl.getUniformLocation(program, 'u_xOffset'),
      coreWidth: gl.getUniformLocation(program, 'u_coreWidth'),
      glowWidth: gl.getUniformLocation(program, 'u_glowWidth'),
      smokeDensity: gl.getUniformLocation(program, 'u_smokeDensity'),
    };

    gl.enableVertexAttribArray(positionLocation);
    gl.vertexAttribPointer(positionLocation, 2, gl.FLOAT, false, 0, 0);
    gl.enable(gl.BLEND);
    gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);

    let rafId = 0;
    let isPaused = false;

    function resize() {
      const dpr = Math.min(window.devicePixelRatio || 1, 1.5);
      const width = Math.max(1, window.innerWidth);
      const height = Math.max(1, window.innerHeight);
      canvas.width = Math.floor(width * dpr);
      canvas.height = Math.floor(height * dpr);
      gl.viewport(0, 0, canvas.width, canvas.height);
    }

    function render(time) {
      if (isPaused) return;
      gl.useProgram(program);
      gl.uniform2f(uniforms.resolution, canvas.width, canvas.height);
      gl.uniform1f(uniforms.time, (time || 0) * 0.001);
      gl.uniform3f(uniforms.color, ACCENT_RGB[0], ACCENT_RGB[1], ACCENT_RGB[2]);
      gl.uniform1f(uniforms.xOffset, 0.0);
      gl.uniform1f(uniforms.coreWidth, 0.004);
      gl.uniform1f(uniforms.glowWidth, 0.035);
      gl.uniform1f(uniforms.smokeDensity, 0.52);

      gl.clearColor(0, 0, 0, 0);
      gl.clear(gl.COLOR_BUFFER_BIT);
      gl.drawArrays(gl.TRIANGLES, 0, 6);

      if (!REDUCE_MOTION) {
        rafId = requestAnimationFrame(render);
      }
    }

    window.addEventListener('resize', () => {
      resize();
      render();
    });

    document.addEventListener('visibilitychange', () => {
      isPaused = document.hidden;
      if (!isPaused && !REDUCE_MOTION) {
        rafId = requestAnimationFrame(render);
      }
    });

    resize();
    render();

    return () => {
      cancelAnimationFrame(rafId);
      gl.deleteBuffer(positionBuffer);
      gl.deleteProgram(program);
    };
  }

  // ─────────────────────────────────────────────────────────────
  // 3. Layer 1: Three.js PBR 3D Brain Core Object
  // ─────────────────────────────────────────────────────────────
  let brainMesh = null;
  let brainWireframe = null;
  let brainRing = null;
  let brainParticles = null;
  let brainRotSpeed = 0.003;

  function initThreeJSBrain(canvas) {
    if (!canvas || typeof THREE === 'undefined') return () => {};

    const renderer = new THREE.WebGLRenderer({
      canvas: canvas,
      antialias: true,
      alpha: true,
    });
    renderer.setClearColor(0x000000, 0);
    renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 1.75));
    renderer.outputColorSpace = THREE.SRGBColorSpace;

    const scene = new THREE.Scene();
    const camera = new THREE.PerspectiveCamera(40, window.innerWidth / window.innerHeight, 0.1, 100);
    camera.position.set(0, 0, 5.0);

    // Inner PBR Icosahedron Brain Mesh
    const geo = new THREE.IcosahedronGeometry(1.2, 2);
    const pbrMat = new THREE.MeshStandardMaterial({
      color: 0x00e5ff,
      metalness: 0.65,
      roughness: 0.28,
      emissive: 0x003344,
      emissiveIntensity: 0.35,
      flatShading: true,
      transparent: true,
      opacity: 0.45,
    });
    brainMesh = new THREE.Mesh(geo, pbrMat);
    scene.add(brainMesh);

    // Outer Wireframe Cage
    const wireGeo = new THREE.IcosahedronGeometry(1.32, 2);
    const wireMat = new THREE.MeshBasicMaterial({
      color: 0x00e5ff,
      wireframe: true,
      transparent: true,
      opacity: 0.22,
    });
    brainWireframe = new THREE.Mesh(wireGeo, wireMat);
    scene.add(brainWireframe);

    // Surrounding Orbital Ring
    const ringGeo = new THREE.TorusGeometry(2.0, 0.008, 16, 120);
    const ringMat = new THREE.MeshBasicMaterial({
      color: 0x00e5ff,
      transparent: true,
      opacity: 0.28,
    });
    brainRing = new THREE.Mesh(ringGeo, ringMat);
    brainRing.rotation.x = Math.PI / 2.8;
    brainRing.rotation.y = Math.PI / 8;
    scene.add(brainRing);

    // Neural Particle Cloud
    const PARTICLE_COUNT = 300;
    const pGeo = new THREE.BufferGeometry();
    const pPos = new Float32Array(PARTICLE_COUNT * 3);
    for (let i = 0; i < PARTICLE_COUNT * 3; i += 3) {
      const r = 1.4 + Math.random() * 2.2;
      const theta = Math.random() * Math.PI * 2;
      const phi = (Math.random() - 0.5) * Math.PI;
      pPos[i] = r * Math.cos(phi) * Math.cos(theta);
      pPos[i + 1] = r * Math.sin(phi);
      pPos[i + 2] = r * Math.cos(phi) * Math.sin(theta);
    }
    pGeo.setAttribute('position', new THREE.BufferAttribute(pPos, 3));
    const pMat = new THREE.PointsMaterial({
      color: 0x00e5ff,
      size: 0.025,
      transparent: true,
      opacity: 0.6,
    });
    brainParticles = new THREE.Points(pGeo, pMat);
    scene.add(brainParticles);

    // Lighting (PBR Key + Ambient + Rim)
    const ambientLight = new THREE.AmbientLight(0xffffff, 0.45);
    scene.add(ambientLight);

    const keyLight = new THREE.DirectionalLight(0xffffff, 2.2);
    keyLight.position.set(3.5, 4.0, 4.5);
    scene.add(keyLight);

    const rimLight = new THREE.DirectionalLight(0x00e5ff, 1.8);
    rimLight.position.set(-4.0, -1.0, -3.0);
    scene.add(rimLight);

    let rafId = 0;
    let isPaused = false;

    function resize() {
      const width = Math.max(1, window.innerWidth);
      const height = Math.max(1, window.innerHeight);
      renderer.setSize(width, height, false);
      camera.aspect = width / height;
      camera.updateProjectionMatrix();
    }

    function render(time) {
      if (isPaused) return;
      const t = (time || 0) * 0.001;

      if (!REDUCE_MOTION) {
        brainMesh.rotation.y = t * (brainRotSpeed * 100);
        brainMesh.rotation.x = Math.sin(t * 0.4) * 0.15;
        brainMesh.position.y = Math.sin(t * 0.8) * 0.06;

        brainWireframe.rotation.y = -t * (brainRotSpeed * 80);
        brainWireframe.rotation.z = Math.sin(t * 0.3) * 0.1;
        brainWireframe.position.y = brainMesh.position.y;

        brainRing.rotation.z = t * 0.15;
        brainRing.rotation.x = Math.PI / 2.8 + Math.sin(t * 0.2) * 0.08;

        brainParticles.rotation.y = t * 0.04;
      }

      renderer.render(scene, camera);

      if (!REDUCE_MOTION) {
        rafId = requestAnimationFrame(render);
      }
    }

    window.addEventListener('resize', () => {
      resize();
      render();
    });

    document.addEventListener('visibilitychange', () => {
      isPaused = document.hidden;
      if (!isPaused && !REDUCE_MOTION) {
        rafId = requestAnimationFrame(render);
      }
    });

    resize();
    render();

    return () => {
      cancelAnimationFrame(rafId);
      geo.dispose();
      wireGeo.dispose();
      ringGeo.dispose();
      pGeo.dispose();
      pbrMat.dispose();
      wireMat.dispose();
      ringMat.dispose();
      pMat.dispose();
      renderer.dispose();
    };
  }

  // ─────────────────────────────────────────────────────────────
  // 4. Layer 2: DOM UI, Telemetry, OODA Cycle, & WebSocket Client
  // ─────────────────────────────────────────────────────────────
  const elStatus = document.getElementById('status-indicator');
  const elMemory = document.getElementById('val-memory');
  const elBarMemory = document.getElementById('bar-memory');
  const elPrincipal = document.getElementById('val-principal');
  const elPlan = document.getElementById('val-plan');
  const elLog = document.getElementById('event-log');
  const elUptime = document.getElementById('val-uptime');
  const elDbCount = document.getElementById('val-db-count');
  const btnClear = document.getElementById('btn-clear-log');
  const oodaPills = document.querySelectorAll('.ooda-pill');

  let startTime = Date.now();

  function formatTime() {
    return new Date().toLocaleTimeString('en-GB', { hour12: false });
  }

  function addLog(message, isHighlight) {
    if (!elLog) return;
    const li = document.createElement('li');
    li.innerHTML = '<span class="ts">' + formatTime() + '</span>' +
      (isHighlight ? '<span class="highlight">' + message + '</span>' : message);
    elLog.prepend(li);
    while (elLog.children.length > 60) {
      elLog.removeChild(elLog.lastChild);
    }
  }

  if (btnClear) {
    btnClear.addEventListener('click', () => {
      if (elLog) elLog.innerHTML = '';
    });
  }

  // Uptime ticker
  setInterval(() => {
    if (elUptime) {
      const elapsed = Math.floor((Date.now() - startTime) / 1000);
      const mins = Math.floor(elapsed / 60);
      const secs = elapsed % 60;
      elUptime.textContent = mins > 0 ? `${mins}m ${secs}s` : `${secs}s`;
    }
  }, 1000);

  // OODA Cycle Stage Activation
  function triggerOODACycle() {
    const phases = ['observe', 'orient', 'decide', 'act'];
    phases.forEach((p, idx) => {
      setTimeout(() => {
        oodaPills.forEach(pill => {
          if (pill.dataset.phase === p) {
            pill.classList.add('active');
            const stateEl = pill.querySelector('.ooda-state');
            if (stateEl) stateEl.textContent = 'ACTIVE';
          } else {
            pill.classList.remove('active');
            const stateEl = pill.querySelector('.ooda-state');
            if (stateEl) stateEl.textContent = 'IDLE';
          }
        });
      }, idx * 250);
    });

    // Reset back to idle after cycle completes
    setTimeout(() => {
      oodaPills.forEach(pill => {
        pill.classList.remove('active');
        const stateEl = pill.querySelector('.ooda-state');
        if (stateEl) stateEl.textContent = 'IDLE';
      });
    }, phases.length * 250 + 600);
  }

  // Visual Pulse on New Cognitive State
  function pulseBrain() {
    brainRotSpeed = 0.012;
    if (brainMesh && brainMesh.material) {
      brainMesh.material.emissiveIntensity = 0.85;
      brainMesh.material.opacity = 0.75;
    }
    if (brainWireframe && brainWireframe.material) {
      brainWireframe.material.opacity = 0.55;
    }
    setTimeout(() => {
      brainRotSpeed = 0.003;
      if (brainMesh && brainMesh.material) {
        brainMesh.material.emissiveIntensity = 0.35;
        brainMesh.material.opacity = 0.45;
      }
      if (brainWireframe && brainWireframe.material) {
        brainWireframe.material.opacity = 0.22;
      }
    }, 900);
  }

  // WebSocket Connection Manager
  const wsProto = location.protocol === 'https:' ? 'wss:' : 'ws:';
  const wsUrl = `${wsProto}//${location.host}/ws`;
  let ws = null;

  function connectWebSocket() {
    try {
      ws = new WebSocket(wsUrl);

      ws.onopen = function () {
        if (elStatus) {
          elStatus.className = 'status connected';
          elStatus.innerHTML = '<span class="status-dot"></span> LIVE';
        }
        addLog('Neural Link Connected → ' + wsUrl, true);
      };

      ws.onclose = function () {
        if (elStatus) {
          elStatus.className = 'status disconnected';
          elStatus.innerHTML = '<span class="status-dot"></span> OFFLINE';
        }
        addLog('Connection lost. Auto-reconnecting in 3s…');
        setTimeout(connectWebSocket, 3000);
      };

      ws.onerror = function () {
        if (elStatus) {
          elStatus.className = 'status disconnected';
          elStatus.innerHTML = '<span class="status-dot"></span> ERROR';
        }
      };

      ws.onmessage = function (event) {
        try {
          let state;
          try {
            state = JSON.parse(event.data);
          } catch (_) {
            const sanitized = event.data
              .replace(/'/g, '"')
              .replace(/None/g, 'null')
              .replace(/True/g, 'true')
              .replace(/False/g, 'false');
            state = JSON.parse(sanitized);
          }

          // Update Working Memory Panel
          if (state.memory_len !== undefined && elMemory) {
            elMemory.textContent = state.memory_len;
            if (elBarMemory) {
              const pct = Math.min(100, (state.memory_len / 10) * 100);
              elBarMemory.style.width = pct + '%';
            }
          }

          // Update Principal
          if (state.principal !== undefined && elPrincipal) {
            elPrincipal.textContent = state.principal;
          }

          // Update Active Plan
          if (elPlan) {
            elPlan.textContent = state.active_plan_id ? state.active_plan_id : 'none';
          }

          // Trigger Visual Feedback & OODA Step
          pulseBrain();
          triggerOODACycle();

          addLog(`OODA Cycle Dispatched: memory=${state.memory_len || 0}, principal=${state.principal || 'AGENT'}`);
        } catch (err) {
          console.error('State parse error:', err);
        }
      };
    } catch (e) {
      console.error('WebSocket initialization error:', e);
      setTimeout(connectWebSocket, 3000);
    }
  }

  // Fetch initial DB count via health check or fallback
  fetch('/health')
    .then(r => r.json())
    .then(data => {
      if (elDbCount) elDbCount.textContent = 'ONLINE';
    })
    .catch(() => {
      if (elDbCount) elDbCount.textContent = 'STANDALONE';
    });

  // ─────────────────────────────────────────────────────────────
  // 5. Initialize Everything
  // ─────────────────────────────────────────────────────────────
  const laserCanvas = document.querySelector('[data-webgl-laser]');
  const brainCanvas = document.getElementById('brainCanvas');

  initWebGLLaser(laserCanvas);
  initThreeJSBrain(brainCanvas);
  connectWebSocket();

  addLog('JARVIS Cognitive Brain HUD v2.0 Initialized', true);
})();
