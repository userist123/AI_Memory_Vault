'use client';
// Studiul de însorire în editor: panoul (amplasament, nord, zi, oră, rezultatele pe camere) și stratul de pe plan
// (harta orelor de soare pe pardoseală, petele de soare din momentul ales, săgeata nordului). Calculul: core/insolation.ts.
import { useEffect, useMemo, useRef, useState } from 'react';
import type { Catalog, Floor, FurniturePlacement } from '@/core/types';
import { insolationStudy, sunPatches, NORM_HOURS, NORM_REFERENCE_DATE, type Study } from '@/core/insolation';
import { sunRemedies, swapIdeas, type Remedy, type UseSwap } from '@/core/sun-design';
import { solarGain, sunOnPieces, type PieceSun } from '@/core/sun-effects';
import { PLACES, DEFAULT_SITE, sunVector, planAzimuth, type SiteInfo } from '@/core/site-location';
import { sunPosition, localToUtc } from '@/core/solar';
import { usePrefs } from '@/lib/prefs';

export interface SunUi { date: string; minutes: number; map: boolean; playing: boolean }
export const SUN_PRESETS = [['winter', '-12-21'], ['equinox', '-03-20'], ['summer', '-06-21']] as const;
export function defaultSunUi(year = new Date().getFullYear()): SunUi { return { date: `${year}-12-21`, minutes: 12 * 60, map: true, playing: false }; }
const hhmm = (m: number) => `${String(Math.floor(m / 60) % 24).padStart(2, '0')}:${String(Math.round(m % 60)).padStart(2, '0')}`;
const toMin = (s: string | null) => { if (!s) return null; const [h, m] = s.split(':').map(Number); return h! * 60 + m!; };

/** Soarele în momentul ales: poziția adevărată, cea față de plan (pentru 3D) și vectorul (pentru petele de pe plan). */
export function sunAt(site: SiteInfo, ui: Pick<SunUi, 'date' | 'minutes'>){
  const p = sunPosition(localToUtc(ui.date, ui.minutes, site.tz), site.lat, site.lon);
  return { ...p, planAzimuthDeg: planAzimuth(p.azimuthDeg, site), dir: sunVector(p.azimuthDeg, p.elevationDeg, site) as [number, number, number] };
}
/** Două calcule: ferestrele la 5 minute (verdictul normei), harta pardoselii la 15 minute (mai grea). */
export function useStudy(floor: Floor, site: SiteInfo | null, date: string, map: boolean){
  const toUtc = (tz: string) => (d: string, m: number) => localToUtc(d, m, tz);
  // doar geometria (pereți și goluri, camere, tavan, nișe și obiecte de perete) declanșează recalculul: mutarea mobilierului nu
  // nișele și obiectele de pe pereți nu umbresc, dar limitează unde poate apărea o fereastră propusă
  const key = useMemo(() => JSON.stringify([floor.ceilingHeight, floor.walls, floor.rooms.map(r => [r.id, r.name, r.type, r.rect, r.boundary, r.spaceKind]), floor.niches ?? [], floor.wallItems ?? []]), [floor]);
  const geo = useMemo(() => floor, [key]); // eslint-disable-line react-hooks/exhaustive-deps
  const win = useMemo(() => site ? insolationStudy(geo, site, toUtc(site.tz), { date, stepMin: 5, floorGrid: false }) : null, [geo, site, date]); // eslint-disable-line
  const grid = useMemo(() => site && map ? insolationStudy(geo, site, toUtc(site.tz), { date, stepMin: 15, cellM: .3 }) : null, [geo, site, date, map]); // eslint-disable-line
  // tabelul ia orele ferestrelor din calculul fin și partea însorită a pardoselii din hartă
  const merged = useMemo(() => win && grid ? { ...win, rooms: win.rooms.map(r => { const g = grid.rooms.find(x => x.roomId === r.roomId); return g ? { ...r, floorShare: g.floorShare, peak: g.peak } : r; }) } : win, [win, grid]);
  // verdictul normei (21 decembrie, oricare ar fi ziua aleasă) și căldura intrată prin geamuri în ziua aleasă
  const norm = useMemo(() => site ? insolationStudy(geo, site, toUtc(site.tz), { date: NORM_REFERENCE_DATE, stepMin: 5, floorGrid: false }) : null, [geo, site]); // eslint-disable-line
  const gain = useMemo(() => site ? solarGain(geo, site, toUtc(site.tz), date) : null, [geo, site, date]); // eslint-disable-line
  return { win: merged, grid, norm, gain, geo };
}

