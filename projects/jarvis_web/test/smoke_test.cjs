/**
 * JARVIS Web Ecosystem — Automated Browser Smoke Test
 * Zero external dependencies — Node.js stdlib only.
 *
 * Starts the HTTP server, verifies critical endpoints and HTML landmarks,
 * then exits 0 on success or 1 on failure.
 *
 * Usage: node test/smoke_test.js
 */

const http   = require('http');
const path   = require('path');
const { spawn } = require('child_process');

const PORT    = 3001; // Use 3001 to avoid conflict with running dev server
const BASE    = `http://localhost:${PORT}`;
const TIMEOUT = 5000;

const LANDMARKS = [
  'id="hologram-container"',
  'id="app-container"',
  'Orbitron',
  'JARVIS',
];

const JS_ENDPOINTS = [
  '/js/app.js',
  '/js/hologram.js',
  '/js/voice_engine.js',
  '/js/vault_client.js',
];

let passed = 0;
let failed = 0;

function log(icon, msg) {
  console.log(`  ${icon} ${msg}`);
}

function pass(msg) { passed++; log('✅', msg); }
function fail(msg) { failed++; log('❌', msg); }

function get(url) {
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error(`Timeout: ${url}`)), TIMEOUT);
    http.get(url, (res) => {
      clearTimeout(timer);
      let body = '';
      res.on('data', chunk => body += chunk);
      res.on('end', () => resolve({ status: res.statusCode, headers: res.headers, body }));
    }).on('error', (e) => { clearTimeout(timer); reject(e); });
  });
}

async function runSmoke(serverProcess) {
  console.log(`\n  🤖 JARVIS Smoke Test — ${BASE}`);
  console.log(`  ─────────────────────────────────────`);

  try {
    // 1. Check main HTML page
    const root = await get(`${BASE}/`);
    if (root.status === 200) {
      pass(`GET / → HTTP ${root.status}`);
    } else {
      fail(`GET / → HTTP ${root.status} (expected 200)`);
    }

    // 2. Check all HTML landmarks
    for (const landmark of LANDMARKS) {
      if (root.body.includes(landmark)) {
        pass(`HTML landmark found: ${landmark}`);
      } else {
        fail(`HTML landmark MISSING: ${landmark}`);
      }
    }

    // 3. Check JS module MIME types
    for (const endpoint of JS_ENDPOINTS) {
      const res = await get(`${BASE}${endpoint}`);
      if (res.status === 200) {
        pass(`GET ${endpoint} → HTTP 200`);
        const ct = (res.headers['content-type'] || '').toLowerCase();
        if (ct.includes('application/javascript')) {
          pass(`  Content-Type: application/javascript ✓`);
        } else {
          fail(`  Wrong Content-Type: ${ct} (expected application/javascript)`);
        }
      } else {
        fail(`GET ${endpoint} → HTTP ${res.status}`);
      }
    }

    // 4. 404 for non-existent file
    const missing = await get(`${BASE}/nonexistent_file_xyz.js`);
    if (missing.status === 404) {
      pass(`GET /nonexistent → HTTP 404 (correct)`);
    } else {
      fail(`GET /nonexistent → HTTP ${missing.status} (expected 404)`);
    }

  } catch (err) {
    fail(`Unexpected error: ${err.message}`);
  }

  console.log(`  ─────────────────────────────────────`);
  console.log(`  Results: ${passed} passed, ${failed} failed\n`);

  serverProcess.kill();

  if (failed > 0) {
    console.error(`  ❌ SMOKE TEST FAILED (${failed} failures)\n`);
    process.exit(1);
  } else {
    console.log(`  ✅ ALL SMOKE TESTS PASSED\n`);
    process.exit(0);
  }
}

// Start a temporary server instance on a different port for testing
const serverScript = path.join(__dirname, '..', 'server.cjs');

// Patch PORT for testing by passing env var
const serverProcess = spawn(process.execPath, [serverScript], {
  env: { ...process.env, JARVIS_TEST_PORT: String(PORT) },
  stdio: 'pipe',
});

serverProcess.stderr.on('data', d => process.stderr.write(d));

// Patch server.js to respect JARVIS_TEST_PORT (the server reads it)
let ready = false;
serverProcess.stdout.on('data', (data) => {
  process.stdout.write(data);
  if (!ready && data.toString().includes('HTTP Server running')) {
    ready = true;
    runSmoke(serverProcess);
  }
});

serverProcess.on('error', (err) => {
  console.error(`\n  ❌ Failed to start server: ${err.message}\n`);
  process.exit(1);
});

// Safety timeout
setTimeout(() => {
  if (!ready) {
    console.error('\n  ❌ Server did not start within 5 seconds\n');
    serverProcess.kill();
    process.exit(1);
  }
}, TIMEOUT);
