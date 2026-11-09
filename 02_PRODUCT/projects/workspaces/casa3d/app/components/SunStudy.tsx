'use client';
// Studiul de însorire în editor: panoul (amplasament, nord, zi, oră, rezultatele pe camere) și stratul de pe plan
// (harta orelor de soare pe pardoseală, petele de soare din momentul ales, săgeata nordului). Calculul: core/insolation.ts.
import { Fragment, useEffect, useMemo, useRef, useState } from 'react';
import type { Catalog, Floor, FurniturePlacement, Snapshot } from '@/core/types';
import { insolationStudy, sunPatches, NORM_HOURS, NORM_REFERENCE_DATE, EN17037_DATE, en17037Level, type Study } from '@/core/insolation';
import { corners, gap, houseBox, moveTo, placeNeighbour, studyTriggers, neighbourStudy, MAX_NEIGHBOURS, type Neighbour, type NeighbourSun, type Obstacle, type Trigger } from '@/core/neighbours';
import { daylightStudy, type Daylight } from '@/core/daylight';
import { sunCalendar, lightScore, type LightScore } from '@/core/light-class';
import { summerCheck, shadeOptions, GLAZING, type GlazingKind, type ShadeKind } from '@/core/overheating';
import { sunRemedies, swapIdeas, type Remedy, type UseSwap } from '@/core/sun-design';
import { solarGain, sunOnPieces, type PieceSun } from '@/core/sun-effects';
import { PLACES, DEFAULT_SITE, sunVector, planAzimuth, guessTz, TZ_CHOICES, type SiteInfo } from '@/core/site-location';
import { sunPosition, localToUtc } from '@/core/solar';
import { usePrefs } from '@/lib/prefs';

export type SunTab = 'norm' | 'nb' | 'light' | 'cal' | 'summer' | 'furn';
/** `layer`: ce arată harta de pe plan — orele de soare din ziua aleasă sau factorul de lumină naturală (cer acoperit). */
export interface SunUi { date: string; minutes: number; map: boolean; playing: boolean; layer: 'sun' | 'df'; tab: SunTab }
export const SUN_PRESETS = [['winter', '-12-21'], ['equinox', '-03-20'], ['summer', '-06-21']] as const;
export function defaultSunUi(year = new Date().getFullYear()): SunUi { return { date: `${year}-12-21`, minutes: 12 * 60, map: true, playing: false, layer: 'sun', tab: 'norm' }; }
const hhmm = (m: number) => `${String(Math.floor(m / 60) % 24).padStart(2, '0')}:${String(Math.round(m % 60)).padStart(2, '0')}`;
const toMin = (s: string | null) => { if (!s) return null; const [h, m] = s.split(':').map(Number); return h! * 60 + m!; };

/** Soarele în momentul ales: poziția adevărată, cea față de plan (pentru 3D) și vectorul (pentru petele de pe plan). */
export function sunAt(site: SiteInfo, ui: Pick<SunUi, 'date' | 'minutes'>){
  const p = sunPosition(localToUtc(ui.date, ui.minutes, site.tz), site.lat, site.lon);
  return { ...p, planAzimuthDeg: planAzimuth(p.azimuthDeg, site), dir: sunVector(p.azimuthDeg, p.elevationDeg, site) as [number, number, number] };
}
/** Două calcule: ferestrele la 5 minute (verdictul normei), harta pardoselii la 15 minute (mai grea). */
export function useStudy(floor: Floor, site: SiteInfo | null, date: string, map: boolean, obs?: readonly Obstacle[]){
  const toUtc = (tz: string) => (d: string, m: number) => localToUtc(d, m, tz);
  // doar geometria (pereți și goluri, camere, tavan, nișe și obiecte de perete) declanșează recalculul: mutarea mobilierului nu
  // nișele și obiectele de pe pereți nu umbresc, dar limitează unde poate apărea o fereastră propusă
  const key = useMemo(() => JSON.stringify([floor.ceilingHeight, floor.walls, floor.rooms.map(r => [r.id, r.name, r.type, r.rect, r.boundary, r.spaceKind]), floor.niches ?? [], floor.wallItems ?? []]), [floor]);
  const geo = useMemo(() => floor, [key]); // eslint-disable-line react-hooks/exhaustive-deps
  const win = useMemo(() => site ? insolationStudy(geo, site, toUtc(site.tz), { date, stepMin: 5, floorGrid: false, obs }) : null, [geo, site, date, obs]); // eslint-disable-line
  const grid = useMemo(() => site && map ? insolationStudy(geo, site, toUtc(site.tz), { date, stepMin: 15, cellM: .3, obs }) : null, [geo, site, date, map, obs]); // eslint-disable-line
  // tabelul ia orele ferestrelor din calculul fin și partea însorită a pardoselii din hartă
  const merged = useMemo(() => win && grid ? { ...win, rooms: win.rooms.map(r => { const g = grid.rooms.find(x => x.roomId === r.roomId); return g ? { ...r, floorShare: g.floorShare, peak: g.peak } : r; }) } : win, [win, grid]);
  // verdictul normei (21 decembrie, oricare ar fi ziua aleasă) și căldura intrată prin geamuri în ziua aleasă
  const norm = useMemo(() => site ? insolationStudy(geo, site, toUtc(site.tz), { date: NORM_REFERENCE_DATE, stepMin: 5, floorGrid: false, obs }) : null, [geo, site, obs]); // eslint-disable-line
  const gain = useMemo(() => site ? solarGain(geo, site, toUtc(site.tz), date, 10, obs) : null, [geo, site, date, obs]); // eslint-disable-line
  // SR EN 17037 (voluntar): 21 martie, punctul la 1,2 m / 0,3 m peste parapet
  const en = useMemo(() => site ? insolationStudy(geo, site, toUtc(site.tz), { date: EN17037_DATE, stepMin: 5, floorGrid: false, obs, point: 'en17037' }) : null, [geo, site, obs]); // eslint-disable-line
  // lumina naturală pe cer acoperit (nu depinde de zi, nici de nord) și vara după AD O
  const df = useMemo(() => site ? daylightStudy(geo, { obs }) : null, [geo, site ? 1 : 0, obs]); // eslint-disable-line
  const summer = useMemo(() => site ? summerCheck(geo, site) : null, [geo, site]); // eslint-disable-line
  return { win: merged, grid, norm, gain, en, df, summer, geo };
}
/** Efectul casei asupra vecinilor și declanșatorul D ≤ H_max: depind de toată casa (toate nivelurile) și de vecini. */
export function useNeighbours(house: Snapshot | null, site: SiteInfo | null){
  const key = house ? JSON.stringify([house.floor, ...(house.levels ?? [])].map(f => [f.ceilingHeight, f.rooms.map(r => [r.rect, r.boundary, r.spaceKind])])) : '';
  return useMemo(() => !house || !site?.neighbors?.length ? { nb: [] as NeighbourSun[], triggers: [] as Trigger[] }
    : { nb: neighbourStudy(house, site, (d, m) => localToUtc(d, m, site.tz), NORM_REFERENCE_DATE, NORM_HOURS), triggers: studyTriggers(house, site) }, [key, site]); // eslint-disable-line react-hooks/exhaustive-deps
}