export interface SunExtras { geo: Floor; /** planul real (fără previzualizare): pe el se caută propunerile */ base: Floor; norm: Study | null; gain: ReturnType<typeof solarGain> | null; placements: FurniturePlacement[]; catalog: Catalog; pieceName(p: FurniturePlacement): string;
  /** previzualizarea unei propuneri (null = închide) și aplicarea ei (un singur pas de anulare) */
  previewId: string | null; onPreview(r: Remedy | null): void; onApply(r: Remedy): void }
export function SunStudyPanel({ site, ui, study, legendMax = null, onSite, onUi, extras }: { site: SiteInfo | null; ui: SunUi; study: Study | null; legendMax?: number | null; onSite(s: SiteInfo | null): void; onUi(p: Partial<SunUi>): void; extras?: SunExtras }){
  const { t, lang } = usePrefs();
  const s = site ?? DEFAULT_SITE, now = site ? sunAt(site, ui) : null;
  const rise = toMin(study?.sunrise ?? null) ?? 360, set = toMin(study?.sunset ?? null) ?? 1080;
  // redarea: ora avansează cu 10 minute la fiecare 120 ms, de la răsărit la apus, apoi se oprește
  const raf = useRef<number | null>(null), uiRef = useRef(ui); uiRef.current = ui;
  useEffect(() => { if (!ui.playing) return; const id = window.setInterval(() => { const m = uiRef.current.minutes + 10; if (m > set){ onUi({ playing: false, minutes: set }); } else onUi({ minutes: m }); }, 120);
    raf.current = id; return () => window.clearInterval(id); }, [ui.playing, set]); // eslint-disable-line
  const year = ui.date.slice(0, 4);
  if (!site) return (<section className="sunstudy" data-testid="sun-study">
    <h3>{t('sun.title')}</h3><p className="muted">{t('sun.intro')}</p>
    <button className="btn primary" onClick={() => onSite({ ...DEFAULT_SITE })}>{t('sun.enable')}</button></section>);
  return (<section className="sunstudy" data-testid="sun-study" aria-label={t('sun.title')}>
    <h3>{t('sun.title')}</h3>
    <div className="sun-grid">
      <label className="f"><span>{t('sun.place')}</span><select value={s.place ?? ''} onChange={e => { const p = PLACES.find(x => x.id === e.target.value); if (p) onSite({ ...s, lat: p.lat, lon: p.lon, tz: p.tz, place: p.id }); else { const { place: _p, ...rest } = s; onSite(rest); } }}>
        {PLACES.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}<option value="">{t('sun.custom')}</option></select></label>
      {!s.place && <div className="sun-row">
        <label className="f"><span>{t('sun.lat')}</span><input type="number" step={0.01} min={-66} max={66} value={s.lat} onChange={e => { const v = Number(e.target.value); if (Number.isFinite(v) && Math.abs(v) <= 66) onSite({ ...s, lat: Math.round(v * 100) / 100 }); }} /></label>
        <label className="f"><span>{t('sun.lon')}</span><input type="number" step={0.01} min={-180} max={180} value={s.lon} onChange={e => { const v = Number(e.target.value); if (Number.isFinite(v) && Math.abs(v) <= 180) onSite({ ...s, lon: Math.round(v * 100) / 100 }); }} /></label></div>}
      <label className="f"><span>{t('sun.north', { deg: s.northDeg })}</span><input type="range" min={0} max={359} value={s.northDeg} aria-valuetext={t('sun.northAria', { deg: s.northDeg })} onChange={e => onSite({ ...s, northDeg: Number(e.target.value) })} /></label>
      <div className="sun-presets" role="group" aria-label={t('sun.day')}>
        {SUN_PRESETS.map(([k, md]) => <button key={k} className="btn" aria-pressed={ui.date === year + md} onClick={() => onUi({ date: year + md })}>{t(`sun.preset.${k}`)}</button>)}
        <input type="date" aria-label={t('sun.day')} value={ui.date} onChange={e => { if (/^\d{4}-\d{2}-\d{2}$/.test(e.target.value)) onUi({ date: e.target.value }); }} /></div>
      <div className="sun-row">
        <button className="btn icon-btn" aria-label={ui.playing ? t('sun.pause') : t('sun.play')} aria-pressed={ui.playing} onClick={() => onUi({ playing: !ui.playing, ...(ui.playing ? {} : ui.minutes >= set || ui.minutes < rise ? { minutes: rise } : {}) })}>{ui.playing ? '❚❚' : '▶'}</button>
        <label className="f" style={{ flex: 1 }}><span>{t('sun.time', { time: hhmm(ui.minutes) })}</span><input type="range" min={0} max={1435} step={5} value={ui.minutes} aria-valuetext={hhmm(ui.minutes)} onChange={e => onUi({ minutes: Number(e.target.value), playing: false })} /></label></div>
      <label className="sun-check"><input type="checkbox" checked={ui.map} onChange={e => onUi({ map: e.target.checked })} /> {t('sun.map')}</label>
      {ui.map && legendMax != null && <div className="sun-legend" aria-label={t('sun.legendAria', { max: fmtH(Math.max(2, legendMax)) })}><span>0 h</span><i style={{ background: `linear-gradient(90deg, ${HEAT_STOPS.map(c => `rgb(${c.join(',')})`).join(', ')})` }} /><span>{fmtH(Math.max(2, legendMax))}</span></div>}
    </div>
    {now && study && <p className="sun-now" role="status">{now.elevationDeg > 0 ? t('sun.now', { az: Math.round(now.azimuthDeg), el: Math.round(now.elevationDeg) }) : t('sun.below')}
      {' · '}{study.sunrise && study.sunset ? t('sun.dayInfo', { rise: study.sunrise, set: study.sunset, len: fmtH(study.dayLengthH) }) : t('sun.polar')}</p>}
    {study && <table className="sun-table"><caption className="sr-only">{t('sun.tableCaption', { date: ui.date })}</caption>
      <thead><tr><th scope="col">{t('sun.col.room')}</th><th scope="col">{t('sun.col.hours')}</th><th scope="col">{t('sun.col.when')}</th>{extras?.gain && <th scope="col" title={t('sun.gainTitle')}>{t('sun.col.gain')}</th>}<th scope="col">{t('sun.col.verdict')}</th></tr></thead>
      <tbody>{study.rooms.map(r => { const ws = study.windows.filter(w => w.roomId === r.roomId), first = ws.map(w => w.first).filter(Boolean).sort()[0], last = ws.map(w => w.last).filter(Boolean).sort().at(-1);
        return <tr key={r.roomId} className={`sun-${r.verdict}`}><th scope="row">{r.name}<small>{ws.length ? [...new Set(ws.map(w => t(`sun.face.${w.facing}`)))].join(', ') : t('sun.noWindows')}</small></th>
          <td>{fmtH(r.hours)}{r.longestH < r.hours ? <small>{t('sun.longest', { h: fmtH(r.longestH) })}</small> : null}</td>
          <td>{first && last ? `${first}–${last}` : '—'}{r.peak && r.floorShare >= .01 ? <small>{t('sun.peak', { time: r.peak, pct: Math.round(r.floorShare * 100) })}</small> : null}</td>
          {extras?.gain && (() => { const g = extras.gain.rooms.find(x => x.roomId === r.roomId); return <td>{g && g.kwh > 0 ? <>{fmtKwh(g.kwh, lang)}{g.overheat && isSummer(ui.date) ? <small className="sun-hot">{t('sun.hot')}</small> : <small>{t('sun.peakW', { w: g.peakW })}</small>}</> : '—'}</td>; })()}
          <td><span className={`sun-badge sun-badge-${r.verdict}`}>{t(`sun.verdict.${r.verdict}`)}</span></td></tr>; })}</tbody></table>}
    {extras && <SunRemedies site={site} extras={extras} />}
    {extras && <SunOnFurniture site={site} date={ui.date} extras={extras} />}
    {study && <p className="muted sun-note">{t('sun.norm', { h: fmtH(NORM_HOURS) })}</p>}
    <p className="muted sun-note">{t('sun.limits')}</p>
    <button className="btn ghost" onClick={() => onSite(null)}>{t('sun.disable')}</button>
  </section>);
}
const fmtKwh = (k: number, lang: string) => { const loc = lang === 'en' ? 'en-GB' : 'ro-RO'; return k < .05 ? `< ${(.1).toLocaleString(loc)} kWh` : `${k.toLocaleString(loc, { maximumFractionDigits: 1, minimumFractionDigits: 1 })} kWh`; };
const isSummer = (date: string) => { const m = Number(date.slice(5, 7)); return m >= 5 && m <= 9; };

