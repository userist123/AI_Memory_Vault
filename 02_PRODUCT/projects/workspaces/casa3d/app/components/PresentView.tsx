'use client';
// Prezentarea pentru client, ca planșe 16:9 (pe ecran sau „Salvează ca PDF”): copertă, moodboard, câte o planșă pe cameră
// (imaginea 3D, mostrele de finisaj, piesele), planul și bugetul. Imaginile vin din motorul 3D, randate în afara ecranului.
import { Fragment, useEffect, useMemo, useRef, useState } from 'react';
import { DeckSkeleton } from './Skeleton';
import type { Catalog, MaterialsCatalog, Snapshot } from '@/core/types';
import { presentation, approvalItems } from '@/core/presentation';
import { lighting, sunDirection } from '@/core/lighting';
import { viewerInput } from '@/lib/viewer-input';
import { floors, levelView, stairwells } from '@/core/levels';
import { floorBounds } from '@/core/dimensions';
import { formatMoney, formatArea } from '@/core/format';
import { usePrefs } from '@/lib/prefs';
import LoadError from './LoadError';
import { intlLocale } from '@/lib/i18n';
import { swatchStyle } from './MaterialPicker';
import { PlanSvg } from './PrintView';
import FeedbackBox, { type FeedbackItem } from './FeedbackBox';
import PinLayer from './PinLayer';
import ClientDeliveries from './ClientDeliveries';
import { ClientChanges } from './ChangeOrders';
import RoomChoiceBox, { type ClientChoice } from './RoomChoiceBox';
import type { ClientProgress } from '@/core/client-progress';
import { FULL_W, renderSize, presentQuality, isMobileEnv, yieldToBrowser, goodEnough } from '@/lib/present-render';
import { useClientNameBox } from './ClientNameBox';
import { askReview } from '@/lib/dialog';
import { cleanClientName } from '@/lib/client-name';
import './present.css';
import { localizeSnapshotRoomNames } from '@/core/templates';
import { feedbackLog } from '@/core/feedback-log';

