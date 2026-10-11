'use client';
// Prezentarea pentru client, ca planșe 16:9 (pe ecran sau „Salvează ca PDF”): copertă, moodboard, câte o planșă pe cameră
// (imaginea 3D, mostrele de finisaj, piesele), planul și bugetul. Imaginile vin din motorul 3D, randate în afara ecranului.
import { Fragment, useEffect, useMemo, useRef, useState } from 'react';
import type { Catalog, MaterialsCatalog, Snapshot } from '@/core/types';
import { presentation, approvalItems } from '@/core/presentation';
import { lighting, sunDirection } from '@/core/lighting';
import { viewerInput } from '@/lib/viewer-input';
import { floors, levelView, stairwells } from '@/core/levels';
import { floorBounds } from '@/core/dimensions';
import { formatMoney, formatArea } from '@/core/format';
import { usePrefs } from '@/lib/prefs';
import { intlLocale } from '@/lib/i18n';
import { swatchStyle } from './MaterialPicker';
import { PlanSvg } from './PrintView';
import FeedbackBox, { type FeedbackItem } from './FeedbackBox';
import ClientDeliveries from './ClientDeliveries';
import { ClientChanges } from './ChangeOrders';
import RoomChoiceBox, { type ClientChoice } from './RoomChoiceBox';
import type { ClientProgress } from '@/core/client-progress';

