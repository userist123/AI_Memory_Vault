'use client';
import dynamic from 'next/dynamic';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { Catalog, Snapshot, Severity, Issue, FurniturePlacement, MaterialsCatalog, BudgetSettings, RoomFinishes, Appearance, Finish, Room, Stair } from '@/core/types';
import BudgetPanel, { type Outbound } from './BudgetPanel';
import { relFor, freshness } from '@/core/outbound';
import DesignPanel from './DesignPanel';
import CatalogPanel from './CatalogPanel';
import TwinDesignPanel from './TwinDesignPanel';
import AdvisorPanel from './AdvisorPanel';
import { adviseProject, type Advice } from '@/core/advisor';
import RevisionDiff from './RevisionDiff';
import TechPanel from './TechPanel';
import FinishesPanel from './FinishesPanel';
import { suggestTechPoints, placeTechPoint, type TechKind } from '@/core/technical';
import { RoomLookPanel, WallLookPanel, OpeningLookPanel, ItemLookPanel } from './AppearanceControls';
import { finishesOf, budgetOf, roomGeometry, computeBudget } from '@/core/boq';
import { appearanceColors } from '@/core/appearance';
import { History } from '@/core/history';
import { usePrefs } from '@/lib/prefs';
import { issueText } from '@/lib/i18n';
import { intlLocale } from '@/lib/i18n';
import PrefsSwitcher from './PrefsSwitcher';
import { formatMoney, formatArea, formatLength, formatDimsCm, cmToInput, inputToCm, lengthInputUnit, catalogCurrency } from '@/core/format';
import { validatePlacement, validateFloor, severityOf } from '@/core/validate';
import { autoLayout, addPlacement } from '@/core/project';
import { resolve, groups, groupOf } from '@/core/catalog';
import { area, r3, wallLength } from '@/core/geometry';
import KeyboardHelp from './KeyboardHelp';
import UnderlayPanel, { type Calib } from './UnderlayPanel';
import { scaleFromPoints, anchorAfterScale } from '@/core/underlay';
import { duplicatePlacement, nudgePlacement, NUDGE_CM, NUDGE_BIG_CM } from '@/core/edit-ops';
import PlanView, { type Tool, type Sel } from './PlanView';
import { floors, levelView, mergeLevel, addLevel, removeLevel, stairIssues, stairGeometry, stairwells, comfortableStairLength, elevationOf, SLAB, MAX_LEVELS } from '@/core/levels';
const Viewer3D = dynamic(() => import('./Viewer3D'), { ssr: false });

const uid = () => crypto.randomUUID();
const ROOM_TYPES = [['living', 'roomType.living'], ['dormitor', 'roomType.dormitor'], ['bucatarie', 'roomType.bucatarie'], ['baie', 'roomType.baie'], ['hol', 'roomType.hol']];
const ICON: Record<Tool, string> = { select: 'M5 3l12 8-6 1 3 7-2 1-3-7-4 4z', wall: 'M3 12h18M3 9v6M21 9v6', room: 'M4 4h16v16H4z', door: 'M5 21V4h9v17M5 21h14M12 12h.01', window: 'M4 5h16v14H4zM12 5v14M4 12h16', measure: 'M3 17L17 3l4 4L7 21zM8 12l2 2M11 9l2 2M14 6l2 2', tech: 'M13 2L4 14h7l-1 8 9-12h-7z' };
const TOOL_LABEL: Record<Tool, string> = { select: 'tool.select', wall: 'tool.wall', room: 'tool.room', door: 'tool.door', window: 'tool.window', measure: 'tool.measure', tech: 'tool.tech' };