export interface SunExtras { /** casa întreagă (toate nivelurile), volumele care umbresc nivelul și rezultatele vecinilor */ house: Snapshot; obs: readonly Obstacle[]; nb: NeighbourSun[]; triggers: Trigger[]; en: Study | null;
  /** linkul raportului de însorire (PDF din browser) */ reportHref: string; geo: Floor; df: Daylight | null; summer: ReturnType<typeof summerCheck> | null;
  /** deschide vederea 3D (pentru înregistrarea zilei) */ open3d(): void; /** planul real (fără previzualizare): pe el se caută propunerile */ base: Floor; norm: Study | null; gain: ReturnType<typeof solarGain> | null; placements: FurniturePlacement[]; catalog: Catalog; pieceName(p: FurniturePlacement): string;
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
        <label className="f"><span>{t('sun.lat')}</span><input type="number" step={0.01} min={-66} max={66} value={s.lat} onChange={e => { const v = Number(e.target.value); if (Number.isFinite(v) && Math.abs(v) <= 66) { const lat = Math.round(v * 100) / 100; onSite({ ...s, lat, tz: guessTz(lat, s.lon) }); } }} /></label>
        <label className="f"><span>{t('sun.lon')}</span><input type="number" step={0.01} min={-180} max={180} value={s.lon} onChange={e => { const v = Number(e.target.value); if (Number.isFinite(v) && Math.abs(v) <= 180) { const lon = Math.round(v * 100) / 100; onSite({ ...s, lon, tz: guessTz(s.lat, lon) }); } }} /></label></div>}
      {!s.place && <label className="f"><span>{t('sun.tz')}</span><select value={s.tz} onChange={e => onSite({ ...s, tz: e.target.value })}>
        {[...new Set([s.tz, guessTz(s.lat, s.lon), ...TZ_CHOICES])].map(z => <option key={z} value={z}>{z}</option>)}</select></label>}
      {!s.place && <p className="muted sun-note">{t(s.tz.startsWith('Etc/') ? 'sun.tzFixed' : 'sun.tzAuto', { tz: s.tz })}</p>}
      <label className="f"><span>{t('sun.north', { deg: s.northDeg })}</span><input type="range" min={0} max={359} value={s.northDeg} aria-valuetext={t('sun.northAria', { deg: s.northDeg })} onChange={e => onSite({ ...s, northDeg: Number(e.target.value) })} /></label>
      <div className="sun-presets" role="group" aria-label={t('sun.day')}>
        {SUN_PRESETS.map(([k, md]) => <button key={k} className="btn" aria-pressed={ui.date === year + md} onClick={() => onUi({ date: year + md })}>{t(`sun.preset.${k}`)}</button>)}
        <input type="date" aria-label={t('sun.day')} value={ui.date} onChange={e => { if (/^\d{4}-\d{2}-\d{2}$/.test(e.target.value)) onUi({ date: e.target.value }); }} /></div>
      <div className="sun-row">
        <button className="btn icon-btn" aria-label={ui.playing ? t('sun.pause') : t('sun.play')} aria-pressed={ui.playing} onClick={() => onUi({ playing: !ui.playing, ...(ui.playing ? {} : ui.minutes >= set || ui.minutes < rise ? { minutes: rise } : {}) })}>{ui.playing ? '❚❚' : '▶'}</button>
        <label className="f" style={{ flex: 1 }}><span>{t('sun.time', { time: hhmm(ui.minutes) })}</span><input type="range" min={0} max={1435} step={5} value={ui.minutes} aria-valuetext={hhmm(ui.minutes)} onChange={e => onUi({ minutes: Number(e.target.value), playing: false })} /></label></div>
      <label className="sun-check"><input type="checkbox" checked={ui.map} onChange={e => onUi({ map: e.target.checked })} /> {t('sun.map')}</label>
      {ui.map && <div className="sun-layer" role="radiogroup" aria-label={t('sun.layer')}>{(['sun', 'df'] as const).map(k => <label key={k} className="sun-check"><input type="radio" name="sun-layer" checked={ui.layer === k} onChange={() => onUi({ layer: k })} /> {t(`sun.layer.${k}`)}</label>)}</div>}
      {ui.map && ui.layer === 'df' && <div className="sun-legend" aria-label={t('sun.dfLegendAria')}><span>0 %</span><i style={{ background: `linear-gradient(90deg, ${DF_STOPS.map(c => `rgb(${c.join(',')})`).join(', ')})` }} /><span>{DF_MAX} %+</span></div>}
      {ui.map && ui.layer === 'sun' && legendMax != null && <div className="sun-legend" aria-label={t('sun.legendAria', { max: fmtH(Math.max(2, legendMax)) })}><span>0 h</span><i style={{ background: `linear-gradient(90deg, ${HEAT_STOPS.map(c => `rgb(${c.join(',')})`).join(', ')})` }} /><span>{fmtH(Math.max(2, legendMax))}</span></div>}
    </div>
    {now && study && <p className="sun-now" role="status">{now.elevationDeg > 0 ? t('sun.now', { az: Math.round(now.azimuthDeg), el: Math.round(now.elevationDeg) }) : t('sun.below')}
      {' · '}{study.sunrise && study.sunset ? t('sun.dayInfo', { rise: study.sunrise, set: study.sunset, len: fmtH(study.dayLengthH) }) : t('sun.polar')}</p>}
    {extras && <LightCard site={site} extras={extras} />}
    <div className="sun-tabs" role="tablist" aria-label={t('sun.tabs')}>{TABS.map(k => <button key={k} role="tab" id={`sun-tab-${k}`} aria-selected={ui.tab === k} aria-controls="sun-tabpanel"
      className={`sun-tab ${ui.tab === k ? 'is-on' : ''}`} tabIndex={ui.tab === k ? 0 : -1} onClick={() => onUi({ tab: k })}
      onKeyDown={e => { const d = e.key === 'ArrowRight' ? 1 : e.key === 'ArrowLeft' ? -1 : 0; if (!d) return; e.preventDefault(); const n = TABS[(TABS.indexOf(k) + d + TABS.length) % TABS.length]!;
        onUi({ tab: n }); requestAnimationFrame(() => document.getElementById(`sun-tab-${n}`)?.focus()); }}>{t(`sun.tab.${k}`)}</button>)}</div>
    <div id="sun-tabpanel" role="tabpanel" aria-labelledby={`sun-tab-${ui.tab}`} className="sun-tabpanel">
    {ui.tab === 'norm' && <>
    {extras && <DayRecorder date={ui.date} rise={rise} set={set} onUi={onUi} open3d={extras.open3d} />}
    {study && <table className="sun-table"><caption className="sr-only">{t('sun.tableCaption', { date: ui.date })}</caption>
      <thead><tr><th scope="col">{t('sun.col.room')}</th><th scope="col">{t('sun.col.hours')}</th><th scope="col">{t('sun.col.when')}</th>{extras?.gain && <th scope="col" title={t('sun.gainTitle')}>{t('sun.col.gain')}</th>}<th scope="col">{t('sun.col.verdict')}</th></tr></thead>
      <tbody>{study.rooms.map(r => { const ws = study.windows.filter(w => w.roomId === r.roomId), first = ws.map(w => w.first).filter(Boolean).sort()[0], last = ws.map(w => w.last).filter(Boolean).sort().at(-1);
        return <tr key={r.roomId} className={`sun-${r.verdict}`}><th scope="row">{r.name}<small>{ws.length ? [...new Set(ws.map(w => t(`sun.face.${w.facing}`)))].join(', ') : t('sun.noWindows')}</small></th>
          <td>{fmtH(r.hours)}{r.longestH < r.hours ? <small>{t('sun.longest', { h: fmtH(r.longestH) })}</small> : null}</td>
          <td>{first && last ? `${first}–${last}` : '—'}{r.peak && r.floorShare >= .01 ? <small>{t('sun.peak', { time: r.peak, pct: Math.round(r.floorShare * 100) })}</small> : null}</td>
          {extras?.gain && (() => { const g = extras.gain.rooms.find(x => x.roomId === r.roomId); return <td>{g && g.kwh > 0 ? <>{fmtKwh(g.kwhTypical, lang)}<small>{t('sun.clearSky', { k: fmtKwh(g.kwh, lang) })}</small></> : '—'}</td>; })()}
          <td><span className={`sun-badge sun-badge-${r.verdict}`}>{t(`sun.verdict.${r.verdict}`)}</span></td></tr>; })}</tbody></table>}
    {extras && <SunVerdicts site={site} extras={extras} />}
    {extras && <SunRemedies site={site} extras={extras} />}
    {extras && <SunEn17037 extras={extras} />}
    {study && <p className="muted sun-note">{t('sun.norm', { h: fmtH(NORM_HOURS) })}</p>}
    {study && <p className="muted sun-note">{t('sun.method')}</p>}</>}
    {ui.tab === 'nb' && extras && <SunNeighbours site={site} extras={extras} onSite={onSite} />}
    {ui.tab === 'light' && extras && <SunDaylight extras={extras} onLayer={() => onUi({ map: true, layer: 'df' })} />}
    {ui.tab === 'cal' && extras && <SunCalendarView site={site} year={Number(year)} extras={extras} />}
    {ui.tab === 'summer' && extras && <SunSummer site={site} year={year} extras={extras} onSite={onSite} />}
    {ui.tab === 'furn' && extras && <SunOnFurniture site={site} date={ui.date} extras={extras} />}
    {ui.tab === 'furn' && extras && !extras.placements.some(p => ['comodaTv', 'birou', 'pat'].includes(p.group)) && <p className="muted sun-note">{t('sun.pieces.none')}</p>}
    </div>
    {extras && <a className="btn" href={extras.reportHref} target="_blank" rel="noopener">{t('sun.report')}</a>}
    <p className="muted sun-note">{t('sun.limits')}</p>
    <button className="btn ghost" onClick={() => onSite(null)}>{t('sun.disable')}</button>
  </section>);
}
const fmtKwh = (k: number, lang: string) => { const loc = lang === 'en' ? 'en-GB' : 'ro-RO'; return k < .05 ? `< ${(.1).toLocaleString(loc)} kWh` : `${k.toLocaleString(loc, { maximumFractionDigits: 1, minimumFractionDigits: 1 })} kWh`; };