const nextFrame = () => new Promise<void>(res => requestAnimationFrame(() => res()));
/** Pe linkul pentru client (`share`) prezentarea vine din revizia partajată și are, sub fiecare planșă de cameră, caseta de păreri. */
export interface SharedDeck { token: string; name: string; snapshot: Snapshot; revisionNumber: number; catalog: Catalog; mc: MaterialsCatalog; progress?: ClientProgress | null }
export default function PresentView({ id, share }: { id?: string; share?: SharedDeck }){
  const { t, lang, units } = usePrefs();
  const [data, setData] = useState<{ snap: Snapshot; name: string; catalog: Catalog; mc: MaterialsCatalog } | null>(null), [err, setErr] = useState(false);
  const [images, setImages] = useState<Record<string, { url: string; w: number }>>({}), [queued, setQueued] = useState(0), [printing, setPrinting] = useState(false), [noGL, setNoGL] = useState(false), canvasRef = useRef<HTMLCanvasElement>(null);
  useEffect(() => { if (share){ setData({ snap: share.snapshot, name: share.snapshot.name || share.name, catalog: share.catalog, mc: share.mc }); return; }
    Promise.all([fetch(`/api/projects/${id}`).then(r => r.ok ? r.json() : Promise.reject(r)), fetch('/api/catalog').then(r => r.json()), fetch('/api/materials').then(r => r.json())])
      .then(([p, catalog, mc]) => setData({ snap: p.draft, name: p.draft.name || p.name, catalog, mc })).catch(() => setErr(true)); }, [id, share]);
  // numele implicite ale camerelor în limba cititorului („Kitchen” → „Bucătărie”), doar la afișare; numele personalizate rămân
  useEffect(() => { setData(d => { if (!d) return d; const snap = localizeSnapshotRoomNames(d.snap, lang); return snap === d.snap ? d : { ...d, snap }; }); }, [data, lang]);
  const [feedback, setFeedback] = useState<FeedbackItem[]>([]);
  useEffect(() => { if (share) fetch(`/api/share/${share.token}/feedback`).then(r => r.ok ? r.json() : []).then(setFeedback).catch(() => {}); }, [share]);
  // serverul poate întoarce un rând existent (produs deja aprobat): nu se adaugă de două ori
  const added = (f: FeedbackItem) => setFeedback(xs => xs.some(x => x.id === f.id) ? xs : [...xs, f]);
  // designerul deschide /share/<token>#pin-<id>: pinul apare după încărcarea părerilor, deci se caută atunci
  const pinCount = feedback.filter(f => f.anchor).length;
  useEffect(() => { if (!share || !pinCount) return; const h = location.hash; if (!/^#pin-[0-9a-f-]{36}$/i.test(h)) return;
    const el = document.getElementById(h.slice(1)); if (el){ el.scrollIntoView({ block: 'center' }); el.focus({ preventScroll: true }); } }, [share, pinCount, data]);
  const nameBox = useClientNameBox(share?.token ?? ''), [bulk, setBulk] = useState<{ busy: boolean; msg: string | null }>({ busy: false, msg: null });
  const [choices, setChoices] = useState<ClientChoice[]>([]);
  useEffect(() => { if (share) fetch(`/api/share/${share.token}/choices`).then(r => r.ok ? r.json() : []).then(setChoices).catch(() => {}); }, [share]);
  // Imaginile 3D se fac la cerere, câte una (un singur motor WebGL, un lanț de lucrări): o planșă intră în ecran → imaginea ei,
  // la rezoluția ecranului; între două imagini firul principal e lăsat liber. Pe link / telefon fără ambient occlusion.
  // „Salvează ca PDF” cere toate imaginile la rezoluția completă (1600) înainte de tipărire.
  const eng = useRef({ viewer: null as any, level: -1, gen: 0, chain: Promise.resolve() as Promise<void>, done: {} as Record<string, number>, queued: new Set<string>() });
  const mobile = useMemo(() => typeof window !== 'undefined' && isMobileEnv(window), []);
  const levelOf = useMemo(() => { const m: Record<string, number> = { overview: 0 }; if (data) for (let i = 0; i < floors(data.snap).length; i++) for (const r of levelView(data.snap, i).floor.rooms) m[r.id] = i; return m; }, [data]);
  useEffect(() => { const e = eng.current; e.gen++; e.level = -1; e.done = {}; e.queued.clear(); setQueued(0); setImages({});
    return () => { e.gen++; const v = e.viewer; e.viewer = null; e.level = -1; e.chain = e.chain.then(() => { try { v?.dispose(); } catch { /* deja eliberat */ } }); }; }, [data]);
  const ensureViewer = async () => { const e = eng.current; if (e.viewer) return e.viewer; if (noGL) return null;
    try { const m = await import('./viewer3d-engine.js'); if (!canvasRef.current) return null; e.viewer = m.createViewer(canvasRef.current); const lp = lighting('day');
      e.viewer.setLighting({ ...lp, dir: sunDirection(lp.azimuthDeg, lp.elevationDeg) }); e.viewer.setQuality?.(presentQuality({ share: !!share, mobile })); return e.viewer; }
    catch { setNoGL(true); return null; } };   // fără WebGL: planșele rămân fără imagini
  // imaginea se cere în forma locului ei din planșă (lățime × înălțime reale), ca motorul 3D să încadreze camera pentru acel format
  const shoot = (key: string, cssWidth: number, print = false, cssHeight?: number): Promise<void> => { const e = eng.current, g = e.gen, d = data;
    const size = renderSize({ cssWidth, cssHeight, dpr: typeof devicePixelRatio === 'number' ? devicePixelRatio : 1, print, overview: key === 'overview' }), w = size.w, tag = `${key}@${w}x${size.h}`;
    if (!d || goodEnough(e.done[key], w) || e.queued.has(tag)) return e.chain;
    e.queued.add(tag); setQueued(n => n + 1);
    e.chain = e.chain.then(async () => { try { if (g !== e.gen || goodEnough(e.done[key], w)) return; const v = await ensureViewer(); if (!v || g !== e.gen) return;
        const L = levelOf[key] ?? 0;
        if (e.level !== L){ const lv = levelView(d.snap, L), { plan, items } = viewerInput(lv, d.catalog, undefined, { voids: stairwells(d.snap, L), mc: d.mc }); v.setState(plan, items); e.level = L;
          await nextFrame(); await nextFrame(); if (g !== e.gen) return; }
        const url = key === 'overview' ? v.renderView(undefined, size.w, size.h) : v.renderView({ roomId: key }, size.w, size.h);
        if (url && g === e.gen){ e.done[key] = w; setImages(m => ({ ...m, [key]: { url, w } })); } }
      catch { /* o cameră care nu se poate randa rămâne cu locul gol */ }
      finally { e.queued.delete(tag); if (g === e.gen) setQueued(n => Math.max(0, n - 1)); await yieldToBrowser(); } });
    return e.chain; };
  // randările fotorealiste (Blender, pe acest calculator) au întâietate față de imaginile din motorul 3D
  const [photos, setPhotos] = useState<Record<string, string>>({});
  useEffect(() => { if (!id) return; fetch(`/api/projects/${id}/renders`).then(r => r.ok ? r.json() : null).then(j => { const out: Record<string, string> = {};
    for (const job of (j?.jobs ?? []).filter((x: any) => x.status === 'done').reverse()) for (const f of job.images) out[f.replace(/\.png$/, '')] = `/api/projects/${id}/renders/${job.job}/${f}`;
    setPhotos(out); }).catch(() => {}); }, [id]);
  const photoFor = (roomId: string) => photos[roomId.replace(/[^\w-]/g, '_')];
  const imgFor = (roomId: string) => photoFor(roomId) ?? images[roomId]?.url;
  // fiecare planșă cu imagine 3D cere imaginea abia când se apropie de ecran
  useEffect(() => { if (!data) return; const els = [...document.querySelectorAll<HTMLElement>('[data-shot]')];
    if (typeof IntersectionObserver === 'undefined'){ for (const el of els){ const b = el.getBoundingClientRect(); shoot(el.dataset.shot!, b.width, false, b.height); } return; }
    const io = new IntersectionObserver(es => { for (const en of es) if (en.isIntersecting){ io.unobserve(en.target); const el = en.target as HTMLElement; shoot(el.dataset.shot!, en.boundingClientRect.width || el.clientWidth, false, en.boundingClientRect.height || el.clientHeight); } }, { rootMargin: '400px 0px' });
    for (const el of els) io.observe(el); return () => io.disconnect(); }, [data, photos]); // eslint-disable-line
  async function printDeck(){ if (printing || !data) return; setPrinting(true);
    try { const keys = ['overview', ...floors(data.snap).flatMap((_, i) => levelView(data.snap, i).floor.rooms.map(r => r.id)).filter(k => !photoFor(k))];
      for (const k of keys) shoot(k, FULL_W, true); await eng.current.chain; await nextFrame(); await nextFrame(); }
    finally { setPrinting(false); } print(); }
  const busy = queued > 0 || printing;
  // linkul clientului: câte produse sunt aprobate din total (ultima părere pe fiecare produs) și „Aprobă tot”, cameră cu cameră
  // decizia pe un element se caută în camera lui: același material (aceeași cheie) apare în mai multe camere
  const lastOn = (roomId: string, key: string) => [...feedback].reverse().find(f => f.roomId === roomId && f.item?.key === key);
  const pendingByRoom = useMemo(() => !share || !data ? [] : presentation(data.snap, data.catalog, data.mc).rooms.map(r => ({ id: r.id, keys: approvalItems(r).map(a => a.key) }))
    .map(r => ({ ...r, todo: r.keys.filter(k => lastOn(r.id, k)?.verdict !== 'like') })), [share, data, feedback]); // eslint-disable-line
  const apTotal = pendingByRoom.reduce((a, r) => a + r.keys.length, 0), apDone = apTotal - pendingByRoom.reduce((a, r) => a + r.todo.length, 0);
  // „Aprobați tot”: clientul vede exact ce aprobă (pe camere), poate scoate excepții și confirmă cu numele; fiecare decizie
  // intră în istoric cu nume și dată (Mydoma/Houzz: aprobarea per element, cu jurnal datat)
  async function approveAll(){ if (!share || bulk.busy || !pres) return;
    const rooms = pendingByRoom.filter(x => x.todo.length), total = rooms.reduce((a, r) => a + r.todo.length, 0);
    const labelOf = new Map(pres.rooms.flatMap(r => approvalItems(r).map(a => [`${r.id}|${a.key}`, a.label] as const))), nameOf = new Map(pres.rooms.map(r => [r.id, r.name] as const));
    const res = await askReview({ title: t('fb.allTitle'), body: t('fb.allBody', { n: total }), confirmLabel: t('fb.approveAllOk'),
      input: { label: t('fb.name'), maxLength: 60, minLength: 2, initial: nameBox.name ?? cleanClientName(nameBox.draft) ?? '' },
      checklist: rooms.map(r => ({ heading: nameOf.get(r.id) ?? r.id, items: r.todo.map(k => ({ key: `${r.id}|${k}`, label: labelOf.get(`${r.id}|${k}`) ?? k })) })) });
    if (!res) return; const author = nameBox.save(cleanClientName(res.text)); if (!author) return;
    const chosen = new Set(res.keys); setBulk({ busy: true, msg: null });
    try { for (const r of rooms){ const items = r.todo.filter(k => chosen.has(`${r.id}|${k}`)); if (!items.length) continue;
        const resp = await fetch(`/api/share/${share.token}/feedback`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ roomId: r.id, author, items: items.map(key => ({ key })) }) });
        const j = await resp.json().catch(() => null); if (!resp.ok){ setBulk({ busy: false, msg: j?.error || t('fb.error') }); return; } setFeedback(xs => [...xs, ...(j as FeedbackItem[])]); }
      setBulk({ busy: false, msg: chosen.size === total ? t('fb.allApproved') : t('fb.someApproved', { n: chosen.size, total }) }); }
    catch { setBulk({ busy: false, msg: t('fb.error') }); } }
  const toComments = () => { const el = document.getElementById('fb-general'); el?.scrollIntoView({ behavior: 'smooth', block: 'start' }); el?.querySelector<HTMLElement>('[role=radio]')?.focus({ preventScroll: true }); };
  const pres = useMemo(() => data ? presentation(data.snap, data.catalog, data.mc) : null, [data]);
  // săgețile / PageDown trec la planșa următoare
  useEffect(() => { const on = (e: KeyboardEvent) => { if (!['ArrowRight', 'ArrowLeft', 'PageDown', 'PageUp'].includes(e.key)) return; const s = [...document.querySelectorAll<HTMLElement>('.slide')];
      const cur = s.findIndex(el => el.getBoundingClientRect().top > -10), k = e.key === 'ArrowRight' || e.key === 'PageDown' ? 1 : -1; s[Math.max(0, Math.min(s.length - 1, cur + k))]?.scrollIntoView({ behavior: 'smooth' }); e.preventDefault(); };
    addEventListener('keydown', on); return () => removeEventListener('keydown', on); }, []);
  const canvas = <canvas ref={canvasRef} width={1600} height={1000} aria-hidden="true" style={{ position: 'fixed', left: -10000, top: 0, width: 1600, height: 1000, pointerEvents: 'none' }} />;
  if (err) return <LoadError />;
  if (!data || !pres) return <main className="deck">{canvas}<DeckSkeleton label={t('print.loading')} /></main>;
  const cur = data.catalog.offers[0]?.currency ?? 'RON', money = (v: number | null) => formatMoney(v, cur, lang, '—'), date = new Date().toLocaleDateString(intlLocale(lang), { month: 'long', year: 'numeric' });
  const role = (r: string) => t(`pres.role.${r.split('.')[0]}`), title = (n?: string) => (n ?? '').split(',')[0];
  const fl = floors(data.snap)[0]!, b = floorBounds(fl) ?? { x0: 0, x1: 6, z0: 0, z1: 5 }, box = { x0: b.x0 - .4, z0: b.z0 - .4, w: b.x1 - b.x0 + .8, d: b.z1 - b.z0 + .8 };
  const n = pres.rooms.length + 4, num = (i: number) => `${String(i).padStart(2, '0')} / ${String(n).padStart(2, '0')}`;
  return (<main className="deck">{canvas}
    {share && <p className="fb-intro no-print">{t('fb.intro')}</p>}
    <div className="deck-bar no-print"><span>{busy ? t('print.preparing') : t('pres.keys')}</span><button className="btn primary" onClick={printDeck} disabled={printing}>{t('pres.pdf')}</button></div>
    <section className="slide cover">
      <div className="shot" data-shot="overview">{images.overview ? <img src={images.overview.url} alt="" /> : <div className={noGL ? 'ph' : 'ph ph-wait'} role="img" aria-label={t('pres.renderWait')} />}
        {share && <PinLayer token={share.token} surface="overview" roomId={null} pins={feedback.filter(f => f.anchor?.surface === 'overview')} onAdded={added} />}</div>
      <div className="cover-txt"><span className="kicker">{t('pres.kicker')}</span>
        <div className="cover-main"><h1>{data.name}</h1><p>{t('pres.coverSub', { rooms: pres.rooms.length, area: formatArea(pres.totalArea, units, lang) })}</p></div>
        {pres.palette.length > 0 && <div className="cover-pal" aria-label={t('pres2.palette')}>{pres.palette.map((c, i) => <i key={i} style={c.material ? { ...swatchStyle(c.material), backgroundColor: c.color } : { background: c.color }} />)}<small>{t('pres2.palette')}</small></div>}
        <dl className="cover-facts"><div><dt>{t('pres2.rooms')}</dt><dd>{pres.rooms.length}</dd></div><div><dt>{t('pres2.area')}</dt><dd>{formatArea(pres.totalArea, units, lang)}</dd></div><div><dt>{t('pres2.prepared')}</dt><dd>{date}</dd></div></dl></div>
      <footer>{num(1)}</footer></section>
    <section className="slide pboard"><header><span className="kicker">{t('pres.conceptKicker')}</span><h2>{t('pres.concept')}</h2></header>
      {pres.palette.length > 0 && <div className="pb-strip" role="img" aria-label={t('pres2.palette')}>{pres.palette.map((c, i) => <i key={i} style={c.material ? { ...swatchStyle(c.material), backgroundColor: c.color } : { background: c.color }} />)}</div>}
      <div className="pb-grid">{pres.board.map(g => <div key={g.role} className="pb-col"><h3>{t(`pres.board.${g.role}`)}</h3>
        {g.items.slice(0, 4).map((it, i) => <figure key={i}><i style={it.material ? { ...swatchStyle(it.material), backgroundColor: it.color } : { background: it.color }} /><figcaption>{it.material ? title(it.material.name) : it.color}<small>{it.material?.supplier}</small></figcaption></figure>)}</div>)}</div>
      <div className="pb-rooms"><h3>{t('pres2.byRoom')}</h3><ul>{pres.rooms.slice(0, 8).map(r => <li key={r.id}><b>{r.name}</b><span>{r.swatches.slice(0, 4).map((s, k) => <i key={k} title={s.material ? title(s.material.name) : s.color} style={s.material ? { ...swatchStyle(s.material), backgroundColor: s.color } : { background: s.color }} />)}</span></li>)}</ul></div>
      <footer>{num(2)}</footer></section>
    {pres.rooms.map((r, i) => <Fragment key={r.id}><section className="slide room">
      <div className="room-img" data-shot={photoFor(r.id) ? undefined : r.id}>{imgFor(r.id) ? <img src={imgFor(r.id)} alt={r.name} /> : <div className={noGL ? 'ph' : 'ph ph-wait'} role="img" aria-label={t('pres.renderWait')} />}
        {share && <PinLayer token={share.token} surface="room" roomId={r.id} pins={feedback.filter(f => f.anchor?.surface === 'room' && f.roomId === r.id)} onAdded={added} />}</div>
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
    {share && <FeedbackBox id="fb-general" token={share.token} roomId={null} title={t('fb.general')} sub={t('fb.generalSub')} items={feedback.filter(f => f.roomId === null)} onAdded={added} />}
    {/* istoricul deciziilor: cine, ce și când (trasabilitate pentru client și designer); cele mai noi primele */}
    {share && feedback.length > 0 && <details className="fb-log no-print"><summary>{t('fb.logTitle', { n: feedback.length })}</summary>
      <ol>{feedbackLog(feedback, rid => (rid ? pres?.rooms.find(r => r.id === rid)?.name : null) ?? t('fb.general')).slice(0, 200).map(e => <li key={e.id}>
        <time dateTime={e.at}>{new Date(e.at).toLocaleString(intlLocale(lang), { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' })}</time>
        <span><b>{e.author}</b> · {e.verdict === 'like' ? t('fb.logLiked') : e.verdict === 'change' ? t('fb.logChange') : t('fb.logQuestion')} · {e.count > 1
          ? t('fb.logGroup', { n: e.count, rooms: e.rooms.map(r => `${r.name} ${r.count}`).join(', ') })
          : `${e.rooms[0]!.name}${e.label ? ` · ${e.label}` : ''}`}{e.text ? `: „${e.text}”` : ''}</span></li>)}</ol></details>}
    {share && apTotal > 0 && <div className="decide-bar no-print" role="region" aria-label={t('fb.decideAria')}>
      <div className="decide-progress"><b>{t('fb.progress', { n: apDone, total: apTotal })}</b><span className="decide-track" aria-hidden="true"><i style={{ width: `${Math.round(apDone / apTotal * 100)}%` }} /></span>
        {bulk.msg && <small role="status">{bulk.msg}</small>}</div>
      <div className="decide-act"><button type="button" className="btn lg" onClick={toComments}>{t('fb.toComments')}</button>
        <button type="button" className="btn primary lg" disabled={bulk.busy || apDone === apTotal} onClick={approveAll}>{apDone === apTotal ? t('fb.allDone') : t('fb.approveAll')}</button></div></div>}
  </main>);
}
