// Texturi generate pentru finisajele de designer: pardoseli pe modul de așezare (drept, decalat, diagonală, spic, chevron, șah)
// cu rost și lamele la dimensiunea reală, tapet, tencuială decorativă, cărămidă, piatră și faianță. Fără imagini externe.
// Fiecare textură e desenată în metri (px/m fix), deci o placă de 60 × 120 cm arată ca o placă de 60 × 120 cm.
import * as THREE from 'three';

const cache = new Map(), MAX = 48;
function cached(key, make){ if (cache.has(key)){ const t = cache.get(key); cache.delete(key); cache.set(key, t); return t; }
  const t = make(); cache.set(key, t); if (cache.size > MAX){ const [k, old] = cache.entries().next().value; cache.delete(k); old.dispose(); } return t; }
// generator determinist (același proiect → aceeași textură la fiecare reconstruire)
function rng(seed){ let s = 0; for (const ch of String(seed)) s = (s * 31 + ch.charCodeAt(0)) | 0; return () => { s = (s * 1664525 + 1013904223) | 0; return ((s >>> 0) % 100000) / 100000; }; }
const hex2rgb = h => { const n = parseInt(h.slice(1), 16); return [(n >> 16) & 255, (n >> 8) & 255, n & 255]; };
const shade = (h, k) => { const [r, g, b] = hex2rgb(h), f = v => Math.max(0, Math.min(255, Math.round(v * k))); return `rgb(${f(r)},${f(g)},${f(b)})`; };
function canvasTexture(wPx, hPx, draw, repeat){ const cv = document.createElement('canvas'); cv.width = wPx; cv.height = hPx; draw(cv.getContext('2d'), wPx, hPx);
  const t = new THREE.CanvasTexture(cv); t.encoding = THREE.sRGBEncoding; t.anisotropy = 8; t.wrapS = t.wrapT = repeat ? THREE.RepeatWrapping : THREE.ClampToEdgeWrapping; return t; }

// ---------- pardoseală ----------
/** O piesă (placă sau lamelă) ca dreptunghi rotit: centru, lungime, lățime, unghi. Desenează fața, rostul și fibra lemnului. */
function piece(g, cx, cy, L, W, ang, fill, joint, jointPx, wood, rnd){
  g.save(); g.translate(cx, cy); g.rotate(ang); g.fillStyle = joint; g.fillRect(-L / 2, -W / 2, L, W);
  const j = jointPx; g.fillStyle = fill; g.fillRect(-L / 2 + j / 2, -W / 2 + j / 2, L - j, W - j);
  if (wood){ g.globalAlpha = .12; g.strokeStyle = '#3a2412'; g.lineWidth = Math.max(.6, W / 40);
    for (let k = 0; k < 7; k++){ const y = -W / 2 + W * (k + .5) / 7 + (rnd() - .5) * W / 10; g.beginPath(); g.moveTo(-L / 2, y); g.bezierCurveTo(-L / 6, y + (rnd() - .5) * W / 6, L / 6, y + (rnd() - .5) * W / 6, L / 2, y); g.stroke(); }
    g.globalAlpha = 1; }
  g.restore(); }
/** Pardoseala unei camere, desenată o singură dată pe toată suprafața (fără repetare vizibilă), la cel mult 2048 px. */
export function floorTexture(fin, roomW, roomD, seed){
  const key = `floor:${seed}:${roomW.toFixed(3)}:${roomD.toFixed(3)}:${JSON.stringify(fin)}`;
  return cached(key, () => { const ppm = Math.min(320, 2048 / Math.max(roomW, roomD)), Wpx = Math.max(8, Math.round(roomW * ppm)), Hpx = Math.max(8, Math.round(roomD * ppm));
    return canvasTexture(Wpx, Hpx, (g, w, h) => { const rnd = rng(seed), wood = fin.kind === 'parquet', L = fin.pieceL * ppm, W = fin.pieceW * ppm, jp = Math.max(wood ? .7 : 1, fin.grout * ppm);
      const tone = () => { const k = wood ? .86 + rnd() * .26 : .96 + rnd() * .06; return shade(fin.color, k); }, joint = fin.groutColor;
      g.fillStyle = joint; g.fillRect(0, 0, w, h); g.save();
      if (fin.angle === 90){ g.translate(w, 0); g.rotate(Math.PI / 2); }
      const span = Math.hypot(w, h) * 1.5;
      if (fin.pattern === 'herringbone'){ // lamele la ±45°: un „șir” de trepte pe diagonală, repetat cu (L, -L) — vezi derivarea din core/finishes.ts
        g.translate(w / 2, h / 2); g.rotate(Math.PI / 4); const n = Math.ceil(span / W) + 2, m = Math.ceil(span / L) + 2;
        for (let s = -m; s <= m; s++) for (let k = -n; k <= n; k++){ const ox = k * W + s * L, oy = k * W - s * L;
          piece(g, ox + L / 2, oy + W / 2, L, W, 0, tone(), joint, jp, wood, rnd); piece(g, ox - W / 2, oy + L / 2, L, W, Math.PI / 2, tone(), joint, jp, wood, rnd); } }
      else if (fin.pattern === 'chevron'){ // coloane de lamele tăiate la 45°, alternativ urcând și coborând
        const cw = L * Math.SQRT1_2, ph = W * Math.SQRT2, cols = Math.ceil(span / cw) + 2, rows = Math.ceil(span / ph) + Math.ceil(cw / ph) + 2;
        for (let c = -1; c < cols; c++){ const x0 = c * cw, x1 = x0 + cw, up = c % 2 === 0;
          for (let r = -Math.ceil(cw / ph) - 1; r < rows; r++){ const y = r * ph, a = up ? 0 : cw, b = up ? cw : 0;
            g.fillStyle = tone(); g.beginPath(); g.moveTo(x0, y + a); g.lineTo(x1, y + b); g.lineTo(x1, y + b + ph); g.lineTo(x0, y + a + ph); g.closePath(); g.fill();
            g.strokeStyle = joint; g.lineWidth = jp; g.stroke(); } } }
      else { // grilă: drept, decalat 1/2 sau 1/3, șah, diagonală (grila rotită la 45°)
        if (fin.pattern === 'diagonal'){ g.translate(w / 2, h / 2); g.rotate(Math.PI / 4); g.translate(-span / 2, -span / 2); }
        const shift = fin.pattern === 'brick' ? .5 : fin.pattern === 'third' ? 1 / 3 : 0, area = span, pw = L, ph = W;
        for (let r = 0; r * ph < area + ph; r++){ const off = ((r * shift) % 1) * pw;
          for (let c = -1; c * pw < area + pw; c++){ const x = c * pw - off, chk = fin.pattern === 'checker' && (r + c) % 2 === 1;
            piece(g, x + pw / 2, r * ph + ph / 2, pw, ph, 0, chk ? shade(fin.color, .78) : tone(), joint, jp, wood, rnd); } } }
      g.restore(); }, false); });
}

