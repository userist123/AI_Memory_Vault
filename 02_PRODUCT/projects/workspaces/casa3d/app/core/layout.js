// core/layout.js — motorul de amenajare extras din prototip (Faza 0).
// Codul funcțiilor este copiat NESCHIMBAT din legacy/prototype-v2.html; doar dependențele
// globale (PLAN, CAT, SEL, PICKED, NOFIT, WT) au devenit parametri ai fabricii.
export function createLayoutEngine({ plan, catalog, selection, picked = new Set(), wallThickness = .15 }) {
  const PLAN = plan, OPTS = catalog, SEL = selection, PICKED = picked, WT = wallThickness;
  const cur = k => { const g = OPTS[k]; return g ? Object.assign({ model: g.model }, g.v[Math.min(SEL[k] || 0, g.v.length - 1)]) : null; };
  const CAT = new Proxy({}, { get: (_, k) => cur(k) });
  const area = r => (r.x1 - r.x0) * (r.z1 - r.z0);
  let NOFIT = [];
/* =====================================================================
   3) AMENAJAREA AUTOMATĂ — reguli pe tip de cameră, cu distanțe minime
   de circulație (90 cm în fața dulapurilor, 60 cm lângă pat, 45 cm
   între canapea și măsuță, 75 cm pentru tras scaunele) și fără să
   blocheze ușile sau să acopere ferestrele cu mobilier înalt.
   ===================================================================== */
function openingsOnSide(room, side){
  const out = [];
  PLAN.pereti.forEach(w => {
    const [ax, az] = w.a, [bx, bz] = w.b, horiz = az === bz;
    const onN = side === 'N' && horiz && az === room.z0, onS = side === 'S' && horiz && az === room.z1;
    const onW = side === 'W' && !horiz && ax === room.x0, onE = side === 'E' && !horiz && ax === room.x1;
    if (!(onN || onS || onW || onE)) return;
    const dir = horiz ? Math.sign(bx - ax) : Math.sign(bz - az), s0 = horiz ? ax : az;
    w.goluri.forEach(g => { const p = s0 + dir * g.la, q = s0 + dir * (g.la + g.l); out.push({ tip: g.tip, a: Math.min(p, q), b: Math.max(p, q) }); });
  });
  const lo = side === 'N' || side === 'S' ? room.x0 : room.z0, hi = side === 'N' || side === 'S' ? room.x1 : room.z1;
  return out.filter(o => o.b > lo + .01 && o.a < hi - .01);
}
const rectHit = (A, B) => A.x0 < B.x1 - 1e-3 && A.x1 > B.x0 + 1e-3 && A.z0 < B.z1 - 1e-3 && A.z1 > B.z0 + 1e-3;
const sideLen = (r, s) => s === 'N' || s === 'S' ? r.x1 - r.x0 : r.z1 - r.z0;
const OPP = { N: 'S', S: 'N', W: 'E', E: 'W' };
function footprint(room, side, along, w, d){ const t = WT / 2 + .01;
  if (side === 'N') return { x0: along - w / 2, x1: along + w / 2, z0: room.z0 + t, z1: room.z0 + t + d, rot: 0 };
  if (side === 'S') return { x0: along - w / 2, x1: along + w / 2, z0: room.z1 - t - d, z1: room.z1 - t, rot: Math.PI };
  if (side === 'W') return { z0: along - w / 2, z1: along + w / 2, x0: room.x0 + t, x1: room.x0 + t + d, rot: Math.PI / 2 };
  return { z0: along - w / 2, z1: along + w / 2, x0: room.x1 - t - d, x1: room.x1 - t, rot: -Math.PI / 2 }; }
function frontZone(fp, side, depth){
  if (side === 'N') return { x0: fp.x0, x1: fp.x1, z0: fp.z1, z1: fp.z1 + depth };
  if (side === 'S') return { x0: fp.x0, x1: fp.x1, z0: fp.z0 - depth, z1: fp.z0 };
  if (side === 'W') return { z0: fp.z0, z1: fp.z1, x0: fp.x1, x1: fp.x1 + depth };
  return { z0: fp.z0, z1: fp.z1, x0: fp.x0 - depth, x1: fp.x0 }; }
function doorZones(room){ const z = [];
  ['N', 'S', 'W', 'E'].forEach(s => openingsOnSide(room, s).filter(o => o.tip === 'usa').forEach(o => {
    const fake = s === 'N' || s === 'S' ? { x0: o.a - .05, x1: o.b + .05, z0: room.z0, z1: room.z1 } : { z0: o.a - .05, z1: o.b + .05, x0: room.x0, x1: room.x1 };
    if (s === 'N') fake.z1 = room.z0 + .95; if (s === 'S') fake.z0 = room.z1 - .95; if (s === 'W') fake.x1 = room.x0 + .95; if (s === 'E') fake.x0 = room.x1 - .95;
    z.push(fake); }));
  return z; }
const inside = (room, r) => r.x0 >= room.x0 - 1e-3 && r.x1 <= room.x1 + 1e-3 && r.z0 >= room.z0 - 1e-3 && r.z1 <= room.z1 + 1e-3;
function freeOf(room, r, placed, extra){ if (!inside(room, r)) return false;
  if (doorZones(room).some(z => rectHit(z, r))) return false;
  if (placed.some(p => rectHit(p.fp, r) || (p.front && rectHit(p.front, r)))) return false;
  return !(extra || []).some(z => rectHit(z, r)); }
function findSpot(room, key, placed, o){
  const it = CAT[key], w = it.w / 100, d = it.d / 100; let best = null;
  (o.sides || ['N', 'E', 'S', 'W']).forEach((s, si) => {
    const L = sideLen(room, s), lo = (s === 'N' || s === 'S' ? room.x0 : room.z0) + w / 2 + .06, hi = lo + L - w - .12;
    if (hi < lo) return;
    const ops = openingsOnSide(room, s);
    for (let c = lo; c <= hi + 1e-6; c += .05){
      if (it.h > 95 && ops.some(op => op.tip === 'fereastra' && c + w / 2 > op.a && c - w / 2 < op.b)) continue;
      if (ops.some(op => op.tip === 'usa' && c + w / 2 > op.a - .05 && c - w / 2 < op.b + .05)) continue;
      const fp = footprint(room, s, c, w, d), fr = o.front ? frontZone(fp, s, o.front) : null;
      const check = fr ? { x0: Math.min(fp.x0, fr.x0), x1: Math.max(fp.x1, fr.x1), z0: Math.min(fp.z0, fr.z0), z1: Math.max(fp.z1, fr.z1) } : fp;
      if (!freeOf(room, check, placed, o.extra)) continue;
      const mid = (lo + hi) / 2, t = hi > lo ? (c - lo) / (hi - lo) : .5;
      let score = si * 10 + (o.prefer === 'corner' ? Math.min(t, 1 - t) * 4 : Math.abs(c - mid)) + (o.windowPenalty ? ops.filter(op => op.tip === 'fereastra').length * 3 : 0);
      if (o.near){ const cx = (fp.x0 + fp.x1) / 2, cz = (fp.z0 + fp.z1) / 2; score += Math.hypot(cx - o.near[0], cz - o.near[1]) * 2; }
      if (!best || score < best.score) best = { key, room: room.id, side: s, along: c, fp, front: fr, score };
    }
  });
  if (best) placed.push(best);
  return best;
}
function sidesByLength(room, penaliseWin){
  return ['N', 'S', 'W', 'E'].map(s => { const ops = openingsOnSide(room, s), doors = ops.filter(o => o.tip === 'usa').length, win = ops.filter(o => o.tip === 'fereastra').length;
    return { s, v: sideLen(room, s) - doors * 1.2 - (penaliseWin ? win * 2 : 0) }; }).sort((a, b) => b.v - a.v).map(x => x.s);
}
function freeRect(room, placed, w, d, pad){ pad = pad || .08; // caută loc liber în mijlocul camerei (ex. masa cu scaune)
  let best = null; const cx = (room.x0 + room.x1) / 2, cz = (room.z0 + room.z1) / 2;
  for (let x = room.x0 + pad + w / 2; x <= room.x1 - pad - w / 2; x += .1) for (let z = room.z0 + pad + d / 2; z <= room.z1 - pad - d / 2; z += .1){
    const r = { x0: x - w / 2 - pad, x1: x + w / 2 + pad, z0: z - d / 2 - pad, z1: z + d / 2 + pad };
    if (!freeOf(room, r, placed)) continue; const sc = Math.hypot(x - cx, z - cz);
    if (!best || sc < best.sc) best = { x, z, sc }; }
  return best;
}
function placeDining(r, placed){ // masa aleasă; dacă n-ai ales-o tu și nu încape, se ia automat prima variantă care încape
  if (tryDining(r, placed)) return true; if (PICKED.has('masa')) return false;
  const keep = SEL.masa; for (let i = 0; i < OPTS.masa.v.length; i++){ if (i === keep) continue; SEL.masa = i; if (tryDining(r, placed)) return true; } SEL.masa = keep; return false; }
function tryDining(r, placed){ // spațiu pentru scaune: 60-65 cm pe laturile cu scaune
  const it = CAT.masa, w = it.w / 100, d = it.d / 100, px = (it.chairs || 4) === 2 ? .45 : .65, pz = .6;
  const f = freeRect(r, placed, w + 2 * px, d + 2 * pz, 0); if (!f) return false;
  placed.push({ key: 'masa', room: r.id, free: true, x: f.x, z: f.z, rot: 0, fp: { x0: f.x - w / 2, x1: f.x + w / 2, z0: f.z - d / 2, z1: f.z + d / 2 }, front: { x0: f.x - w / 2 - px, x1: f.x + w / 2 + px, z0: f.z - d / 2 - pz, z1: f.z + d / 2 + pz } }); return true; }
function autoFurnish(){
  const placed = [], byType = t => PLAN.camere.filter(r => r.tip === t), kitchen = byType('bucatarie')[0];
  let diningInKitchen = false;
  byType('bucatarie').forEach(r => {
    const k = findSpot(r, 'bucatarie', placed, { sides: sidesByLength(r, true), front: 1.0, prefer: 'corner', windowPenalty: true });
    findSpot(r, 'frigider', placed, { sides: k ? [k.side, ...['N', 'S', 'E', 'W'].filter(s => s !== k.side)] : undefined, front: .9, prefer: 'corner', near: k ? [(k.fp.x0 + k.fp.x1) / 2, (k.fp.z0 + k.fp.z1) / 2] : null });
    if (area(r) >= 9.5 && placeDining(r, placed)) diningInKitchen = true;
  });
  byType('living').forEach(r => {
    const cx0 = (r.x0 + r.x1) / 2, cz0 = (r.z0 + r.z1) / 2, kc = kitchen ? [(kitchen.x0 + kitchen.x1) / 2, (kitchen.z0 + kitchen.z1) / 2] : [cx0, cz0];
    const wide = r.x1 - r.x0 >= r.z1 - r.z0, farEnd = diningInKitchen ? null : wide ? [kc[0] < cx0 ? r.x1 - 1.4 : r.x0 + 1.4, cz0] : [cx0, kc[1] < cz0 ? r.z1 - 1.4 : r.z0 + 1.4];
    const sofa = findSpot(r, 'canapea', placed, { sides: sidesByLength(r, true), front: .45 + .78 + .9, windowPenalty: true, near: farEnd });
    if (sofa){ const tvSide = OPP[sofa.side], c = sofa.along;
      const fpTv = footprint(r, tvSide, c, CAT.comodaTv.w / 100, CAT.comodaTv.d / 100);
      if (freeOf(r, fpTv, placed)) placed.push({ key: 'comodaTv', room: r.id, side: tvSide, along: c, fp: fpTv });
      else findSpot(r, 'comodaTv', placed, { sides: [tvSide], near: [c, c] });
      const f = frontZone(sofa.fp, sofa.side, .45 + .78), mt = frontZone(sofa.fp, sofa.side, .45);
      const cx = (sofa.fp.x0 + sofa.fp.x1) / 2, cz = (sofa.fp.z0 + sofa.fp.z1) / 2, horiz = sofa.side === 'N' || sofa.side === 'S';
      const w = CAT.masuta.w / 100, d = CAT.masuta.d / 100;
      const fp = horiz ? { x0: cx - w / 2, x1: cx + w / 2, z0: sofa.side === 'N' ? mt.z1 : mt.z0 - d, z1: sofa.side === 'N' ? mt.z1 + d : mt.z0 } : { z0: cz - w / 2, z1: cz + w / 2, x0: sofa.side === 'W' ? mt.x1 : mt.x0 - d, x1: sofa.side === 'W' ? mt.x1 + d : mt.x0 };
      sofa.front = null; placed.push({ key: 'masuta', room: r.id, free: true, x: (fp.x0 + fp.x1) / 2, z: (fp.z0 + fp.z1) / 2, rot: horiz ? 0 : Math.PI / 2, fp }); }
    if (!diningInKitchen && !placeDining(r, placed)) NOFIT.push({ key: 'masa', room: r.nume });
    findSpot(r, 'biblioteca', placed, { front: .6, prefer: 'corner' });
    findSpot(r, 'biblioteca', placed, { front: .6, prefer: 'corner' });
  });
  byType('dormitor').forEach(r => {
    const bed = findSpot(r, 'pat', placed, { sides: sidesByLength(r, true), front: .6, windowPenalty: true, extra: [] });
    if (bed){ const horiz = bed.side === 'N' || bed.side === 'S', w = CAT.pat.w / 100;
      // noptiera cu dimensiunile ei (prototipul presupunea mereu 39×41 cm, iar una mai lată intra în pat), la 1.5 cm de pat
      const ns = CAT.noptiera, nw = ns && ns.w ? ns.w / 100 : .39, nd = ns && ns.d ? ns.d / 100 : .41;
      [-1, 1].forEach(k => { const c = bed.along + k * (w / 2 + nw / 2 + .015), fp = footprint(r, bed.side, c, nw, nd);
        if (freeOf(r, fp, placed)) placed.push({ key: 'noptiera', room: r.id, side: bed.side, along: c, fp }); });
      bed.front = frontZone(bed.fp, bed.side, .6);
      const sidePad = horiz ? [{ x0: bed.fp.x0 - .6, x1: bed.fp.x0, z0: bed.fp.z0, z1: bed.fp.z1 }, { x0: bed.fp.x1, x1: bed.fp.x1 + .6, z0: bed.fp.z0, z1: bed.fp.z1 }] : [{ z0: bed.fp.z0 - .6, z1: bed.fp.z0, x0: bed.fp.x0, x1: bed.fp.x1 }, { z0: bed.fp.z1, z1: bed.fp.z1 + .6, x0: bed.fp.x0, x1: bed.fp.x1 }];
      findSpot(r, 'dulap', placed, { front: .9, prefer: 'corner', extra: sidePad });
      const d = findSpot(r, 'birou', placed, { front: .8, extra: sidePad, sides: ['N', 'E', 'S', 'W'].sort((a, b) => openingsOnSide(r, b).filter(o => o.tip === 'fereastra').length - openingsOnSide(r, a).filter(o => o.tip === 'fereastra').length) });
      if (d){ const fz = frontZone(d.fp, d.side, .45), cx = (fz.x0 + fz.x1) / 2, cz = (fz.z0 + fz.z1) / 2; placed.push({ key: 'scaunBirou', room: r.id, free: true, x: cx, z: cz, rot: d.fp.rot + Math.PI, fp: { x0: cx - .3, x1: cx + .3, z0: cz - .3, z1: cz + .3 }, decorFp: true }); } }
  });
  byType('baie').forEach(r => {
    const door = ['N', 'S', 'W', 'E'].map(s => ({ s, o: openingsOnSide(r, s).find(o => o.tip === 'usa') })).find(x => x.o);
    const far = door ? (door.s === 'N' || door.s === 'S' ? [((door.o.a + door.o.b) / 2 < (r.x0 + r.x1) / 2 ? r.x1 : r.x0), door.s === 'N' ? r.z1 : r.z0] : [door.s === 'W' ? r.x1 : r.x0, ((door.o.a + door.o.b) / 2 < (r.z0 + r.z1) / 2 ? r.z1 : r.z0)]) : [r.x1, r.z1];
    findSpot(r, 'dus', placed, { prefer: 'corner', near: far, sides: [far[1] === r.z1 ? 'S' : 'N', far[0] === r.x1 ? 'E' : 'W', far[1] === r.z1 ? 'N' : 'S', far[0] === r.x1 ? 'W' : 'E'] });
    findSpot(r, 'lavoar', placed, { front: .6 });
    findSpot(r, 'wc', placed, { front: .6 });
  });
  byType('hol').forEach(r => { findSpot(r, 'pantofar', placed, { front: .5 }); findSpot(r, 'oglinda', placed, { front: .6, prefer: 'corner' }); });
  return placed;
}


  // ---- adăugate din secțiunea de editare a prototipului (neschimbate) ----
  const fpFrom = (x, z, rot, w, d) => { const q = Math.round(rot / (Math.PI / 2)) % 2 !== 0; const hw = (q ? d : w) / 2, hd = (q ? w : d) / 2; return { x0: x - hw, x1: x + hw, z0: z - hd, z1: z + hd, rot }; };
  function validSpot(p, fp, others){ const room = PLAN.camere.find(r => r.id === p.room); if (!room || !inside(room, fp)) return false;
    if (doorZones(room).some(z => rectHit(z, fp))) return false; return !others.some(o => o !== p && rectHit(o.fp, fp)); }
  function run(){ NOFIT = []; const placed = autoFurnish(); const cnt = {}; placed.forEach(p => { const k = p.key + '#' + p.room; cnt[k] = (cnt[k] || 0) + 1; p.id = k + '#' + cnt[k]; }); return { placed, notFit: NOFIT }; }
  return { run, openingsOnSide, rectHit, footprint, frontZone, doorZones, inside, freeOf, findSpot, freeRect, fpFrom, validSpot, area, current: cur };
}