/** Propunerile automate pentru camerele de locuit sub normă: căutare la cerere, previzualizare pe plan și în 3D, aplicare. */
function SunRemedies({ site, extras }: { site: SiteInfo; extras: SunExtras }){
  const { t, lang } = usePrefs();
  const [found, setFound] = useState<Record<string, Remedy[]>>({}), [swaps, setSwaps] = useState<Record<string, UseSwap[]>>({});
  // verdictul pe planul real: în timpul previzualizării lista nu dispare
  const norm = useMemo(() => insolationStudy(extras.base, site, (d, m) => localToUtc(d, m, site.tz), { date: NORM_REFERENCE_DATE, stepMin: 5, floorGrid: false }), [extras.base, site]);
  const low = norm.rooms.filter(r => r.verdict === 'low' || r.verdict === 'none');
  // o geometrie nouă (inclusiv după aplicare) face vechile propuneri depășite
  useEffect(() => { setFound({}); setSwaps({}); }, [extras.base, site]);
  if (!low.length) return norm.rooms.some(r => r.habitable) ? <p className="sun-allok">{t('sun.rem.allOk')}</p> : null;
  const search = (roomId: string) => { const rem = sunRemedies(extras.base, site, (d, m) => localToUtc(d, m, site.tz), roomId, { max: 3, newId: () => `op-${crypto.randomUUID().slice(0, 8)}` }); setFound(f => ({ ...f, [roomId]: rem }));
    // fără nicio variantă care respectă norma: ideea de a muta funcțiunea într-o cameră mai însorită
    if (!rem.some(x => x.meets)){ const sw = swapIdeas(extras.base, site, (d, m) => localToUtc(d, m, site.tz), roomId); setSwaps(x => ({ ...x, [roomId]: sw })); } };
  return (<section className="sun-rem" aria-label={t('sun.rem.title')}>
    <h4>{t('sun.rem.title')}</h4><p className="muted sun-note">{t('sun.rem.intro')}</p>
    {low.map(r => { const list = found[r.roomId];
      return <div key={r.roomId} className="sun-rem-room"><div className="sun-rem-head"><b>{r.name}</b> <span className="muted">{t('sun.rem.now', { h: fmtH(r.hours) })}</span>
        {!list && <button className="btn sm" onClick={() => search(r.roomId)}>{t('sun.rem.find')}</button>}</div>
        {list && !list.length && <p className="muted sun-note">{t('sun.rem.none')}</p>}
        {list && list.map(x => { const on = extras.previewId === x.id;
          return <article key={x.id} className={`sun-rem-card ${x.meets ? 'is-ok' : ''}`} aria-current={on || undefined}>
            <div>{t(`sun.rem.${x.kind}`, { w: Math.round(x.widthM * 100), face: t(`sun.face.${x.facing}`), sill: Math.round(x.sillM * 100) })}</div>
            <div className="sun-rem-gain"><b>{fmtH(x.beforeH)} → {fmtH(x.afterH)}</b> <span className="muted">{t('sun.rem.glass', { m2: x.glazingM2.toLocaleString(lang === 'en' ? 'en-GB' : 'ro-RO') })}</span>{x.meets && <span className="sun-badge sun-badge-ok">{t('sun.verdict.ok')}</span>}</div>
            <div className="sun-rem-actions"><button className="btn sm" aria-pressed={on} onClick={() => extras.onPreview(on ? null : x)}>{on ? t('sun.rem.hide') : t('sun.rem.preview')}</button>
              <button className="btn sm primary" onClick={() => extras.onApply(x)}>{t('sun.rem.apply')}</button></div></article>; })}
        {list && !list.some(x => x.meets) && <p className="muted sun-note">{t('sun.rem.cannot')}</p>}
        {(swaps[r.roomId] ?? []).slice(0, 2).map(w => <article key={w.withRoomId} className="sun-rem-card sun-rem-swap">
          <div>{t('sun.rem.swap', { room: r.name, other: w.withName, h: fmtH(w.hours), m2: w.areaM2.toLocaleString(lang === 'en' ? 'en-GB' : 'ro-RO') })}</div></article>)}</div>; })}
  </section>);
}