// ---------- pereți ----------
/** Textură pe un perete de `lenM` × `hM`: tapet (marmură sau dungi fine), tencuială, cărămidă, piatră, faianță. Repetată la 1 m. */
export function wallTexture(vis, seed){
  const key = `wall:${seed}:${JSON.stringify(vis)}`;
  return cached(key, () => { const ppm = 256, S = 256, rnd = rng(seed), col = vis.color;
    const t = canvasTexture(S, S, (g, w, h) => {
      g.fillStyle = col; g.fillRect(0, 0, w, h);
      if (vis.kind === 'wallpaper'){
        if (vis.marble){ g.globalAlpha = .35; for (let i = 0; i < 9; i++){ g.strokeStyle = rnd() < .5 ? '#9a978f' : '#bdb9b0'; g.lineWidth = .6 + rnd() * 1.6; g.beginPath(); let x = rnd() * w, y = 0; g.moveTo(x, y);
            while (y < h){ x += (rnd() - .5) * 40; y += 12 + rnd() * 20; g.lineTo(((x % w) + w) % w, y); } g.stroke(); } g.globalAlpha = 1; }
        else { g.globalAlpha = .08; g.fillStyle = '#000'; for (let x = 0; x < w; x += 16) g.fillRect(x, 0, 3, h); g.globalAlpha = 1; }
        g.globalAlpha = .05; g.fillStyle = '#000'; g.fillRect(0, 0, 1, h); g.globalAlpha = 1; } // îmbinarea fâșiilor (0,53 m ≈ la jumătate de metru)
      else if (vis.kind === 'plaster'){ const id = g.getImageData(0, 0, w, h); for (let i = 0; i < id.data.length; i += 4){ const n = (rnd() - .5) * 26; id.data[i] += n; id.data[i + 1] += n; id.data[i + 2] += n; } g.putImageData(id, 0, 0); }
      else if (vis.kind === 'brick' || vis.kind === 'tile'){ const [a, b] = vis.sizeCm || (vis.kind === 'brick' ? [24, 7.1] : [30, 60]), bw = Math.max(a, b) / 100 * ppm, bh = Math.min(a, b) / 100 * ppm;
        const tileVertical = vis.kind === 'tile', tw = tileVertical ? bh : bw, th = tileVertical ? bw : bh, joint = vis.kind === 'brick' ? '#cfc8bd' : '#e4e1db', jp = vis.kind === 'brick' ? 3 : 1.2;
        g.fillStyle = joint; g.fillRect(0, 0, w, h);
        for (let r = 0; r * th < h; r++){ const off = vis.kind === 'brick' && r % 2 ? tw / 2 : 0; for (let c = -1; c * tw < w + tw; c++){ g.fillStyle = shade(col, vis.kind === 'brick' ? .8 + rnd() * .35 : .97 + rnd() * .05);
          g.fillRect(c * tw + off + jp / 2, r * th + jp / 2, tw - jp, th - jp); } } }
      else if (vis.kind === 'stone'){ let y = 0; while (y < h){ const rh = 14 + rnd() * 22; let x = -rnd() * 40; while (x < w){ const rw = 30 + rnd() * 70; g.fillStyle = shade(col, .78 + rnd() * .35); g.fillRect(x + 2, y + 2, rw - 4, rh - 4); x += rw; } y += rh; } }
    }, true); t.repeat.set(1, 1); t.userData = { ppm, size: S / ppm }; return t; });
}