export default function Editor({ id }: { id: string }){
  const { t, tp, lang, units } = usePrefs(), iT = (i: Parameters<typeof issueText>[1]) => issueText(lang, i, units), u = lengthInputUnit(units);
  const [house, setHouse] = useState<Snapshot | null>(null), [lvl, setLvl] = useState(0), [catalog, setCatalog] = useState<Catalog | null>(null), [mc, setMc] = useState<MaterialsCatalog | null>(null), [out, setOut] = useState<Outbound | null>(null);
  const [sideOpen, setSideOpen] = useState(true), [sel, setSel] = useState<Sel>(null), [tool, setTool] = useState<Tool>('select'), [view, setView] = useState<'2d' | '3d' | 'split'>('split'), [help, setHelp] = useState(false), [calib, setCalib] = useState<Calib | null>(null);
  const [save, setSave] = useState<'saved' | 'dirty' | 'saving' | 'error'>('saved'), [toast, setToast] = useState(''), [revs, setRevs] = useState<any[]>([]), [rev, setRev] = useState(0);
  const [showTech, setShowTech] = useState(true), [techKind, setTechKind] = useState<TechKind>('outlet_double');
  const [pending, setPending] = useState<{ before: Snapshot; issues: Issue[] } | null>(null), [persistent, setPersistent] = useState(true), [panel, setPanel] = useState<'props' | 'catalog' | 'budget' | 'design' | 'twin' | 'revs' | 'advisor' | 'tech'>('props'), [preview, setPreview] = useState<{ v: any; pid: string } | null>(null), [shares, setShares] = useState<any[]>([]);
  const hist = useRef(new History<Snapshot>()), dragStart = useRef<Snapshot | null>(null), timer = useRef<any>(null), latest = useRef<Snapshot | null>(null), [, force] = useState(0);
  // Se salvează casa întreagă (`house`); editorul lucrează pe nivelul ales (`snap`, o vedere din core/levels.ts),
  // iar fiecare modificare a vederii se pune la loc în casă cu mergeLevel.
  const L = house ? Math.min(lvl, floors(house).length - 1) : 0;
  const snap = useMemo(() => house ? levelView(house, L) : null, [house, L]);
  const say = (t: string) => { setToast(t); clearTimeout((say as any).t); (say as any).t = setTimeout(() => setToast(''), 3500); };

  useEffect(() => { Promise.all([fetch(`/api/projects/${id}`).then(r => r.ok ? r.json() : Promise.reject(r)), fetch('/api/catalog').then(r => r.json()), fetch('/api/health').then(r => r.json()), fetch('/api/materials').then(r => r.json())])
    .then(([p, c, h, m]) => { setHouse(p.draft); latest.current = p.draft; setCatalog(c); setMc(m); setRev(p.currentRevision); setPersistent(h.persistent); }).catch(() => say(t('editor.loadError')));
    fetch('/api/outbound').then(r => r.ok ? r.json() : null).then(setOut).catch(() => {});
    loadRevs(); }, [id]); // eslint-disable-line
  const loadRevs = () => { fetch(`/api/projects/${id}/revisions`).then(r => r.ok ? r.json() : []).then(setRevs); fetch(`/api/projects/${id}/shares`).then(r => r.ok ? r.json() : []).then(setShares); };
  async function shareRevision(n: number){ const r = await fetch(`/api/projects/${id}/shares`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ revision: n }) }); const j = await r.json();
    if (!r.ok){ say(j.error || t('editor.shareFailed')); return; } const url = `${location.origin}${j.path}`; try { await navigator.clipboard.writeText(url); say(t('editor.shareCopied')); } catch { say(t('editor.shareLink', { url })); } loadRevs(); }
  async function revokeShare(token: string){ const r = await fetch(`/api/projects/${id}/shares`, { method: 'DELETE', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ token }) }); if (!r.ok){ say(t('editor.revokeFailed')); return; } say(t('editor.revoked')); loadRevs(); }
  const persist = useCallback((s: Snapshot) => { latest.current = s; setSave('dirty'); clearTimeout(timer.current);
    timer.current = setTimeout(async () => { setSave('saving'); const r = await fetch(`/api/projects/${id}`, { method: 'PUT', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ snapshot: latest.current }) });
      if (r.ok) setSave('saved'); else { setSave('error'); say((await r.json()).error || t('editor.saveFailed')); } }, 700); }, [id]);
  const commitHouse = (h: Snapshot, record = true) => { if (record && house) hist.current.push(house); setHouse(h); persist(h); force(x => x + 1); };
  const commit = (next: Snapshot, record = true) => { if (house) commitHouse(mergeLevel(house, L, next), record); };
  const mutate = (fn: (s: Snapshot) => void) => { if (!snap) return; const n = structuredClone(snap); fn(n); commit(n); };
  // aspect: modifică snap.appearance și șterge intrările rămase goale (un aspect gol = implicit)
  const look = (fn: (a: Appearance) => void) => mutate(s => { const a: Appearance = s.appearance || {}; fn(a);
    for (const k of ['rooms', 'wallFaces', 'openings', 'items'] as const){ const m: any = a[k]; if (!m) continue; for (const id of Object.keys(m)){ const v = m[id]; if (k === 'rooms') for (const part of Object.keys(v)) if (!v[part] || !Object.keys(v[part]).length) delete v[part]; if (!Object.keys(v).length) delete m[id]; } if (!Object.keys(m).length) delete a[k]; }
    if (Object.keys(a).length) s.appearance = a; else delete s.appearance; });
  const setFinish = (map: Record<string, Finish> | undefined, id: string, patch: Partial<Record<keyof Finish, string | null>>): Record<string, Finish> => { const m = { ...(map || {}) }, f: any = { ...(m[id] || {}) };
    for (const [k, v] of Object.entries(patch)) if (v == null) delete f[k]; else f[k] = v; m[id] = f; return m; };
  // schimbarea variantei sau a dimensiunii unei piese trece prin validare: ERROR refuză, WARNING cere confirmare
  const changePiece = (id: string, fn: (q: FurniturePlacement) => void, refused: string) => { if (!snap) return; const n = structuredClone(snap), q = n.placements.find(x => x.id === id)!; fn(q); const iss = validatePlacement(n, catalog!, q);
    if (severityOf(iss) === 'ERROR'){ say(`${refused}: ${iT(iss.find(i => i.severity === 'ERROR')!)}`); return; } commit(n); if (iss.length) setPending({ before: house!, issues: iss }); };

  const stairIss = useMemo(() => house && catalog ? stairIssues(house, catalog) : [], [house, catalog]);
  const issues = useMemo(() => { const m: Record<string, Issue[]> = {}; if (snap && catalog) for (const p of snap.placements) m[p.id] = validatePlacement(snap, catalog, p);
    for (const i of stairIss) if (i.with && m[i.with]) m[i.with].push(i); return m; }, [snap, catalog, stairIss]);
  const sev = useMemo(() => Object.fromEntries(Object.entries(issues).map(([k, v]) => [k, severityOf(v)])) as Record<string, Severity>, [issues]);
  // problemele nivelului: pereți/goluri și scările lui (cele legate de o piesă apar la piesă)
  const floorIssues = useMemo(() => snap ? [...validateFloor(snap.floor), ...stairIss.filter(i => !i.with && snap.floor.stairs?.some(s => s.id === i.vars?.stair))] : [], [snap, stairIss]);
  const advice = useMemo(() => { if (!snap || !catalog) return [] as Advice[]; let budget; if (mc && house){ const b = computeBudget(house, catalog, mc); budget = { total: b.chosen.total, target: b.target, unknown: b.unknownItems.length }; }
    return adviseProject(snap, catalog, { accessibility: !!snap.brief?.accessibility, budget, colorsOf: appearanceColors, currency: catalogCurrency(catalog) }); }, [snap, house, catalog, mc]);
  const showAdvice = (a: Advice) => { const pid = a.refs.placementIds?.[0], wid = a.refs.wallIds?.[0]; if (pid) setSel({ kind: 'placement', id: pid }); else if (a.refs.roomId) setSel({ kind: 'room', id: a.refs.roomId }); else if (wid) setSel({ kind: 'wall', id: wid }); };

  // editare din plan: mutările se validează la final (ERROR → revine, WARNING → cere confirmare)
  const onEdit = (fn: (s: Snapshot) => void, phase: 'start' | 'move' | 'end') => { if (!snap || !catalog) return;
    if (phase === 'start'){ dragStart.current = house; hist.current.push(house!); }
    if (phase === 'end'){ const before = dragStart.current!; dragStart.current = null; const cur = levelView(latest.current!, L);
      if (sel?.kind === 'placement'){ const p = cur.placements.find(x => x.id === sel.id); const iss = p ? validatePlacement(cur, catalog, p) : [];
        if (severityOf(iss) === 'ERROR'){ hist.current.discardLast(); setHouse(before); persist(before); say(t('editor.positionRefused', { msg: iT(iss.find(i => i.severity === 'ERROR')!) })); return; }
        if (severityOf(iss) === 'WARNING'){ setPending({ before, issues: iss }); } }
      return; }
    const base = latest.current || house!, n = structuredClone(levelView(base, L)); fn(n); const h = mergeLevel(base, L, n); latest.current = h; setHouse(h); persist(h); };
  const addUnderlay = (dataUrl: string) => mutate(s => { const xs = s.floor.rooms.flatMap(r => [r.rect.x0, r.rect.x1]), zs = s.floor.rooms.flatMap(r => [r.rect.z0]);
    const x0 = xs.length ? Math.min(...xs) : 0, w = xs.length ? Math.max(...xs) - x0 : 10; s.underlay = { dataUrl, x: r3(x0), z: r3(zs.length ? Math.min(...zs) : 0), widthM: Math.min(100, Math.max(1, r3(w || 10))), opacity: .5, locked: false }; });
  const patchUnderlay = (patch: Partial<NonNullable<Snapshot['underlay']>>, record: boolean) => { if (!snap?.underlay) return; const n = structuredClone(snap); Object.assign(n.underlay!, patch); commit(n, record); };
  const applyCalib = (realCm: number) => { const u = snap?.underlay; if (!snap || !u || calib?.stage !== 'enter' || !calib.a || !calib.b) return false; const w = scaleFromPoints(calib.a, calib.b, realCm, u.widthM); if (w == null) return false;
    const [x, z] = anchorAfterScale(u, calib.a, w); const n = structuredClone(snap); n.underlay = { ...u, widthM: w, x, z }; commit(n); setCalib(null); return true; };
  const undo = () => { if (!house) return; const s = hist.current.undo(house); if (s){ setPending(null); setHouse(s); persist(s); force(x => x + 1); } };
  const redo = () => { if (!house) return; const s = hist.current.redo(house); if (s){ setHouse(s); persist(s); force(x => x + 1); } };
  const del = () => { if (!sel) return; mutate(s => {
    if (sel.kind === 'placement') s.placements = s.placements.filter(p => p.id !== sel.id);
    if (sel.kind === 'wall') s.floor.walls = s.floor.walls.filter(w => w.id !== sel.id);
    if (sel.kind === 'stair'){ s.floor.stairs = (s.floor.stairs || []).filter(x => x.id !== sel.id); if (!s.floor.stairs.length) delete s.floor.stairs; }
    if (sel.kind === 'opening'){ const w = s.floor.walls.find(w => w.id === sel.wallId); if (w) w.openings = w.openings.filter(o => o.id !== sel.id); }
    if (sel.kind === 'room'){ s.floor.rooms = s.floor.rooms.filter(r => r.id !== sel.id); s.placements = s.placements.filter(p => p.roomId !== sel.id); } }); setSel(null); };
  const rotate = (pid: string) => { if (!snap || !catalog) return; const n = structuredClone(snap), p = n.placements.find(x => x.id === pid)!; p.rotation = r3(((p.rotation + Math.PI / 2) % (Math.PI * 2))); p.source = 'manual';
    const iss = validatePlacement(n, catalog, p); if (severityOf(iss) === 'ERROR'){ say(t('editor.rotateNoFit', { msg: iT(iss.find(i => i.severity === 'ERROR')!) })); return; } commit(n); if (iss.length) setPending({ before: house!, issues: iss }); };
  // ---- niveluri și scări ----
  const goLevel = (i: number) => { setLvl(i); setSel(null); setPreview(null); setCalib(null); };
  const levelName = (i: number) => i === 0 ? t('level.ground') : (house && floors(house)[i]?.name) || t('level.n', { n: i });
  const onAddLevel = () => { if (!house) return; const n = floors(house).length; if (n >= MAX_LEVELS){ say(t('level.max', { n: MAX_LEVELS })); return; }
    commitHouse(addLevel(house, { name: t('level.n', { n }) })); goLevel(n); };
  const onRemoveLevel = () => { if (!house || L === 0 || !confirm(t('level.removeConfirm', { name: levelName(L) }))) return; commitHouse(removeLevel(house, L)); goLevel(L - 1); };
  // scara nouă: în mijlocul camerei, pe latura lungă, cât de lungă e confortabil și încape
  const onAddStair = (room: Room) => { if (!house || !snap) return; if (L >= floors(house).length - 1){ say(t('stair.needLevel')); return; }
    const w = room.rect.x1 - room.rect.x0, d = room.rect.z1 - room.rect.z0, alongX = w > d, long = alongX ? w : d, short = alongX ? d : w, rise = snap.floor.ceilingHeight + SLAB;
    const st: Stair = { id: `scara-${uid().slice(0, 8)}`, x: r3((room.rect.x0 + room.rect.x1) / 2), z: r3((room.rect.z0 + room.rect.z1) / 2),
      width: r3(Math.max(.6, Math.min(.9, short - .1))), length: r3(Math.max(.8, Math.min(comfortableStairLength(rise), long - .1))), rotation: alongX ? r3(Math.PI / 2) : 0 };
    mutate(s => { s.floor.stairs = [...(s.floor.stairs || []), st]; }); setSel({ kind: 'stair', id: st.id }); };
  const patchStair = (sid: string, patch: Partial<Stair>) => mutate(s => { const st = s.floor.stairs?.find(x => x.id === sid); if (st) Object.assign(st, patch); });
  const rotateStair = (sid: string) => { const st = snap?.floor.stairs?.find(x => x.id === sid); if (st) patchStair(sid, { rotation: r3((st.rotation + Math.PI / 2) % (Math.PI * 2)) }); };
  const duplicate = (pid: string) => { if (!snap || !catalog) return; const r = duplicatePlacement(snap, catalog, pid, uid());
    if (!r.ok){ say(iT(r)); return; } commit(r.snapshot); setSel({ kind: 'placement', id: r.id }); if (r.issues.length) setPending({ before: house!, issues: r.issues }); };
  const nudge = (pid: string, dx: number, dz: number) => { if (!snap || !catalog) return; const r = nudgePlacement(snap, catalog, pid, dx, dz);
    if (!r.ok){ say(iT(r)); return; } commit(r.snapshot); };
  useEffect(() => { const k = (e: KeyboardEvent) => { if (preview || (e.target as HTMLElement).closest('input,select,textarea')) return;
    if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'z'){ e.preventDefault(); e.shiftKey ? redo() : undo(); }
    else if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'y'){ e.preventDefault(); redo(); }
    else if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'd'){ e.preventDefault(); if (sel?.kind === 'placement') duplicate(sel.id); }
    else if (e.key === '?'){ setHelp(h => !h); }
    else if (e.key.startsWith('Arrow') && sel?.kind === 'placement' && !e.ctrlKey && !e.metaKey && !e.altKey){ e.preventDefault(); const st = e.shiftKey ? NUDGE_BIG_CM : NUDGE_CM;
      nudge(sel.id, e.key === 'ArrowLeft' ? -st : e.key === 'ArrowRight' ? st : 0, e.key === 'ArrowUp' ? -st : e.key === 'ArrowDown' ? st : 0); }
    else if (e.key === 'Delete' || e.key === 'Backspace'){ del(); }
    else if (e.key.toLowerCase() === 'r' && sel?.kind === 'placement') rotate(sel.id);
    else if (e.key.toLowerCase() === 'r' && sel?.kind === 'stair') rotateStair(sel.id);
    else if (e.key === 'Escape'){ setTool('select'); setHelp(false); setCalib(null); } };
    addEventListener('keydown', k); return () => removeEventListener('keydown', k); });

  async function saveRevision(){ if (!house) return; clearTimeout(timer.current); await fetch(`/api/projects/${id}`, { method: 'PUT', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ snapshot: latest.current ?? house }) });
    const note = prompt(t('editor.notePrompt')) ?? ''; const r = await fetch(`/api/projects/${id}/revisions`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ note }) }); const j = await r.json();
    if (!r.ok){ say(`${j.error} ${(j.details || []).slice(0, 2).map((d: any) => iT(d)).join(' ')}`); return; } setRev(j.number); setSave('saved'); say(t('editor.revSaved', { n: j.number })); loadRevs(); }
  async function restore(n: number){ if (!confirm(t('editor.restoreConfirm', { n }))) return;
    const r = await fetch(`/api/projects/${id}/revisions/${n}`, { method: 'POST' }); if (!r.ok){ say(t('editor.restoreFailed')); return; } const s = await r.json(); hist.current.push(house!); setHouse(s); latest.current = s; setSel(null); say(t('editor.restored', { n })); }

  if (!snap || !catalog || !mc) return <div className="home"><p className="muted">{toast || t('editor.loading')}</p></div>;
  const G = groups(catalog);
  const onAddWall = (a: [number, number], b: [number, number]) => mutate(s => { s.floor.walls.push({ id: `wall-${uid().slice(0, 8)}`, a, b, thickness: .15, exterior: false, openings: [] }); });
  const onAddRoom = (rect: any) => { const rid = `camera-${uid().slice(0, 8)}`; mutate(s => { s.floor.rooms.push({ id: rid, name: t('editor.newRoomName', { n: s.floor.rooms.length + 1 }), type: 'living', rect }); }); setSel({ kind: 'room', id: rid }); setTool('select'); };
  const onAddOpening = (wallId: string, offset: number, kind: 'door' | 'window') => { const w = snap.floor.walls.find(x => x.id === wallId)!, L = wallLength(w.a, w.b), width = kind === 'door' ? .8 : 1.2;
    if (L < width + .1){ say(t('editor.wallTooShort')); return; } const oid = `op-${uid().slice(0, 8)}`;
    mutate(s => { s.floor.walls.find(x => x.id === wallId)!.openings.push({ id: oid, kind, offset: r3(Math.max(0, Math.min(L - width, offset - width / 2))), width }); }); setSel({ kind: 'opening', id: oid, wallId }); };
  const total = snap.placements.reduce((a, p) => a + (resolve(catalog, p.variantId)?.offer?.price || 0), 0);
  const selPl = sel?.kind === 'placement' ? snap.placements.find(p => p.id === sel.id) : undefined, selRoom = sel?.kind === 'room' ? snap.floor.rooms.find(r => r.id === sel.id) : undefined;
  const shown = (c: Snapshot): Snapshot => c.levels?.length || !house!.levels?.length ? levelView(c, Math.min(L, floors(c).length - 1)) : levelView({ ...house!, placements: c.placements }, L);
  // golurile scărilor de dedesubt, cu direcția de urcare (latura de sosire rămâne fără balustradă în 3D)
  const voids = stairwells(house!, L);
  const selStair = sel?.kind === 'stair' ? snap.floor.stairs?.find(s => s.id === sel.id) : undefined, nLevels = floors(house!).length;
  const selWall = sel?.kind === 'wall' ? snap.floor.walls.find(w => w.id === sel.id) : undefined, selOp = sel?.kind === 'opening' ? snap.floor.walls.find(w => w.id === sel.wallId)?.openings.find(o => o.id === sel.id) : undefined;
  // câmpurile numerice sunt în cm (metric) sau inci (imperial); `f` și `min` primesc mereu cm, datele rămân metrice
  // în inci, minimul afișat (ex. 78,7″ pentru 200 cm) se întoarce puțin sub pragul în cm: acceptăm o jumătate de inci toleranță și fixăm la minim
  const num = (v: string, f: (x: number) => void, min = 0) => { const x = v.trim() === '' ? 0 : inputToCm(v, units), tol = units === 'imperial' ? 1.27 : 1e-9; if (x != null && x >= min - tol) f(Math.max(x, min)); };
  const cur = catalogCurrency(catalog), money = (v: number | null | undefined) => formatMoney(v, cur, lang, t('common.unknownPrice')), when = (d: string) => new Date(d).toLocaleString(intlLocale(lang)), inField = (m: number) => cmToInput(m * 100, units);

  return (<div className="ws">
    {!persistent && <div className="banner">{t('editor.demoBanner')}</div>}
    <header className="topbar">
      <a className="btn" href="/">{t('editor.projects')}</a>
      <input className="name" value={snap.name} aria-label={t('editor.projectName')} maxLength={120} onChange={e => { const n = { ...house!, name: e.target.value }; setHouse(n); persist(n); }} />
      <span className={`status ${save === 'error' ? 'err' : ''}`}>{save === 'saved' ? t('editor.saved') : save === 'saving' ? t('editor.saving') : save === 'dirty' ? t('editor.dirty') : t('editor.saveError')} · {rev ? t('editor.revisionN', { n: rev }) : t('editor.noRevisions')}</span>
      <button className="btn" onClick={undo} disabled={!hist.current.canUndo} title="Ctrl+Z">↶ {t('editor.undo')}</button>
      <button className="btn" onClick={redo} disabled={!hist.current.canRedo} title="Ctrl+Y">↷ {t('editor.redo')}</button>
      <div className="btn" role="group" aria-label={t('editor.view')} style={{ padding: 2, gap: 2 }}>
        {(['2d', 'split', '3d'] as const).map(vv => <button key={vv} className="btn" style={{ minHeight: 30, border: 0, background: view === vv ? 'var(--graphite)' : 'transparent', color: view === vv ? '#fff' : undefined }} onClick={() => { setView(vv); if (vv === '3d' && typeof matchMedia === 'function' && matchMedia('(max-width: 900px)').matches) setSideOpen(false); }}>{vv === '2d' ? t('editor.view2d') : vv === '3d' ? '3D' : t('editor.viewSplit')}</button>)}
      </div>
      <a className="btn" href={`/p/${id}/print`} target="_blank" rel="noopener">{t('editor.print')}</a>
      <a className="btn" href={`/api/projects/${id}/export.dxf?lang=${lang}`} download title={t('editor.exportDxf')}>{t('editor.exportDxf')}</a>
      <PrefsSwitcher />
      <button className="btn primary" onClick={saveRevision}>{t('editor.saveRevision')}</button>
    </header>
    <div className="body">
      <nav className="tools" aria-label={t('editor.tools')}>
        {(Object.keys(ICON) as Tool[]).map(tk => <button key={tk} aria-pressed={tool === tk} onClick={() => setTool(tk)} title={t(TOOL_LABEL[tk])}><svg viewBox="0 0 24 24"><path d={ICON[tk]} /></svg>{t(TOOL_LABEL[tk])}</button>)}
      </nav>
      <div className={`canvas ${view === 'split' ? 'split' : ''}`}>
        <div className="levelbar" role="tablist" aria-label={t('level.tabs')}>
          {floors(house!).map((_, i) => <button key={i} role="tab" aria-selected={i === L} className="btn" onClick={() => goLevel(i)}>{levelName(i)}</button>)}
          <button className="btn" onClick={onAddLevel} title={t('level.addTitle')} disabled={nLevels >= MAX_LEVELS}>{t('level.add')}</button></div>
        {view !== '3d' && <div style={{ position: 'relative', minHeight: 0 }}><PlanView showTech={showTech && !preview} onTechAt={(x, z) => { if (!snap || preview) return; const pt = placeTechPoint(snap, techKind, x, z); if (!pt){ say(t('tech.outside')); return; } mutate(s => { s.tech = [...(s.tech || []), pt]; }); say(t('tech.placed', { rn: snap.floor.rooms.find(r => r.id === pt.roomId)?.name ?? '' })); }} key={(preview ? 'p' + preview.v.tier : 'live') + L} voids={voids} snap={preview ? shown(preview.v.candidate) : snap} catalog={catalog} sel={preview ? null : sel} tool={preview ? 'select' : tool} severities={preview ? {} : sev} onSelect={preview ? () => {} : setSel} onEdit={preview ? () => {} : onEdit} onAddWall={preview ? () => {} : onAddWall} onAddRoom={preview ? () => {} : onAddRoom} onAddOpening={preview ? () => {} : onAddOpening} calib={preview ? null : calib} onCalibPick={(a, b) => setCalib({ stage: 'enter', a, b })} />
          <div className="hintbar">{tool === 'wall' ? t('editor.hintWall') : tool === 'room' ? t('editor.hintRoom') : tool === 'door' || tool === 'window' ? t('editor.hintOpening') : tool === 'measure' ? t('editor.hintMeasure') : t('editor.hintSelect')}</div></div>}
        {view !== '2d' && <Viewer3D voids={voids} mc={mc} snap={preview ? shown(preview.v.candidate) : snap} catalog={catalog} onPick={pid => !preview && pid && setSel({ kind: 'placement', id: pid })} />}
        {preview && <div className="previewbar" role="status">{t('editor.previewBar', { title: preview.v.title })} <button className="btn" onClick={() => setPreview(null)}>{t('common.close')}</button></div>}
        {help && <KeyboardHelp onClose={() => setHelp(false)} />}
        {pending && <div className="pending" role="alertdialog" aria-label={t('editor.warnings')}>
          <strong>{t('editor.positionWarnings')}</strong>{pending.issues.map((i, k) => <div key={k} className={`issue ${i.severity}`}>{iT(i)}</div>)}
          <div style={{ display: 'flex', gap: 8 }}><button className="btn primary" onClick={() => setPending(null)}>{t('editor.keepPosition')}</button><button className="btn" onClick={() => { setHouse(pending.before); persist(pending.before); setPending(null); }}>{t('editor.revert')}</button></div></div>}
      </div>
      <aside className={`side ${sideOpen ? '' : 'closed'}`}>
        <div className="sidetabs" style={{ display: 'flex', gap: 6, alignItems: 'center', flexWrap: 'wrap' }}>
        <div className="btn" role="tablist" style={{ padding: 2, gap: 2, justifySelf: 'start', flexWrap: 'wrap', height: 'auto', maxWidth: '100%', minWidth: 0 }}>
          {([['props', 'tab.props'], ['catalog', 'tab.catalog'], ['budget', 'tab.budget'], ['design', 'tab.design'], ['twin', 'tab.twin'], ['revs', 'tab.revs'], ['advisor', 'tab.advisor'], ['tech', 'tab.tech']] as const).map(([k, l]) => <button key={k} role="tab" aria-selected={panel === k} className="btn" style={{ minHeight: 30, border: 0, background: panel === k ? 'var(--graphite)' : 'transparent', color: panel === k ? '#fff' : undefined }} onClick={() => { setPanel(k); setSideOpen(true); }}>{t(l)}{k === 'advisor' && advice.filter(a => a.severity !== 'TIP').length > 0 && <span className="badge" style={{ marginLeft: 4, background: '#B7791F', color: '#fff', borderRadius: 8, padding: '0 6px', fontSize: 11 }}>{advice.filter(a => a.severity !== 'TIP').length}</span>}</button>)}
        </div>
          <button className="btn sidetoggle" aria-expanded={sideOpen} onClick={() => setSideOpen(o => !o)}>{sideOpen ? t('editor.hidePanel') : t('editor.showPanel')}</button>
        </div>
        {panel === 'props' && <>
          {floorIssues.filter(i => !(sel?.kind === 'stair' && i.vars?.stair === sel.id)).map((i, k) => <div key={k} className={`issue ${i.severity}`}>{iT(i)}</div>)}
          {!sel && <>
            <h3>{snap.name}{nLevels > 1 ? ` · ${levelName(L)}` : ''}</h3>
            {L > 0 && <div style={{ display: 'grid', gap: 6 }}>
              <label className="f"><span>{t('level.name')}</span><input value={snap.floor.name} maxLength={60} onChange={e => mutate(s => { s.floor.name = e.target.value; })} /></label>
              <div className="prov">{t('level.elevation', { h: formatLength(elevationOf(house!, L), units, lang) })}</div>
              <button className="btn" onClick={onRemoveLevel}>{t('level.remove')}</button></div>}
            <div className="prov">{tp('editor.summaryRooms', snap.floor.rooms.length)} · {formatArea(snap.floor.rooms.reduce((a, r) => a + area(r.rect), 0), units, lang)} · {tp('editor.summaryPieces', snap.placements.length)} · {t('editor.summaryFurniture', { total: money(total) })} · {t('editor.summaryBudget')}</div>
            <label className="f"><span>{t('editor.ceilingHeight', { u })}</span><input type="number" value={inField(snap.floor.ceilingHeight)} onChange={e => num(e.target.value, x => mutate(s => { s.floor.ceilingHeight = r3(x / 100); }), 200)} /></label>
            <button className="btn" onClick={() => { if (!confirm(t('editor.autoConfirm'))) return; const r = autoLayout(snap, catalog); commit(r.snapshot); say(r.notFit.length ? t('editor.notFit', { list: r.notFit.map(n => t('editor.notFitItem', { key: n.key, room: n.room })).join(', ') }) : t('editor.autoApplied')); }}>{t('editor.autoAll')}</button>
            {L > 0 ? <p className="muted" style={{ margin: 0, fontSize: 12.5 }}>{t('level.underlayGround')}</p> : <UnderlayPanel underlay={snap.underlay} calib={calib} say={say} onAdd={addUnderlay} onPatch={patchUnderlay} onRemove={() => { mutate(s => { delete s.underlay; }); setCalib(null); }}
              onCalibStart={() => setCalib({ stage: 'pick' })} onCalibCancel={() => setCalib(null)} onCalibApply={applyCalib} />}
            <p className="muted" style={{ margin: 0, fontSize: 12.5 }}>{t('editor.selectHint')}</p>
          </>}
          {selRoom && <RoomPanel room={selRoom} snap={snap} G={G} cat={catalog} mc={mc} cur={cur} onFinish={(patch: Partial<RoomFinishes>) => mutate(s => { s.finishes = { ...(s.finishes || {}), [selRoom.id]: { ...finishesOf(s, selRoom), ...patch } }; })} onChange={(fn: (r: any) => void) => mutate(s => fn(s.floor.rooms.find(r => r.id === selRoom.id)!))}
            onAuto={() => { const r = autoLayout(snap, catalog, { roomId: selRoom.id }); commit(r.snapshot); say(r.notFit.length ? t('editor.someNotFit') : t('editor.roomAutoApplied', { room: selRoom.name })); }}
            onAdd={(vid: string) => { const r = addPlacement(snap, catalog, selRoom.id, vid); if (!r){ say(t('editor.noFreeSpot')); return; } commit(r); setSel({ kind: 'placement', id: r.placements.at(-1)!.id }); }} num={num} />}
          {selRoom && <button className="btn" title={t('stair.addTitle')} onClick={() => onAddStair(selRoom)}>{t('stair.add')}</button>}
          {selStair && (() => { const g = stairGeometry(selStair, snap.floor.ceilingHeight + SLAB), cm1 = (m: number) => Math.round(m * 1000) / 10; return (<div style={{ display: 'grid', gap: 8 }}>
            <h3>{t('stair.title')}</h3>
            <div className="prov">{t('stair.info', { steps: g.steps, riser: cm1(g.riser), going: cm1(g.going), to: levelName(L + 1) })}</div>
            {stairIss.filter(i => i.vars?.stair === selStair.id && !i.with).map((i, k) => <div key={k} className={`issue ${i.severity}`}>{iT(i)}</div>)}
            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 8 }}>
              <label className="f"><span>{t('stair.width', { u })}</span><input type="number" value={inField(selStair.width)} onChange={e => num(e.target.value, x => patchStair(selStair.id, { width: r3(Math.min(300, x) / 100) }), 60)} /></label>
              <label className="f"><span>{t('stair.length', { u })}</span><input type="number" value={inField(selStair.length)} onChange={e => num(e.target.value, x => patchStair(selStair.id, { length: r3(Math.min(800, x) / 100) }), 80)} /></label></div>
            <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}><button className="btn" onClick={() => rotateStair(selStair.id)}>{t('stair.rotate')} (R)</button><button className="btn" onClick={del}>{t('stair.delete')}</button></div></div>); })()}
          {selRoom && <RoomLookPanel room={selRoom} snap={snap} onRoom={(part, hex) => look(a => { const r: any = { ...(a.rooms?.[selRoom.id] || {}) }; if (hex) r[part] = { ...(r[part] || {}), color: hex }; else delete r[part]; a.rooms = { ...(a.rooms || {}), [selRoom.id]: r }; })}
            onAllWalls={hex => look(a => { a.rooms = { ...(a.rooms || {}) }; for (const r of snap.floor.rooms) a.rooms[r.id] = { ...(a.rooms[r.id] || {}), walls: { color: hex } }; })} />}
          {selWall && <>
            <h3>{t('editor.wall')}</h3>
            <div className="grid2">
              <label className="f"><span>{t('editor.length', { u })}</span><input type="number" value={inField(wallLength(selWall.a, selWall.b))} onChange={e => num(e.target.value, x => mutate(s => { const w = s.floor.walls.find(w => w.id === selWall.id)!, L = wallLength(w.a, w.b), ux = (w.b[0] - w.a[0]) / L, uz = (w.b[1] - w.a[1]) / L; w.b = [r3(w.a[0] + ux * x / 100), r3(w.a[1] + uz * x / 100)]; }), 20)} /></label>
              <label className="f"><span>{t('editor.thickness', { u })}</span><input type="number" value={inField(selWall.thickness)} onChange={e => num(e.target.value, x => mutate(s => { s.floor.walls.find(w => w.id === selWall.id)!.thickness = r3(x / 100); }), 5)} /></label>
            </div>
            <label className="f" style={{ display: 'flex', gap: 8, alignItems: 'center' }}><input type="checkbox" checked={selWall.exterior} onChange={e => mutate(s => { s.floor.walls.find(w => w.id === selWall.id)!.exterior = e.target.checked; })} /> {t('editor.exteriorWall')}</label>
            <div className="prov">{tp('editor.openingsDrag', selWall.openings.length)}</div>
            <WallLookPanel wall={selWall} snap={snap} onFace={(rid, hex) => look(a => { a.wallFaces = setFinish(a.wallFaces, `${selWall.id}@${rid}`, { color: hex }); })} />
            <button className="btn danger" onClick={del}>{t('editor.deleteWall')}</button>
          </>}
          {selOp && <>
            <h3>{selOp.kind === 'door' ? t('tool.door') : t('tool.window')}</h3>
            <div className="grid2">
              <label className="f"><span>{t('editor.width', { u })}</span><input type="number" value={inField(selOp.width)} onChange={e => num(e.target.value, x => mutate(s => { s.floor.walls.find(w => w.id === (sel as any).wallId)!.openings.find(o => o.id === selOp.id)!.width = r3(x / 100); }), 30)} /></label>
              <label className="f"><span>{t('editor.offsetOnWall', { u })}</span><input type="number" value={inField(selOp.offset)} onChange={e => num(e.target.value, x => mutate(s => { s.floor.walls.find(w => w.id === (sel as any).wallId)!.openings.find(o => o.id === selOp.id)!.offset = r3(x / 100); }))} /></label>
            </div>
            <OpeningLookPanel op={selOp} snap={snap} num={num} onPatch={patch => mutate(s => { Object.assign(s.floor.walls.find(w => w.id === (sel as any).wallId)!.openings.find(o => o.id === selOp.id)!, patch); })}
              onFrame={hex => look(a => { a.openings = setFinish(a.openings, selOp.id, { color: hex }); })} />
            <button className="btn danger" onClick={del}>{t('editor.deleteOpening')}</button>
          </>}
          {selPl && <PlacementPanel out={out} p={selPl} snap={snap} catalog={catalog} issues={issues[selPl.id] || []} G={G} num={num}
            onVariant={vid => changePiece(selPl.id, q => { q.variantId = vid; }, t('editor.variantNoFit'))}
            onMove={(x, z) => { const n = structuredClone(snap), q = n.placements.find(p => p.id === selPl.id)!; q.x = r3(x); q.z = r3(z); q.source = 'manual'; const iss = validatePlacement(n, catalog, q);
              if (severityOf(iss) === 'ERROR'){ say(t('editor.positionRefused', { msg: iT(iss.find(i => i.severity === 'ERROR')!) })); return; } commit(n); }}
            onRotate={() => rotate(selPl.id)} onDuplicate={() => duplicate(selPl.id)} onDelete={del} />}
          {selPl && <ItemLookPanel p={selPl} snap={snap} catalog={catalog} model={resolve(catalog, selPl.variantId)?.product.model3d ?? ''} num={num}
            onItem={patch => look(a => { a.items = setFinish(a.items, selPl.id, patch); })}
            onSize={size => changePiece(selPl.id, q => { if (size) q.size = size; else delete q.size; }, t('editor.sizeNoFit'))}
            onVariant={vid => changePiece(selPl.id, q => { q.variantId = vid; }, t('editor.variantNoFit'))} />}
        </>}
        {panel === 'design' && <DesignPanel id={id} snap={snap} say={say} cur={cur} onPreview={(v: any, pid: string | null) => setPreview(v && pid ? { v, pid } : null)} onApplied={(s: Snapshot, n: number) => { hist.current.push(house!); setHouse(s); latest.current = s; setRev(n); setSave('saved'); setSel(null); }} />}
        {panel === 'twin' && <TwinDesignPanel id={id} snap={snap} catalog={catalog} say={say} onPreview={(s: Snapshot | null, label: string | null) => setPreview(s && label ? { v: { candidate: s, title: label, tier: 'twin-' + label }, pid: 'twin' } : null)} onApplied={(s: Snapshot, n: number) => { hist.current.push(house!); setHouse(s); latest.current = s; setRev(n); setSave('saved'); setSel(null); loadRevs(); }} />}
        {panel === 'catalog' && <CatalogPanel locale={lang} units={units} catalog={catalog} snap={snap} onUse={(g, vid) => mutate(s => { s.selections[g] = vid; if (!s.picked.includes(g)) s.picked.push(g); })} onAdd={selRoom ? (vid: string) => { const r = addPlacement(snap, catalog, selRoom.id, vid); if (!r){ say(t('editor.noFreeSpot')); return; } commit(r); setSel({ kind: 'placement', id: r.placements.at(-1)!.id }); } : undefined} />}
        {panel === 'tech' && <TechPanel snap={snap} show={showTech} kind={techKind} onShow={setShowTech} onKind={k => { setTechKind(k); setTool('tech'); }}
          onSuggest={() => { const pts = suggestTechPoints(snap); mutate(s => { s.tech = pts; }); setShowTech(true); say(t('tech.suggested', { n: pts.length })); }}
          onClear={() => mutate(s => { delete s.tech; })} onDelete={id => mutate(s => { s.tech = (s.tech || []).filter(p => p.id !== id); })} />}
        {panel === 'advisor' && <AdvisorPanel lang={lang} units={units} currency={cur} advice={advice} rooms={snap.floor.rooms} onShow={showAdvice} />}
        {panel === 'budget' && <BudgetPanel snap={house!} catalog={catalog} mc={mc} out={out} onBudget={(patch: Partial<BudgetSettings>) => mutate(s => { s.budget = { ...budgetOf(s), ...patch }; })} />}
        {panel === 'revs' && <div className="revs"><h3>{t('tab.revs')}</h3>{revs.length === 0 && <p className="muted">{t('editor.noRevsYet')}</p>}
          {revs.map(r => <div key={r.number} className="r"><span>{t('editor.revisionN', { n: r.number })}{r.note ? ` · ${r.note}` : ''}</span><button className="btn" onClick={() => restore(r.number)}>{t('editor.revert')}</button><button className="btn" onClick={() => shareRevision(r.number)} title={t('editor.shareTitle')}>{t('editor.share')}</button><small>{when(r.created_at)}</small></div>)}
          <RevisionDiff id={id} revs={revs} lang={lang} units={units} />
          {shares.filter(s => !s.revokedAt).length > 0 && <><h4>{t('editor.activeShares')}</h4>{shares.filter(s => !s.revokedAt).map(s => <div key={s.token} className="r"><span>{t('editor.revisionN', { n: s.revisionNumber })}</span><a className="btn" href={s.path} target="_blank" rel="noopener">{t('common.open')}</a><button className="btn danger" onClick={() => revokeShare(s.token)}>{t('editor.revoke')}</button><small>{when(s.createdAt)}</small></div>)}</>}</div>}
      </aside>
    </div>
    {toast && <div className="toast" role="status">{toast}</div>}
  </div>);
}

