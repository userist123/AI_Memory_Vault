// Tranziții de cameră amortizate și întreruptibile (UI/UX P2-14), pentru motorul 3D: în locul unei tăieturi bruște,
// vederea alunecă spre ținta nouă cu o amortizare exponențială (aceeași viteză la 30 sau la 144 de cadre pe secundă);
// orice comandă a utilizatorului o oprește, iar la prefers-reduced-motion se sare direct (tăietură).

/** Fracțiunea din distanța rămasă parcursă în `dt` secunde, independentă de cadre: 1 − e^(−k·dt). */
export const dampFactor = (k: number, dt: number) => (dt > 0 && k > 0 ? 1 - Math.exp(-k * dt) : 0);
/** Diferența de unghi pe drumul cel mai scurt, în (−π, π]. */
export function angleDelta(from: number, to: number){ let d = (to - from) % (2 * Math.PI); if (d > Math.PI) d -= 2 * Math.PI; if (d <= -Math.PI) d += 2 * Math.PI; return d; }

export type Pose = { x: number; z: number; yaw: number; pitch: number };
/** Un pas al alunecării; `done` când a ajuns (sub 5 mm și sub 0,3°), iar atunci poza e exact ținta. */
export function glideStep(cur: Pose, target: Pose, dt: number, reduce: boolean, k = 6): { pose: Pose; done: boolean } {
  if (reduce) return { pose: { ...target }, done: true };
  const f = dampFactor(k, dt), dy = angleDelta(cur.yaw, target.yaw), dp = target.pitch - cur.pitch;
  const pose = { x: cur.x + (target.x - cur.x) * f, z: cur.z + (target.z - cur.z) * f, yaw: cur.yaw + dy * f, pitch: cur.pitch + dp * f };
  const close = Math.hypot(target.x - pose.x, target.z - pose.z) < .005 && Math.abs(angleDelta(pose.yaw, target.yaw)) < .005 && Math.abs(target.pitch - pose.pitch) < .005;
  return close ? { pose: { ...target }, done: true } : { pose, done: false };
}