/** Soarele direct pe televizor, birou și pat în ziua aleasă (și primul soare pe pat, care trezește). */
function SunOnFurniture({ site, date, extras }: { site: SiteInfo; date: string; extras: SunExtras }){
  const { t } = usePrefs();
  const key = JSON.stringify(extras.placements.map(p => [p.id, p.group, p.x, p.z, p.rotation, p.variantId, p.size]));
  const pieces: PieceSun[] = useMemo(() => sunOnPieces(extras.geo, extras.placements, extras.catalog, site, (d, m) => localToUtc(d, m, site.tz), date), [extras.geo, key, site, date]); // eslint-disable-line react-hooks/exhaustive-deps
  if (!pieces.length) return null;
  const rooms = new Map(extras.geo.rooms.map(r => [r.id, r.name]));
  return (<section className="sun-pieces" aria-label={t('sun.pieces.title')}><h4>{t('sun.pieces.title')}</h4>
    <ul>{pieces.map(p => { const pl = extras.placements.find(x => x.id === p.placementId)!;
      return <li key={p.placementId} className={`sun-piece-${p.effect}`}><b>{extras.pieceName(pl)}</b> · {rooms.get(p.roomId)}<br />
        {t(`sun.pieces.${p.effect}`, { when: p.spans.map(s => `${s.from}–${s.to}`).join(', '), h: fmtH(p.minutes / 60), first: p.spans[0]?.from ?? '' })}</li>; })}</ul>
    <p className="muted sun-note">{t('sun.pieces.note')}</p></section>);
}
export const maxHours = (grid: Study | null) => grid ? grid.cells.reduce((m, c) => Math.max(m, c.hours), 0) : 0;
const fmtH = (h: number) => { const m = Math.round(h * 60); return `${Math.floor(m / 60)} h ${String(m % 60).padStart(2, '0')}`; };

