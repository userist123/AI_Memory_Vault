// Auditul de precizie al modelelor 3D: fiecare variantă din catalog, desenată de motor (viewer3d-engine.js, model()),
// comparată cu cotele din fișa produsului. Se rulează în consola browserului, pe `npm run dev`, cu un proiect deschis în 3D
// (în dezvoltare, Viewer3D expune window.__casa3dViewer și window.__casa3dEngineCat).
//
// Ce se măsoară: cutia produsului fără accesoriile de punere în scenă (televizorul de pe comodă, lampa de pe noptieră,
// oglinda peste lavoar, burlanul sobei: userData.prop) și fără piesele de set (scaunele unei mese: userData.setPiece).
// Excepții (cotele din fișă au alt înțeles): planta (ghiveciul), oglinda pe suport (înclinată), dușul walk-in (zona de duș),
// bucătăria (corpurile suspendate). Pragul: 2 cm pe fiecare axă și 2 cm față de centrul amprentei din plan.
(() => {
  const V = window.__casa3dViewer, C = window.__casa3dEngineCat;
  if (!V || !C) return 'Deschide un proiect în vederea 3D (npm run dev).';
  const SKIP = new Set(['mirror', 'plant', 'shower', 'kitchen']), TOL = 2.05, rows = [];
  for (const [g, grp] of Object.entries(C)){
    if (!grp.model) continue;
    grp.v.forEach((v, i) => { if (!v.w) return; const m = V.measureModel({ ...v, model: grp.model, s: v.s || {} });
      const err = Math.max(Math.abs(m.w - v.w), Math.abs(m.d - v.d), Math.abs(m.h - v.h)), off = Math.max(Math.abs((m.x0 + m.x1) / 2), Math.abs((m.z0 + m.z1) / 2));
      rows.push({ id: `${g}-${i}`, model: grp.model, name: v.nume, declared: [v.w, v.d, v.h], drawn: [m.w, m.d, m.h].map(x => Math.round(x * 10) / 10), err: Math.round(err * 10) / 10, off: Math.round(off * 10) / 10, checked: !SKIP.has(grp.model) }); }); }
  const checked = rows.filter(r => r.checked), bad = checked.filter(r => r.err > TOL || r.off > TOL);
  console.table(bad.length ? bad : checked.slice(0, 10));
  return { variants: rows.length, checked: checked.length, withinTolerance: checked.length - bad.length, maxErrCm: Math.max(...checked.map(r => r.err)), failures: bad.map(r => `${r.id} ${r.name}: ${r.declared} -> ${r.drawn}`) };
})();
