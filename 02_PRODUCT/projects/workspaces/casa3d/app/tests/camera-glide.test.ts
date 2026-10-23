// UI/UX P2-14: alunecarea camerei: amortizată, independentă de cadre, pe drumul scurt, tăietură la mișcare redusă.
import { test } from 'vitest';
import assert from 'node:assert/strict';
import { dampFactor, angleDelta, glideStep, type Pose } from '../core/camera-glide';
const near = (a: number, b: number, e = 1e-9) => assert.ok(Math.abs(a - b) < e, `${a} ≈ ${b}`);

test('amortizarea nu depinde de numărul de cadre', () => {
  const at = (fps: number) => { let p: Pose = { x: 0, z: 0, yaw: 0, pitch: 0 }; for (let i = 0; i < fps / 2; i++) p = glideStep(p, { x: 10, z: 0, yaw: 0, pitch: 0 }, 1 / fps, false).pose; return p.x; };
  near(at(30), at(144), 1e-6); near(at(60), 10 * (1 - Math.exp(-3)), 1e-6);
  assert.equal(dampFactor(6, 0), 0); assert.equal(dampFactor(6, -1), 0); near(dampFactor(6, 1e9), 1);
});
test('unghiul ia drumul cel mai scurt (350° → 10° trece prin 0°, nu înapoi)', () => {
  near(angleDelta(0, Math.PI / 2), Math.PI / 2); near(angleDelta(350 * Math.PI / 180, 10 * Math.PI / 180), 20 * Math.PI / 180, 1e-12);
  near(angleDelta(10 * Math.PI / 180, 350 * Math.PI / 180), -20 * Math.PI / 180, 1e-12); near(angleDelta(0, 4 * Math.PI), 0);
});
test('ajunge exact la țintă; la mișcare redusă sare direct', () => {
  const t: Pose = { x: 3, z: -2, yaw: 1, pitch: -.1 };
  assert.deepEqual(glideStep({ x: 0, z: 0, yaw: 0, pitch: 0 }, t, 1 / 60, true), { pose: t, done: true });
  let p: Pose = { x: 0, z: 0, yaw: 0, pitch: 0 }, n = 0, done = false; while (!done && n < 1000){ ({ pose: p, done } = glideStep(p, t, 1 / 60, false)); n++; }
  assert.ok(done && n > 10 && n < 200, `${n} cadre`); assert.deepEqual(p, t);
});