/** Propunerile automate pentru camerele de locuit sub normă: căutare la cerere, previzualizare pe plan și în 3D, aplicare. */
function SunRemedies({ site, extras }: { site: SiteInfo; extras: SunExtras }){
  const { t, lang } = usePrefs();
  const [found, setFound] = useState<Record<string, Remedy[]>>({}), [swaps, setSwaps] = useState<Record<string, UseSwap[]>>({});
  // verdictul pe planul real: în timpul previzualizării lista nu dispare
  const norm = useMemo(() => insolationStudy(extras.base, site, (d, m) => localToUtc(d, m, site.tz), { date: NORM_REFERENCE_DATE, stepMin: 5, floorGrid: false, obs: extras.obs }), [extras.base, site, extras.obs]);
  const low = norm.rooms.filter(r => r.verdict === 'low' || r.verdict === 'none');
  // o geometrie nouă (inclusiv după aplicare) face vechile propuneri depășite
  useEffect(() => { setFound({}); setSwaps({}); }, [extras.base, site, extras.obs]);
  if (!low.length) return norm.rooms.some(r => r.habitable) ? <p className="sun-allok">{t('sun.rem.allOk')}</p> : null;
  const search = (roomId: string) => { const rem = sunRemedies(extras.base, site, (d, m) => localToUtc(d, m, site.tz), roomId, { max: 3, newId: () => `op-${crypto.randomUUID().slice(0, 8)}`, obs: extras.obs }); setFound(f => ({ ...f, [roomId]: rem }));
    // fără nicio variantă care respectă norma: ideea de a muta funcțiunea într-o cameră mai însorită
    if (!rem.some(x => x.meets)){ const sw = swapIdeas(extras.base, site, (d, m) => localToUtc(d, m, site.tz), roomId, extras.obs); setSwaps(x => ({ ...x, [roomId]: sw })); } };
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
  const pieces: PieceSun[] = useMemo(() => sunOnPieces(extras.geo, extras.placements, extras.catalog, site, (d, m) => localToUtc(d, m, site.tz), date, 10, extras.obs), [extras.geo, key, site, date, extras.obs]); // eslint-disable-line react-hooks/exhaustive-deps
  if (!pieces.length) return null;
  const rooms = new Map(extras.geo.rooms.map(r => [r.id, r.name]));
  return (<section className="sun-pieces" aria-label={t('sun.pieces.title')}><h4>{t('sun.pieces.title')}</h4>
    <ul>{pieces.map(p => { const pl = extras.placements.find(x => x.id === p.placementId)!;
      return <li key={p.placementId} className={`sun-piece-${p.effect}`}><b>{extras.pieceName(pl)}</b> · {rooms.get(p.roomId)}<br />
        {t(`sun.pieces.${p.effect}`, { when: p.spans.map(s => `${s.from}–${s.to}`).join(', '), h: fmtH(p.minutes / 60), first: p.spans[0]?.from ?? '' })}</li>; })}</ul>
    <p className="muted sun-note">{t('sun.pieces.note')}</p></section>);
}
const fmtM = (v: number, lang: string) => `${v.toLocaleString(lang === 'en' ? 'en-GB' : 'ro-RO', { maximumFractionDigits: 1 })} m`;
const fmtPct = (v: number | null, lang: string) => v == null ? '—' : `${v.toLocaleString(lang === 'en' ? 'en-GB' : 'ro-RO', { maximumFractionDigits: 1 })} %`;
/** Cele două mesaje distincte cerute de art. 3: declanșatorul studiului (alin. 2, D ≤ H_max) și verificarea de fond (alin. 1). */
function SunVerdicts({ site, extras }: { site: SiteInfo; extras: SunExtras }){
  const { t, lang } = usePrefs(), n = extras.norm; if (!n) return null;
  const hab = n.rooms.filter(r => r.habitable), low = hab.filter(r => r.verdict !== 'ok'), req = extras.triggers.filter(x => x.required).sort((a, b) => a.distM - b.distM);
  const hurt = extras.nb.filter(x => x.status === 'affected');
  return (<div className="sun-verdicts" role="group" aria-label={t('sun.chk.group')}>
    <p className={`sun-verdict ${req.length ? 'is-warn' : ''}`}><b>{t('sun.trig.title')}</b> {!site.neighbors?.length ? t('sun.trig.noNb')
      : req.length ? t('sun.trig.yes', { d: fmtM(req[0]!.distM, lang), h: fmtM(req[0]!.hMaxM, lang), nm: req[0]!.name }) : t('sun.trig.no')}</p>
    <p className={`sun-verdict ${low.length ? 'is-bad' : hab.length ? 'is-ok' : ''}`}><b>{t('sun.chk.title', { h: fmtH(NORM_HOURS) })}</b> {!hab.length ? t('sun.chk.none')
      : low.length ? t('sun.chk.fail', { n: low.length, rooms: low.map(r => r.name).join(', ') }) : t('sun.chk.pass', { n: hab.length })}</p>
    {site.neighbors?.some(x => x.kind === 'home' && !x.calcan) && <p className={`sun-verdict ${hurt.length ? 'is-bad' : 'is-ok'}`}><b>{t('sun.chk.nbTitle')}</b> {hurt.length ? t('sun.chk.nbFail', { list: hurt.map(x => x.name).join(', ') }) : t('sun.chk.nbPass')}</p>}
  </div>);
}
/** Câmp numeric care se aplică la Enter sau la ieșirea din câmp (fiecare aplicare e un pas de anulare). */
function Num({ label, value, min, max, step = .5, onChange }: { label: string; value: number; min: number; max: number; step?: number; onChange(v: number): void }){
  const shown = String(Math.round(value * 10) / 10), [v, setV] = useState(shown); useEffect(() => setV(shown), [shown]);
  const commit = () => { const x = Number(v.replace(',', '.')); if (v.trim() && Number.isFinite(x) && x >= min && x <= max && Math.abs(x - value) > .049) onChange(Math.round(x * 100) / 100); else setV(shown); };
  return <label className="f"><span>{label}</span><input type="number" inputMode="decimal" step={step} min={min} max={max} value={v} onChange={e => setV(e.target.value)} onBlur={commit} onKeyDown={e => { if (e.key === 'Enter') commit(); }} /></label>;
}
const FACES = ['N', 'NE', 'E', 'SE', 'S', 'SW', 'W', 'NW'] as const;
/** Clădirile din jur: volume editabile, efectul lor asupra casei și efectul casei asupra ferestrelor lor. */
function SunNeighbours({ site, extras, onSite }: { site: SiteInfo; extras: SunExtras; onSite(s: SiteInfo | null): void }){
  const { t } = usePrefs(), list = site.neighbors ?? [], box = houseBox(extras.house);
  const set = (n: Neighbour[]) => { const { neighbors: _n, ...rest } = site; onSite(n.length ? { ...rest, neighbors: n } : rest); };
  const upd = (id: string, f: (n: Neighbour) => Neighbour) => set(list.map(x => x.id === id ? f(x) : x));
  const add = () => set([...list, placeNeighbour(extras.house, site, { id: `nb-${crypto.randomUUID().slice(0, 8)}`, name: t('sun.nb.default', { n: list.length + 1 }), trueAz: 180, dist: 8, w: 12, d: 10, h: 12 })]);
  return (<section className="sun-nb" aria-label={t('sun.nb.title')}><h4>{t('sun.nb.title')}</h4><p className="muted sun-note">{t('sun.nb.intro')}</p>
    {list.map(n => { const r = extras.nb.find(x => x.neighbourId === n.id), az = Math.round((((n.rot + site.northDeg) % 360) + 360) % 360 / 45) % 8, D = box ? gap(n, box) : 0;
      // lățimea, adâncimea și direcția se schimbă păstrând distanța liberă față de casă
      const keep = (x: Neighbour) => moveTo(x, extras.house, D);
      return <fieldset key={n.id} className={`sun-nb-item sun-nb-${r?.status ?? 'ok'}`}><legend><input key={n.name} className="sun-nb-name" aria-label={t('sun.nb.name')} defaultValue={n.name} maxLength={60}
          onBlur={e => { const v = e.target.value.trim(); if (v && v !== n.name) upd(n.id, x => ({ ...x, name: v })); else e.target.value = n.name; }} onKeyDown={e => { if (e.key === 'Enter') e.currentTarget.blur(); }} /></legend>
        <div className="sun-row">
          <label className="f"><span>{t('sun.nb.dir')}</span><select value={az} onChange={e => upd(n.id, x => keep({ ...x, rot: (((Number(e.target.value) * 45 - site.northDeg) % 360) + 360) % 360 }))}>{FACES.map((f, i) => <option key={f} value={i}>{t(`sun.face.${f}`)}</option>)}</select></label>
          <Num label={t('sun.nb.dist')} value={D} min={0} max={300} onChange={v => upd(n.id, x => moveTo(x, extras.house, v))} />
          <Num label={t('sun.nb.h')} value={n.h} min={1} max={250} onChange={v => upd(n.id, x => ({ ...x, h: v }))} /></div>
        <div className="sun-row">
          <Num label={t('sun.nb.w')} value={n.w} min={1} max={300} step={1} onChange={v => upd(n.id, x => keep({ ...x, w: v }))} />
          <Num label={t('sun.nb.d')} value={n.d} min={1} max={300} step={1} onChange={v => upd(n.id, x => keep({ ...x, d: v }))} />
          <label className="f"><span>{t('sun.nb.kind')}</span><select value={n.kind} onChange={e => upd(n.id, x => ({ ...x, kind: e.target.value === 'other' ? 'other' : 'home' }))}><option value="home">{t('sun.nb.kind.home')}</option><option value="other">{t('sun.nb.kind.other')}</option></select></label></div>
        <div className="sun-row">
          <label className="sun-check"><input type="checkbox" checked={n.older} onChange={e => upd(n.id, x => ({ ...x, older: e.target.checked }))} /> {t('sun.nb.older')}</label>
          <label className="sun-check"><input type="checkbox" checked={n.calcan} onChange={e => upd(n.id, x => ({ ...x, calcan: e.target.checked }))} /> {t('sun.nb.calcan')}</label></div>
        {r && <p className={`sun-nb-res sun-nb-res-${r.status}`} role="status">{t(`sun.nb.st.${r.status}`, { was: fmtH(r.worstBefore), got: fmtH(r.worstAfter), n: r.affected, of: r.targets.length })}{r.status === 'affected' ? ` ${t(n.older ? 'sun.nb.priorityOld' : 'sun.nb.priorityNew')}` : ''}</p>}
        <button className="btn sm ghost" onClick={() => set(list.filter(x => x.id !== n.id))}>{t('sun.nb.remove')}</button></fieldset>; })}
    {list.length < MAX_NEIGHBOURS && <button className="btn sm" onClick={add}>{t('sun.nb.add')}</button>}
    {list.length > 0 && <p className="muted sun-note">{t('sun.nb.note')}</p>}
  </section>);
}
/** SR EN 17037 (voluntar): expunerea la soare pe 21 martie, niveluri minim / mediu / ridicat. */
function SunEn17037({ extras }: { extras: SunExtras }){
  const { t } = usePrefs(), rs = extras.en?.rooms.filter(r => r.habitable) ?? []; if (!rs.length) return null;
  return (<section className="sun-en" aria-label={t('sun.en.title')}><h4>{t('sun.en.title')}</h4>
    <ul>{rs.map(r => { const L = r.windows ? en17037Level(r.hours) : 0; return <li key={r.roomId}><b>{r.name}</b> {fmtH(r.hours)} <span className={`sun-badge sun-en-${L}`}>{t(`sun.en.l${L}`)}</span></li>; })}</ul>
    <p className="muted sun-note">{t('sun.en.note')}</p></section>);
}
/** Vara: aria de sticlă față de AD O, protecția solară lipsă și comparatorul de umbrire cu prețul în lei și căldura evitată. */
function SunSummer({ site, year, extras, onSite }: { site: SiteInfo; year: string; extras: SunExtras; onSite(s: SiteInfo | null): void }){
  const { t, lang } = usePrefs(), loc = lang === 'en' ? 'en-GB' : 'ro-RO', [open, setOpen] = useState<string | null>(null);
  const sc = useMemo(() => extras.summer ?? summerCheck(extras.geo, site), [extras.summer, extras.geo, site]);
  const july = useMemo(() => solarGain(extras.geo, site, (d, m) => localToUtc(d, m, site.tz), `${year}-07-21`, 10, extras.obs), [extras.geo, site, year, extras.obs]);
  const rooms = sc.rooms.filter(r => r.verdict !== 'na'); if (!rooms.length) return null;
  const setShades = (ids: string[], kind: ShadeKind | null) => { const m = { ...(site.shades ?? {}) }; for (const id of ids){ if (kind) m[id] = kind; else delete m[id]; }
    const { shades: _s, ...rest } = site; onSite(Object.keys(m).length ? { ...rest, shades: m } : rest); };
  const bare = (id: string) => july.windows.find(w => w.openingId === id)?.kwhTypicalBare ?? 0;
  return (<section className="sun-summer" aria-label={t('sun.sum.title')}><h4>{t('sun.sum.title')}</h4><p className="muted sun-note">{t('sun.sum.intro')}</p>
    <label className="f"><span>{t('sun.sum.glazing')}</span><select value={site.glazing ?? 'double'} onChange={e => onSite({ ...site, glazing: e.target.value as GlazingKind })}>
      {(Object.keys(GLAZING) as GlazingKind[]).map(g => <option key={g} value={g}>{t(`sun.glz.${g}`, { g: GLAZING[g].toLocaleString(loc) })}</option>)}</select></label>
    <p className="muted sun-note">{t(sc.cross ? 'sun.sum.cross' : 'sun.sum.single')}</p>
    <table className="sun-table"><caption className="sr-only">{t('sun.sum.title')}</caption>
      <thead><tr><th scope="col">{t('sun.col.room')}</th><th scope="col">{t('sun.sum.glass')}</th><th scope="col" title={t('sun.sum.gainTitle')}>{t('sun.sum.gain')}</th><th scope="col">{t('sun.sum.col')}</th></tr></thead>
      <tbody>{rooms.map(r => { const g = july.rooms.find(x => x.roomId === r.roomId), shaded = sc.windows.filter(w => w.roomId === r.roomId && w.shade);
        return <Fragment key={r.roomId}><tr className={r.verdict === 'over' ? 'sun-low' : 'sun-ok'}><th scope="row">{r.name}<small>{r.orient ? t(`sun.face.${r.orient}`) : ''}</small></th>
          <td>{fmtPct(r.pct, lang)} / {fmtPct(r.limitPct, lang)}<small>{t('sun.sum.glassM2', { m2: r.glassM2.toLocaleString(loc) })}</small></td>
          <td>{g ? fmtKwh(g.kwhTypical, lang) : '—'}</td>
          <td><span className={`sun-badge sun-badge-${r.verdict === 'over' ? 'low' : 'ok'}`}>{t(`sun.sum.v.${r.verdict}`)}</span>{r.unshaded.length > 0 && <small>{t('sun.sum.unshaded', { n: r.unshaded.length })}</small>}</td></tr>
          {(r.unshaded.length > 0 || shaded.length > 0) && <tr className="sun-sum-act"><td colSpan={4}>
            {r.unshaded.length > 0 && <button className="btn sm" aria-expanded={open === r.roomId} onClick={() => setOpen(open === r.roomId ? null : r.roomId)}>{t('sun.sum.options')}</button>}
            {shaded.length > 0 && <span className="muted"> {t('sun.sum.has', { list: [...new Set(shaded.map(w => t(`sun.shade.${w.shade}`)))].join(', ') })} <button className="btn sm ghost" onClick={() => setShades(shaded.map(w => w.openingId), null)}>{t('sun.sum.removeShade')}</button></span>}
            {open === r.roomId && <ul className="sun-shades">{shadeOptions(r, sc.windows, bare).map(o => <li key={o.kind} className={o.meets ? 'is-ok' : ''}>
              <span><b>{t(`sun.shade.${o.kind}`)}</b> · Fc {o.fc.toLocaleString(loc)}{o.fcNote === 'threshold' ? '*' : ''} · {t('sun.sum.saved', { k: fmtKwh(o.kwhSaved, lang) })} · {o.costLei ? t('sun.sum.cost', { a: o.costLei[0].toLocaleString(loc), b: o.costLei[1].toLocaleString(loc), src: o.source ?? '' }) : t('sun.sum.noPrice')}</span>
              <button className="btn sm primary" onClick={() => { setShades(r.unshaded, o.kind); setOpen(null); }}>{t('sun.rem.apply')}</button></li>)}
              <li className="muted sun-note">{t('sun.sum.fcNote')}</li></ul>}</td></tr>}</Fragment>; })}</tbody></table>
    <p className="muted sun-note">{t('sun.sum.note')}</p></section>);
}
const TABS: SunTab[] = ['norm', 'nb', 'light', 'cal', 'summer', 'furn'];
/** Paleta factorului de lumină naturală: de la albastru deschis (0) la albastru închis (5 % și peste). */
export const DF_STOPS = [[226, 238, 248], [120, 172, 222], [24, 78, 150]] as const, DF_MAX = 5;
export function dfColor(df: number): string | null {
  if (df <= .05) return null; const k = Math.min(1, df / DF_MAX), i = k < .5 ? 0 : 1, f = k < .5 ? k * 2 : (k - .5) * 2, a = DF_STOPS[i]!, b = DF_STOPS[i + 1]!;
  return `rgb(${a.map((v, j) => Math.round(v + (b[j]! - v) * f)).join(',')})`;
}
/** Scorul de lumină al nivelului, din rezultatele deja calculate. */
export function scoreOf(extras: Pick<SunExtras, 'norm' | 'en' | 'df' | 'summer' | 'nb'>, site: SiteInfo): LightScore | null {
  if (!extras.norm || !extras.en || !extras.df || !extras.summer) return null;
  return lightScore({ norm: extras.norm.rooms, en: extras.en.rooms, daylight: extras.df.rooms, summer: extras.summer.rooms, nb: site.neighbors?.length ? extras.nb : null });
}
/** Fișa de lumină: clasa A–E (indice Casa3D, nu certificare) și cele cinci verificări din care e făcută. */
function LightCard({ site, extras }: { site: SiteInfo; extras: SunExtras }){
  const { t } = usePrefs(), sc = scoreOf(extras, site); if (!sc?.cls) return null;
  return (<div className={`sun-class sun-class-${sc.cls}`} role="group" aria-label={t('sun.cls.aria', { cls: sc.cls, p: sc.points, max: sc.max })}>
    <div className="sun-class-badge" aria-hidden="true">{sc.cls}</div>
    <div><b>{t('sun.cls.title', { cls: sc.cls })}</b> <span className="muted">{t('sun.cls.points', { p: sc.points, max: sc.max })}</span>{sc.capped && <span className="muted"> · {t('sun.cls.capped')}</span>}
      <ul>{sc.parts.map(p => <li key={p.key} className={!p.known ? 'is-unknown' : p.got === p.max ? 'is-ok' : p.got ? 'is-mid' : 'is-bad'}>{t(`sun.cls.${p.key}`)} {p.got}/{p.max}{!p.known ? ` · ${t('sun.cls.unknown')}` : ''}</li>)}</ul>
      <p className="muted sun-note">{t('sun.cls.note')}</p></div></div>);
}
/** „O zi în casa ta”: înregistrează vederea 3D cât soarele trece de la răsărit la apus (video WebM, în browser). */
function DayRecorder({ date, rise, set, onUi, open3d }: { date: string; rise: number; set: number; onUi(p: Partial<SunUi>): void; open3d(): void }){
  const { t } = usePrefs(), [busy, setBusy] = useState(false), [msg, setMsg] = useState<string | null>(null);
  // o singură înregistrare odată; la demontare (altă filă, alt nivel) bucla se oprește
  const running = useRef(false), alive = useRef(true); useEffect(() => { alive.current = true; return () => { alive.current = false; }; }, []); // repus la montare (StrictMode montează de două ori)
  const wait = (ms: number) => new Promise(r => setTimeout(r, ms));
  const run = async () => {
    if (running.current) return; running.current = true; setBusy(true); setMsg(null);
    let rec: MediaRecorder | null = null, ok = false; const chunks: Blob[] = [];
    try {
      let cv = document.querySelector<HTMLCanvasElement>('.view3d canvas');
      if (!cv){ open3d(); for (let i = 0; i < 30 && !cv && alive.current; i++){ await wait(150); cv = document.querySelector<HTMLCanvasElement>('.view3d canvas'); } }
      if (!cv || typeof cv.captureStream !== 'function' || typeof MediaRecorder === 'undefined'){ if (alive.current) setMsg(t('sun.rec.unsupported')); return; }
      const type = ['video/webm;codecs=vp9', 'video/webm;codecs=vp8', 'video/webm'].find(x => MediaRecorder.isTypeSupported(x));
      rec = new MediaRecorder(cv.captureStream(30), type ? { mimeType: type } : undefined);
      rec.ondataavailable = e => { if (e.data.size) chunks.push(e.data); }; const done = new Promise(r => { rec!.onstop = r; });
      rec.start();
      for (let m = rise; m <= set && alive.current && cv.isConnected; m += 5){ onUi({ minutes: m, playing: false }); await wait(70); }
      ok = alive.current && cv.isConnected; await wait(300);
      if (rec.state !== 'inactive') rec.stop(); await done;
    } catch { if (alive.current) setMsg(t('sun.rec.unsupported')); }
    finally { if (rec && rec.state !== 'inactive') try { rec.stop(); } catch { /* deja oprit */ } running.current = false; if (alive.current) setBusy(false); }
    if (!ok || !chunks.length) return;
    const url = URL.createObjectURL(new Blob(chunks, { type: 'video/webm' })), a = document.createElement('a'); a.href = url; a.download = `casa3d-zi-${date}.webm`; a.click();
    setTimeout(() => URL.revokeObjectURL(url), 10000); if (alive.current) setMsg(t('sun.rec.done'));
  };
  return (<div className="sun-rec"><button className="btn sm" disabled={busy} onClick={run}>{busy ? t('sun.rec.busy') : t('sun.rec.start')}</button>
    {msg && <span className="muted sun-note" role="status">{msg}</span>}</div>);
}
/** Lumina naturală pe cer acoperit: FLN mediu, minim și uniformitatea pe cameră, cu ținta orientativă. */
function SunDaylight({ extras, onLayer }: { extras: SunExtras; onLayer(): void }){
  const { t, lang } = usePrefs(), loc = lang === 'en' ? 'en-GB' : 'ro-RO', rooms = extras.df?.rooms.filter(r => r.target != null || r.mean > 0) ?? [];
  const pct = (v: number) => `${v.toLocaleString(loc, { maximumFractionDigits: 1, minimumFractionDigits: 1 })} %`;
  return (<section className="sun-df" aria-label={t('sun.df.title')}><h4>{t('sun.df.title')}</h4><p className="muted sun-note">{t('sun.df.intro')}</p>
    <button className="btn sm" onClick={onLayer}>{t('sun.df.show')}</button>
    {rooms.length > 0 && <table className="sun-table"><caption className="sr-only">{t('sun.df.title')}</caption>
      <thead><tr><th scope="col">{t('sun.col.room')}</th><th scope="col">{t('sun.df.mean')}</th><th scope="col">{t('sun.df.min')}</th><th scope="col">{t('sun.df.target')}</th><th scope="col">{t('sun.df.col')}</th></tr></thead>
      <tbody>{rooms.map(r => <tr key={r.roomId} className={`sun-${r.verdict}`}><th scope="row">{r.name}<small>{t('sun.df.split', { sky: pct(r.sky), irc: pct(r.irc) })}</small></th>
        <td>{r.verdict === 'none' ? '—' : pct(r.mean)}</td><td>{r.verdict === 'none' ? '—' : pct(r.min)}<small>{r.verdict === 'none' ? '' : t('sun.df.uni', { u: r.uniformity.toLocaleString(loc) })}</small></td>
        <td>{r.target != null ? pct(r.target) : '—'}</td><td><span className={`sun-badge sun-badge-${r.verdict}`}>{t(`sun.df.v.${r.verdict}`)}</span></td></tr>)}</tbody></table>}
    <p className="muted sun-note">{t('sun.df.note')}</p></section>);
}
/** Calendarul anual pe cameră: lună × oră, minutele de soare direct în ziua de 21 a fiecărei luni. */
function SunCalendarView({ site, year, extras }: { site: SiteInfo; year: number; extras: SunExtras }){
  const { t, lang } = usePrefs(), cal = useMemo(() => sunCalendar(extras.geo, site, (d, m) => localToUtc(d, m, site.tz), { year, obs: extras.obs }), [extras.geo, site, year, extras.obs]);
  const [pick, setPick] = useState<string | null>(null), c = cal.find(x => x.roomId === pick) ?? cal.find(x => x.type === 'living' || x.type === 'dormitor') ?? cal[0];
  if (!c) return <p className="muted sun-note">{t('sun.cal.none')}</p>;
  const months = Array.from({ length: 12 }, (_, i) => new Date(Date.UTC(2026, i, 15)).toLocaleDateString(lang === 'en' ? 'en-GB' : 'ro-RO', { month: 'short', timeZone: 'UTC' }));
  // orele de la primul la ultimul soare din an, fără goluri între ele (o coloană lipsă ar sări peste amiază)
  const used = Array.from({ length: 24 }, (_, h) => h).filter(h => cal.some(x => x.grid.some(m => m[h]! > 0))), hrs = used.length ? Array.from({ length: used.at(-1)! - used[0]! + 1 }, (_, i) => used[0]! + i) : [];
  return (<section className="sun-cal" aria-label={t('sun.cal.title')}><h4>{t('sun.cal.title')}</h4>
    <label className="f"><span>{t('sun.col.room')}</span><select value={c.roomId} onChange={e => setPick(e.target.value)}>{cal.map(x => <option key={x.roomId} value={x.roomId}>{x.name}</option>)}</select></label>
    <table className="sun-cal-grid"><caption className="sr-only">{t('sun.cal.caption', { room: c.name })}</caption>
      <thead><tr><th scope="col" />{hrs.map(h => <th key={h} scope="col">{h}</th>)}<th scope="col">{t('sun.cal.total')}</th></tr></thead>
      <tbody>{c.grid.map((m, i) => <tr key={i}><th scope="row">{months[i]}</th>{hrs.map(h => <td key={h} title={`${months[i]} ${h}:00 · ${m[h]} min`} style={{ background: m[h]! ? `rgba(245,146,40,${(.15 + .85 * m[h]! / 60).toFixed(2)})` : undefined }} />)}
        <td className="num">{fmtH(c.hours[i]!)}</td></tr>)}</tbody></table>
    <p className="muted sun-note">{t('sun.cal.note')}</p></section>);
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
export function SunOverlay({ floor, site, ui, grid, nb = [], df = null }: { floor: Floor; site: SiteInfo; ui: SunUi; grid: Study | null; nb?: NeighbourSun[]; df?: Daylight | null }){
  const { t, lang } = usePrefs();
  const now = sunAt(site, ui), patches = now.elevationDeg > 0 ? sunPatches(floor, now.dir) : [];
  const xs = floor.walls.flatMap(w => [w.a[0], w.b[0]]).concat(floor.rooms.flatMap(r => [r.rect.x0, r.rect.x1])), zs = floor.walls.flatMap(w => [w.a[1], w.b[1]]).concat(floor.rooms.flatMap(r => [r.rect.z0, r.rect.z1]));
  const x1 = xs.length ? Math.max(...xs) : 6, z0 = zs.length ? Math.min(...zs) : 0, ax = x1 + .9, az = z0 - .2, rot = site.northDeg;
  const maxH = maxHours(grid);
  const sx = now.dir[0], sz = now.dir[2], sl = Math.hypot(sx, sz) || 1;
  return (<g className="sun-overlay" pointerEvents="none" aria-hidden="true" data-testid="sun-overlay">
    {ui.map && ui.layer === 'df' && df?.cells.map((c, i) => { const col = dfColor(c.df); return col ? <rect key={`d${i}`} x={c.x - df.cellM / 2} y={c.z - df.cellM / 2} width={df.cellM + .005} height={df.cellM + .005} fill={col} fillOpacity={.7} /> : null; })}
    {ui.map && ui.layer === 'df' && df?.rooms.filter(r => r.mean > 0).map(r => { const cs = df.cells.filter(c => c.roomId === r.roomId), x = cs.reduce((a, c) => a + c.x, 0) / cs.length, z = cs.reduce((a, c) => a + c.z, 0) / cs.length;
      return <text key={`t${r.roomId}`} x={x} y={z + .55} textAnchor="middle" fontSize={.3} fontWeight={700} fill="#14365c" fontFamily="Instrument Sans, Segoe UI, sans-serif">{t('sun.dfLabel', { v: r.mean.toLocaleString(lang === 'en' ? 'en-GB' : 'ro-RO', { maximumFractionDigits: 1 }) })}</text>; })}
    {ui.map && ui.layer === 'sun' && grid?.cells.map((c, i) => { const col = heatColor(c.hours, maxH); return col ? <rect key={i} x={c.x - grid.cellM / 2} y={c.z - grid.cellM / 2} width={grid.cellM + .005} height={grid.cellM + .005} fill={col} fillOpacity={.62} /> : null; })}
    {(site.neighbors ?? []).map(n => { const c = corners(n), r = nb.find(x => x.neighbourId === n.id), col = r?.status === 'affected' ? '#C62828' : r?.status === 'already' ? '#B26A00' : n.calcan || n.kind !== 'home' ? '#5E636B' : '#2E7D32';
      return <g key={n.id} data-testid="sun-neighbour"><polygon points={c.map(q => q.join(',')).join(' ')} fill="#8A8F98" fillOpacity={.25} stroke="#5E636B" strokeWidth={.04} strokeDasharray=".25 .12" />
        <line x1={c[0]![0]} y1={c[0]![1]} x2={c[1]![0]} y2={c[1]![1]} stroke={col} strokeWidth={.16} strokeDasharray={n.calcan ? '.35 .18' : undefined} strokeLinecap="round" />
        <text x={n.x} y={n.z} textAnchor="middle" dominantBaseline="middle" fontSize={.42} fontWeight={600} fill="#2B2F36" fontFamily="Instrument Sans, Segoe UI, sans-serif">{`${n.name} · ${Math.round(n.h * 10) / 10} m`}</text></g>; })}
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
