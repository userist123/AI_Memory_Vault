// Plan A1, rândul 5: nivelurile de calitate: clasare locală, parametri, adaptare doar în jos, preferință validată.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import { TIER_PARAMS, classifyTier, gpuKind, adaptTier, lowerTier, parseTierPick, median, ADAPT_SAMPLES, SLOW_MEDIAN_MS } from '../core/viewport-tiers';

const pc = { coarse: false, shortSide: 1080, cores: 12, memoryGb: 16, softwareGpu: false };

test('parametrii fiecărui nivel', () => {
  assert.deepEqual(TIER_PARAMS.desktop, { maxDpr: 2, composerDpr: 1.5, ao: 'always', shadow: 2048, maxLights: 4 });
  assert.deepEqual(TIER_PARAMS.laptop, { maxDpr: 1.5, composerDpr: 1.5, ao: 'rest', shadow: 2048, maxLights: 4 });
  assert.deepEqual(TIER_PARAMS.phone, { maxDpr: 1.5, composerDpr: 1.5, ao: 'off', shadow: 1024, maxLights: 2 });
});
test('clasare: telefon', () => {
  assert.equal(classifyTier({ ...pc, coarse: true, shortSide: 390 }), 'phone');
  assert.equal(classifyTier({ ...pc, coarse: true, shortSide: 599 }), 'phone');
});
test('clasare: randatorul software merge pe cel mai ușor nivel, oricâte nuclee ar avea', () => { assert.equal(classifyTier({ ...pc, softwareGpu: true }), 'phone'); });
test('clasare: tableta e laptop', () => { assert.equal(classifyTier({ ...pc, coarse: true, shortSide: 768 }), 'laptop'); });
test('clasare: desktop de la 8 nuclee și, când se știe, 8 GB', () => {
  assert.equal(classifyTier(pc), 'desktop'); assert.equal(classifyTier({ ...pc, cores: 8, memoryGb: 8 }), 'desktop');
  assert.equal(classifyTier({ ...pc, memoryGb: undefined }), 'desktop');
  assert.equal(classifyTier({ ...pc, cores: 7 }), 'laptop'); assert.equal(classifyTier({ ...pc, memoryGb: 4 }), 'laptop');
  assert.equal(classifyTier({ ...pc, cores: undefined }), 'laptop');
});
test('clasare: placa integrată stă pe laptop, oricâte nuclee ar avea (măsurat pe un GPU slab: desktop sacadează, laptop nu)', () => {
  assert.equal(classifyTier({ ...pc, integratedGpu: true }), 'laptop');
  assert.equal(classifyTier({ ...pc, integratedGpu: false }), 'desktop');
  assert.equal(classifyTier({ ...pc, integratedGpu: true, coarse: true, shortSide: 390 }), 'phone');
});
test('tipul GPU-ului din numele raportat de browser', () => {
  for (const n of ['ANGLE (Intel, Intel(R) UHD Graphics 620 Direct3D11 vs_5_0 ps_5_0)', 'ANGLE (Intel, Intel(R) Iris(R) Xe Graphics Direct3D11)', 'Mali-G72', 'Adreno (TM) 640', 'ANGLE (AMD, AMD Radeon(TM) Graphics Direct3D11)', 'ANGLE (AMD, Radeon Vega 8 Graphics Direct3D11)']) assert.equal(gpuKind(n), 'integrated', n);
  for (const n of ['ANGLE (NVIDIA, NVIDIA GeForce RTX 3060 Direct3D11)', 'ANGLE (AMD, AMD Radeon RX 6600 Direct3D11)', 'ANGLE (Intel, Intel(R) Arc(TM) A770 Graphics Direct3D11)', 'Apple M2']) assert.equal(gpuKind(n), 'dedicated', n);
  for (const n of ['ANGLE (Google, Vulkan 1.3.0 (SwiftShader Device), SwiftShader driver)', 'llvmpipe (LLVM 15)', 'Microsoft Basic Render Driver']) assert.equal(gpuKind(n), 'software', n);
  assert.equal(gpuKind(''), 'unknown'); assert.equal(gpuKind('WebKit WebGL'), 'unknown');
});
test('coborârea are capăt', () => { assert.equal(lowerTier('desktop'), 'laptop'); assert.equal(lowerTier('laptop'), 'phone'); assert.equal(lowerTier('phone'), null); });
test('mediana', () => {
  assert.equal(median([]), 0); assert.equal(median([7]), 7); assert.equal(median([9, 1, 5]), 5);
});
test('adaptare: nu decide până n-are destule cadre', () => {
  assert.equal(adaptTier(Array(ADAPT_SAMPLES - 1).fill(500), 'desktop'), null);
});
test('adaptare: cadre lente coboară un nivel, cele bune nu schimbă nimic', () => {
  assert.equal(adaptTier(Array(ADAPT_SAMPLES).fill(SLOW_MEDIAN_MS + 1), 'desktop'), 'laptop');
  assert.equal(adaptTier(Array(ADAPT_SAMPLES).fill(SLOW_MEDIAN_MS + 1), 'laptop'), 'phone');
  assert.equal(adaptTier(Array(ADAPT_SAMPLES).fill(SLOW_MEDIAN_MS + 1), 'phone'), null);
  assert.equal(adaptTier(Array(ADAPT_SAMPLES).fill(16.7), 'desktop'), null);
});
test('adaptare: un ecran de 30 Hz nu coboară nivelul', () => { assert.equal(adaptTier(Array(ADAPT_SAMPLES).fill(33.4), 'desktop'), null); });
test('adaptare: cadrele lungi de la compilarea shaderelor nu coboară nivelul', () => {
  assert.equal(adaptTier([...Array(ADAPT_SAMPLES - 15).fill(4), ...Array(15).fill(900)], 'desktop'), null);
});
test('adaptare: lentoarea de durată coboară nivelul chiar dacă unele cadre sunt bune', () => {
  assert.equal(adaptTier([...Array(20).fill(16.7), ...Array(ADAPT_SAMPLES - 20).fill(80)], 'desktop'), 'laptop');
});
test('preferința din stocare e validată', () => {
  for (const k of ['auto', 'phone', 'laptop', 'desktop'] as const) assert.equal(parseTierPick(k), k);
  for (const k of [null, undefined, '', 'high', '__proto__', 'constructor', 3]) assert.equal(parseTierPick(k), 'auto');
});