/** Culoarea unei celule după orele de soare, relativ la maximul zilei (cel puțin 2 h): 0 = transparent. */
export const HEAT_STOPS = [[255, 214, 102], [245, 146, 40], [196, 62, 28]] as const;
export function heatColor(hours: number, maxH = 6): string | null {
  if (hours <= 0) return null; const k = Math.min(1, hours / Math.max(2, maxH));
  const c = HEAT_STOPS, i = k < .5 ? 0 : 1, f = k < .5 ? k * 2 : (k - .5) * 2, a = c[i]!, b = c[i + 1]!;
  return `rgb(${a.map((v, j) => Math.round(v + (b[j]! - v) * f)).join(',')})`;
}

/** Stratul de pe plan (coordonate în metri, ca planul). Fără evenimente: nu încurcă selecția și desenarea. */
export function SunOverlay({ floor, site, ui, grid }: { floor: Floor; site: SiteInfo; ui: SunUi; grid: Study | null }){
  const { t } = usePrefs();
  const now = sunAt(site, ui), patches = now.elevationDeg > 0 ? sunPatches(floor, now.dir) : [];
  const xs = floor.walls.flatMap(w => [w.a[0], w.b[0]]).concat(floor.rooms.flatMap(r => [r.rect.x0, r.rect.x1])), zs = floor.walls.flatMap(w => [w.a[1], w.b[1]]).concat(floor.rooms.flatMap(r => [r.rect.z0, r.rect.z1]));
  const x1 = xs.length ? Math.max(...xs) : 6, z0 = zs.length ? Math.min(...zs) : 0, ax = x1 + .9, az = z0 - .2, rot = site.northDeg;
  const maxH = maxHours(grid);
  const sx = now.dir[0], sz = now.dir[2], sl = Math.hypot(sx, sz) || 1;
  return (<g className="sun-overlay" pointerEvents="none" aria-hidden="true" data-testid="sun-overlay">
    {ui.map && grid?.cells.map((c, i) => { const col = heatColor(c.hours, maxH); return col ? <rect key={i} x={c.x - grid.cellM / 2} y={c.z - grid.cellM / 2} width={grid.cellM + .005} height={grid.cellM + .005} fill={col} fillOpacity={.62} /> : null; })}
    {patches.map((p, i) => <polygon key={i} points={p.pts.map(q => q.join(',')).join(' ')} fill="#FFD54A" fillOpacity={.6} stroke="#C98A00" strokeWidth={.02} />)}
    <g transform={`translate(${ax} ${az}) rotate(${rot})`}>
      <circle r={.42} fill="#fff" fillOpacity={.85} stroke="#5E636B" strokeWidth={.02} />
      <path d="M0 -.36 L.13 .1 L0 .02 L-.13 .1 Z" fill="#2B2F36" />
      <text y={-.48} textAnchor="middle" fontSize={.26} fontWeight={700} fill="#2B2F36" fontFamily="Instrument Sans, Segoe UI, sans-serif">{t('sun.northLetter')}</text></g>
    {now.elevationDeg > 0 && <g transform={`translate(${ax} ${az + 1.2})`}>
      <line x1={0} y1={0} x2={sx / sl * .38} y2={sz / sl * .38} stroke="#C98A00" strokeWidth={.05} strokeLinecap="round" />
      <circle cx={sx / sl * .38} cy={sz / sl * .38} r={.12} fill="#FFC107" stroke="#C98A00" strokeWidth={.02} /></g>}
  </g>);
}