function RoomPanel({ room, snap, G, cat, mc, cur, onFinish, onChange, onAuto, onAdd, num }: any){
  const { t, tp, lang, units } = usePrefs(), iT = (i: Parameters<typeof issueText>[1]) => issueText(lang, i, units), u = lengthInputUnit(units), inField = (m: number) => cmToInput(m * 100, units);
  const [grp, setGrp] = useState(Object.keys(G).find(k => !G[k].includedWith)!), [vi, setVi] = useState(0);
  const w = room.rect.x1 - room.rect.x0, d = room.rect.z1 - room.rect.z0;
  return (<>
    <h3>{room.name}</h3><div className="prov">{formatArea(area(room.rect), units, lang)} · {tp('editor.pieces', snap.placements.filter((p: any) => p.roomId === room.id).length)}</div>
    <div className="grid2">
      <label className="f"><span>{t('editor.name')}</span><input type="text" value={room.name} maxLength={60} onChange={e => onChange((r: any) => { r.name = e.target.value; })} /></label>
      <label className="f"><span>{t('editor.type')}</span><select value={room.type} onChange={e => onChange((r: any) => { r.type = e.target.value; })}>{ROOM_TYPES.map(([v, l]) => <option key={v} value={v}>{t(l)}</option>)}</select></label>
      <label className="f"><span>{t('editor.width', { u })}</span><input type="number" value={inField(w)} onChange={e => num(e.target.value, (x: number) => onChange((r: any) => { r.rect.x1 = r3(r.rect.x0 + x / 100); }), 60)} /></label>
      <label className="f"><span>{t('editor.length', { u })}</span><input type="number" value={inField(d)} onChange={e => num(e.target.value, (x: number) => onChange((r: any) => { r.rect.z1 = r3(r.rect.z0 + x / 100); }), 60)} /></label>
      <label className="f"><span>{t('editor.posX', { u })}</span><input type="number" value={inField(room.rect.x0)} onChange={e => { const cm = inputToCm(e.target.value, units), x = cm == null ? NaN : cm / 100; if (Number.isFinite(x)) onChange((r: any) => { r.rect.x1 = r3(x + (r.rect.x1 - r.rect.x0)); r.rect.x0 = r3(x); }); }} /></label>
      <label className="f"><span>{t('editor.posZ', { u })}</span><input type="number" value={inField(room.rect.z0)} onChange={e => { const cm = inputToCm(e.target.value, units), z = cm == null ? NaN : cm / 100; if (Number.isFinite(z)) onChange((r: any) => { r.rect.z1 = r3(z + (r.rect.z1 - r.rect.z0)); r.rect.z0 = r3(z); }); }} /></label>
    </div>
    <p className="prov" style={{ margin: 0 }}>{t('editor.roomWallsNote')}</p>
    <FinishesPanel room={room} snap={snap} cat={cat} mc={mc} cur={cur} onFinish={onFinish} />
    <button className="btn" onClick={onAuto}>{t('editor.autoRoom')}</button>
    <h4>{t('editor.addFurniture')}</h4>
    <select value={grp} onChange={e => { setGrp(e.target.value); setVi(0); }}>{Object.entries<any>(G).filter(([, g]) => !g.includedWith).map(([k, g]) => <option key={k} value={k}>{g.label}</option>)}</select>
    <select value={vi} onChange={e => setVi(Number(e.target.value))}>{G[grp].variants.map((v: any, i: number) => <option key={v.id} value={i}>{v.name}</option>)}</select>
    <button className="btn primary" onClick={() => onAdd(G[grp].variants[vi].id)}>{t('editor.addToRoom')}</button>
  </>);
}
function PlacementPanel({ out, p, snap, catalog, issues, G, num, onVariant, onMove, onRotate, onDuplicate, onDelete }: { out: Outbound | null; p: FurniturePlacement; snap: Snapshot; catalog: Catalog; issues: Issue[]; G: any; num: any; onVariant(v: string): void; onMove(x: number, z: number): void; onRotate(): void; onDuplicate(): void; onDelete(): void }){
  const { t, tp, lang, units } = usePrefs(), iT = (i: Parameters<typeof issueText>[1]) => issueText(lang, i, units), u = lengthInputUnit(units), cur = catalogCurrency(catalog), lei = (v: number) => formatMoney(v, cur, lang), inField = (m: number) => cmToInput(m * 100, units);
  const rv = resolve(catalog, p.variantId)!, room = snap.floor.rooms.find(r => r.id === p.roomId), g = G[groupOf(p.variantId)];
  return (<>
    <h3>{rv.variant.name}</h3>
    <div className="prov">{g.label} · {room?.name} · {rv.offer ? lei(rv.offer.price) : t('common.unknownPrice')}</div>
    {rv.offer && <div className="prov">{t('editor.provenance', { source: rv.offer.provenance.source ?? '', date: rv.offer.provenance.verifiedAt ?? '', conf: rv.offer.provenance.confidence ?? '', dimConf: rv.variant.dimensionsConfidence, stock: rv.offer.availability === 'UNKNOWN' ? t('common.unknown') : rv.offer.availability })}{freshness(rv.offer.provenance.verifiedAt).stale ? ` · ${t('editor.priceRecheck')}` : ''}</div>}
    {issues.length ? issues.map((i, k) => <div key={k} className={`issue ${i.severity}`}>{iT(i)}</div>) : <div className="issue PASS">{t('editor.positionValid')}</div>}
    <div className="grid2">
      <label className="f"><span>{t('editor.xField', { u })}</span><input type="number" value={inField(p.x)} onChange={e => num(e.target.value, (x: number) => onMove(x / 100, p.z), -1e9)} /></label>
      <label className="f"><span>{t('editor.zField', { u })}</span><input type="number" value={inField(p.z)} onChange={e => num(e.target.value, (z: number) => onMove(p.x, z / 100), -1e9)} /></label>
    </div>
    <div style={{ display: 'flex', gap: 8 }}><button className="btn" onClick={onRotate}>{t('editor.rotate')}</button><button className="btn" onClick={onDuplicate} title="Ctrl+D">{t('editor.duplicate')}</button>{rv.offer && out?.targets[`o:${rv.offer.id}`] && <a className="btn" href={`/go/o/${rv.offer.id}`} target="_blank" rel={relFor(out.targets[`o:${rv.offer.id}`])}>{t('editor.viewProduct')}{out.targets[`o:${rv.offer.id}`] === 'affiliate' ? ` ${t('editor.affiliateTag')}` : ''}</a>}<button className="btn danger" onClick={onDelete}>{t('common.delete')}</button></div>
    <h4>{t('editor.variantsOnly', { n: g.variants.length })}</h4>
    {g.variants.map((v: any) => { const o = catalog.offers.find(x => x.variantId === v.id), dm = v.dimensionsCm;
      return <button key={v.id} className="var" aria-pressed={v.id === p.variantId} onClick={() => onVariant(v.id)}><span>{v.name}</span><span className="mono">{o ? lei(o.price) : '—'}</span><small>{dm ? formatDimsCm(dm, units, lang) : t('common.unknownDims')}{v.dimensionsConfidence === 'MEDIUM' ? ` ${t('common.approx')}` : ''}</small></button>; })}
  </>);
}

