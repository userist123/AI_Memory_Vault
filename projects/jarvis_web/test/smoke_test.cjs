/**
 * JARVIS Web Ecosystem — Automated command-center smoke test
 * Zero external dependencies — Node.js stdlib only.
 */
const http = require('http');
const path = require('path');
const { spawn } = require('child_process');

const PORT = 3001;
const BASE = `http://127.0.0.1:${PORT}`;
const TIMEOUT = 5000;
const LANDMARKS = [
  'id="hologram-container"','id="app-container"','AI MEMORY VAULT V6','JARVIS',
  'Memory Retrieval','Memory Proposal','Agent Council','Execution Timeline'
];
const JS_ENDPOINTS = ['/js/app.js','/js/hologram.js','/js/voice_engine.js','/js/vault_client.js'];
let passed = 0;
let failed = 0;
function log(icon,msg){console.log(`  ${icon} ${msg}`)}
function pass(msg){passed++;log('✅',msg)}
function fail(msg){failed++;log('❌',msg)}
function get(url){return new Promise((resolve,reject)=>{const timer=setTimeout(()=>reject(new Error(`Timeout: ${url}`)),TIMEOUT);http.get(url,res=>{clearTimeout(timer);let body='';res.on('data',c=>body+=c);res.on('end',()=>resolve({status:res.statusCode,headers:res.headers,body}))}).on('error',e=>{clearTimeout(timer);reject(e)})})}
async function runSmoke(serverProcess){
  try{
    const root=await get(`${BASE}/`);
    root.status===200?pass('GET / → HTTP 200'):fail(`GET / → HTTP ${root.status}`);
    for(const landmark of LANDMARKS) root.body.includes(landmark)?pass(`HTML landmark: ${landmark}`):fail(`Missing HTML landmark: ${landmark}`);
    for(const endpoint of JS_ENDPOINTS){const res=await get(`${BASE}${endpoint}`);if(res.status!==200){fail(`GET ${endpoint} → HTTP ${res.status}`);continue}pass(`GET ${endpoint} → HTTP 200`);const ct=String(res.headers['content-type']||'').toLowerCase();ct.includes('application/javascript')?pass(`${endpoint} MIME is JavaScript`):fail(`${endpoint} wrong MIME: ${ct}`)}
    const manifest=await get(`${BASE}/data/agent-council.json`);manifest.status===200?pass('Agent Council manifest available'):fail(`Agent Council manifest returned ${manifest.status}`);
    const missing=await get(`${BASE}/nonexistent_file_xyz.js`);missing.status===404?pass('404 handling'):fail(`404 handling returned ${missing.status}`);
    const traversal=await get(`${BASE}/%2e%2e/%2e%2e/README.md`);traversal.status===403?pass('Path traversal blocked'):fail(`Path traversal returned ${traversal.status}`);
  }catch(error){fail(`Unexpected error: ${error.message}`)}
  finally{serverProcess.kill()}
  console.log(`  ─────────────────────────────────────`);console.log(`  Results: ${passed} passed, ${failed} failed`);if(failed)process.exit(1);console.log('  ✅ ALL SMOKE TESTS PASSED');process.exit(0)
}
const serverScript=path.join(__dirname,'..','server.cjs');
const serverProcess=spawn(process.execPath,[serverScript],{env:{...process.env,JARVIS_TEST_PORT:String(PORT)},stdio:'pipe'});
let ready=false;
serverProcess.stdout.on('data',data=>{const output=data.toString();process.stdout.write(output);if(!ready&&output.includes('HTTP Server running at')){ready=true;runSmoke(serverProcess)}});
serverProcess.stderr.on('data',data=>process.stderr.write(data));
serverProcess.on('error',err=>{console.error(`❌ Failed to start server: ${err.message}`);process.exit(1)});
setTimeout(()=>{if(!ready){console.error('❌ Server did not start within 5 seconds');serverProcess.kill();process.exit(1)}},TIMEOUT);