const nextFrame = () => new Promise<void>(res => requestAnimationFrame(() => res()));
/** Pe linkul pentru client (`share`) prezentarea vine din revizia partajată și are, sub fiecare planșă de cameră, caseta de păreri. */
export interface SharedDeck { token: string; name: string; snapshot: Snapshot; revisionNumber: number; catalog: Catalog; mc: MaterialsCatalog; progress?: ClientProgress | null }
export default function PresentView({ id, share }: { id?: string; share?: SharedDeck }){
  const { t, lang, units } = usePrefs();
  const [data, setData] = useState<{ snap: Snapshot; name: string; catalog: Catalog; mc: MaterialsCatalog } | null>(null), [err, setErr] = useState(false);
  const [images, setImages] = useState<Record<string, string>>({}), [busy, setBusy] = useState(true), canvasRef = useRef<HTMLCanvasElement>(null);
  useEffect(() => { if (share){ setData({ snap: share.snapshot, name: share.snapshot.name || share.name, catalog: share.catalog, mc: share.mc }); return; }
    Promise.all([fetch(`/api/projects/${id}`).then(r => r.ok ? r.json() : Promise.reject(r)), fetch('/api/catalog').then(r => r.json()), fetch('/api/materials').then(r => r.json())])
      .then(([p, catalog, mc]) => setData({ snap: p.draft, name: p.draft.name || p.name, catalog, mc })).catch(() => setErr(true)); }, [id, share]);
  const [feedback, setFeedback] = useState<FeedbackItem[]>([]);
  useEffect(() => { if (share) fetch(`/api/share/${share.token}/feedback`).then(r => r.ok ? r.json() : []).then(setFeedback).catch(() => {}); }, [share]);
  const added = (f: FeedbackItem) => setFeedback(xs => [...xs, f]);
  const [choices, setChoices] = useState<ClientChoice[]>([]);
  useEffect(() => { if (share) fetch(`/api/share/${share.token}/choices`).then(r => r.ok ? r.json() : []).then(setChoices).catch(() => {}); }, [share]);
  useEffect(() => { if (!data) return; let alive = true, viewer: any = null;
    (async () => { try { const m = await import('./viewer3d-engine.js'); if (!alive || !canvasRef.current) return;
      viewer = m.createViewer(canvasRef.current); const lp = lighting('day'); viewer.setLighting({ ...lp, dir: sunDirection(lp.azimuthDeg, lp.elevationDeg) }); viewer.setQuality?.('high');
      const out: Record<string, string> = {};
      for (let i = 0; i < floors(data.snap).length; i++){ const v = levelView(data.snap, i), { plan, items } = viewerInput(v, data.catalog, undefined, { voids: stairwells(data.snap, i), mc: data.mc }); viewer.setState(plan, items);
        await nextFrame(); await nextFrame(); if (!alive) return;
        if (i === 0){ const ov = viewer.renderView(undefined, 1600, 900); if (ov) out.overview = ov; }
        for (const r of v.floor.rooms){ const u = viewer.renderView({ roomId: r.id }, 1600, 1000); if (u) out[r.id] = u; } }
      if (alive) setImages(out); } catch { /* fără WebGL: planșele rămân fără imagini */ } finally { try { viewer?.dispose(); } catch { /* deja eliberat */ } if (alive) setBusy(false); } })();
    return () => { alive = false; }; }, [data]);
  // randările fotorealiste (Blender, pe acest calculator) au întâietate față de imaginile din motorul 3D
  const [photos, setPhotos] = useState<Record<string, string>>({});
  useEffect(() => { if (!id) return; fetch(`/api/projects/${id}/renders`).then(r => r.ok ? r.json() : null).then(j => { const out: Record<string, string> = {};
    for (const job of (j?.jobs ?? []).filter((x: any) => x.status === 'done').reverse()) for (const f of job.images) out[f.replace(/\.png$/, '')] = `/api/projects/${id}/renders/${job.job}/${f}`;
    setPhotos(out); }).catch(() => {}); }, [id]);
  const imgFor = (roomId: string) => photos[roomId.replace(/[^\w-]/g, '_')] ?? images[roomId];
  const pres = useMemo(() => data ? presentation(data.snap, data.catalog, data.mc) : null, [data]);
  // săgețile / PageDown trec la planșa următoare
  useEffect(() => { const on = (e: KeyboardEvent) => { if (!['ArrowRight', 'ArrowLeft', 'PageDown', 'PageUp'].includes(e.key)) return; const s = [...document.querySelectorAll<HTMLElement>('.slide')];
      const cur = s.findIndex(el => el.getBoundingClientRect().top > -10), k = e.key === 'ArrowRight' || e.key === 'PageDown' ? 1 : -1; s[Math.max(0, Math.min(s.length - 1, cur + k))]?.scrollIntoView({ behavior: 'smooth' }); e.preventDefault(); };
    addEventListener('keydown', on); return () => removeEventListener('keydown', on); }, []);
  const canvas = <canvas ref={canvasRef} width={1600} height={1000} aria-hidden="true" style={{ position: 'fixed', left: -10000, top: 0, width: 1600, height: 1000, pointerEvents: 'none' }} />;
  if (err) return <main className="deck"><p>{t('print.loadError')}</p></main>;
  if (!data || !pres) return <main className="deck">{canvas}<p className="muted">{t('print.loading')}</p></main>;
  const cur = data.catalog.offers[0]?.currency ?? 'RON', money = (v: number | null) => formatMoney(v, cur, lang, '—'), date = new Date().toLocaleDateString(intlLocale(lang), { month: 'long', year: 'numeric' });
  const role = (r: string) => t(`pres.role.${r.split('.')[0]}`), title = (n?: string) => (n ?? '').split(',')[0];
  const fl = floors(data.snap)[0]!, b = floorBounds(fl) ?? { x0: 0, x1: 6, z0: 0, z1: 5 }, box = { x0: b.x0 - .4, z0: b.z0 - .4, w: b.x1 - b.x0 + .8, d: b.z1 - b.z0 + .8 };
  const n = pres.rooms.length + 4, num = (i: number) => `${String(i).padStart(2, '0')} / ${String(n).padStart(2, '0')}`;
  return (<main className="deck">{canvas}
    {share && <p className="fb-intro no-print">{t('fb.intro')}</p>}
    <div className="deck-bar no-print"><span>{busy ? t('print.preparing') : t('pres.keys')}</span><button className="btn primary" onClick={() => print()} disabled={busy}>{t('pres.pdf')}</button></div>
    <section className="slide cover">
      {images.overview ? <img src={images.overview} alt="" /> : <div className="ph" />}
      <div className="cover-txt"><span className="kicker">{t('pres.kicker')}</span><h1>{data.name}</h1><p>{t('pres.coverSub', { rooms: pres.rooms.length, area: formatArea(pres.totalArea, units, lang) })}</p><small>{date}</small></div>
      <footer>{num(1)}</footer></section>
    <section className="slide board"><header><span className="kicker">{t('pres.conceptKicker')}</span><h2>{t('pres.concept')}</h2></header>
      <div className="board-grid">{pres.board.map(g => <div key={g.role} className="board-col"><h3>{t(`pres.board.${g.role}`)}</h3>
        {g.items.slice(0, 6).map((it, i) => <figure key={i}><i style={it.material ? { ...swatchStyle(it.material), backgroundColor: it.color } : { background: it.color }} /><figcaption>{it.material ? title(it.material.name) : it.color}<small>{it.material?.supplier}</small></figcaption></figure>)}</div>)}</div>
      <footer>{num(2)}</footer></section>
    {pres.rooms.map((r, i) => <Fragment key={r.id}><section className="slide room">
      <div className="room-img">{imgFor(r.id) ? <img src={imgFor(r.id)} alt={r.name} /> : <div className="ph" />}</div>
      <div className="room-txt"><span className="kicker">{t(`roomType.${r.type}`)} · {formatArea(r.areaM2, units, lang)}</span><h2>{r.name}</h2>
        <ul className="sw-list">{r.swatches.slice(0, 9).map((s, k) => <li key={k}><i style={s.material ? { ...swatchStyle(s.material), backgroundColor: s.color } : { background: s.color }} /><span><small>{role(s.role)}</small>{s.material ? title(s.material.name) : s.color}</span></li>)}</ul>
        {r.products.length > 0 && <p className="prod">{r.products.slice(0, 6).map(p => p.name).join(' · ')}</p>}</div>
      <footer>{num(i + 3)}</footer></section>
      {share && <RoomChoiceBox token={share.token} roomId={r.id} roomName={r.name} snap={data.snap} cat={data.catalog} mc={data.mc} cur={cur} chosen={choices.find(c => c.roomId === r.id)} onChosen={c => setChoices(xs => [...xs.filter(x => x.roomId !== c.roomId), c])} />}
      {share && <FeedbackBox token={share.token} roomId={r.id} title={`${t('fb.title')} · ${r.name}`} items={feedback.filter(f => f.roomId === r.id)} onAdded={added}
        approvals={approvalItems(r).map(a => { const sw = r.swatches.find(s => `f:${s.key}` === a.key); return { ...a, swatch: sw ? (sw.material ? { ...swatchStyle(sw.material), backgroundColor: sw.color } : { background: sw.color }) : undefined }; })} />}</Fragment>)}
    <section className="slide planslide"><header><span className="kicker">{t('pres.planKicker')}</span><h2>{t('pres.plan')}</h2></header>
      <div className="plan-wrap"><PlanSvg snap={levelView(data.snap, 0)} catalog={data.catalog} box={box} widthMm={box.w * 22} heightMm={box.d * 22} mm={1 / 22} units={units} locale={lang} aria={t('pres.plan')} /></div>
      <footer>{num(n - 1)}</footer></section>
    <section className="slide budgetslide"><header><span className="kicker">{t('pres.budgetKicker')}</span><h2>{t('pres.budget')}</h2></header>
      <div className="bud"><div className="bud-total"><small>{t('pres.total')}</small><b>{money(pres.budget.total)}</b><p>{t('pres.budgetNote', { n: pres.budget.unknown })}</p></div>
        <dl>{Object.entries(pres.budget.categories).filter(([, v]) => v > 0).map(([k, v]) => <div key={k}><dt>{t(`budget.cat.${k}`)}</dt><dd>{money(v)}</dd></div>)}
          {pres.budget.labor > 0 && <div><dt>{t('pres.labour')}</dt><dd>{money(pres.budget.labor)}</dd></div>}<div><dt>{t('pres.contingency')}</dt><dd>{money(pres.budget.contingency)}</dd></div></dl></div>
      <footer>{num(n)}</footer></section>
    {share && <ClientChanges token={share.token} offered={pres.budget.total} cur={cur} />}
    {share?.progress && <ClientDeliveries p={share.progress} />}
    {share && <FeedbackBox token={share.token} roomId={null} title={t('fb.general')} sub={t('fb.generalSub')} items={feedback.filter(f => f.roomId === null)} onAdded={added} />}
  </main>);
}
