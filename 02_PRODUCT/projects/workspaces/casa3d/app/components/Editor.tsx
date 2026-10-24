'use client';
import StylePanel from './StylePanel';
import TraceabilityPanel from './TraceabilityPanel';
import DesignOptionsLab from './DesignOptionsLab';
import ProjectHealthPanel from './ProjectHealthPanel';
import ImpactModal from './ImpactModal';
import { computeDesignImpact, type DesignImpact } from '@/core/impact-map';
import { evaluateProjectHealth, type HealthAction } from '@/core/project-health';
import { EditorSkeleton } from './Skeleton';
import dynamic from 'next/dynamic';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { Catalog, Snapshot, Severity, Issue, FurniturePlacement, MaterialsCatalog, BudgetSettings, RoomFinishes, Appearance, Finish, Room, Stair, WallFeature, WallFeatureKind, WallItem, WallItemKind, Niche, Annotation, DoorChoice, DoorType, Opening } from '@/core/types';
import { type Outbound } from './BudgetPanel';
import BudgetWithCustom from './CustomItemsImport';
import { relFor, freshness } from '@/core/outbound';
import DesignPanel from './DesignPanel';
import CatalogPanel from './CatalogPanel';
import TwinDesignPanel from './TwinDesignPanel';
import AdvisorPanel from './AdvisorPanel';
import { adviseProject, type Advice } from '@/core/advisor';
import RevisionDiff from './RevisionDiff';
import TechPanel from './TechPanel';
import FinishesPanel from './FinishesPanel';
import RoomOptionsPanel from './RoomOptionsPanel';
import MaterialPicker from './MaterialPicker';
import { downloadSchedule } from './FinishSchedule';
import { finishSchedule } from '@/core/finish-schedule';
import { WALL_ITEM_KINDS, WALL_ITEM_CATEGORY, wallItemDefaults, furnitureBelow, wallItemFurnitureIssues } from '@/core/wall-items';
import { materialOf, insertBand, sideSpan, FEATURE_CATEGORY, featureMarks } from '@/core/finishes';
import { doorIssues, doorSwing, doorTypeOf, isSlidingType } from '@/core/doors';
import { DOOR_TYPES, DOOR_TYPE_RULES, WALL_ITEM_RULES } from '@/core/rules';
import { normalizeHex } from '@/core/appearance';
import { footprintOf } from '@/core/validate';
import { suggestTechPoints, placeTechPoint, TECH_KINDS, techIssues, techBodyCount, MAX_TECH_POINTS, type TechKind, type TechPoint } from '@/core/technical';
import { defaultZoneMaterial, intersect, zoneAreaIn, removeRoomZones } from '@/core/floor-zones';
import { wallStatus } from '@/core/renovation';
import type { FloorZone, WallStatus, CeilingZone, Railing, RoomSpaceKind } from '@/core/types';
import { ceilingZoneRect, ceilingZoneQty, removeRoomCeilingZones } from '@/core/ceiling-zones';
import { railingHeight, railingKind, railingLength } from '@/core/railings';
import { RAILING_KINDS, SPACE_KINDS } from '@/core/plan-limits';
import { RoomLookPanel, WallLookPanel, OpeningLookPanel, ItemLookPanel } from './AppearanceControls';
import { finishesOf, budgetOf, roomGeometry, computeBudget } from '@/core/boq';
import { appearanceColors } from '@/core/appearance';
import { History, type HistoryMark } from '@/core/history';
import { usePrefs } from '@/lib/prefs';
import { issueText } from '@/lib/i18n';
import { intlLocale, LANGS } from '@/lib/i18n';
import PrefsSwitcher from './PrefsSwitcher';
import { formatMoney, formatArea, formatLength, formatDimsCm, cmToInput, inputToCm, lengthInputUnit, catalogCurrency } from '@/core/format';
import { validatePlacement, validateFloor, severityOf, issueTarget } from '@/core/validate';
import { autoLayout, addPlacement } from '@/core/project';
import { resolve, groups, groupOf } from '@/core/catalog';
import { area, r3, wallLength } from '@/core/geometry';
import KeyboardHelp from './KeyboardHelp';
import UnderlayPanel, { type Calib } from './UnderlayPanel';
import ProjectCard from './ProjectCard';
import TasksPanel from './TasksPanel';
import SurveyPanel from './SurveyPanel';
import TimePanel from './TimePanel';
import ContractPanel from './ContractPanel';
import WardrobePanel from './WardrobePanel';
import { DesignerChanges } from './ChangeOrders';
import OrdersPage from './OrdersPage';
import CommercialPanel from './CommercialPanel';
import SitePage from './SitePage';
import type { ProjectMeta } from '@/core/project-meta';
import { scaleFromPoints, anchorAfterScale } from '@/core/underlay';
import { duplicatePlacement, nudgePlacement, NUDGE_CM, NUDGE_BIG_CM } from '@/core/edit-ops';
import EmptyState from './EmptyState';
import PlanView, { type Tool, type Sel } from './PlanView';
import { singleFlight } from '@/lib/single-flight';
import { putDraft, retryDelay, shouldRetry, stashDraft, readStash, clearStash, stashBase, reloginUrl, type SaveOutcome, type Stash } from '@/lib/draft-sync';
import { floors, levelView, mergeLevel, addLevel, removeLevel, stairIssues, stairGeometry, stairwells, comfortableStairLength, elevationOf, SLAB, MAX_LEVELS } from '@/core/levels';
import Icon from './Icon';
import { askConfirm, askPrompt } from '@/lib/dialog';
import { RoomResizeError } from '@/core/room-resize';
import { planRoomResize, sameState, splitRoom, mergeRooms } from '@/core/room-edit';
import { groupsForRoom, defaultGroupForRoom, swapFitHint } from '@/core/furniture-fit';
import type { PlanKeyAction } from '@/core/plan-keys';
import { isLocalHostname } from '@/lib/public-gate';
import { PHASES, phaseOf, stageDone, nextStage, doneCount, STAGE_IDS, activeShares } from '@/core/stage-progress';
import CommandPalette, { type Command } from './CommandPalette';
import { isPaletteShortcut } from '@/lib/fuzzy';
import { editorIgnoresKey } from '@/lib/palette-keys';
import { setWallLength, WallEditError } from '@/core/wall-edit';
import { SEGMENT_THICKNESS } from '@/core/snap';
import { matchCommand, commandById, commandHint, formatShortcut, ariaShortcut, toolShortcut } from '@/lib/commands';
import { prepareShareImages } from '@/lib/share-images';
import { withViewTransition } from '@/lib/motion';
import StagePicker from './StagePicker';
import PlanObjects from './PlanObjects';
import { PLAN_LIMITS, PLAN_DEFAULTS, cleanPlanText, stripPlanText } from '@/core/plan-limits';
import ToolRail from './ToolRail';
import { dimGeometry } from '@/core/annotations';
import { describePlan, describeSelection, type MirrorItem } from '@/core/plan-describe';
import { flushSync } from 'react-dom';
const Viewer3D = dynamic(() => import('./Viewer3D'), { ssr: false });

const uid = () => crypto.randomUUID();
const newWallId = () => `wall-${uid().slice(0, 8)}`;
/** Tipurile oferite de unealta „Placare” (lemnul decorativ întâi); bagheta se pune din panou. */
const CLAD_KINDS: WallFeatureKind[] = ['slats', 'panel', 'frames', 'wallpaper', 'plaster', 'stone', 'brick', 'paint', 'tile'];
const ROOM_TYPES = [['living', 'roomType.living'], ['dormitor', 'roomType.dormitor'], ['bucatarie', 'roomType.bucatarie'], ['baie', 'roomType.baie'], ['hol', 'roomType.hol']];
const ICON: Record<Tool, string> = {
  select: 'M5 3l12 8-6 1 3 7-2 1-3-7-4 4z',
  wall: 'M3 12h18M3 9v6M21 9v6',
  room: 'M4 4h16v16H4z',
  door: 'M5 21V4h9v17M5 21h14M12 12h.01',
  window: 'M4 5h16v14H4zM12 5v14M4 12h16',
  passage: 'M5 21V4h14v17M3 21h4M17 21h4M8 4v2M16 4v2',
  shaft: 'M6 4h12v16H6zM6 9l5-5M6 14l10-10M6 19l12-12M11 20l7-7',
  niche: 'M3 4h18v16H3zM8 8h8v7H8z',
  clad: 'M4 3v18M8 3v18M12 3v18M16 3v18M20 3v18',
  wall_item: 'M4 5h16v12H4zM7 14l3-4 3 3 2-2 2 3M8 20h8',
  floor_zone: 'M4 4h16v16H4zM4 12h8v8M6 20l6-6M4 16l4-4',
  ceiling_zone: 'M3 4h18M6 4v4h12V4M6 8l1 2M18 8l-1 2M7 10h10M8 14h2M12 14h2M16 14h0M5 20h14',
  dim: 'M3 8v8M21 8v8M3 12h18M6 10l-2 2 2 2M18 10l2 2-2 2',
  text: 'M5 5h14M12 5v14M9 19h6',
  column: 'M6 4h12v3H6zm2 3h8v10H8zm-2 10h12v3H6z',
  beam: 'M3 7h18v4H3zm0 6h18v1H3zm0 3h18v1H3z',
  glass_wall: 'M4 4h16v16H4zm2 2v12h5V6zm7 0v12h5V6z',
  railing: 'M3 8h18M3 20h18M4 8v12M20 8v12M8 8v12M12 8v12M16 8v12',
  demolish: 'M3 8h7M14 8h7M3 16h18M8 4l8 16M16 4L8 20',
  stair: 'M4 19h4v-3h4v-3h4v-3h4V7h-3v3h-4v3H8v3H4z',
  measure: 'M3 17L17 3l4 4L7 21zM8 12l2 2M11 9l2 2M14 6l2 2',
  tech: 'M13 2L4 14h7l-1 8 9-12h-7z'
};
const TOOL_LABEL: Record<Tool, string> = {
  select: 'tool.select',
  wall: 'tool.wall',
  room: 'tool.room',
  door: 'tool.door',
  window: 'tool.window',
  passage: 'tool.passage',
  shaft: 'tool.shaft',
  niche: 'tool.niche',
  clad: 'tool.clad',
  wall_item: 'tool.wall_item',
  floor_zone: 'tool.floor_zone',
  ceiling_zone: 'tool.ceiling_zone',
  dim: 'tool.dim',
  text: 'tool.text',
  column: 'tool.column',
  beam: 'tool.beam',
  glass_wall: 'tool.glass_wall',
  railing: 'tool.railing',
  demolish: 'tool.demolish',
  stair: 'tool.stair',
  measure: 'tool.measure',
  tech: 'tool.tech'
};

/** Etapele unui proiect de amenajare, în ordinea în care lucrează un designer: fiecare are vederea, uneltele și panoul ei. */
type Stage = typeof STAGE_IDS[number];
const STAGES: { id: Stage; view: '2d' | '3d' | 'split'; tools: Tool[]; page?: true }[] = [
  { id: 'survey', view: '2d', tools: ['select', 'wall', 'room', 'door', 'window', 'passage', 'niche', 'column', 'shaft', 'beam', 'glass_wall', 'railing', 'demolish', 'stair', 'measure', 'dim', 'text'] },
  { id: 'concept', view: 'split', tools: ['select', 'niche', 'column', 'glass_wall', 'wall_item', 'measure'] },
  { id: 'finishes', view: 'split', tools: ['select', 'clad', 'wall_item', 'floor_zone', 'ceiling_zone', 'measure'] },
  { id: 'ffe', view: 'split', tools: ['select', 'wall_item', 'measure'] },
  { id: 'mep', view: '2d', tools: ['select', 'tech', 'shaft', 'measure'] },
  { id: 'docs', view: '2d', tools: ['select', 'measure', 'dim', 'text'] },
  { id: 'quote', view: '2d', tools: ['select'] },
  { id: 'present', view: '3d', tools: ['select'] },
  { id: 'orders', view: '2d', tools: [], page: true },
  { id: 'site', view: '2d', tools: [], page: true }
];
export default function Editor({ id }: { id: string }){
  const { t, tp, lang, units, setLang, setUnits } = usePrefs(), iT = (i: Parameters<typeof issueText>[1]) => issueText(lang, i, units), u = lengthInputUnit(units);
  const [meta, setMeta] = useState<ProjectMeta | null>(null), [house, setHouse] = useState<Snapshot | null>(null), [lvl, setLvl] = useState(0), [catalog, setCatalog] = useState<Catalog | null>(null), [mc, setMc] = useState<MaterialsCatalog | null>(null), [out, setOut] = useState<Outbound | null>(null);
  const compact = useCompact(), [sheet, setSheet] = useState<'peek' | 'half' | 'full'>('half'), [sideOpen, setSideOpen] = useState(true), [sel, setSel] = useState<Sel>(null), [frame, setFrame] = useState<{ sel: Sel; n: number; at: number } | null>(null), [wideSide, setWideSide] = useState(false), [hoverPid, setHoverPid] = useState<string | null>(null), [frame3d, setFrame3d] = useState<{ id: string; n: number; at: number } | null>(null), [tool, setTool] = useState<Tool>('select'), [view, setView] = useState<'2d' | '3d' | 'split'>('2d'), [help, setHelp] = useState(false), [calib, setCalib] = useState<Calib | null>(null);
  const [save, setSave] = useState<'saved' | 'dirty' | 'saving' | 'error' | 'conflict' | 'offline' | 'auth'>('saved'), baseRev = useRef<number | null>(null), saveRef = useRef<string>('saved'), [toast, setToast] = useState(''), [revs, setRevs] = useState<any[]>([]), [rev, setRev] = useState(0);
  const [showTech, setShowTech] = useState(true), [techKind, setTechKind] = useState<TechKind>('outlet_double');
  const [pending, setPending] = useState<{ before: Snapshot; issues: Issue[]; discard?: () => void } | null>(null), [persistent, setPersistent] = useState(true), [demo, setDemo] = useState(false), [stage, setStageRaw] = useState<Stage>('survey'), [preview, setPreview] = useState<{ v: any; pid: string } | null>(null), [shares, setShares] = useState<any[]>([]);
  const [showLab, setShowLab] = useState(false), [showHealth, setShowHealth] = useState(false), [activeImpact, setActiveImpact] = useState<DesignImpact | null>(null);
  const paletteOpenRef = useRef(false), hist = useRef(new History<Snapshot>()), dragStart = useRef<Snapshot | null>(null), dragMark = useRef<HistoryMark | null>(null), timer = useRef<any>(null), latest = useRef<Snapshot | null>(null), [, force] = useState(0);
  // Se salvează casa întreagă (`house`); editorul lucrează pe nivelul ales (`snap`, o vedere din core/levels.ts),
  // iar fiecare modificare a vederii se pune la loc în casă cu mergeLevel.
  const L = house ? Math.min(lvl, floors(house).length - 1) : 0;
  const snap = useMemo(() => house ? levelView(house, L) : null, [house, L]);
  const health = useMemo(() => snap && catalog && mc ? evaluateProjectHealth(snap, catalog, mc) : null, [snap, catalog, mc]);
  const labAlternatives = useMemo(() => {
    const alts = [];
    if (preview?.v?.candidate) {
      alts.push({ id: 'preview-alt', title: preview.v.title || t('lab.currentProposal'), snapshot: preview.v.candidate });
    }
    if (catalog && snap) {
      const relaid = autoLayout(snap, catalog);
      alts.push({ id: 'auto-alt', title: t('lab.circulationOpt'), snapshot: relaid.snapshot });
    }
    return alts;
  }, [preview, snap, catalog, t]);
  const [wallItemKind, setWallItemKind] = useState<WallItemKind>('art'), [cladKind, setCladKind] = useState<WallFeatureKind>('slats'), [cladFocus, setCladFocus] = useState<{ room: string; i: number; n: number } | null>(null);
  const toastT = useRef<any>(null), say = (msg: string) => { setToast(msg); clearTimeout(toastT.current); toastT.current = setTimeout(() => setToast(''), 6000); };

  useEffect(() => { Promise.all([fetch(`/api/projects/${id}`).then(r => r.ok ? r.json() : Promise.reject(r)), fetch('/api/catalog').then(r => r.json()), fetch('/api/health').then(r => r.json()), fetch('/api/materials').then(r => r.json())])
    .then(([p, c, h, m]) => { setHouse(p.draft); latest.current = p.draft; baseRev.current = p.draftRev ?? null; setMeta(p.meta ?? { status: 'design' }); setCatalog(c); setMc(m); setRev(p.currentRevision); setPersistent(h.persistent); setDemo(!!h.demo); const st = readStash(store(), id); if (st){ stashOpen.current = true; setStash(st); } }).catch(() => say(t('editor.loadError')));
    fetch('/api/outbound').then(r => r.ok ? r.json() : null).then(setOut).catch(() => {});
    loadRevs(); }, [id]); // eslint-disable-line
  const loadRevs = () => { fetch(`/api/projects/${id}/revisions`).then(r => r.ok ? r.json() : []).then(setRevs); fetch(`/api/projects/${id}/shares`).then(r => r.ok ? r.json() : []).then(setShares); };
  // linkurile active fără imagini gata randate (linkuri vechi, o randare întreruptă) se completează în fundal, fără mesaj
  useEffect(() => { const revs = [...new Set(shares.filter(s => s.active).map(s => s.revisionNumber as number))]; if (!revs.length) return;
    const tm = setTimeout(() => { revs.reduce((p, n) => p.then(() => prepareShareImages(id, n)).then(() => {}), Promise.resolve()); }, 4000); return () => clearTimeout(tm); }, [shares, id]);
  const flight = useRef(singleFlight()).current, [revBusy, setRevBusy] = useState(false);
  async function shareRevision(n: number){ return flight.run(`share:${n}`, () => shareRevisionNow(n)); }
  async function shareRevisionNow(n: number){ const r = await fetch(`/api/projects/${id}/shares`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ revision: n }) }); if (!authOk(r)) return; const j = await r.json().catch(() => ({}));
    if (!r.ok){ say(j.error || t('editor.shareFailed')); return; } prepareShareImages(id, n).then(k => { if (k) say(t('client.imagesReady')); }); const url = `${location.origin}${j.path}`; try { await navigator.clipboard.writeText(url); say(t('editor.shareCopied')); } catch { say(t('editor.shareLink', { url })); } loadRevs(); }
  async function revokeShare(token: string){ const r = await fetch(`/api/projects/${id}/shares`, { method: 'DELETE', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ token }) }); if (!authOk(r)) return; if (!r.ok){ say(t('editor.revokeFailed')); return; } say(t('editor.revoked')); loadRevs(); }
  // fiecare salvare spune de la ce versiune a pornit; dacă proiectul s-a schimbat între timp (alt dispozitiv), nu suprascrie tăcut
  // Salvările merg una după alta (fiecare pornește de la versiunea primită de precedenta); rețeaua căzută nu aruncă: starea devine
  // „offline · nesalvat”, se reîncearcă cu pauze crescânde și la evenimentul `online`; sesiunea expirată păstrează ciorna local.
  const inflight = useRef<Promise<SaveOutcome> | null>(null), retryT = useRef<any>(null), attempt = useRef(0), stashOpen = useRef(false), [stash, setStash] = useState<Stash | null>(null);
  const store = () => { try { return typeof sessionStorage === 'undefined' ? null : sessionStorage; } catch { return null; } };
  const lostAuth = () => { clearTimeout(retryT.current); setSave('auth'); saveRef.current = 'auth'; if (latest.current) stashDraft(store(), id, latest.current, baseRev.current); };
  const authOk = (r: Response) => { if (r.status === 401) lostAuth(); return r.status !== 401; };
  const send = (force = false): Promise<SaveOutcome> => { clearTimeout(retryT.current);
    const run = (inflight.current ?? Promise.resolve(null)).catch(() => null).then(async (): Promise<SaveOutcome> => { const sent = latest.current; setSave('saving');
      const o = await putDraft(fetch, id, sent, baseRev.current, force);
      if (o.kind === 'saved'){ baseRev.current = o.draftRev; attempt.current = 0; if (!stashOpen.current) clearStash(store(), id); setSave(latest.current === sent ? 'saved' : 'dirty'); }
      else if (o.kind === 'conflict') setSave('conflict');
      else if (o.kind === 'auth') lostAuth();
      else { setSave(o.kind === 'offline' ? 'offline' : 'error'); if (o.kind === 'error') say(o.message || t('editor.saveFailed')); else if (sent) stashDraft(store(), id, sent, baseRev.current);
        if (shouldRetry(o)) retryT.current = setTimeout(() => { if (!['conflict', 'auth', 'saving'].includes(saveRef.current)) send(); }, retryDelay(attempt.current++)); }
      return o; });
    inflight.current = run; return run; };
  useEffect(() => { saveRef.current = save; }, [save]);
  const refreshBase = () => fetch(`/api/projects/${id}`).then(r => r.ok ? r.json() : null).then(p => { if (p) baseRev.current = p.draftRev ?? null; }).catch(() => {});
  const persist = useCallback((s: Snapshot) => { latest.current = s; setSave(x => x === 'conflict' || x === 'auth' ? x : 'dirty'); clearTimeout(timer.current);
    timer.current = setTimeout(() => { if (saveRef.current === 'auth') stashDraft(store(), id, latest.current, baseRev.current); else if (saveRef.current !== 'conflict') send(); }, 700); }, [id]); // eslint-disable-line
  // revenirea rețelei reia salvarea; închiderea paginii cu modificări nesalvate cere confirmare
  useEffect(() => { const on = () => { if (['offline', 'error', 'dirty'].includes(saveRef.current)){ attempt.current = 0; send(); } };
    const leave = (e: BeforeUnloadEvent) => { if (saveRef.current !== 'saved'){ e.preventDefault(); e.returnValue = ''; } };
    addEventListener('online', on); addEventListener('beforeunload', leave);
    return () => { removeEventListener('online', on); removeEventListener('beforeunload', leave); clearTimeout(retryT.current); }; }, [id]); // eslint-disable-line
  // ciorna rămasă de dinainte de reconectare: se repune doar la cererea omului și se salvează cu versiunea ei (verificarea normală de conflict)
  const takeStash = () => { if (!stash || !house) return; const s = stash.snapshot as Snapshot; hist.current.push(house); baseRev.current = stashBase(stash, baseRev.current);
    stashOpen.current = false; setStash(null); setHouse(s); latest.current = s; setSel(null); force(x => x + 1); send(); };
  const dropStash = () => { stashOpen.current = false; setStash(null); clearStash(store(), id); };
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
  const changePiece = (id: string, fn: (q: FurniturePlacement) => void, refused: string) => { if (!snap) return; const n = structuredClone(snap), q = n.placements.find(x => x.id === id)!, before = snap.placements.find(x => x.id === id)!; fn(q); const iss = validatePlacement(n, catalog!, q);
    if (severityOf(iss) === 'ERROR'){ say(`${refused}: ${iT(iss.find(i => i.severity === 'ERROR')!)}${fitHint(before, q)}`); return; } commit(n); if (iss.length) setPending({ before: house!, issues: iss, discard: () => hist.current.discardLast() }); };
  // de ce nu încape varianta nouă: latura care crește și cu câți cm (core/furniture-fit.ts)
  const fitHint = (a: FurniturePlacement, b: FurniturePlacement) => { const dims = (p: FurniturePlacement) => p.size ? { w: p.size.w, d: p.size.d } : resolve(catalog!, p.variantId)?.variant.dimensionsCm;
    const da = dims(a), db = dims(b), h = da && db ? swapFitHint(da, db) : null; return h ? ` ${t(h.dim === 'width' ? 'editor.swapHintW' : 'editor.swapHintD', { over: h.overCm, n: h.newCm, o: h.oldCm })}` : ''; };

  const stairIss = useMemo(() => house && catalog ? stairIssues(house, catalog) : [], [house, catalog]);
  const issues = useMemo(() => { const m: Record<string, Issue[]> = {}; if (snap && catalog) for (const p of snap.placements) m[p.id] = validatePlacement(snap, catalog, p);
    for (const i of stairIss) if (i.with && m[i.with]) m[i.with].push(i); return m; }, [snap, catalog, stairIss]);
  const sev = useMemo(() => Object.fromEntries(Object.entries(issues).map(([k, v]) => [k, severityOf(v)])) as Record<string, Severity>, [issues]);
  // problemele nivelului: pereți/goluri și scările lui (cele legate de o piesă apar la piesă)
  const floorIssues = useMemo(() => snap ? [...validateFloor(snap.floor), ...(catalog ? wallItemFurnitureIssues(snap, catalog, snap.floor) : []), ...(catalog ? techIssues(snap, catalog, snap.floor) : []), ...stairIss.filter(i => !i.with && snap.floor.stairs?.some(s => s.id === i.vars?.stair))] : [], [snap, stairIss, catalog]);
  const advice = useMemo(() => { if (!snap || !catalog) return [] as Advice[]; let budget; if (mc && house){ const b = computeBudget(house, catalog, mc); budget = { total: b.chosen.total, target: b.target, unknown: b.unknownItems.length }; }
    return adviseProject(snap, catalog, { accessibility: !!snap.brief?.accessibility, budget, colorsOf: appearanceColors, currency: catalogCurrency(catalog) }); }, [snap, house, catalog, mc]);
  const showAdvice = (a: Advice) => { setStageRaw(a.refs.placementIds?.length ? 'ffe' : 'survey'); const pid = a.refs.placementIds?.[0], wid = a.refs.wallIds?.[0]; if (pid) setSel({ kind: 'placement', id: pid }); else if (a.refs.roomId) setSel({ kind: 'room', id: a.refs.roomId }); else if (wid) setSel({ kind: 'wall', id: wid }); };
  // tavanul fals pe zonă se editează în Finisaje; restul planului în Releveu, piesele în Mobilier
  const focusIssue = (i: Issue) => { const tgt = issueTarget(i, snap?.floor, snap?.placements); if (!tgt) return; setStageRaw(tgt.kind === 'placement' ? 'ffe' : tgt.kind === 'ceiling_zone' ? 'finishes' : 'survey'); setSel(tgt as Sel); if (compact && sheet === 'peek') setSheet('half'); };

  // editare din plan: mutările se validează la final (ERROR → revine, WARNING → cere confirmare)
  const onEdit = (fn: (s: Snapshot) => void, phase: 'start' | 'move' | 'end') => { if (!snap || !catalog) return;
    if (phase === 'start'){ dragStart.current = house; dragMark.current = hist.current.begin(house!); }
    if (phase === 'end'){ const before = dragStart.current!, mark = dragMark.current; dragStart.current = null; dragMark.current = null;
      const discard = () => { if (mark) hist.current.rollback(mark); };
      // o tragere care se termină cu planul neschimbat (clamp, înapoi la locul inițial) nu lasă pas de anulare
      if (sameState(before, latest.current)){ discard(); force(x => x + 1); return; }
      const cur = levelView(latest.current!, L);
      if (sel?.kind === 'placement'){ const p = cur.placements.find(x => x.id === sel.id); const iss = p ? validatePlacement(cur, catalog, p) : [];
        if (severityOf(iss) === 'ERROR'){ discard(); setHouse(before); persist(before); say(t('editor.positionRefused', { msg: iT(iss.find(i => i.severity === 'ERROR')!) })); return; }
        if (severityOf(iss) === 'WARNING'){ setPending({ before, issues: iss, discard }); } }
      else {
        const changedRooms = cur.floor.rooms.filter(nr => {
          const old = before.floor.rooms.find(or => or.id === nr.id);
          return !old || Math.abs(old.rect.x0 - nr.rect.x0) > 0.01 || Math.abs(old.rect.x1 - nr.rect.x1) > 0.01 ||
            Math.abs(old.rect.z0 - nr.rect.z0) > 0.01 || Math.abs(old.rect.z1 - nr.rect.z1) > 0.01;
        });
        if (changedRooms.length > 0) {
          let updated = cur;
          for (const cr of changedRooms) {
            const hasManual = updated.placements.some(p => p.roomId === cr.id && p.source === 'manual');
            if (!hasManual) {
              const relaid = autoLayout(updated, catalog, { roomId: cr.id });
              updated = { ...updated, placements: relaid.snapshot.placements };
            }
          }
          const h = mergeLevel(latest.current!, L, updated); latest.current = h; setHouse(h); persist(h);
        }
      }
      return; }
    const base = latest.current || house!, n = structuredClone(levelView(base, L)); fn(n); const h = mergeLevel(base, L, n); latest.current = h; setHouse(h); persist(h); };
  const addUnderlay = (dataUrl: string) => mutate(s => { const xs = s.floor.rooms.flatMap(r => [r.rect.x0, r.rect.x1]), zs = s.floor.rooms.flatMap(r => [r.rect.z0]);
    const x0 = xs.length ? Math.min(...xs) : 0, w = xs.length ? Math.max(...xs) - x0 : 10; s.underlay = { dataUrl, x: r3(x0), z: r3(zs.length ? Math.min(...zs) : 0), widthM: Math.min(100, Math.max(1, r3(w || 10))), opacity: .5, locked: false }; });
  const patchUnderlay = (patch: Partial<NonNullable<Snapshot['underlay']>>, record: boolean) => { if (!snap?.underlay) return; const n = structuredClone(snap); Object.assign(n.underlay!, patch); commit(n, record); };
  const applyCalib = (realCm: number) => { const u = snap?.underlay; if (!snap || !u || calib?.stage !== 'enter' || !calib.a || !calib.b) return false; const w = scaleFromPoints(calib.a, calib.b, realCm, u.widthM); if (w == null) return false;
    const [x, z] = anchorAfterScale(u, calib.a, w); const n = structuredClone(snap); n.underlay = { ...u, widthM: w, x, z }; commit(n); setCalib(null); return true; };
  // lățime/lungime din panou: camera și vecinii care împart latura mutată (un pas de anulare); un vecin nu scade sub 60 cm
  // un rezultat identic cu planul de acum (vecinul deja la 60 cm) nu lasă pas de anulare; mesajul de limitare apare oricum
  const resizeSel = (roomId: string, rect: Room['rect']) => { if (!snap || !catalog) return; try { const r = planRoomResize(snap.floor, roomId, rect, newWallId);
    if (r.changed) mutate(s => {
      s.floor = r.floor;
      const changedRooms = r.floor.rooms.filter(nr => {
        const old = snap.floor.rooms.find(or => or.id === nr.id);
        return !old || Math.abs(old.rect.x0 - nr.rect.x0) > 0.01 || Math.abs(old.rect.x1 - nr.rect.x1) > 0.01 ||
          Math.abs(old.rect.z0 - nr.rect.z0) > 0.01 || Math.abs(old.rect.z1 - nr.rect.z1) > 0.01;
      });
      for (const cr of changedRooms) {
        const hasManual = s.placements.some(p => p.roomId === cr.id && p.source === 'manual');
        if (!hasManual) {
          const relaid = autoLayout(s, catalog, { roomId: cr.id });
          s.placements = relaid.snapshot.placements;
        }
      }
    });
    if (r.clamped) say(t('editor.resizeClamped')); }
    catch (e) { if (e instanceof RoomResizeError) say(e.message); else throw e; } };
  const undo = () => { if (!house || dragStart.current) return; const s = hist.current.undo(house); if (s){ setPending(null); setHouse(s); persist(s); force(x => x + 1); } };
  const redo = () => { if (!house || dragStart.current) return; const s = hist.current.redo(house); if (s){ setHouse(s); persist(s); force(x => x + 1); } };
  const del = () => { if (!sel) return; mutate(s => {
    if (sel.kind === 'placement') s.placements = s.placements.filter(p => p.id !== sel.id);
    // peretele șters își ia și nișele
    if (sel.kind === 'wall'){ s.floor.walls = s.floor.walls.filter(w => w.id !== sel.id); if (s.floor.niches){ s.floor.niches = s.floor.niches.filter(n => n.wallId !== sel.id); if (!s.floor.niches.length) delete s.floor.niches; }
      if (s.floor.wallItems){ s.floor.wallItems = s.floor.wallItems.filter(n => n.wallId !== sel.id); if (!s.floor.wallItems.length) delete s.floor.wallItems; } }
    if (sel.kind === 'wall_item'){ s.floor.wallItems = (s.floor.wallItems || []).filter(x => x.id !== sel.id); if (!s.floor.wallItems.length) delete s.floor.wallItems; }
    if (sel.kind === 'floor_zone'){ s.floor.zones = (s.floor.zones || []).filter(x => x.id !== sel.id); if (!s.floor.zones.length) delete s.floor.zones; }
    if (sel.kind === 'ceiling_zone'){ s.floor.ceilingZones = (s.floor.ceilingZones || []).filter(x => x.id !== sel.id); if (!s.floor.ceilingZones.length) delete s.floor.ceilingZones; }
    if (sel.kind === 'railing'){ s.floor.railings = (s.floor.railings || []).filter(x => x.id !== sel.id); if (!s.floor.railings.length) delete s.floor.railings; }
    if (sel.kind === 'stair'){ s.floor.stairs = (s.floor.stairs || []).filter(x => x.id !== sel.id); if (!s.floor.stairs.length) delete s.floor.stairs; }
    if (sel.kind === 'column'){ s.floor.columns = (s.floor.columns || []).filter(x => x.id !== sel.id); if (!s.floor.columns.length) delete s.floor.columns; }
    if (sel.kind === 'beam'){ s.floor.beams = (s.floor.beams || []).filter(x => x.id !== sel.id); if (!s.floor.beams.length) delete s.floor.beams; }
    if (sel.kind === 'annotation'){ s.floor.annotations = (s.floor.annotations || []).filter(x => x.id !== sel.id); if (!s.floor.annotations.length) delete s.floor.annotations; }
    if (sel.kind === 'niche'){ s.floor.niches = (s.floor.niches || []).filter(x => x.id !== sel.id); if (!s.floor.niches.length) delete s.floor.niches; }
    if (sel.kind === 'glass_wall'){ s.floor.glassWalls = (s.floor.glassWalls || []).filter(x => x.id !== sel.id); if (!s.floor.glassWalls.length) delete s.floor.glassWalls; }
    if (sel.kind === 'opening'){ const w = s.floor.walls.find(w => w.id === sel.wallId); if (w) w.openings = w.openings.filter(o => o.id !== sel.id); }
    // camera ștearsă își ia piesele și zonele de pardoseală
    if (sel.kind === 'room'){ s.floor.rooms = s.floor.rooms.filter(r => r.id !== sel.id); s.placements = s.placements.filter(p => p.roomId !== sel.id);
      removeRoomZones(s.floor, sel.id); removeRoomCeilingZones(s.floor, sel.id); } }); setSel(null); };
  const rotate = (pid: string) => { if (!snap || !catalog) return; const n = structuredClone(snap), p = n.placements.find(x => x.id === pid)!; p.rotation = r3(((p.rotation + Math.PI / 2) % (Math.PI * 2))); p.source = 'manual';
    const iss = validatePlacement(n, catalog, p); if (severityOf(iss) === 'ERROR'){ say(t('editor.rotateNoFit', { msg: iT(iss.find(i => i.severity === 'ERROR')!) })); return; } commit(n); if (iss.length) setPending({ before: house!, issues: iss, discard: () => hist.current.discardLast() }); };
  // ---- niveluri și scări ----
  const goLevel = (i: number) => { setLvl(i); setSel(null); setPreview(null); setCalib(null); };
  const levelName = (i: number) => i === 0 ? t('level.ground') : (house && floors(house)[i]?.name) || t('level.n', { n: i });
  const onAddLevel = () => { if (!house) return; const n = floors(house).length; if (n >= MAX_LEVELS){ say(t('level.max', { n: MAX_LEVELS })); return; }
    commitHouse(addLevel(house, { name: t('level.n', { n }) })); goLevel(n); };
  const onRemoveLevel = async () => { if (!house || L === 0 || !(await askConfirm({ title: t('dlg.removeLevelTitle'), body: t('level.removeConfirm', { name: levelName(L) }), confirmLabel: t('level.remove'), danger: true }))) return; commitHouse(removeLevel(house, L)); goLevel(L - 1); };
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
  // tastatura din plan (core/plan-keys.ts): săgețile mută piesa, scara sau camera aleasă, R rotește, Delete șterge
  const onPlanKey = (a: PlanKeyAction) => { if (!a || !sel || preview) return;
    if (a.type === 'deselect') setSel(null);
    else if (a.type === 'delete') del();
    else if (a.type === 'lift' && sel.kind === 'wall_item') patchWallItem(sel.id, q => { q.elevation = r3(Math.max(q.height / 2, Math.min((snap?.floor.ceilingHeight ?? 2.6) - q.height / 2, q.elevation + a.dyCm / 100))); });
    else if (a.type === 'rotate'){ if (sel.kind === 'placement') rotate(sel.id); else if (sel.kind === 'stair') rotateStair(sel.id); else if (sel.kind === 'column') rotateColumn(sel.id); }
    else if (a.type === 'move'){ const dx = a.dxCm / 100, dz = a.dzCm / 100;
      if (sel.kind === 'placement') nudge(sel.id, a.dxCm, a.dzCm);
      else if (sel.kind === 'stair'){ const st = snap?.floor.stairs?.find(x => x.id === sel.id); if (st) patchStair(sel.id, { x: r3(st.x + dx), z: r3(st.z + dz) }); }
      else if (sel.kind === 'column'){ const c = snap?.floor.columns?.find(x => x.id === sel.id); if (c) mutate(s => { const col = s.floor.columns?.find(x => x.id === sel.id); if (col) { col.x = r3(col.x + dx); col.z = r3(col.z + dz); } }); }
      else if (sel.kind === 'annotation') patchAnn(sel.id, an => { const mv = (q: [number, number]): [number, number] => [r3(q[0] + dx), r3(q[1] + dz)]; if (an.kind === 'dim'){ an.a = mv(an.a); an.b = mv(an.b); } else an.at = mv(an.at); });
      else if (sel.kind === 'wall_item'){ const n = snap?.floor.wallItems?.find(x => x.id === sel.id), w = n && snap!.floor.walls.find(x => x.id === n.wallId); if (n && w){ const L = wallLength(w.a, w.b), along = ((w.b[0] - w.a[0]) * dx + (w.b[1] - w.a[1]) * dz) / (L || 1);
        patchWallItem(sel.id, q => { q.offset = r3(Math.max(q.width / 2, Math.min(L - q.width / 2, q.offset + along))); }); } }
      else if (sel.kind === 'niche'){ const n = snap?.floor.niches?.find(x => x.id === sel.id), w = n && snap!.floor.walls.find(x => x.id === n.wallId); if (n && w){ const L = wallLength(w.a, w.b), along = ((w.b[0] - w.a[0]) * dx + (w.b[1] - w.a[1]) * dz) / (L || 1);
        patchNiche(sel.id, q => { q.offset = r3(Math.max(0, Math.min(L - q.width, q.offset + along))); }); } }
      else if (sel.kind === 'floor_zone') patchZone(sel.id, z => { z.rect = { x0: r3(z.rect.x0 + dx), x1: r3(z.rect.x1 + dx), z0: r3(z.rect.z0 + dz), z1: r3(z.rect.z1 + dz) }; });
      else if (sel.kind === 'ceiling_zone') patchCeilingZone(sel.id, z => { z.rect = { x0: r3(z.rect.x0 + dx), x1: r3(z.rect.x1 + dx), z0: r3(z.rect.z0 + dz), z1: r3(z.rect.z1 + dz) }; });
      else if (sel.kind === 'railing') patchRailing(sel.id, q => { q.a = [r3(q.a[0] + dx), r3(q.a[1] + dz)]; q.b = [r3(q.b[0] + dx), r3(q.b[1] + dz)]; });
      else if (sel.kind === 'room') mutate(s => { const r = s.floor.rooms.find(x => x.id === sel.id); if (r) r.rect = { x0: r3(r.rect.x0 + dx), x1: r3(r.rect.x1 + dx), z0: r3(r.rect.z0 + dz), z1: r3(r.rect.z1 + dz) }; }); } };
  useEffect(() => { const k = (e: KeyboardEvent) => { const el = e.target as HTMLElement;
    // în lista obiectelor planului tastele aparțin listei (săgeți, Enter, Escape): Backspace nu șterge selecția pe neașteptate
    if (el.closest?.('.plan-mirror')) return;
    if (editorIgnoresKey({ preview: !!preview, inField: !!el.closest?.('input,select,textarea,[contenteditable=true]'), inDialog: !!el.closest?.('dialog[open],[aria-modal=true]'), paletteOpen: paletteOpenRef.current })) return;
    // scurtăturile vin din registru (lib/commands.ts); Ctrl/⌘K rămâne la paletă (isPaletteShortcut)
    const cid = matchCommand(e); if (!cid) return;
    switch (cid){
      case 'undo': e.preventDefault(); undo(); break;
      case 'redo': e.preventDefault(); redo(); break;
      case 'duplicate': e.preventDefault(); if (sel?.kind === 'placement') duplicate(sel.id); break;
      case 'help': setHelp(h => !h); break;
      case 'nudge': case 'nudge.big': if (sel?.kind !== 'placement') break; { e.preventDefault(); const st = cid === 'nudge.big' ? NUDGE_BIG_CM : NUDGE_CM;
        nudge(sel.id, e.key === 'ArrowLeft' ? -st : e.key === 'ArrowRight' ? st : 0, e.key === 'ArrowUp' ? -st : e.key === 'ArrowDown' ? st : 0); } break;
      case 'delete': del(); break;
      case 'rotate': if (sel?.kind === 'placement') rotate(sel.id); else if (sel?.kind === 'stair') rotateStair(sel.id); else if (sel?.kind === 'column') rotateColumn(sel.id); break;
      case 'escape': setTool('select'); setHelp(false); setCalib(null); setShowHealth(false); setShowLab(false); setActiveImpact(null); break;
      default: { const tk = commandById(cid)?.tool; if (tk && STAGES.find(x => x.id === stage)?.tools.includes(tk)) setTool(tk); } } };
    addEventListener('keydown', k); return () => removeEventListener('keydown', k); });

  // salvarea automată e amânată 700 ms; înainte de o revizie sau de un link pentru client se trimite imediat ultima stare
  // aceeași cale ca salvarea automată (cu versiunea de bază): un conflict oprește revizia / linkul / aplicarea, nu suprascrie
  const flushDraft = async (): Promise<boolean> => { if (!house) return false; clearTimeout(timer.current);
    if (saveRef.current === 'conflict'){ say(t('editor.conflict')); return false; } if (saveRef.current === 'auth') return false;
    if (!latest.current) latest.current = house; const o = await send(); if (o.kind !== 'saved' && o.kind !== 'conflict' && o.kind !== 'auth') say(t('editor.flushBlocked')); return o.kind === 'saved'; };
  async function saveRevision(){ if (!house) return; if (!(await flushDraft())) return;
    const note = await askPrompt({ title: t('dlg.revTitle'), body: t('dlg.revBody'), confirmLabel: t('editor.saveRevision'), input: { label: t('editor.notePrompt'), placeholder: t('dlg.notePh'), maxLength: 200 } }); if (note == null) return; const r = await fetch(`/api/projects/${id}/revisions`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ note }) }); if (!authOk(r)) return; const j = await r.json().catch(() => ({}));
    if (!r.ok){ say(`${j.error || t('editor.saveFailed')} ${(Array.isArray(j.details) ? j.details : []).slice(0, 2).map((d: any) => iT(d)).join(' ')}`); return; } setRev(j.number); say(t('editor.revSaved', { n: j.number })); loadRevs(); }
  // un design aplicat pe server mută versiunea ciornei: editorul continuă de la ea (altfel următoarea salvare ar fi un conflict fals)
  const onApplied = (s: Snapshot, n: number, draftRev?: number | null) => { clearTimeout(timer.current); hist.current.push(house!); setHouse(s); latest.current = s; setRev(n); setSave('saved'); setSel(null);
    if (typeof draftRev === 'number') baseRev.current = draftRev; else refreshBase(); loadRevs(); };
  const onBrief = (brief: Snapshot['brief'], draftRev?: number | null) => { if (!house) return; const h = { ...(latest.current ?? house), brief }; setHouse(h); latest.current = h;
    if (typeof draftRev === 'number') baseRev.current = draftRev; else refreshBase(); };
  async function restore(n: number){ if (!(await askConfirm({ title: t('dlg.restoreTitle', { n }), body: t('editor.restoreConfirm', { n }), confirmLabel: t('dlg.restoreOk', { n }) }))) return;
    clearTimeout(timer.current); const r = await fetch(`/api/projects/${id}/revisions/${n}`, { method: 'POST' }); if (!authOk(r)) return; if (!r.ok){ say(t('editor.restoreFailed')); return; } const s = await r.json(); hist.current.push(house!); setHouse(s); latest.current = s; setSel(null); refreshBase(); say(t('editor.restored', { n })); }

  // etapa activă rămâne vizibilă în bara de etape (pe tabletă și telefon nu încap toate zece)
  const stageBar = useRef<HTMLElement | null>(null);
  useEffect(() => { const b = stageBar.current?.querySelector<HTMLElement>(`[data-stage="${stage}"]`); try { b?.scrollIntoView({ block: 'nearest', inline: 'center', behavior: 'smooth' }); } catch { /* browser vechi */ } }, [stage]);
  // când etapele nu încap (de ex. 1024 px), bara arată că se derulează: margini estompate pe partea cu etape ascunse și săgeți
  const [barFade, setBarFade] = useState({ over: false, l: false, r: false });
  // pe desktop săgețile apar doar din cauza depășirii: lățimea lor se adaugă înapoi, altfel odată afișate ar rămâne afișate
  // măsurarea citește layoutul: rulează doar la derulare și când bara sau conținutul ei își schimbă mărimea, nu la fiecare
  // randare (în timpul unei trageri pe plan ar forța layoutul la fiecare mișcare a cursorului)
  const measureBar = useCallback(() => { const el = stageBar.current; if (!el) return; const max = el.scrollWidth - el.clientWidth;
    const arrows = el.parentElement?.classList.contains('is-over') && matchMedia('(min-width: 1024px)').matches ? [...el.parentElement.querySelectorAll<HTMLElement>('.stage-step')].reduce((s, b) => s + b.offsetWidth, 0) : 0;
    const over = max - arrows > 1, l = max > 1 && el.scrollLeft > 1, r = max > 1 && el.scrollLeft < max - 1;
    setBarFade(f => f.over === over && f.l === l && f.r === r ? f : { over, l, r }); }, []);
  // un singur ResizeObserver pe element (ref-callback): urmărește bara și grupurile de etape (lățimea lor se schimbă cu limba
  // sau cu numărul de avertismente); la desprinderea elementului observatorul se oprește
  const barRO = useRef<ResizeObserver | null>(null);
  const stageBarRef = useCallback((el: HTMLElement | null) => { barRO.current?.disconnect(); barRO.current = null; stageBar.current = el; if (!el) return;
    measureBar(); if (typeof ResizeObserver === 'undefined') return; const ro = new ResizeObserver(measureBar); ro.observe(el); for (const c of Array.from(el.children)) ro.observe(c); barRO.current = ro; }, [measureBar]);
  // pe ecranele înguste „Plan + 3D” nu are loc: rămâne vederea plan
  useEffect(() => { if (compact && view === 'split') setView('2d'); }, [compact, view]);
  // paleta de comenzi: Ctrl+K / Cmd+K oriunde în editor (și în câmpuri), sau butonul „Caută” din antet
  const [cmdOpen, setCmdOpen] = useState(false), [mac, setMac] = useState(false);
  useEffect(() => { paletteOpenRef.current = cmdOpen; }, [cmdOpen]);
  useEffect(() => { const isMac = /Mac|iPhone|iPad/.test(navigator.platform || navigator.userAgent); setMac(isMac);
    const k = (e: KeyboardEvent) => { const el = e.target as HTMLElement | null;
      if (isPaletteShortcut(e, { mac: isMac, inField: !!el?.closest?.('input,textarea,[contenteditable=true]') })){ e.preventDefault(); setCmdOpen(o => !o); } };
    addEventListener('keydown', k); return () => removeEventListener('keydown', k); }, []);
  // „gata” pe etapă, din datele proiectului (core/stage-progress.ts); se recalculează la fiecare modificare
  const done = useMemo(() => house ? stageDone({ house, warnings: advice.filter(a => a.severity !== 'TIP').length, shares: activeShares(shares), status: meta?.status ?? 'design' }) : null, [house, advice, shares, meta]);
  // echivalentul text al planului (UI/UX P3-16): lista obiectelor și descrierea selecției, anunțată grupat (o mutare cu
  // săgețile nu schimbă descrierea, deci nu produce zeci de anunțuri), în limba interfeței
  const mirror = useMemo(() => snap && catalog ? describePlan(snap, catalog, { t, units, lang }) : [], [snap, catalog, t, units, lang]);
  const selDesc = useMemo(() => describeSelection(mirror, sel), [mirror, sel]);
  // descrierea + ce se poate face cu obiectul; un spațiu invizibil alternant face ca a doua piesă identică să fie anunțată și ea
  const [liveSel, setLiveSel] = useState(''), liveFlip = useRef(false);
  useEffect(() => { const tm = setTimeout(() => { if (!selDesc || !sel){ setLiveSel(''); return; } liveFlip.current = !liveFlip.current;
      const keys = ['placement', 'room', 'stair', 'column', 'wall_item'].includes(sel.kind) ? t(`mirror.keys.${sel.kind}`) : t('mirror.keys.other');
      // din listă tastele de editare nu acționează (săgețile trec prin obiecte): mesajul spune întâi cum se ajunge în plan
      const inList = !!(document.activeElement as HTMLElement | null)?.closest?.('.plan-mirror');
      setLiveSel(`${t('mirror.selected', { label: selDesc })}. ${inList ? t('mirror.escFirst') + ' ' : ''}${keys}${liveFlip.current ? ' ' : ''}`); }, 350); return () => clearTimeout(tm); }, [selDesc, sel?.kind, sel?.id, t]); // eslint-disable-line
  // hover-ul comun nu rămâne agățat când una din vederi dispare (schimbarea Plan / 3D)
  useEffect(() => { setHoverPid(null); }, [view]);
  // (hook înainte de orice return timpuriu) modul „document lat” al panoului, ținut minte în browser
  useEffect(() => { try { setWideSide(localStorage.getItem('casa3d.sideWide') === '1'); } catch { /* stocare blocată */ } }, []);
  if (!snap || !catalog || !mc) return toast ? <div className="home"><p className="muted" role="status">{toast}</p></div> : <EditorSkeleton label={t('editor.loading')} />;
  const G = groups(catalog);
  const onAddWall = (a: [number, number], b: [number, number]) => mutate(s => { s.floor.walls.push({ id: `wall-${uid().slice(0, 8)}`, a, b, thickness: SEGMENT_THICKNESS.wall, exterior: false, openings: [] }); });
  // lungimea peretelui (inspector și cota din plan): core/wall-edit.ts păstrează colțul închis; un refuz se anunță, nu se aplică
  const onSetWallLength = (wallId: string, m: number): boolean => { const w = snap.floor.walls.find(x => x.id === wallId);
    if (w && Math.abs(wallLength(w.a, w.b) - m) < 5e-4) return true;   // aceeași lungime: niciun pas de anulare gol
    try { mutate(s => { s.floor = setWallLength(s.floor, wallId, m); }); return true; }
    catch (e) { if (e instanceof WallEditError){ say(t(e.key)); return false; } throw e; } };
  const onAddRoom = (rect: any) => { const rid = `camera-${uid().slice(0, 8)}`; mutate(s => { s.floor.rooms.push({ id: rid, name: t('editor.newRoomName', { n: s.floor.rooms.length + 1 }), type: 'living', rect }); }); setSel({ kind: 'room', id: rid }); setTool('select'); };
  // golul de trecere e o ușă fără foaie (`passage`): devizul îl scade ca pe o ușă, dar nu cere produs de ușă
  const onAddOpening = (wallId: string, offset: number, kind: 'door' | 'window' | 'passage') => { const w = snap.floor.walls.find(x => x.id === wallId)!, L = wallLength(w.a, w.b), width = kind === 'door' ? .8 : kind === 'passage' ? PLAN_DEFAULTS.passageWidth : 1.2;
    if (L < width + .1){ say(t('editor.wallTooShort')); return; } const oid = `op-${uid().slice(0, 8)}`;
    mutate(s => { s.floor.walls.find(x => x.id === wallId)!.openings.push({ id: oid, kind: kind === 'window' ? 'window' : 'door', offset: r3(Math.max(0, Math.min(L - width, offset - width / 2))), width, ...(kind === 'passage' ? { passage: true as const } : {}) }); }); setSel({ kind: 'opening', id: oid, wallId }); };
  // ghena nouă: 30 × 30 cm până la tavan; rolul și înălțimea se schimbă în panou
  // peste limita serverului (PLAN_LIMITS.*.max pe nivel) salvarea întregului proiect ar eșua: refuzăm la plasare
  const full = (n: number | undefined, max: number) => { if ((n ?? 0) < max) return false; say(t('editor.limitReached', { max })); return true; };
  const onAddColumn = (x: number, z: number, shape: 'rect' | 'round' = 'rect', role?: 'shaft') => {
    if (full(snap.floor.columns?.length, PLAN_LIMITS.column.max)) return;
    const cid = `col-${uid().slice(0, 8)}`, w = role ? PLAN_DEFAULTS.shaft : .35;
    mutate(s => { s.floor.columns = [...(s.floor.columns || []), { id: cid, x: r3(x), z: r3(z), w, d: w, shape, ...(role ? { role } : {}) }]; });
    setSel({ kind: 'column', id: cid }); setTool('select');
  };
  // nișa nouă: 60 × 40 × 10 cm la 110 cm de pardoseală, pe fața peretelui unde s-a dat clic; mărimile se schimbă în panou
  // placare nouă (unealta „Placare”): pe porțiunea aleasă sau pe toată latura, cu primul produs al tipului ales; benzile existente
  // de pe latură care o acoperă se taie în jurul ei (vopseaua de accent rămâne stânga și dreapta); camera se deschide în panou
  const onAddClad = (roomId: string, side: WallFeature['side'], from?: number, to?: number) => { const room = snap.floor.rooms.find(r => r.id === roomId); if (!room) return;
    const feats = finishesOf(snap, room).wallFeatures || [], len = sideSpan(room, side), mat = mc?.materials.find(m => m.category === FEATURE_CATEGORY[cladKind])?.id;
    if (!mat){ say(t('fin.noProduct')); return; }
    const whole = from == null || to == null || (from <= .01 && to >= len - .01), add: WallFeature = { side, kind: cladKind, material: mat, ...(whole ? {} : { alongFromM: r3(from), alongToM: r3(to) }) };
    const next = insertBand(feats, add, len, snap.floor.ceilingHeight); if (next.length > 24){ say(t('editor.limitReached', { max: 24 })); return; }
    mutate(s => { s.finishes = { ...(s.finishes || {}), [roomId]: { ...finishesOf(s, room), wallFeatures: next } }; });
    setSel({ kind: 'room', id: roomId }); setCladFocus({ room: roomId, i: next.length - 1, n: Date.now() });
    say(t('fin.cladAdded', { kind: t(`fin.kind.${cladKind}`), room: room.name })); };
  const onAddNiche = (wallId: string, offset: number, side: 1 | -1) => { if (full(snap.floor.niches?.length, PLAN_LIMITS.niche.max)) return; const w = snap.floor.walls.find(x => x.id === wallId)!, L = wallLength(w.a, w.b), D = PLAN_DEFAULTS.niche, width = Math.min(D.width, r3(L - .1));
    if (width < PLAN_LIMITS.niche.widthMinM){ say(t('editor.wallTooShort')); return; } const nid = `nisa-${uid().slice(0, 8)}`, depth = r3(Math.max(PLAN_LIMITS.niche.depthMinM, Math.min(D.depth, w.thickness - .05)));
    mutate(s => { s.floor.niches = [...(s.floor.niches || []), { id: nid, wallId, offset: r3(Math.max(0, Math.min(L - width, offset - width / 2))), width, height: D.height, depth, elevation: D.elevation, side }]; });
    setSel({ kind: 'niche', id: nid }); setTool('select'); };
  // cote și texte de planșă: rămân selectate după plasare, ca restul uneltelor
  const onAddDim = (a: [number, number], b: [number, number], offset: number, mode: 'aligned' | 'ortho') => { if (full(snap.floor.annotations?.length, PLAN_LIMITS.annotation.max)) return; const aid = `cota-${uid().slice(0, 8)}`;
    mutate(s => { s.floor.annotations = [...(s.floor.annotations || []), { id: aid, kind: 'dim', a, b, offset: r3(offset), ...(mode === 'ortho' ? { mode } : {}) }]; }); setSel({ kind: 'annotation', id: aid }); setTool('select'); };
  const onAddText = (at: [number, number], text: string) => { const v = cleanPlanText(text); if (!v || full(snap.floor.annotations?.length, PLAN_LIMITS.annotation.max)) return; const aid = `text-${uid().slice(0, 8)}`;
    mutate(s => { s.floor.annotations = [...(s.floor.annotations || []), { id: aid, kind: 'text', at, text: v }]; }); setSel({ kind: 'annotation', id: aid }); setTool('select'); };
  const patchAnn = (aid: string, fn: (a: Annotation) => void) => mutate(s => { const a = s.floor.annotations?.find(x => x.id === aid); if (a) fn(a); });
  const patchNiche = (nid: string, fn: (n: Niche) => void) => mutate(s => { const n = s.floor.niches?.find(x => x.id === nid); if (n) fn(n); });
  // obiect pe perete nou (unealta „Pe perete”): mărimea și cota implicite ale tipului; tabloul deasupra canapelei, a patului sau a
  // comodei are marginea de jos la 20 cm peste piesă; rămâne selectat, mărimile se schimbă în panou
  const onAddWallItem = (wallId: string, offset: number, side: 1 | -1) => { if (!catalog || full(snap.floor.wallItems?.length, PLAN_LIMITS.wallItem.max)) return; const w = snap.floor.walls.find(x => x.id === wallId)!, L = wallLength(w.a, w.b);
    const probe = { wallId, side, offset, width: Math.min(wallItemDefaults(wallItemKind).width, r3(L - .1)) }; if (probe.width < PLAN_LIMITS.wallItem.widthMinM){ say(t('editor.wallTooShort')); return; }
    const d = wallItemDefaults(wallItemKind, furnitureBelow(snap, catalog, snap.floor, probe)), wid = `perete-${uid().slice(0, 8)}`, width = probe.width;
    const it: WallItem = { id: wid, wallId, side, kind: wallItemKind, offset: r3(Math.max(width / 2, Math.min(L - width / 2, offset))), elevation: r3(Math.min(snap.floor.ceilingHeight - d.height / 2, d.elevation)), width, height: d.height };
    mutate(s => { s.floor.wallItems = [...(s.floor.wallItems || []), it]; }); setSel({ kind: 'wall_item', id: wid }); setTool('select'); };
  const patchWallItem = (wid: string, fn: (n: WallItem) => void) => mutate(s => { const n = s.floor.wallItems?.find(x => x.id === wid); if (n) fn(n); });
  // starea peretelui: lipsă = existent (câmpul se șterge, ca proiectul să rămână ca înainte)
  const setWallStatus = (wid: string, st: WallStatus) => mutate(s => { const w = s.floor.walls.find(x => x.id === wid); if (!w) return; if (st === 'existing') delete w.status; else w.status = st; });
  // unealta „Demolare”: clic pe perete comută existent ↔ de demolat; unealta rămâne activă pentru peretele următor
  const onToggleDemolish = (wid: string) => { const w = snap.floor.walls.find(x => x.id === wid); if (!w) return; const next: WallStatus = w.status === 'demolish' ? 'existing' : 'demolish';
    setWallStatus(wid, next); setSel({ kind: 'wall', id: wid }); say(t(next === 'demolish' ? 'wallStatus.marked' : 'wallStatus.restored', { len: formatLength(wallLength(w.a, w.b), units, lang) })); };
  // zona de pardoseală nouă: în camera în care cade mijlocul dreptunghiului, decupată la cameră; materialul implicit contrastează cu al camerei
  const onAddZone = (r: { x0: number; z0: number; x1: number; z1: number }) => { if (full(snap.floor.zones?.length, PLAN_LIMITS.zone.max)) return;
    const cx = (r.x0 + r.x1) / 2, cz = (r.z0 + r.z1) / 2, room = snap.floor.rooms.find(q => cx >= q.rect.x0 && cx <= q.rect.x1 && cz >= q.rect.z0 && cz <= q.rect.z1);
    if (!room){ say(t('zone.noRoom')); return; } const c = intersect(room.rect, r); if (!c || c.x1 - c.x0 < PLAN_LIMITS.zone.minM || c.z1 - c.z0 < PLAN_LIMITS.zone.minM){ say(t('zone.tooSmall')); return; }
    const mat = defaultZoneMaterial(mc, finishesOf(snap, room).floor); if (!mat){ say(t('fin.noProduct')); return; }
    const zid = `zona-${uid().slice(0, 8)}`, z: FloorZone = { id: zid, roomId: room.id, rect: { x0: r3(c.x0), z0: r3(c.z0), x1: r3(c.x1), z1: r3(c.z1) }, floor: mat };
    mutate(s => { s.floor.zones = [...(s.floor.zones || []), z]; }); setSel({ kind: 'floor_zone', id: zid }); setTool('select'); say(t('zone.added', { room: room.name })); };
  const patchZone = (zid: string, fn: (z: FloorZone) => void) => mutate(s => { const z = s.floor.zones?.find(x => x.id === zid); if (z) fn(z); });
  // tavanul fals pe zonă nou: în camera în care cade mijlocul dreptunghiului, decupat la cameră, coborât 15 cm, fără scafă și fără LED
  const onAddCeilingZone = (r: { x0: number; z0: number; x1: number; z1: number }) => { if (full(snap.floor.ceilingZones?.length, PLAN_LIMITS.ceilingZone.max)) return;
    const cx = (r.x0 + r.x1) / 2, cz = (r.z0 + r.z1) / 2, room = snap.floor.rooms.find(q => cx >= q.rect.x0 && cx <= q.rect.x1 && cz >= q.rect.z0 && cz <= q.rect.z1);
    if (!room){ say(t('czone.noRoom')); return; } const c = intersect(room.rect, r); if (!c || c.x1 - c.x0 < PLAN_LIMITS.ceilingZone.minM || c.z1 - c.z0 < PLAN_LIMITS.ceilingZone.minM){ say(t('czone.tooSmall')); return; }
    const zid = `tavan-${uid().slice(0, 8)}`, z: CeilingZone = { id: zid, roomId: room.id, rect: { x0: r3(c.x0), z0: r3(c.z0), x1: r3(c.x1), z1: r3(c.z1) }, dropCm: PLAN_DEFAULTS.ceilingZone.dropCm };
    mutate(s => { s.floor.ceilingZones = [...(s.floor.ceilingZones || []), z]; }); setSel({ kind: 'ceiling_zone', id: zid }); setTool('select'); say(t('czone.added', { room: room.name })); };
  const patchCeilingZone = (zid: string, fn: (z: CeilingZone) => void) => mutate(s => { const z = s.floor.ceilingZones?.find(x => x.id === zid); if (z) fn(z); });
  // balustrada nouă: metal, 1 m; unealta rămâne activă (clic-clic în lanț, ca grinda și peretele de sticlă)
  const onAddRailing = (a: [number, number], b: [number, number]) => { if (full(snap.floor.railings?.length, PLAN_LIMITS.railing.max)) return;
    const rid = `bal-${uid().slice(0, 8)}`; mutate(s => { s.floor.railings = [...(s.floor.railings || []), { id: rid, a, b }]; }); say(t('railing.added', { len: formatLength(wallLength(a, b), units, lang) })); };
  const patchRailing = (rid: string, fn: (r: Railing) => void) => mutate(s => { const r = s.floor.railings?.find(x => x.id === rid); if (r) fn(r); });
  const patchTech = (tid: string, fn: (p: TechPoint) => void) => mutate(s => { const p = s.tech?.find(x => x.id === tid); if (p) fn(p); });
  const onAddBeam = (a: [number, number], b: [number, number]) => {
    if (full(snap.floor.beams?.length, PLAN_LIMITS.beam.max)) return;
    const bid = `bm-${uid().slice(0, 8)}`;
    mutate(s => { s.floor.beams = [...(s.floor.beams || []), { id: bid, a, b, width: SEGMENT_THICKNESS.beam, depth: .35 }]; });
    setSel({ kind: 'beam', id: bid }); setTool('select');
  };
  const onAddGlassWall = (a: [number, number], b: [number, number]) => {
    if (full(snap.floor.glassWalls?.length, PLAN_LIMITS.glassWall.max)) return;
    const gid = `gw-${uid().slice(0, 8)}`;
    mutate(s => { s.floor.glassWalls = [...(s.floor.glassWalls || []), { id: gid, a, b, thickness: SEGMENT_THICKNESS.glass_wall, mullions: 2, frameColor: '#23262B' }]; });
    setSel({ kind: 'glass_wall', id: gid }); setTool('select');
  };
  const onAddStairAt = (x: number, z: number) => {
    const sid = `scara-${uid().slice(0, 8)}`;
    const st: Stair = { id: sid, x: r3(x), z: r3(z), width: .9, length: 2.5, rotation: 0 };
    mutate(s => { s.floor.stairs = [...(s.floor.stairs || []), st]; });
    setSel({ kind: 'stair', id: sid }); setTool('select');
  };
  const rotateColumn = (cid: string) => mutate(s => { const c = s.floor.columns?.find(x => x.id === cid); if (c) { const tmp = c.w; c.w = c.d; c.d = tmp; } });
  // pe telefon nu e loc pentru plan + 3D unul sub altul: etapele care îl folosesc pornesc pe plan
  // modul „document lat” al panoului (UI/UX P2-9): formularele lungi (client, contract, releveu) au loc; ținut minte în browser
  const toggleWide = () => setWideSide(w => { try { localStorage.setItem('casa3d.sideWide', w ? '0' : '1'); } catch { /* stocare blocată */ } return !w; });
  // selecția comună plan ↔ 3D (UI/UX P2-10): „Arată în 3D” deschide vederea 3D lângă plan și încadrează piesa;
  // „Arată în plan” face invers. Pe ecran îngust vederea se schimbă (nu încap amândouă).
  const pickMirror = (i: MirrorItem) => { const s: Sel = i.kind === 'opening' ? { kind: 'opening', id: i.id, wallId: i.wallId! } : { kind: i.kind, id: i.id } as Sel; setSel(s); setFrame(f => ({ sel: s, n: (f?.n ?? 0) + 1, at: Date.now() })); };
  const frameIn3d = (pid: string) => { if (view === '2d') setView(compact ? '3d' : 'split'); setFrame3d(f => ({ id: pid, n: (f?.n ?? 0) + 1, at: Date.now() })); };
  const frameInPlan = (pid: string) => { const s: Sel = { kind: 'placement', id: pid }; setSel(s); if (view === '3d') setView(compact ? '2d' : 'split'); setFrame(f => ({ sel: s, n: (f?.n ?? 0) + 1, at: Date.now() })); };
  // schimbarea etapei animă doar panoul lateral (View Transition, lib/motion.ts); flushSync: DOM-ul nou trebuie să existe în callback
  // `after`: ce mai schimbă apelantul, în aceeași actualizare (tranziția rulează callback-ul mai târziu, deci după
  // orice setState făcut imediat după apel)
  const setStage = (s: Stage, after?: () => void) => { const st = STAGES.find(x => x.id === s)!; withViewTransition(() => flushSync(() => { setStageRaw(s); setView(st.view === 'split' && isCompact() ? '2d' : st.view); if (!st.tools.includes(tool)) setTool('select'); if (s === 'mep') setShowTech(true); setSideOpen(true); setSheet('half'); after?.(); })); };
  const nextId = nextStage(stage);
  // stările goale „desenează camerele”: etapa Releveu cu unealta Cameră, plan vizibil (și foaia de jos coborâtă pe ecranele mici)
  const drawRooms = () => { const go = () => { setView('2d'); setTool('room'); if (compact) setSheet('peek'); }; if (stage !== 'survey') setStage('survey', go); else go(); };
  const stageDef = STAGES.find(x => x.id === stage)!, warnCount = advice.filter(a => a.severity !== 'TIP').length;
  const total = snap.placements.reduce((a, p) => a + (resolve(catalog, p.variantId)?.offer?.price || 0), 0);
  const selPl = sel?.kind === 'placement' ? snap.placements.find(p => p.id === sel.id) : undefined, selRoom = sel?.kind === 'room' ? snap.floor.rooms.find(r => r.id === sel.id) : undefined;
  const shown = (c: Snapshot): Snapshot => c.levels?.length || !house!.levels?.length ? levelView(c, Math.min(L, floors(c).length - 1)) : levelView({ ...house!, placements: c.placements }, L);
  // golurile scărilor de dedesubt, cu direcția de urcare (latura de sosire rămâne fără balustradă în 3D)
  const voids = stairwells(house!, L);
  const selStair = sel?.kind === 'stair' ? snap.floor.stairs?.find(s => s.id === sel.id) : undefined, nLevels = floors(house!).length;
  const selWall = sel?.kind === 'wall' ? snap.floor.walls.find(w => w.id === sel.id) : undefined, selOp = sel?.kind === 'opening' ? snap.floor.walls.find(w => w.id === sel.wallId)?.openings.find(o => o.id === sel.id) : undefined;
  const selColumn = sel?.kind === 'column' ? snap.floor.columns?.find(c => c.id === sel.id) : undefined;
  const selBeam = sel?.kind === 'beam' ? snap.floor.beams?.find(b => b.id === sel.id) : undefined;
  const selGlassWall = sel?.kind === 'glass_wall' ? snap.floor.glassWalls?.find(g => g.id === sel.id) : undefined;
  const selAnn = sel?.kind === 'annotation' ? snap.floor.annotations?.find(a => a.id === sel.id) : undefined;
  const selNiche = sel?.kind === 'niche' ? snap.floor.niches?.find(n => n.id === sel.id) : undefined;
  const selWallItem = sel?.kind === 'wall_item' ? snap.floor.wallItems?.find(n => n.id === sel.id) : undefined;
  const selZone = sel?.kind === 'floor_zone' ? snap.floor.zones?.find(z => z.id === sel.id) : undefined;
  const selCeilingZone = sel?.kind === 'ceiling_zone' ? snap.floor.ceilingZones?.find(z => z.id === sel.id) : undefined;
  const selRailing = sel?.kind === 'railing' ? snap.floor.railings?.find(r => r.id === sel.id) : undefined;
  const selAnnounce = selPl ? t('plan.selPiece', { name: resolve(catalog, selPl.variantId)?.variant.name ?? selPl.group }) : selRoom ? t('plan.selRoom', { name: selRoom.name }) : selStair ? t('plan.selStair') : selWall ? t('editor.wall') : selColumn ? t(selColumn.role === 'shaft' ? 'tool.shaft' : selColumn.role === 'boxing' ? 'column.boxing' : 'tool.column') : selBeam ? t('tool.beam') : selGlassWall ? t('tool.glass_wall') : selNiche ? t('tool.niche') : selWallItem ? t(`wallItem.kind.${selWallItem.kind}`) : selZone ? t('tool.floor_zone') : selCeilingZone ? t('tool.ceiling_zone') : selRailing ? t('tool.railing') : selAnn ? t(selAnn.kind === 'dim' ? 'tool.dim' : 'tool.text') : '';
  // „Adaugă în cameră” fără cameră aleasă: camera potrivită grupei (dormitor pentru pat), altfel cea mai mare
  const roomsFor = (vid: string) => { const g = groupOf(vid), bySize = [...snap.floor.rooms].sort((a, b) => area(b.rect) - area(a.rect));
    return [...bySize.filter(r => groupsForRoom(r.type, [g]).suggested.includes(g)), ...bySize.filter(r => !groupsForRoom(r.type, [g]).suggested.includes(g))]; };
  const addToRoom = (vid: string, room?: Room | null) => { const cands = room ? [room] : roomsFor(vid); if (!cands.length){ say(t('editor.noRoomYet')); return; }
    for (const r0 of cands){ const r = addPlacement(snap, catalog, r0.id, vid); if (!r) continue;
      commit(r); setSel({ kind: 'placement', id: r.placements.at(-1)!.id }); if (!room) say(t('editor.addedTo', { room: r0.name })); return; }
    say(t('editor.noFreeSpot')); };
  // ce se vede în panou: selecția (dacă etapa o folosește) sau conținutul etapei
  const roomStage = stage === 'survey' || stage === 'finishes' || stage === 'ffe';
  const showSel = !!(selAnn ? stage === 'survey' || stage === 'docs' : selZone || selCeilingZone ? roomStage || stage === 'concept' : selRailing ? roomStage : selWallItem ? stage === 'concept' || stage === 'finishes' || stage === 'ffe' : selColumn ? roomStage || stage === 'mep' : selNiche ? roomStage || stage === 'concept' : (selRoom || selWall || selOp || selStair || selBeam || selGlassWall) ? roomStage : selPl ? stage === 'ffe' || stage === 'concept' || stage === 'finishes' || stage === 'present' : false);
  // câmpurile numerice sunt în cm (metric) sau inci (imperial); `f` și `min` primesc mereu cm, datele rămân metrice
  // în inci, minimul afișat (ex. 78,7″ pentru 200 cm) se întoarce puțin sub pragul în cm: acceptăm o jumătate de inci toleranță și fixăm la minim
  const num = (v: string, f: (x: number) => void, min = 0) => { const x = v.trim() === '' ? 0 : inputToCm(v, units), tol = units === 'imperial' ? 1.27 : 1e-9; if (x != null && x >= min - tol) f(Math.max(x, min)); };
  const cur = catalogCurrency(catalog), money = (v: number | null | undefined) => formatMoney(v, cur, lang, t('common.unknownPrice')), when = (d: string) => new Date(d).toLocaleString(intlLocale(lang)), inField = (m: number) => cmToInput(m * 100, units);

  const hint = (cid: string) => commandHint(cid, { mac }), stageName = (sid: Stage) => t(`stage.${sid}`);
  const commands: Command[] = [
    ...(nextId ? [{ id: 'next', group: t('cmdk.group.next'), label: t('cmdk.continue', { stage: stageName(nextId) }), keywords: [t('stage.continue'), t('cmdk.kw.next')], done: !!done?.[nextId], run: () => setStage(nextId) }] : []),
    ...STAGES.map((st, i) => ({ id: `st-${st.id}`, group: t('cmdk.group.stages'), label: t('cmdk.goStage', { n: String(i + 1).padStart(2, '0'), stage: stageName(st.id) }), keywords: [stageName(st.id), t(`phase.${phaseOf(st.id)}`)],
      hint: stage === st.id ? t('stage.current') : undefined, done: !!done?.[st.id], run: () => setStage(st.id) })),
    ...(stageDef.page ? [] : [{ id: 'v-2d', group: t('cmdk.group.views'), label: t('cmdk.view2d'), keywords: ['2D'], run: () => { setView('2d'); if (compact) setSheet('peek'); } },
      { id: 'v-split', group: t('cmdk.group.views'), label: t('cmdk.viewSplit'), disabled: compact, run: () => setView('split') },
      { id: 'v-3d', group: t('cmdk.group.views'), label: t('cmdk.view3d'), run: () => { setView('3d'); if (compact) setSheet('peek'); } }]),
    // UI/UX P1-5: paleta acoperă tot ce se poate face din editor: unelte (cu scurtătura din registru), obiecte după nume,
    // setări, toate documentele
    ...(stageDef.page ? [] : stageDef.tools.map(tk => { const k = formatShortcut(toolShortcut(tk), { mac });
      return { id: `tool-${tk}`, group: t('cmdk.group.tools'), label: t(TOOL_LABEL[tk]), keywords: [t('cmdk.kw.tool')],
        hint: tool === tk ? [t('cmdk.active'), k].filter(Boolean).join(' · ') : k || undefined, run: () => setTool(tk) }; })),
    { id: 'undo', group: t('cmdk.group.actions'), label: t('editor.undo'), hint: hint('undo'), keywords: [t('cmdk.kw.undo')], disabled: !hist.current.canUndo, run: undo },
    { id: 'redo', group: t('cmdk.group.actions'), label: t('editor.redo'), hint: hint('redo'), disabled: !hist.current.canRedo, run: redo },
    { id: 'rev', group: t('cmdk.group.actions'), label: t('editor.saveRevision'), keywords: [t('cmdk.kw.save')], disabled: revBusy, run: () => { setRevBusy(true); flight.run('revision', saveRevision).finally(() => setRevBusy(false)); } },
    { id: 'help', group: t('cmdk.group.actions'), label: t('editor.shortcuts'), hint: hint('help'), run: () => setHelp(true) },
    { id: 'print', group: t('cmdk.group.docs'), label: t('cmdk.print'), keywords: [t('cmdk.kw.print')], run: () => { window.open(`/p/${id}/print`, '_blank', 'noopener'); } },
    { id: 'present', group: t('cmdk.group.docs'), label: t('cmdk.present'), run: () => { window.open(`/p/${id}/present`, '_blank', 'noopener'); } },
    { id: 'board', group: t('cmdk.group.docs'), label: t('cmdk.board'), run: () => { window.open(`/p/${id}/board`, '_blank', 'noopener'); } },
    { id: 'home', group: t('cmdk.group.actions'), label: t('cmdk.home'), run: () => { location.href = '/'; } },
    ...(stageDef.page ? [] : snap.floor.rooms.map(r => ({ id: `room-${r.id}`, group: t('cmdk.group.objects'), label: r.name, keywords: [t(`roomType.${r.type}`), t('cmdk.kw.room')],
      hint: t('cmdk.room'), run: () => { setTool('select'); setSel({ kind: 'room', id: r.id }); } }))),
    ...(stageDef.page ? [] : snap.placements.map(pl => { const rv = resolve(catalog, pl.variantId), room = snap.floor.rooms.find(r => r.id === pl.roomId)?.name;
      return { id: `pl-${pl.id}`, group: t('cmdk.group.objects'), label: rv ? rv.product.name : pl.group, keywords: [rv?.variant.name ?? '', room ?? '', pl.group],
        hint: room, run: () => { setTool('select'); setSel({ kind: 'placement', id: pl.id }); } }; })),
    ...(sel?.kind === 'placement' ? [{ id: 'frame-other', group: t('cmdk.group.actions'), label: t('cmdk.frameOther'), keywords: ['3d', 'plan', 'frame', t('hud.frame3d'), t('viewer.showInPlan')], run: () => (view === '3d' ? frameInPlan(sel.id) : frameIn3d(sel.id)) }] : []),
    { id: 'health', group: t('cmdk.group.actions'), label: t('cmdk.health'), keywords: [t('cmdk.kw.health')], run: () => setShowHealth(true) },
    { id: 'lang', group: t('cmdk.group.settings'), label: LANGS.find(l => l.code !== lang)!.name, keywords: [t('cmdk.kw.lang'), 'language', 'limba'], run: () => setLang(lang === 'ro' ? 'en' : 'ro') },
    { id: 'units', group: t('cmdk.group.settings'), label: units === 'metric' ? t('cmdk.unitsImperial') : t('cmdk.unitsMetric'), keywords: [t('cmdk.kw.units')], run: () => setUnits(units === 'metric' ? 'imperial' : 'metric') },
    ...(['offer', 'contract', 'proforma', 'handover', 'site-report'] as const).map(d => ({ id: `doc-${d}`, group: t('cmdk.group.docs'), label: t(`cmdk.doc.${d}`), keywords: [t('cmdk.kw.print')],
      run: () => { window.open(`/p/${id}/${d}`, '_blank', 'noopener'); } })),
  ];

  return (<div className="ws spatial-studio" data-ui="spatial-studio">
    <CommandPalette open={cmdOpen} onClose={() => setCmdOpen(false)} commands={commands} />
    {(!persistent || demo) && <div className="banner">{t(demo ? 'editor.publicDemoBanner' : 'editor.demoBanner')}</div>}
    <header className="topbar studio-topbar">
      <a className="btn ghost icon-btn" href="/" aria-label={t('home.projects')} title={t('home.projects')}><Icon name="back" /><span className="lbl">{t('home.projects')}</span></a>
      <input className="name" value={snap.name} aria-label={t('editor.projectName')} maxLength={120} onChange={e => { const n = { ...house!, name: e.target.value }; setHouse(n); persist(n); }} />
      <SaveChip save={save} rev={rev} />
      <div className="tb-actions">
      <button type="button" className="btn ghost cmdk-trigger" aria-haspopup="dialog" aria-expanded={cmdOpen} aria-keyshortcuts="Control+K Meta+K" title={`${t('cmdk.open')} (${hint('palette')})`} onClick={() => setCmdOpen(true)}><Icon name="search" /><span className="lbl">{t('cmdk.openShort')}</span><kbd className="cmdk-kbd" aria-hidden="true">{hint('palette')}</kbd></button>
      <button className="btn ghost icon-btn" onClick={undo} disabled={!hist.current.canUndo} title={hist.current.canUndo ? `${t('editor.undo')} (${hint('undo')})` : t('editor.nothingToUndo')} aria-label={t('editor.undo')} aria-keyshortcuts={ariaShortcut(commandById('undo')!.shortcuts[0]!, { mac })}><Icon name="undo" /><span className="lbl">{t('editor.undo')}</span></button>
      <button className="btn ghost icon-btn" onClick={redo} disabled={!hist.current.canRedo} title={hist.current.canRedo ? `${t('editor.redo')} (${hint('redo')})` : t('editor.nothingToRedo')} aria-label={t('editor.redo')} aria-keyshortcuts={ariaShortcut(commandById('redo')!.shortcuts[0]!, { mac })}><Icon name="redo" /><span className="lbl">{t('editor.redo')}</span></button>
      {!stageDef.page && <div className="seg" role="group" aria-label={t('editor.view')}>
        {compact ? (['2d', '3d', 'panel'] as const).map(vv => { const on = vv === 'panel' ? sheet === 'full' : view === vv && sheet !== 'full';
            return <button key={vv} aria-pressed={on} onClick={() => { if (vv === 'panel'){ setSheet('full'); setSideOpen(true); } else { setView(vv); setSheet('peek'); } }}><Icon name={vv === '2d' ? 'plan' : vv === '3d' ? 'cube' : 'panel'} /><span>{vv === '2d' ? t('editor.view2d') : vv === '3d' ? '3D' : t('editor.viewPanel')}</span></button>; })
          : (['2d', 'split', '3d'] as const).map(vv => <button key={vv} className={`v-${vv}`} aria-pressed={view === vv} onClick={() => setView(vv)}>{vv === '2d' ? t('editor.view2d') : vv === '3d' ? '3D' : t('editor.viewSplit')}</button>)}
      </div>}
      <span className="prefs-inline"><PrefsSwitcher /></span>
      {health && (
        <button
          type="button"
          className="btn sm"
          style={{
            height: 32,
            padding: '0 10px',
            gap: 6,
            borderRadius: '999px',
            backgroundColor: health.score >= 85 ? 'var(--ok-soft)' : health.score >= 65 ? 'var(--warn-soft)' : 'var(--err-soft)',
            borderColor: health.score >= 85 ? 'var(--ok)' : health.score >= 65 ? 'var(--warn)' : 'var(--err)',
            color: health.score >= 85 ? 'var(--ok)' : health.score >= 65 ? 'var(--warn)' : 'var(--err)',
            fontWeight: 600,
          }}
          title={t('health.scoreTitle')}
          onClick={() => setShowHealth(true)}
        >
          <span>{health.score}%</span>
          <small style={{ fontWeight: 400, opacity: 0.85 }}>
            {t('health.short')}
          </small>
        </button>
      )}
      <details className="tb-more"><summary className="btn ghost icon-btn" aria-label={t('editor.more')} title={t('editor.more')}><Icon name="more" /></summary><div className="tb-menu"><PrefsSwitcher /><button type="button" className="btn ghost" onClick={() => setHelp(true)}>{t('editor.shortcuts')}</button></div></details>
      </div>
      <button className="btn primary tb-save" disabled={revBusy} title={t('dlg.revBody')} onClick={() => { setRevBusy(true); flight.run('revision', saveRevision).finally(() => setRevBusy(false)); }}><span className="lbl">{t('editor.saveRevision')}</span><span className="lbl-s">{t('editor.saveShort')}</span></button>
      {save === 'conflict' && <span className="conflict-bar" role="alert"><Icon name="alert" />{t('editor.conflict')} <button className="btn" onClick={() => { dropStash(); saveRef.current = 'saved'; location.reload(); }}>{t('editor.conflictReload')}</button><button className="btn" onClick={() => send(true)}>{t('editor.conflictKeep')}</button></span>}
      {save === 'offline' && <span className="conflict-bar save-alert" role="alert"><Icon name="cloudOff" />{t('editor.offlineNote')} <button className="btn" onClick={() => { attempt.current = 0; send(); }}>{t('editor.retryNow')}</button></span>}
      {save === 'auth' && <span className="conflict-bar save-alert" role="alert"><Icon name="alert" />{t('editor.sessionExpired')} <a className="btn" href={reloginUrl(`/p/${id}`)} onClick={() => { if (latest.current) stashDraft(store(), id, latest.current, baseRev.current); saveRef.current = 'saved'; }}>{t('editor.relogin')}</a></span>}
      {stash && <span className="conflict-bar save-alert" role="alert">{t('editor.stashFound', { time: new Date(stash.at).toLocaleTimeString(intlLocale(lang), { hour: '2-digit', minute: '2-digit' }) })} <button className="btn" onClick={takeStash}>{t('editor.stashRestore')}</button><button className="btn" onClick={dropStash}>{t('editor.stashDrop')}</button></span>}
    </header>
    <div className={`stagewrap studio-stagewrap ${barFade.over ? 'is-over' : ''}`}>
    <button type="button" className="stage-step" aria-label={t('stage.prev')} title={t('stage.prev')} disabled={STAGES.indexOf(stageDef) === 0} onClick={() => setStage(STAGES[Math.max(0, STAGES.indexOf(stageDef) - 1)]!.id)}><Icon name="chevronLeft" /></button>
    <StagePicker stages={STAGES} phases={PHASES} current={stage} done={done} warn={warnCount} onPick={id => setStage(id as Stage)} />
    <nav className={`stagebar ${barFade.l ? 'fade-l' : ''} ${barFade.r ? 'fade-r' : ''}`} aria-label={t('stage.aria')} ref={stageBarRef} onScroll={measureBar}>
      {PHASES.map(ph => <div key={ph.id} className="phase" role="group" aria-label={t('phase.aria', { phase: t(`phase.${ph.id}`) })} data-phase={ph.id}>
        <span className="phase-label" aria-hidden="true">{t(`phase.${ph.id}`)}</span>
        {ph.stages.map(sid => { const st = STAGES.find(x => x.id === sid)!, i = STAGES.indexOf(st), ok = !!done?.[sid];
          return <button key={st.id} data-stage={st.id} aria-current={stage === st.id ? 'step' : undefined} title={t(`stage.why.${st.id}`)} aria-label={`${String(i + 1).padStart(2, '0')} ${t(`stage.${st.id}`)}${st.id === 'docs' && warnCount > 0 ? ` ${warnCount}` : ''} · ${ok ? t('stage.doneAria') : t('stage.todoAria')}${st.id === 'docs' && warnCount > 0 ? ` · ${t('stage.warnAria', { n: warnCount })}` : ''}`} onClick={() => setStage(st.id)} onFocus={e => { try { e.currentTarget.scrollIntoView({ block: 'nearest', inline: 'nearest' }); } catch { /* browser vechi */ } }}><i aria-hidden="true">{String(i + 1).padStart(2, '0')}</i>{' '}<span className={`st-dot ${ok ? 'is-done' : ''}`} aria-hidden="true" /><span className="st-name" aria-hidden="true">{t(`stage.${st.id}`)}</span>{st.id === 'docs' && warnCount > 0 && ' '}{st.id === 'docs' && warnCount > 0 && <b className="count" title={t('stage.checks')} aria-hidden="true">{warnCount}</b>}</button>; })}
      </div>)}
    </nav>
    <button type="button" className="stage-step" aria-label={t('stage.next')} title={t('stage.next')} disabled={STAGES.indexOf(stageDef) === STAGES.length - 1} onClick={() => setStage(STAGES[Math.min(STAGES.length - 1, STAGES.indexOf(stageDef) + 1)]!.id)}><Icon name="chevronRight" /></button>
    </div>
    {stageDef.page ? <div className="pagebody">{stage === 'orders' && <OrdersPage onPickFurniture={() => setStage('ffe')} id={id} cur={cur} say={say} projectName={snap.name} rooms={floors(house!).flatMap(f => f.rooms).map(r => ({ id: r.id, name: r.name }))} />}{stage === 'site' && <SitePage id={id} rooms={floors(house!).flatMap(f => f.rooms)} say={say} />}</div> :
    <div className={`body studio-body ${compact ? `is-compact sheet-${sheet}` : ""} ${!compact && wideSide ? "wide-side" : ""}`}>
      <ToolRail tools={(Object.keys(ICON) as Tool[]).filter(tk => stageDef.tools.includes(tk))} active={tool} grouped={!compact} icon={ICON} label={tk => t(TOOL_LABEL[tk])} ariaLabel={t('editor.tools')}
        title={tk => `${t(TOOL_LABEL[tk])} (${formatShortcut(toolShortcut(tk), { mac })})`} keys={tk => ariaShortcut(toolShortcut(tk), { mac })} groupLabel={g => t(`toolGroup.${g}`)} onPick={setTool} />
      <div className={`canvas studio-canvas ${view === 'split' && !compact ? 'split' : ''}`}>
        <div className="levelbar">
          {/* doar nivelurile sunt tab-uri; „+ Nivel” e o acțiune, în afara listei de tab-uri (ARIA: tablist conține doar tab) */}
          <div role="tablist" aria-label={t('level.tabs')} className="levelbar-tabs">{floors(house!).map((_, i) => <button key={i} role="tab" aria-selected={i === L} className="btn" onClick={() => goLevel(i)}>{levelName(i)}</button>)}</div>
          <button className="btn" onClick={onAddLevel} title={t('level.addTitle')} disabled={nLevels >= MAX_LEVELS}>{t('level.add')}</button></div>
        {(view === '2d' || (view === 'split' && !compact)) && <div className="planwrap">{!preview && <PlanObjects groups={mirror} selected={sel} onPick={pickMirror} />}<PlanView
          onKey={onPlanKey}
          showTech={showTech && !preview}
          onTechAt={(x, z) => { if (!snap || preview) return;
            // plafoanele serverului: toate punctele (600), caloriferele cu corp (100), unitățile de aer condiționat (40)
            if (full(snap.tech?.length, MAX_TECH_POINTS) || (techKind === 'radiator' && full(techBodyCount(house?.tech, 'radiator'), PLAN_LIMITS.radiator.max)) || (techKind === 'hvac_split' && full(techBodyCount(house?.tech, 'hvac_split'), PLAN_LIMITS.hvac.max))) return;
            const pt = placeTechPoint(snap, techKind, x, z); if (!pt){ say(t('tech.outside')); return; } mutate(s => { s.tech = [...(s.tech || []), pt]; }); say(t('tech.placed', { rn: snap.floor.rooms.find(r => r.id === pt.roomId)?.name ?? '' })); }}
          key={(preview ? 'p' + preview.v.tier : 'live') + L}
          voids={voids}
          snap={preview ? shown(preview.v.candidate) : snap}
          catalog={catalog}
          sel={preview ? null : sel}
          frame={preview ? undefined : frame ?? undefined}
          hoverId={preview ? null : hoverPid} onHover={setHoverPid} onFrameOther={preview ? undefined : frameIn3d}
          tool={preview ? 'select' : tool}
          severities={preview ? {} : sev}
          onSelect={preview ? () => {} : (s: Sel) => { setSel(s); if (s && compact && sheet === 'peek') setSheet('half'); }}
          onEdit={preview ? () => {} : onEdit}
          onAddWall={preview ? () => {} : onAddWall}
          onAddRoom={preview ? () => {} : onAddRoom}
          onAddOpening={preview ? () => {} : onAddOpening}
          onAddColumn={preview ? undefined : onAddColumn}
          onAddNiche={preview ? undefined : onAddNiche}
          onAddClad={preview ? undefined : onAddClad}
          onAddWallItem={preview ? undefined : onAddWallItem} showWallItems={stage !== 'survey'}
          showFinishes={!preview && (stage === 'finishes' || stage === 'docs')} mc={mc}
          onAddDim={preview ? undefined : onAddDim}
          onAddText={preview ? undefined : onAddText}
          onToggleDemolish={preview ? undefined : onToggleDemolish}
          onAddZone={preview ? undefined : onAddZone}
          onAddCeilingZone={preview ? undefined : onAddCeilingZone}
          onAddRailing={preview ? undefined : onAddRailing}
          onAddBeam={preview ? undefined : onAddBeam}
          onAddGlassWall={preview ? undefined : onAddGlassWall}
          onAddStairAt={preview ? undefined : onAddStairAt}
          onRotateSelected={() => { if (!sel) return; if (sel.kind === 'placement') rotate(sel.id); else if (sel.kind === 'stair') rotateStair(sel.id); else if (sel.kind === 'column') rotateColumn(sel.id); }}
          onDuplicateSelected={() => { if (sel?.kind === 'placement') duplicate(sel.id); }}
          onDeleteSelected={del}
          onSetWallLength={preview ? undefined : onSetWallLength}
          onToggleColumnShape={() => { if (sel?.kind === 'column') mutate(s => { const c = s.floor.columns?.find(x => x.id === sel.id); if (c) c.shape = c.shape === 'round' ? 'rect' : 'round'; }); }}
          onAutoFurnishRoom={(roomId: string) => { const r = autoLayout(snap, catalog, { roomId }); commit(r.snapshot); say(r.notFit.length ? t('editor.someNotFit') : t('editor.roomAutoApplied', { room: snap.floor.rooms.find(x => x.id === roomId)?.name ?? '' })); }}
          onSplitRoom={(roomId: string, axis: 'x' | 'z') => { const r = snap.floor.rooms.find(x => x.id === roomId); if (!r) return; try { const mid = axis === 'x' ? r3((r.rect.x0 + r.rect.x1) / 2) : r3((r.rect.z0 + r.rect.z1) / 2), nid = `room-${uid().slice(0, 8)}`, nf = splitRoom(snap.floor, r.id, axis, mid, { id: nid, name: `${r.name} B`, type: r.type }, newWallId()); mutate(s => { s.floor = nf; }); } catch (e: any) { say(e?.message || 'Error'); } }}
          calib={preview ? null : calib}
          onCalibPick={(a, b) => setCalib({ stage: 'enter', a, b })}
        />
          {!preview && !snap.floor.rooms.length && !snap.floor.walls.length && tool !== 'room' && tool !== 'wall' && <EmptyState className="plan-empty" title={t('empty.plan.title')} text={t('empty.plan.text')} action={{ label: t('empty.plan.action'), onClick: drawRooms }} />}
          {!preview && tool === 'wall_item' && <div className="toolopts" role="group" aria-label={t('tool.wall_item')}><label>{t('wallItem.kind')} <select value={wallItemKind} onChange={e => setWallItemKind(e.target.value as WallItemKind)}>
            {WALL_ITEM_KINDS.map(k => <option key={k} value={k}>{t(`wallItem.kind.${k}`)}</option>)}</select></label></div>}
          {!preview && tool === 'clad' && <div className="toolopts" role="group" aria-label={t('tool.clad')}><label>{t('fin.kind')} <select value={cladKind} onChange={e => setCladKind(e.target.value as WallFeatureKind)}>
            {CLAD_KINDS.filter(k => mc?.materials.some(m => m.category === FEATURE_CATEGORY[k])).map(k => <option key={k} value={k}>{t(`fin.kind.${k}`)}</option>)}</select></label></div>}
          {!preview && (stage === 'finishes' || stage === 'docs') && (() => { const marks = featureMarks(snap, snap.floor, finishesOf); if (!marks.length) return null;
            return (<details className="plan-legend"><summary>{t('fin.legend')}</summary><ul>{marks.map(mk => { const room = snap.floor.rooms.find(r => r.id === mk.roomId)!, w = finishesOf(snap, room).wallFeatures![mk.index]!;
              return <li key={`${mk.roomId}:${mk.index}`}><b>{t(`fin.code.${mk.kind}`)}{mk.n}</b> {t(`fin.kind.${mk.kind}`)} · {room.name}, {t(`fin.side.${w.side}`)}{w.alongFromM != null || w.alongToM != null ? ` · ${formatLength(w.alongFromM ?? 0, units, lang)}–${formatLength(w.alongToM ?? sideSpan(room, w.side), units, lang)}` : ''}</li>; })}</ul></details>); })()}
          <p className="sr-only" aria-live="polite">{selDesc ? liveSel : selAnnounce}</p><div className="hintbar">{tool === 'wall' ? t('editor.hintWall') : tool === 'room' ? t('editor.hintRoom') : tool === 'door' || tool === 'window' ? t('editor.hintOpening') : tool === 'passage' ? t('editor.hintPassage') : tool === 'column' ? t('editor.hintColumn') : tool === 'shaft' ? t('editor.hintShaft') : tool === 'niche' ? t('editor.hintNiche') : tool === 'dim' ? t('editor.hintDim') : tool === 'text' ? t('editor.hintText') : tool === 'beam' ? t('editor.hintBeam') : tool === 'glass_wall' ? t('editor.hintGlassWall') : tool === 'stair' ? t('editor.hintStair') : tool === 'measure' ? t('editor.hintMeasure') : tool === 'clad' ? t('editor.hintClad') : tool === 'wall_item' ? t('editor.hintWallItem') : tool === 'demolish' ? t('editor.hintDemolish') : tool === 'floor_zone' ? t('editor.hintZone') : tool === 'ceiling_zone' ? t('editor.hintCeilingZone') : tool === 'railing' ? t('editor.hintRailing') : tool === 'tech' && techKind === 'radiator' ? t('editor.hintRadiator') : t('editor.hintSelect')}</div></div>}
        {(view === '3d' || (view === 'split' && !compact)) && <Viewer3D projectId={id} voids={voids} mc={mc} snap={preview ? shown(preview.v.candidate) : snap} catalog={catalog} onPick={pid => !preview && pid && setSel({ kind: 'placement', id: pid })}
          selectedId={!preview && sel?.kind === 'placement' ? sel.id : null} hoverId={preview ? null : hoverPid} onHover={setHoverPid} frame={frame3d ?? undefined} onFrameOther={frameInPlan} />}
        {preview && <div className="previewbar" role="status">{t('editor.previewBar', { title: preview.v.title })} <button className="btn" onClick={() => setPreview(null)}>{t('common.close')}</button></div>}
        {help && <KeyboardHelp onClose={() => setHelp(false)} />}
        {pending && <div className="pending" role="alertdialog" aria-label={t('editor.warnings')}>
          <strong>{t('editor.positionWarnings')}</strong>{pending.issues.map((i, k) => { const tgt = issueTarget(i, snap?.floor, snap?.placements); return <div key={k} className={`issue ${i.severity}`} role={tgt ? 'button' : undefined} tabIndex={tgt ? 0 : undefined} style={tgt ? { cursor: 'pointer' } : undefined} onClick={tgt ? () => focusIssue(i) : undefined} onKeyDown={tgt ? e => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); focusIssue(i); } } : undefined}>{iT(i)}</div>; })}
          <div style={{ display: 'flex', gap: 8 }}><button className="btn primary" onClick={() => setPending(null)}>{t('editor.keepPosition')}</button><button className="btn" onClick={() => { pending.discard?.(); setHouse(pending.before); persist(pending.before); setPending(null); force(x => x + 1); }}>{t('editor.revert')}</button></div></div>}
      </div>
      <aside className={`side studio-inspector ${compact ? `sheet sheet-${sheet}` : ''}`} aria-label={t('editor.panelAria', { stage: t(`stage.${stage}`) })}>
        <div className="sidetabs">
          {compact && <button type="button" className="sheet-grip" aria-label={sheet === 'full' ? t('editor.sheetLess') : t('editor.sheetMore')} aria-expanded={sheet !== 'peek'} onClick={() => setSheet(x => x === 'peek' ? 'half' : x === 'half' ? 'full' : 'peek')}><span aria-hidden="true" /></button>}
          <h2 className="stage-title"><i>{String(STAGES.indexOf(stageDef) + 1).padStart(2, '0')}</i>{t(`stage.${stage}`)}</h2>
          {nextId && <button type="button" className="btn primary sm stage-continue" aria-label={t('stage.continueTo', { stage: t(`stage.${nextId}`) })} title={t('stage.continueTo', { stage: t(`stage.${nextId}`) })} onClick={() => setStage(nextId)}><span>{t('stage.continue')}</span><Icon name="chevronRight" size={16} /></button>}
          {!compact && <button type="button" className="btn ghost icon-btn side-wide" aria-pressed={wideSide} aria-label={t('editor.sideWide')} title={t('editor.sideWide')} onClick={toggleWide}><Icon name={wideSide ? 'chevronRight' : 'chevronLeft'} /></button>}
          {compact && <button type="button" className="btn ghost icon-btn" aria-label={sheet === 'peek' ? t('editor.showPanel') : t('editor.hidePanel')} onClick={() => setSheet(x => x === 'peek' ? 'half' : 'peek')}><Icon name={sheet === 'peek' ? 'chevronUp' : 'chevronDown'} /></button>}
        </div>
        <StageMeta stage={stage} done={done} />
        {(showSel || stage === 'survey') && <>
          {stage === 'survey' && floorIssues.filter(i => !(sel?.kind === 'stair' && i.vars?.stair === sel.id)).map((i, k) => { const tgt = issueTarget(i, snap?.floor, snap?.placements); return <div key={k} className={`issue ${i.severity}`} role={tgt ? 'button' : undefined} tabIndex={tgt ? 0 : undefined} style={tgt ? { cursor: 'pointer' } : undefined} onClick={tgt ? () => focusIssue(i) : undefined} onKeyDown={tgt ? e => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); focusIssue(i); } } : undefined}>{iT(i)}</div>; })}
          {!showSel && <>
            <h3>{snap.name}{nLevels > 1 ? ` · ${levelName(L)}` : ''}</h3>
            {meta && <ProjectCard id={id} initial={meta} onSaved={setMeta} areaM2={floors(house!).flatMap(f => f.rooms).reduce((a, r) => a + area(r.rect), 0)} cur={cur} say={say} />}
            {meta && L === 0 && <TasksPanel projectId={id} />}
            {meta && L === 0 && <TimePanel id={id} cur={cur} say={say} />}
            {meta && L === 0 && <ContractPanel id={id} meta={meta} say={say} />}
            {meta && <SurveyPanel id={id} rooms={floors(house!).flatMap(f => f.rooms)} say={say} />}
            {L > 0 && <div style={{ display: 'grid', gap: 6 }}>
              <label className="f"><span>{t('level.name')}</span><input value={snap.floor.name} maxLength={60} onChange={e => mutate(s => { s.floor.name = e.target.value; })} /></label>
              <div className="prov">{t('level.elevation', { h: formatLength(elevationOf(house!, L), units, lang) })}</div>
              <button className="btn" onClick={onRemoveLevel}>{t('level.remove')}</button></div>}
            <div className="prov">{tp('editor.summaryRooms', snap.floor.rooms.length)} · {formatArea(snap.floor.rooms.reduce((a, r) => a + area(r.rect), 0), units, lang)} · {tp('editor.summaryPieces', snap.placements.length)} · {t('editor.summaryFurniture', { total: money(total) })} · {t('editor.summaryBudget')}</div>
            <label className="f"><span>{t('editor.ceilingHeight', { u })}</span><input type="number" value={inField(snap.floor.ceilingHeight)} onChange={e => num(e.target.value, x => mutate(s => { s.floor.ceilingHeight = r3(x / 100); }), 200)} /></label>
            {L > 0 ? <p className="muted" style={{ margin: 0, fontSize: 12.5 }}>{t('level.underlayGround')}</p> : <UnderlayPanel underlay={snap.underlay} calib={calib} say={say} onAdd={addUnderlay} onPatch={patchUnderlay} onRemove={() => { mutate(s => { delete s.underlay; }); setCalib(null); }}
              onCalibStart={() => setCalib({ stage: 'pick' })} onCalibCancel={() => setCalib(null)} onCalibApply={applyCalib} />}
            <p className="muted" style={{ margin: 0, fontSize: 12.5 }}>{t('editor.selectHint')}</p>
          </>}
          {selRoom && roomStage && <RoomPanel part={stage} focusFeature={cladFocus?.room === selRoom.id ? cladFocus : null} room={selRoom} snap={snap} G={G} cat={catalog} mc={mc} cur={cur} onSwap={(pid: string, vid: string) => changePiece(pid, q => { q.variantId = vid; }, t('editor.variantNoFit'))} onFinish={(patch: Partial<RoomFinishes>) => mutate(s => { s.finishes = { ...(s.finishes || {}), [selRoom.id]: { ...finishesOf(s, selRoom), ...patch } }; })} onChange={(fn: (r: any) => void) => mutate(s => fn(s.floor.rooms.find(r => r.id === selRoom.id)!))}
            onAuto={() => { const r = autoLayout(snap, catalog, { roomId: selRoom.id }); commit(r.snapshot); say(r.notFit.length ? t('editor.someNotFit') : t('editor.roomAutoApplied', { room: selRoom.name })); }}
            onAdd={(vid: string) => addToRoom(vid, selRoom)} onResize={(rect: Room['rect']) => resizeSel(selRoom.id, rect)}
            onSplit={(axis: 'x' | 'z') => { try { const mid = axis === 'x' ? r3((selRoom.rect.x0 + selRoom.rect.x1) / 2) : r3((selRoom.rect.z0 + selRoom.rect.z1) / 2), nid = `room-${uid().slice(0, 8)}`, nf = splitRoom(snap.floor, selRoom.id, axis, mid, { id: nid, name: `${selRoom.name} B`, type: selRoom.type }, newWallId()); mutate(s => { s.floor = nf; }); } catch (e: any) { say(e?.message || 'Error'); } }}
            onMerge={(otherId: string) => { try { const nf = mergeRooms(snap.floor, selRoom.id, otherId); mutate(s => { s.floor = nf; s.placements = s.placements.map(p => p.roomId === otherId ? { ...p, roomId: selRoom.id } : p); }); } catch (e: any) { say(e?.message || 'Error'); } }} num={num} />}
          {selRoom && (stage === 'finishes' || stage === 'ffe') && <RoomOptionsPanel id={id} room={selRoom} snap={snap} cat={catalog} mc={mc} cur={cur} onSnap={n => commit(n)} say={say} />}
          {selRoom && stage === 'survey' && <button className="btn" title={t('stair.addTitle')} onClick={() => onAddStair(selRoom)}>{t('stair.add')}</button>}
          {selStair && stage === 'survey' && (() => { const g = stairGeometry(selStair, snap.floor.ceilingHeight + SLAB), cm1 = (m: number) => Math.round(m * 1000) / 10; return (<div style={{ display: 'grid', gap: 8 }}>
            <h3>{t('stair.title')}</h3>
            <div className="prov">{t('stair.info', { steps: g.steps, riser: cm1(g.riser), going: cm1(g.going), to: levelName(L + 1) })}</div>
            {stairIss.filter(i => i.vars?.stair === selStair.id && !i.with).map((i, k) => <div key={k} className={`issue ${i.severity}`}>{iT(i)}</div>)}
            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 8 }}>
              <label className="f"><span>{t('stair.width', { u })}</span><input type="number" value={inField(selStair.width)} onChange={e => num(e.target.value, x => patchStair(selStair.id, { width: r3(Math.min(300, x) / 100) }), 60)} /></label>
              <label className="f"><span>{t('stair.length', { u })}</span><input type="number" value={inField(selStair.length)} onChange={e => num(e.target.value, x => patchStair(selStair.id, { length: r3(Math.min(800, x) / 100) }), 80)} /></label></div>
            <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}><button className="btn" onClick={() => rotateStair(selStair.id)}>{t('stair.rotate')} (R)</button><button className="btn" onClick={del}>{t('stair.delete')}</button></div></div>); })()}
          {selRoom && stage === 'finishes' && <RoomLookPanel room={selRoom} snap={snap} onRoom={(part, hex) => look(a => { const r: any = { ...(a.rooms?.[selRoom.id] || {}) }; if (hex) r[part] = { ...(r[part] || {}), color: hex }; else delete r[part]; a.rooms = { ...(a.rooms || {}), [selRoom.id]: r }; })}
            onAllWalls={hex => look(a => { a.rooms = { ...(a.rooms || {}) }; for (const r of snap.floor.rooms) a.rooms[r.id] = { ...(a.rooms[r.id] || {}), walls: { color: hex } }; })} />}
          {selWall && roomStage && <>
            <h3>{t('editor.wall')}</h3>
            <div className="grid2">
              <label className="f"><span>{t('editor.length', { u })}</span><input type="number" key={`${selWall.id}:${wallLength(selWall.a, selWall.b)}:${units}`} defaultValue={inField(wallLength(selWall.a, selWall.b))}
                // se aplică la Enter sau la ieșirea din câmp, nu la fiecare pas al săgeților: un singur pas de anulare, iar
                // capătul nu trece prin piciorul unui perete în T (care altfel l-ar urma de atunci încolo)
                // Esc renunță; textul neatins nu se aplică (valoarea afișată e rotunjită, un perete oblic s-ar muta)
                onKeyDown={e => { if (e.key === 'Escape'){ e.currentTarget.value = e.currentTarget.defaultValue; e.stopPropagation(); } if (e.key === 'Enter' || e.key === 'Escape') e.currentTarget.blur(); }}
                onBlur={e => { const el = e.currentTarget; if (el.value === el.defaultValue) return; const back = inField(wallLength(selWall.a, selWall.b)); let ok = false; num(el.value, x => { ok = onSetWallLength(selWall.id, x / 100); }, 20); if (!ok) el.value = String(back); }} /></label>
              <label className="f"><span>{t('editor.thickness', { u })}</span><input type="number" value={inField(selWall.thickness)} onChange={e => num(e.target.value, x => mutate(s => { s.floor.walls.find(w => w.id === selWall.id)!.thickness = r3(x / 100); }), 5)} /></label>
            </div>
            <label className="f" style={{ display: 'flex', gap: 8, alignItems: 'center' }}><input type="checkbox" checked={selWall.exterior} onChange={e => mutate(s => { s.floor.walls.find(w => w.id === selWall.id)!.exterior = e.target.checked; })} /> {t('editor.exteriorWall')}</label>
            <label className="f"><span>{t('wallStatus.label')}</span><select value={wallStatus(selWall)} onChange={e => setWallStatus(selWall.id, e.target.value as WallStatus)}>
              {(['existing', 'new', 'demolish'] as const).map(k => <option key={k} value={k}>{t(`wallStatus.${k}`)}</option>)}</select></label>
            {selWall.status && <p className="prov" style={{ margin: 0 }}>{t(selWall.status === 'new' ? 'wallStatus.newNote' : 'wallStatus.demolishNote')}</p>}
            {floorIssues.filter(i => i.code === 'WALL_DEMOLISH' && i.vars?.id === selWall.id).map((i, k) => <div key={k} className={`issue ${i.severity}`}>{iT(i)}</div>)}
            <div className="prov">{tp('editor.openingsDrag', selWall.openings.length)}</div>
            <WallLookPanel wall={selWall} snap={snap} onFace={(rid, hex) => look(a => { a.wallFaces = setFinish(a.wallFaces, `${selWall.id}@${rid}`, { color: hex }); })} />
            <button className="btn danger" onClick={del}>{t('editor.deleteWall')}</button>
          </>}
          {selOp && roomStage && <>
            <h3>{selOp.passage ? t('tool.passage') : selOp.kind === 'door' ? t('tool.door') : t('tool.window')}</h3>
            <div className="grid2">
              <label className="f"><span>{t('editor.width', { u })}</span><input type="number" value={inField(selOp.width)} onChange={e => num(e.target.value, x => mutate(s => { s.floor.walls.find(w => w.id === (sel as any).wallId)!.openings.find(o => o.id === selOp.id)!.width = r3((selOp.passage ? Math.min(x, PLAN_LIMITS.passage.widthMaxM * 100) : selOp.kind === 'door' && selOp.doorType ? Math.min(x, DOOR_TYPE_RULES[selOp.doorType].maxM * 100) : x) / 100); }), selOp.kind === 'door' && !selOp.passage && selOp.doorType ? DOOR_TYPE_RULES[selOp.doorType].minM * 100 : 30)} /></label>
              <label className="f"><span>{t('editor.offsetOnWall', { u })}</span><input type="number" value={inField(selOp.offset)} onChange={e => num(e.target.value, x => mutate(s => { s.floor.walls.find(w => w.id === (sel as any).wallId)!.openings.find(o => o.id === selOp.id)!.offset = r3(x / 100); }))} /></label>
            </div>
            <OpeningLookPanel op={selOp} snap={snap} num={num} onPatch={patch => mutate(s => { Object.assign(s.floor.walls.find(w => w.id === (sel as any).wallId)!.openings.find(o => o.id === selOp.id)!, patch); })}
              onFrame={hex => look(a => { a.openings = setFinish(a.openings, selOp.id, { color: hex }); })} />
            {selOp.kind === 'door' && !selOp.entrance && <label className="f" style={{ display: 'flex', gap: 8, alignItems: 'center' }}><input type="checkbox" checked={!!selOp.passage} onChange={e => mutate(s => { const o = s.floor.walls.find(w => w.id === (sel as any).wallId)!.openings.find(o => o.id === selOp.id)!; if (e.target.checked){ o.passage = true; o.width = r3(Math.min(Math.max(o.width, PLAN_LIMITS.passage.widthMinM), PLAN_LIMITS.passage.widthMaxM)); delete o.hinge; delete o.swing; delete o.doorType; delete o.flush; if (s.doors) delete s.doors[o.id]; } else delete o.passage; })} /> {t('door.passage')}</label>}
            {selOp.kind === 'door' && !selOp.passage && (() => { const wi = snap.floor.walls.findIndex(w => w.id === (sel as any).wallId), oi = snap.floor.walls[wi]?.openings.findIndex(o => o.id === selOp.id) ?? -1;
              if (wi < 0 || oi < 0) return null; const sw = doorSwing(snap.floor, wi, oi), patchOp = (p: object) => mutate(s => { Object.assign(s.floor.walls[wi]!.openings[oi]!, p); });
              const type = doorTypeOf(selOp) ?? 'swing', sliding = isSlidingType(type), wl = wallLength(snap.floor.walls[wi]!.a, snap.floor.walls[wi]!.b);
              // un tip mai lat decât peretele (dubla pe 70 cm) ar da o lățime sub minimul tipului, pe care serverul o respinge: nu se poate alege
              // tipul ușii: lățimea iese din intervalul tipului → lățimea standard a tipului (dubla sub 80 cm primește 120 cm), fără să iasă din perete
              const setType = (v: DoorType) => mutate(s => { const w = s.floor.walls[wi]!, o = w.openings[oi]!, r = DOOR_TYPE_RULES[v], L = wallLength(w.a, w.b); if (L < r.minM - 1e-9) return;
                if (v === 'swing') delete o.doorType; else o.doorType = v; delete o.hinge;
                if (o.width < r.minM - 1e-9 || o.width > r.maxM + 1e-9){ o.width = r3(Math.min(r.std, L)); o.offset = r3(Math.max(0, Math.min(o.offset, L - o.width))); }
                const sys = s.doors?.[o.id]; if (sys?.system){ const m = mc ? materialOf(mc, sys.system) : undefined; if (m?.specs?.system !== v){ delete sys.system; if (!Object.keys(sys).length) delete s.doors![o.id]; } } });
              return <>
                  <label className="f"><span>{t('door.type')}</span><select value={type} aria-describedby={`door-hint-${selOp.id}`} onChange={e => setType(e.target.value as DoorType)}>{DOOR_TYPES.map(k => <option key={k} value={k} disabled={k !== type && DOOR_TYPE_RULES[k].minM > wl + 1e-9}>{t(`door.type.${k}`)}</option>)}</select></label>
                  <label className="f" style={{ display: 'flex', gap: 8, alignItems: 'center' }}><input type="checkbox" checked={!!selOp.flush} onChange={e => patchOp(e.target.checked ? { flush: true } : { flush: undefined })} /> {t('door.flush')}</label>
                <small className="prov" id={`door-hint-${selOp.id}`}>{t(`door.typeHint.${type}`)}</small>
                <div className="grid2">
                  {type !== 'double' && <button className="btn" onClick={() => patchOp({ hinge: sw.hinge === 'start' ? 'end' : 'start', swing: sw.swing })}>{t(sliding ? 'door.flipSlide' : type === 'folding' ? 'door.flipFold' : 'door.flipHinge')}</button>}
                  {type !== 'pocket' && <button className="btn" onClick={() => patchOp({ swing: sw.swing === 1 ? -1 : 1, hinge: sw.hinge })}>{t(type === 'sliding' ? 'door.flipFace' : 'door.flipSwing')}</button>}
                  <small className="prov" style={{ gridColumn: '1/-1' }}>{sw.auto ? t(sliding ? 'door.slideAuto' : 'door.swingAuto') : t('door.swingSet')}</small></div></>; })()}
            {selOp.kind === 'door' && !selOp.passage && mc && <DoorPanel op={selOp} snap={house!} mc={mc} cur={cur} onChange={(p: DoorChoice) => mutate(s => { const cur0 = s.doors?.[selOp.id] ?? {}, next = { ...cur0, ...p };
              const clean = { ...(next.product ? { product: next.product } : {}), ...(next.handle ? { handle: next.handle } : {}), ...(next.system ? { system: next.system } : {}) }; s.doors = { ...(s.doors || {}) }; if (Object.keys(clean).length) s.doors[selOp.id] = clean; else delete s.doors[selOp.id]; })} />}
            <button className="btn danger" onClick={del}>{t('editor.deleteOpening')}</button>
          </>}
          {selPl && showSel && <PlacementPanel out={out} p={selPl} snap={snap} catalog={catalog} issues={issues[selPl.id] || []} G={G} num={num} onFocusIssue={focusIssue}
            onVariant={vid => changePiece(selPl.id, q => { q.variantId = vid; }, t('editor.variantNoFit'))}
            onMove={(x, z) => { const n = structuredClone(snap), q = n.placements.find(p => p.id === selPl.id)!; q.x = r3(x); q.z = r3(z); q.source = 'manual'; const iss = validatePlacement(n, catalog, q);
              if (severityOf(iss) === 'ERROR'){ say(t('editor.positionRefused', { msg: iT(iss.find(i => i.severity === 'ERROR')!) })); return; } commit(n); }}
            onRotate={() => rotate(selPl.id)} onDuplicate={() => duplicate(selPl.id)} onDelete={del} />}
          {selPl && showSel && stage === 'ffe' && selPl.group === 'dulap' && <WardrobePanel p={selPl} snap={snap} catalog={catalog} onChange={spec => mutate(s => { const j = { ...(s.joinery ?? {}) }; if (spec) j[selPl.id] = spec; else delete j[selPl.id]; if (Object.keys(j).length) s.joinery = j; else delete s.joinery; })} />}
          {selPl && showSel && <ItemLookPanel p={selPl} snap={snap} catalog={catalog} model={resolve(catalog, selPl.variantId)?.product.model3d ?? ''} num={num}
            onItem={patch => look(a => { a.items = setFinish(a.items, selPl.id, patch); })}
            onSize={size => changePiece(selPl.id, q => { if (size) q.size = size; else delete q.size; }, t('editor.sizeNoFit'))}
            onVariant={vid => changePiece(selPl.id, q => { q.variantId = vid; }, t('editor.variantNoFit'))} />}
          {selColumn && (roomStage || stage === 'mep') && (
            <div style={{ display: 'grid', gap: 8 }}>
              <h3>{selColumn.role === 'shaft' ? t('tool.shaft') : selColumn.role === 'boxing' ? t('column.boxing') : t('tool.column')}</h3>
              <label className="f"><span>{t('column.role')}</span>
                <select value={selColumn.role ?? 'structural'} onChange={e => mutate(s => { const c = s.floor.columns?.find(c => c.id === selColumn.id); if (!c) return; const v = e.target.value as 'structural' | 'shaft' | 'boxing'; if (v === 'structural'){ delete c.role; delete c.height; } else { c.role = v; c.shape = 'rect'; } })}>
                  <option value="structural">{t('column.structural')}</option><option value="shaft">{t('tool.shaft')}</option><option value="boxing">{t('column.boxing')}</option>
                </select></label>
              {selColumn.role && selColumn.role !== 'structural' && <label className="f"><span>{t('column.height', { u })}</span><input type="number" placeholder={t('column.toCeiling')} value={selColumn.height != null ? inField(selColumn.height) : ''} onChange={e => { const v = e.target.value; if (v.trim() === '') mutate(s => { const c = s.floor.columns?.find(c => c.id === selColumn.id); if (c) delete c.height; }); else num(v, x => mutate(s => { const c = s.floor.columns?.find(c => c.id === selColumn.id); if (c) c.height = r3(Math.min(x, PLAN_LIMITS.column.heightMaxM * 100) / 100); }), PLAN_LIMITS.column.heightMinM * 100); }} /></label>}
              <div className="grid2">
                <label className="f"><span>{t('editor.width', { u })}</span><input type="number" value={inField(selColumn.w)} onChange={e => num(e.target.value, x => mutate(s => { const c = s.floor.columns?.find(c => c.id === selColumn.id); if (c) c.w = r3(Math.min(x, PLAN_LIMITS.column.maxM * 100) / 100); }), PLAN_LIMITS.column.minM * 100)} /></label>
                <label className="f"><span>{t('editor.depth', { u })}</span><input type="number" value={inField(selColumn.d)} onChange={e => num(e.target.value, x => mutate(s => { const c = s.floor.columns?.find(c => c.id === selColumn.id); if (c) c.d = r3(Math.min(x, PLAN_LIMITS.column.maxM * 100) / 100); }), PLAN_LIMITS.column.minM * 100)} /></label>
              </div>
              {!selColumn.role && <label className="f"><span>{t('column.shape')}</span>
                <select value={selColumn.shape} onChange={e => mutate(s => { const c = s.floor.columns?.find(c => c.id === selColumn.id); if (c) c.shape = e.target.value as any; })}>
                  <option value="rect">{t('column.rect')}</option>
                  <option value="round">{t('column.round')}</option>
                </select>
              </label>}
              <div style={{ display: 'flex', gap: 8 }}>
                <button className="btn" onClick={() => rotateColumn(selColumn.id)}>{t('editor.rotate')}</button>
                <button className="btn danger" onClick={del}>{t('common.delete')}</button>
              </div>
            </div>
          )}
          {selBeam && roomStage && (
            <div style={{ display: 'grid', gap: 8 }}>
              <h3>{t('tool.beam')}</h3>
              <div className="prov">{formatLength(wallLength(selBeam.a, selBeam.b), units, lang)}</div>
              <div className="grid2">
                <label className="f"><span>{t('editor.width', { u })}</span><input type="number" value={inField(selBeam.width)} onChange={e => num(e.target.value, x => mutate(s => { const b = s.floor.beams?.find(b => b.id === selBeam.id); if (b) b.width = r3(Math.min(x, PLAN_LIMITS.beam.maxM * 100) / 100); }), PLAN_LIMITS.beam.minM * 100)} /></label>
                <label className="f"><span>{t('beam.depth', { u })}</span><input type="number" value={inField(selBeam.depth)} onChange={e => num(e.target.value, x => mutate(s => { const b = s.floor.beams?.find(b => b.id === selBeam.id); if (b) b.depth = r3(Math.min(x, PLAN_LIMITS.beam.maxM * 100) / 100); }), PLAN_LIMITS.beam.minM * 100)} /></label>
              </div>
              <button className="btn danger" onClick={del}>{t('common.delete')}</button>
            </div>
          )}
          {selAnn && (stage === 'survey' || stage === 'docs') && (<div style={{ display: 'grid', gap: 8 }}>
            <h3>{t(selAnn.kind === 'dim' ? 'tool.dim' : 'tool.text')}</h3>
            {selAnn.kind === 'dim' ? <>
              <div className="prov">{formatLength(dimGeometry(selAnn).value, units, lang)}</div>
              <div className="grid2">
                <label className="f"><span>{t('dim.offset', { u })}</span><input type="number" value={inField(Math.abs(selAnn.offset))} onChange={e => num(e.target.value, x => patchAnn(selAnn.id, a => { if (a.kind === 'dim') a.offset = r3((Math.sign(a.offset) || 1) * Math.min(x, PLAN_LIMITS.annotation.offsetMaxM * 100) / 100); }))} /></label>
                <label className="f"><span>{t('dim.mode')}</span><select value={selAnn.mode ?? 'aligned'} onChange={e => patchAnn(selAnn.id, a => { if (a.kind !== 'dim') return; if (e.target.value === 'ortho') a.mode = 'ortho'; else delete a.mode; })}>
                  <option value="aligned">{t('dim.aligned')}</option><option value="ortho">{t('dim.ortho')}</option></select></label>
              </div>
              <button className="btn" onClick={() => patchAnn(selAnn.id, a => { if (a.kind === 'dim') a.offset = -a.offset; })}>{t('dim.flip')}</button></>
            : <>
              <label className="f"><span>{t('text.content')}</span><input value={selAnn.text} maxLength={PLAN_LIMITS.annotation.textMax} onChange={e => { const v = stripPlanText(e.target.value); patchAnn(selAnn.id, a => { if (a.kind === 'text') a.text = v; }); }} onBlur={() => { if (!cleanPlanText(selAnn.text)) del(); }} /></label>
              <div className="grid2">
                <label className="f"><span>{t('text.size')}</span><select value={selAnn.size ?? 'm'} onChange={e => patchAnn(selAnn.id, a => { if (a.kind !== 'text') return; const v = e.target.value; if (v === 's' || v === 'l') a.size = v; else delete a.size; })}>
                  <option value="s">{t('text.sizeS')}</option><option value="m">{t('text.sizeM')}</option><option value="l">{t('text.sizeL')}</option></select></label>
                <label className="f"><span>{t('text.rotation')}</span><select value={String(selAnn.rotation ?? 0)} onChange={e => patchAnn(selAnn.id, a => { if (a.kind !== 'text') return; const v = Number(e.target.value); if (v) a.rotation = v; else delete a.rotation; })}>
                  {[0, 90, 270].map(r => <option key={r} value={r}>{r}°</option>)}</select></label>
              </div></>}
            <button className="btn danger" onClick={del}>{t('common.delete')}</button>
          </div>)}
          {selNiche && (roomStage || stage === 'concept') && (() => { const NL = PLAN_LIMITS.niche, wl = snap.floor.walls.find(w => w.id === selNiche.wallId), cm = (k: 'width' | 'height' | 'depth' | 'elevation' | 'offset', lo: number, hi: number) =>
              (v: string) => num(v, x => patchNiche(selNiche.id, n => { n[k] = r3(Math.min(x, hi * 100) / 100); }), lo * 100);
            return (<div style={{ display: 'grid', gap: 8 }}>
              <h3>{t('tool.niche')}</h3>
              {floorIssues.filter(i => i.vars?.niche === selNiche.id).map((i, k) => <div key={k} className={`issue ${i.severity}`}>{iT(i)}</div>)}
              <div className="grid2">
                <label className="f"><span>{t('editor.width', { u })}</span><input type="number" value={inField(selNiche.width)} onChange={e => cm('width', NL.widthMinM, NL.widthMaxM)(e.target.value)} /></label>
                <label className="f"><span>{t('niche.height', { u })}</span><input type="number" value={inField(selNiche.height)} onChange={e => cm('height', NL.heightMinM, NL.heightMaxM)(e.target.value)} /></label>
                <label className="f"><span>{t('editor.depth', { u })}</span><input type="number" value={inField(selNiche.depth)} onChange={e => cm('depth', NL.depthMinM, NL.depthMaxM)(e.target.value)} /></label>
                <label className="f"><span>{t('niche.elevation', { u })}</span><input type="number" value={inField(selNiche.elevation)} onChange={e => cm('elevation', 0, NL.elevMaxM)(e.target.value)} /></label>
                <label className="f"><span>{t('editor.offsetOnWall', { u })}</span><input type="number" value={inField(selNiche.offset)} onChange={e => cm('offset', 0, wl ? wallLength(wl.a, wl.b) : PLAN_LIMITS.coordM)(e.target.value)} /></label>
                <label className="f"><span>{t('niche.finish')}</span><select value={selNiche.finish ?? ''} onChange={e => patchNiche(selNiche.id, n => { const v = e.target.value; if (v === 'tile' || v === 'paint' || v === 'led') n.finish = v; else delete n.finish; })}>
                  <option value="">{t('niche.finishAuto')}</option><option value="tile">{t('niche.tile')}</option><option value="paint">{t('niche.paint')}</option><option value="led">{t('niche.led')}</option></select></label>
              </div>
              <div style={{ display: 'flex', gap: 8 }}>
                <button className="btn" onClick={() => patchNiche(selNiche.id, n => { n.side = (n.side ?? 1) === 1 ? -1 : 1; })}>{t('niche.flip')}</button>
                <button className="btn danger" onClick={del}>{t('common.delete')}</button>
              </div>
            </div>); })()}
          {selZone && (roomStage || stage === 'concept') && (() => { const z = selZone, room = snap.floor.rooms.find(r => r.id === z.roomId), ZL = PLAN_LIMITS.zone;
            const floorsMc = mc ? mc.materials.filter(m => m.category === 'floor_tile' || m.category === 'parquet') : [], w = z.rect.x1 - z.rect.x0, d = z.rect.z1 - z.rect.z0;
            const size = (k: 'w' | 'd') => (v: string) => num(v, x => patchZone(z.id, q => { const m = r3(x / 100); if (k === 'w') q.rect.x1 = r3(q.rect.x0 + m); else q.rect.z1 = r3(q.rect.z0 + m); }), ZL.minM * 100);
            return (<div style={{ display: 'grid', gap: 8 }}>
              <h3>{t('tool.floor_zone')}{z.label ? ` · ${z.label}` : ''}</h3>
              <div className="prov">{t('zone.info', { room: room?.name ?? '-', area: formatArea(zoneAreaIn(snap.floor, z), units, lang) })}</div>
              {floorIssues.filter(i => i.vars?.zone === z.id).map((i, k) => <div key={k} className={`issue ${i.severity}`}>{iT(i)}</div>)}
              {floorsMc.length > 0 && <label className="f"><span>{t('zone.material')}</span><MaterialPicker cur={cur} value={z.floor} options={floorsMc} onChange={v => { if (v) patchZone(z.id, q => { q.floor = v; delete q.layout; }); }} /></label>}
              <div className="grid2">
                <label className="f"><span>{t('editor.width', { u })}</span><input type="number" value={inField(w)} onChange={e => size('w')(e.target.value)} /></label>
                <label className="f"><span>{t('editor.depth', { u })}</span><input type="number" value={inField(d)} onChange={e => size('d')(e.target.value)} /></label>
              </div>
              <label className="f"><span>{t('zone.label')}</span><input type="text" maxLength={ZL.labelMax} value={z.label ?? ''} onChange={e => patchZone(z.id, q => { const v = stripPlanText(e.target.value).slice(0, ZL.labelMax); if (v.trim()) q.label = v; else delete q.label; })} /></label>
              <p className="prov" style={{ margin: 0 }}>{t('zone.rule')}</p>
              <button className="btn danger" onClick={del}>{t('common.delete')}</button>
            </div>); })()}
          {selCeilingZone && (roomStage || stage === 'concept') && (() => { const z = selCeilingZone, room = snap.floor.rooms.find(r => r.id === z.roomId), CL = PLAN_LIMITS.ceilingZone, w = z.rect.x1 - z.rect.x0, d = z.rect.z1 - z.rect.z0;
            const roomDrop = room ? (finishesOf(snap, room).ceiling?.type ?? 'flat') !== 'flat' ? (finishesOf(snap, room).ceiling?.dropCm ?? 10) : 0 : 0, q = ceilingZoneQty(snap.floor, z, roomDrop), leds = mc ? mc.materials.filter(m => m.category === 'led_strip') : [];
            const size = (k: 'w' | 'd') => (v: string) => num(v, x => patchCeilingZone(z.id, q2 => { const m = r3(x / 100); if (k === 'w') q2.rect.x1 = r3(q2.rect.x0 + m); else q2.rect.z1 = r3(q2.rect.z0 + m); }), CL.minM * 100);
            return (<div style={{ display: 'grid', gap: 8 }} data-panel="ceiling-zone">
              <h3>{t('tool.ceiling_zone')}{z.label ? ` · ${z.label}` : ''}</h3>
              <div className="prov">{t('czone.info', { room: room?.name ?? '-', area: formatArea(q?.areaM2 ?? 0, units, lang), h: formatLength(snap.floor.ceilingHeight - z.dropCm / 100, units, lang) })}</div>
              {floorIssues.filter(i => i.vars?.czone === z.id).map((i, k) => <div key={k} className={`issue ${i.severity}`}>{iT(i)}</div>)}
              <div className="grid2">
                <label className="f"><span>{t('editor.width', { u })}</span><input type="number" value={inField(w)} onChange={e => size('w')(e.target.value)} /></label>
                <label className="f"><span>{t('editor.depth', { u })}</span><input type="number" value={inField(d)} onChange={e => size('d')(e.target.value)} /></label>
                <label className="f"><span>{t('czone.drop')}</span><input type="number" min={CL.dropMinCm} max={CL.dropMaxCm} value={z.dropCm} onChange={e => { const v = Number(e.target.value); if (Number.isFinite(v) && e.target.value !== '') patchCeilingZone(z.id, q2 => { q2.dropCm = Math.round(Math.max(CL.dropMinCm, Math.min(CL.dropMaxCm, v))); }); }} /></label>
                <label className="f"><span>{t('czone.cove')}</span><input type="number" min={0} max={CL.coveMaxCm} value={z.coveCm ?? 0} onChange={e => { const v = Number(e.target.value); if (Number.isFinite(v)) patchCeilingZone(z.id, q2 => { const c = Math.round(Math.max(0, Math.min(CL.coveMaxCm, v))); if (c > 0) q2.coveCm = c; else delete q2.coveCm; }); }} /></label>
              </div>
              {leds.length > 0 && <label className="f"><span>{t('czone.led')}</span><MaterialPicker cur={cur} value={z.led ?? ''} options={leds} none={t('fin.none')} onChange={v => patchCeilingZone(z.id, q2 => { if (v) q2.led = v; else delete q2.led; })} /></label>}
              <label className="f"><span>{t('zone.label')}</span><input type="text" maxLength={CL.labelMax} value={z.label ?? ''} onChange={e => patchCeilingZone(z.id, q2 => { const v = stripPlanText(e.target.value).slice(0, CL.labelMax); if (v.trim()) q2.label = v; else delete q2.label; })} /></label>
              {q && <div className="prov">{t('czone.qty', { board: q.boardM2, band: q.bandM2, led: q.ledM })}</div>}
              <p className="prov" style={{ margin: 0 }}>{t('czone.rule')}</p>
              <button className="btn danger" onClick={del}>{t('common.delete')}</button>
            </div>); })()}
          {selRailing && roomStage && (() => { const rl = selRailing, RL = PLAN_LIMITS.railing;
            return (<div style={{ display: 'grid', gap: 8 }} data-panel="railing">
              <h3>{t('tool.railing')}</h3>
              <div className="prov">{formatLength(railingLength(rl), units, lang)}</div>
              {floorIssues.filter(i => i.vars?.rail === rl.id).map((i, k) => <div key={k} className={`issue ${i.severity}`}>{iT(i)}</div>)}
              <div className="grid2">
                <label className="f"><span>{t('railing.height', { u })}</span><input type="number" value={inField(railingHeight(rl))} onChange={e => num(e.target.value, x => patchRailing(rl.id, q => { const h = r3(Math.min(RL.heightMaxM * 100, x) / 100); if (Math.abs(h - PLAN_DEFAULTS.railing.height) < 1e-9) delete q.height; else q.height = h; }), RL.heightMinM * 100)} /></label>
                <label className="f"><span>{t('railing.kind')}</span><select value={railingKind(rl)} onChange={e => patchRailing(rl.id, q => { if (e.target.value === 'glass') q.kind = 'glass'; else delete q.kind; })}>
                  {RAILING_KINDS.map(k => <option key={k} value={k}>{t(`railing.kind.${k}`)}</option>)}</select></label>
              </div>
              <p className="prov" style={{ margin: 0 }}>{t('railing.rule')}</p>
              <button className="btn danger" onClick={del}>{t('common.delete')}</button>
            </div>); })()}
          {selWallItem && (stage === 'concept' || stage === 'finishes' || stage === 'ffe') && (() => { const WL = PLAN_LIMITS.wallItem, it = selWallItem, wl = snap.floor.walls.find(w => w.id === it.wallId), L = wl ? wallLength(wl.a, wl.b) : PLAN_LIMITS.coordM, H = snap.floor.ceilingHeight;
            const cm = (k: 'width' | 'height' | 'depth' | 'elevation' | 'offset', lo: number, hi: number) => (v: string) => num(v, x => patchWallItem(it.id, n => { n[k] = r3(Math.min(x, hi * 100) / 100); }), lo * 100);
            const below = catalog ? furnitureBelow(snap, catalog, snap.floor, it) : null, cat = WALL_ITEM_CATEGORY[it.kind], products = cat && mc ? mc.materials.filter(m => m.category === cat) : [];
            return (<div style={{ display: 'grid', gap: 8 }}>
              <h3>{it.label || t(`wallItem.kind.${it.kind}`)}</h3>
              {floorIssues.filter(i => i.vars?.item === it.id).map((i, k) => <div key={k} className={`issue ${i.severity}`}>{iT(i)}</div>)}
              <div className="grid2">
                <label className="f"><span>{t('wallItem.kind')}</span><select value={it.kind} onChange={e => patchWallItem(it.id, n => { n.kind = e.target.value as WallItemKind; delete n.product; delete n.depth; })}>
                  {WALL_ITEM_KINDS.map(k => <option key={k} value={k}>{t(`wallItem.kind.${k}`)}</option>)}</select></label>
                <label className="f"><span>{t('wallItem.label')}</span><input type="text" maxLength={WL.labelMax} value={it.label ?? ''} onChange={e => patchWallItem(it.id, n => { const v = stripPlanText(e.target.value).slice(0, WL.labelMax); if (v.trim()) n.label = v; else delete n.label; })}
                  onBlur={() => patchWallItem(it.id, n => { const v = cleanPlanText(n.label); if (v) n.label = v; else delete n.label; })} /></label>
                <label className="f"><span>{t('editor.width', { u })}</span><input type="number" value={inField(it.width)} onChange={e => cm('width', WL.widthMinM, Math.min(WL.widthMaxM, L))(e.target.value)} /></label>
                <label className="f"><span>{t('niche.height', { u })}</span><input type="number" value={inField(it.height)} onChange={e => cm('height', WL.heightMinM, Math.min(WL.heightMaxM, H))(e.target.value)} /></label>
                <label className="f"><span>{t('wallItem.offset', { u })}</span><input type="number" value={inField(it.offset)} onChange={e => cm('offset', 0, L)(e.target.value)} /></label>
                <label className="f"><span>{t('wallItem.elevation', { u })}</span><input type="number" value={inField(it.elevation)} onChange={e => cm('elevation', 0, H)(e.target.value)} /></label>
                <label className="f"><span>{t('editor.depth', { u })}</span><input type="number" value={inField(it.depth ?? wallItemDefaults(it.kind).depth)} onChange={e => cm('depth', 0, WL.depthMaxM)(e.target.value)} /></label>
                {(it.kind === 'art' || it.kind === 'shelf' || it.kind === 'mirror') && <label className="f"><span>{t(it.kind === 'art' ? 'wallItem.canvas' : 'fin.color')}</span><input type="color" value={it.color ?? (it.kind === 'art' ? '#7f8f7c' : it.kind === 'shelf' ? '#b88a5a' : '#2d2a26')} onChange={e => patchWallItem(it.id, n => { const h = normalizeHex(e.target.value); if (h) n.color = h; })} /></label>}
              </div>
              {products.length > 0 && <label className="f"><span>{t('fin.product')}</span><MaterialPicker cur={cur} value={it.product ?? ''} options={products} none={t('wallItem.noProduct')} onChange={v => patchWallItem(it.id, n => { if (v) n.product = v; else delete n.product; })} /></label>}
              <p className="prov" style={{ margin: 0 }}>{t('wallItem.rule')}</p>
              <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
                <button className="btn" onClick={() => patchWallItem(it.id, n => { n.offset = r3(L / 2); })}>{t('wallItem.centerWall')}</button>
                {below && <button className="btn" onClick={() => { const p = snap.placements.find(x => x.id === below.id), fp = p && catalog && footprintOf(catalog, p), g = wl && fp ? (() => { const L2 = wallLength(wl.a, wl.b) || 1, ux = (wl.b[0] - wl.a[0]) / L2, uz = (wl.b[1] - wl.a[1]) / L2, cx = (fp.x0 + fp.x1) / 2, cz = (fp.z0 + fp.z1) / 2; return (cx - wl.a[0]) * ux + (cz - wl.a[1]) * uz; })() : null;
                  patchWallItem(it.id, n => { if (g != null) n.offset = r3(Math.max(n.width / 2, Math.min(L - n.width / 2, g))); n.elevation = r3(Math.min(H - n.height / 2, below.top + WALL_ITEM_RULES.aboveFurnitureM + n.height / 2)); }); }}>{t('wallItem.centerAbove')}</button>}
                <button className="btn" onClick={() => patchWallItem(it.id, n => { n.side = n.side === 1 ? -1 : 1; })}>{t('niche.flip')}</button>
                <button className="btn danger" onClick={del}>{t('common.delete')}</button>
              </div>
            </div>); })()}
          {selGlassWall && roomStage && (
            <div style={{ display: 'grid', gap: 8 }}>
              <h3>{t('tool.glass_wall')}</h3>
              <div className="prov">{formatLength(wallLength(selGlassWall.a, selGlassWall.b), units, lang)}</div>
              <div className="grid2">
                <label className="f"><span>{t('editor.thickness', { u })}</span><input type="number" value={inField(selGlassWall.thickness)} onChange={e => num(e.target.value, x => mutate(s => { const g = s.floor.glassWalls?.find(g => g.id === selGlassWall.id); if (g) g.thickness = r3(Math.min(x, PLAN_LIMITS.glassWall.thickMaxM * 100) / 100); }), PLAN_LIMITS.glassWall.thickMinM * 100)} /></label>
                <label className="f"><span>{t('glassWall.mullions')}</span><input type="number" min={0} max={PLAN_LIMITS.glassWall.mullionsMax} value={selGlassWall.mullions ?? 2} onChange={e => mutate(s => { const g = s.floor.glassWalls?.find(g => g.id === selGlassWall.id); if (g) g.mullions = Math.min(PLAN_LIMITS.glassWall.mullionsMax, Math.max(0, parseInt(e.target.value, 10) || 0)); })} /></label>
              </div>
              <button className="btn danger" onClick={del}>{t('common.delete')}</button>
            </div>
          )}
        </>}
        {stage === 'concept' && !showSel && <>
          <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap', marginBottom: 4 }}>
            <button
              type="button"
              className="btn primary sm"
              onClick={() => setShowLab(true)}
            >
              {t('lab.openBtn')}
            </button>
            <button
              type="button"
              className="btn sm"
              onClick={() => {
                if (house && catalog && mc) {
                  const impact = computeDesignImpact(house, snap, catalog, mc);
                  setActiveImpact(impact);
                }
              }}
            >
              {t('impact.openBtn')}
            </button>
          </div>
          {house && <StylePanel house={house} cat={catalog} mc={mc} cur={cur} onApply={(h, id) => { commitHouse(h); say(t('style.applied', { name: t(`style.${id}`) })); }} />}
          <button className="btn" onClick={async () => { if (!(await askConfirm({ title: t('dlg.autoTitle'), body: t('editor.autoConfirm'), confirmLabel: t('dlg.autoOk') }))) return; const r = autoLayout(snap, catalog); commit(r.snapshot); say(r.notFit.length ? t('editor.notFit', { list: r.notFit.map(n => t('editor.notFitItem', { key: n.key, room: n.room })).join(', ') }) : t('editor.autoApplied')); }}>{t('editor.autoAll')}</button>
          {catalog && mc && (
            <TraceabilityPanel
              snap={snap}
              catalog={catalog}
              mc={mc}
              onFocus={target => {
                if (target.kind === 'placement') {
                  setStageRaw('ffe');
                  setSel({ kind: 'placement', id: target.id });
                } else if (target.kind === 'room') {
                  setStageRaw('survey');
                  setSel({ kind: 'room', id: target.id });
                }
              }}
            />
          )}
        </>}
        {stage === 'concept' && !showSel && <DesignPanel id={id} snap={snap} say={say} cur={cur} flush={flushDraft} onBrief={onBrief} onPreview={(v: any, pid: string | null) => setPreview(v && pid ? { v, pid } : null)} onApplied={onApplied} />}
        {stage === 'concept' && !showSel && <TwinDesignPanel id={id} snap={snap} catalog={catalog} say={say} flush={flushDraft} onSelect={s => { setStageRaw(s.kind === 'placement' ? 'ffe' : 'survey'); setSel(s); }} onPreview={(s: Snapshot | null, label: string | null) => setPreview(s && label ? { v: { candidate: s, title: label, tier: 'twin-' + label }, pid: 'twin' } : null)} onApplied={onApplied} />}
        {stage === 'finishes' && !showSel && <RoomPicker snap={snap} mc={mc} onPick={rid => setSel({ kind: 'room', id: rid })} onDraw={drawRooms} />}
        {stage === 'ffe' && !showSel && <CatalogPanel locale={lang} units={units} catalog={catalog} snap={snap} onUse={(g, vid) => mutate(s => { s.selections[g] = vid; if (!s.picked.includes(g)) s.picked.push(g); })} onAdd={(vid: string) => addToRoom(vid, selRoom)} />}
        {stage === 'mep' && <TechPanel onDraw={() => setTool('tech')} snap={snap} show={showTech} kind={techKind} onShow={setShowTech} onKind={k => { setTechKind(k); setTool('tech'); }}
          onSuggest={() => { const pts = suggestTechPoints(snap); mutate(s => { s.tech = pts; }); setShowTech(true); say(t('tech.suggested', { n: pts.length })); }}
          onClear={() => mutate(s => { delete s.tech; })} onDelete={id => mutate(s => { s.tech = (s.tech || []).filter(p => p.id !== id); })}
          onPatch={(id, patch) => patchTech(id, p => { Object.assign(p, patch); })} issues={catalog ? techIssues(snap, catalog, snap.floor).map(i => ({ text: iT(i), severity: i.severity })) : []} />}
        {stage === 'docs' && <DocsPanel id={id} lang={lang} onSchedule={() => downloadSchedule(finishSchedule(house!, catalog, mc), t, cur, snap.name || 'proiect', levelName)} />}
        {stage === 'docs' && <AdvisorPanel lang={lang} units={units} currency={cur} advice={advice} rooms={snap.floor.rooms} onShow={showAdvice} />}
        {stage === 'quote' && <BudgetWithCustom id={id} snap={house!} catalog={catalog} mc={mc} out={out} onBudget={(patch: Partial<BudgetSettings>) => mutate(s => { s.budget = { ...budgetOf(s), ...patch }; })} />}
        {stage === 'quote' && <CommercialPanel id={id} cur={cur} say={say} />}
        {stage === 'present' && !showSel && <ClientPanel id={id} cur={cur} say={say} onLinked={loadRevs} flush={flushDraft} roomNames={Object.fromEntries(floors(house!).flatMap(f => f.rooms).map(r => [r.id, r.name]))} />}
        {stage === 'present' && !showSel && <RenderGallery id={id} />}
        {stage === 'present' && !showSel && <div className="docs"><a className="docrow" href={`/p/${id}/present`} target="_blank" rel="noopener"><b>{t('stage.openPresentation')}</b><small>{t('stage.openPresentationSub')}</small></a><a className="docrow" href={`/p/${id}/board`} target="_blank" rel="noopener"><b>{t('board.open')}</b><small>{t('board.openSub')}</small></a></div>}
        {stage === 'present' && !showSel && <div className="revs"><h3>{t('stage.revisions')}</h3>{revs.length === 0 && <EmptyState title={t('empty.revs.title')} text={t('empty.revs.text')} action={{ label: t('editor.saveRevision'), onClick: () => { setRevBusy(true); flight.run('revision', saveRevision).finally(() => setRevBusy(false)); } }} />}
          {revs.map(r => <div key={r.number} className="r"><span>{t('editor.revisionN', { n: r.number })}{r.note ? ` · ${r.note}` : ''}</span><button className="btn" onClick={() => restore(r.number)}>{t('editor.revert')}</button><button className="btn" onClick={() => shareRevision(r.number)} title={t('editor.shareTitle')}>{t('editor.share')}</button><small>{when(r.created_at)}</small></div>)}
          <RevisionDiff id={id} revs={revs} lang={lang} units={units} />
          {shares.filter(s => s.active).length > 0 && <><h4>{t('editor.activeShares')}</h4>{shares.filter(s => s.active).map(s => <div key={s.token} className="r"><span>{t('editor.revisionN', { n: s.revisionNumber })}</span><a className="btn" href={s.path} target="_blank" rel="noopener">{t('common.open')}</a><button className="btn danger" onClick={() => revokeShare(s.token)}>{t('editor.revoke')}</button><small>{when(s.createdAt)} · {s.expiresAt ? t('editor.shareExpires', { date: new Date(s.expiresAt).toLocaleDateString(intlLocale(lang)) }) : t('editor.shareNoExpiry')} · {s.lastAccessAt ? t('editor.shareLastOpen', { date: new Date(s.lastAccessAt).toLocaleDateString(intlLocale(lang)) }) : t('editor.shareNeverOpened')}</small></div>)}</>}</div>}
      </aside>
    </div>}
    {toast && <div className={`toast ${compact ? "above-sheet" : ""}`} role="status" aria-live="polite" onMouseEnter={() => clearTimeout(toastT.current)} onMouseLeave={() => { toastT.current = setTimeout(() => setToast(""), 3000); }}><span>{toast}</span><button type="button" className="toast-x" aria-label={t("common.close")} onClick={() => setToast("")}><Icon name="close" size={16} /></button></div>}
    {showLab && snap && catalog && mc && (
      <DesignOptionsLab
        baseSnap={snap}
        alternatives={labAlternatives}
        catalog={catalog}
        mc={mc}
        onApply={(optSnap, title) => {
          commit(optSnap);
          say(t('lab.appliedToast', { title }));
        }}
        onClose={() => setShowLab(false)}
      />
    )}
    {showHealth && snap && catalog && mc && (
      <div
        role="dialog"
        aria-modal="true"
        onClick={(e) => {
          if (e.target === e.currentTarget) setShowHealth(false);
        }}
        style={{
          position: 'fixed',
          inset: 0,
          zIndex: 100,
          backgroundColor: 'rgba(25, 24, 22, 0.72)',
          backdropFilter: 'blur(4px)',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          padding: 20,
        }}
      >
        <div
          style={{
            backgroundColor: '#FFFFFF',
            borderRadius: 12,
            maxWidth: 580,
            width: '100%',
            padding: 20,
            boxShadow: '0 20px 50px rgba(0,0,0,0.3)',
            position: 'relative',
            maxHeight: '85vh',
            overflowY: 'auto',
          }}
        >
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 14 }}>
            <h3 style={{ margin: 0, fontSize: 'var(--fs-md)' }}>
              {t('health.cockpit')}
            </h3>
            <button type="button" className="btn ghost sm" onClick={() => setShowHealth(false)}>
              <Icon name="close" size={18} />
            </button>
          </div>
          <ProjectHealthPanel
            snap={snap}
            catalog={catalog}
            mc={mc}
            onAction={act => {
              if (act.stageTarget) setStageRaw(act.stageTarget as any);
              // problema se selectează și se încadrează în plan (UI/UX P2-12)
              const target: Sel = act.targetKind === 'placement' && act.targetId ? { kind: 'placement', id: act.targetId } : act.targetKind === 'room' && act.targetId ? { kind: 'room', id: act.targetId } : null;
              if (target){ setSel(target); setFrame(f => ({ sel: target, n: (f?.n ?? 0) + 1, at: Date.now() })); }
              setShowHealth(false);
            }}
          />
        </div>
      </div>
    )}
    {activeImpact && (
      <ImpactModal
        impact={activeImpact}
        onClose={() => setActiveImpact(null)}
      />
    )}
  </div>);
}

function RoomPanel({ part, focusFeature, room, snap, G, cat, mc, cur, onFinish, onChange, onAuto, onAdd, onSwap, onResize, onSplit, onMerge, num }: any){
  const { t, tp, lang, units } = usePrefs(), iT = (i: Parameters<typeof issueText>[1]) => issueText(lang, i, units), u = lengthInputUnit(units), inField = (m: number) => cmToInput(m * 100, units);
  // „Adaugă mobilier” pornește de la grupele potrivite camerei (dormitor: pat, dulap, noptieră…), restul sub „Altele”
  const avail = useMemo(() => Object.keys(G).filter(k => !G[k].includedWith), [G]), fit = useMemo(() => groupsForRoom(room.type, avail), [room.type, avail]);
  const [grp, setGrp] = useState<string>(() => defaultGroupForRoom(room.type, avail) ?? avail[0]!), [vi, setVi] = useState(0);
  useEffect(() => { setGrp(defaultGroupForRoom(room.type, avail) ?? avail[0]!); setVi(0); }, [room.id, room.type]); // eslint-disable-line
  const w = room.rect.x1 - room.rect.x0, d = room.rect.z1 - room.rect.z0;
  const mergePeers = useMemo(() => (snap.floor?.rooms ?? []).filter((o: any) => o.id !== room.id && ((Math.abs(o.rect.z0 - room.rect.z0) < 1e-4 && Math.abs(o.rect.z1 - room.rect.z1) < 1e-4 && (Math.abs(o.rect.x0 - room.rect.x1) < 1e-4 || Math.abs(o.rect.x1 - room.rect.x0) < 1e-4)) || (Math.abs(o.rect.x0 - room.rect.x0) < 1e-4 && Math.abs(o.rect.x1 - room.rect.x1) < 1e-4 && (Math.abs(o.rect.z0 - room.rect.z1) < 1e-4 || Math.abs(o.rect.z1 - room.rect.z0) < 1e-4)))), [snap.floor?.rooms, room]);
  return (<>
    <h3>{room.name}</h3><div className="prov">{formatArea(area(room.rect), units, lang)} · {tp('editor.pieces', snap.placements.filter((p: any) => p.roomId === room.id).length)}</div>
    {part === 'survey' && <><div className="grid2">
      <label className="f"><span>{t('editor.name')}</span><input type="text" value={room.name} maxLength={60} onChange={e => onChange((r: any) => { r.name = e.target.value; })} /></label>
      <label className="f"><span>{t('editor.type')}</span><select value={room.type} onChange={e => onChange((r: any) => { r.type = e.target.value; })}>{ROOM_TYPES.map(([v, l]) => <option key={v} value={v}>{t(l)}</option>)}</select></label>
      {/* tipul spațiului (balcon, terasă): interior = câmpul lipsește, ca proiectele de până acum */}
      <label className="f"><span>{t('space.label')}</span><select value={room.spaceKind ?? 'interior'} onChange={e => onChange((r: any) => { const v = e.target.value as RoomSpaceKind; if (v === 'interior') delete r.spaceKind; else r.spaceKind = v; })}>{SPACE_KINDS.map(k => <option key={k} value={k}>{t(`space.${k}`)}</option>)}</select></label>
      <label className="f"><span>{t('editor.width', { u })}</span><input type="number" inputMode="decimal" value={inField(w)} onChange={e => num(e.target.value, (x: number) => onResize({ ...room.rect, x1: r3(room.rect.x0 + x / 100) }), 60)} /></label>
      <label className="f"><span>{t('editor.length', { u })}</span><input type="number" inputMode="decimal" value={inField(d)} onChange={e => num(e.target.value, (x: number) => onResize({ ...room.rect, z1: r3(room.rect.z0 + x / 100) }), 60)} /></label>
      <label className="f"><span>{t('editor.posX', { u })}</span><input type="number" value={inField(room.rect.x0)} onChange={e => { const cm = inputToCm(e.target.value, units), x = cm == null ? NaN : cm / 100; if (Number.isFinite(x)) onChange((r: any) => { r.rect.x1 = r3(x + (r.rect.x1 - r.rect.x0)); r.rect.x0 = r3(x); }); }} /></label>
      <label className="f"><span>{t('editor.posZ', { u })}</span><input type="number" value={inField(room.rect.z0)} onChange={e => { const cm = inputToCm(e.target.value, units), z = cm == null ? NaN : cm / 100; if (Number.isFinite(z)) onChange((r: any) => { r.rect.z1 = r3(z + (r.rect.z1 - r.rect.z0)); r.rect.z0 = r3(z); }); }} /></label>
    </div>
    <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
      {onSplit && <button type="button" className="btn sm" disabled={w < 1.2} onClick={() => onSplit('x')}>{t('editor.splitX')}</button>}
      {onSplit && <button type="button" className="btn sm" disabled={d < 1.2} onClick={() => onSplit('z')}>{t('editor.splitZ')}</button>}
      {onMerge && mergePeers.map((o: any) => <button key={o.id} type="button" className="btn sm" onClick={() => onMerge(o.id)}>{t('editor.mergeWith', { room: o.name })}</button>)}
    </div>
    <p className="prov" style={{ margin: 0 }}>{t('editor.roomWallsShort')}</p></>}
    {part === 'finishes' && <FinishesPanel room={room} snap={snap} cat={cat} mc={mc} cur={cur} onFinish={onFinish} onSwap={onSwap} onAdd={onAdd} focusFeature={focusFeature} />}
    {part === 'ffe' && <><button className="btn" onClick={onAuto}>{t('editor.autoRoom')}</button>
    <h4>{t('editor.addFurniture')}</h4>
    <label className="f"><span>{t('editor.addGroup')}</span><select value={grp} onChange={e => { setGrp(e.target.value); setVi(0); }}>
      {fit.suggested.length > 0 && <optgroup label={t('editor.groupsFor', { room: t(`roomType.${room.type}`) })}>{fit.suggested.map(k => <option key={k} value={k}>{G[k].label}</option>)}</optgroup>}
      <optgroup label={fit.suggested.length ? t('editor.groupsOther') : t('editor.groupsAll')}>{fit.others.map(k => <option key={k} value={k}>{G[k].label}</option>)}</optgroup></select></label>
    <label className="f"><span>{t('editor.addVariant')}</span><select value={vi} onChange={e => setVi(Number(e.target.value))}>{(G[grp]?.variants ?? []).map((v: any, i: number) => <option key={v.id} value={i}>{v.name}</option>)}</select></label>
    <button className="btn primary" disabled={!G[grp]?.variants[vi]} onClick={() => onAdd(G[grp].variants[vi].id)}>{t('editor.addToRoom')}</button></>}
  </>);
}
function PlacementPanel({ out, p, snap, catalog, issues, G, num, onVariant, onMove, onRotate, onDuplicate, onDelete, onFocusIssue }: { out: Outbound | null; p: FurniturePlacement; snap: Snapshot; catalog: Catalog; issues: Issue[]; G: any; num: any; onVariant(v: string): void; onMove(x: number, z: number): void; onRotate(): void; onDuplicate(): void; onDelete(): void; onFocusIssue(i: Issue): void }){
  const { t, tp, lang, units } = usePrefs(), iT = (i: Parameters<typeof issueText>[1]) => issueText(lang, i, units), u = lengthInputUnit(units), cur = catalogCurrency(catalog), lei = (v: number) => formatMoney(v, cur, lang), inField = (m: number) => cmToInput(m * 100, units);
  const rv = resolve(catalog, p.variantId)!, room = snap.floor.rooms.find(r => r.id === p.roomId), g = G[groupOf(p.variantId)];
  return (<>
    <h3>{rv.variant.name}</h3>
    <div className="prov">{g.label} · {room?.name} · {rv.offer ? lei(rv.offer.price) : t('common.unknownPrice')}</div>
    {rv.offer && <div className="prov">{t('editor.provenance', { source: rv.offer.provenance.source ?? '', date: rv.offer.provenance.verifiedAt ?? '', conf: rv.offer.provenance.confidence ?? '', dimConf: rv.variant.dimensionsConfidence, stock: rv.offer.availability === 'UNKNOWN' ? t('common.unknown') : rv.offer.availability })}{freshness(rv.offer.provenance.verifiedAt).stale ? ` · ${t('editor.priceRecheck')}` : ''}</div>}
    {issues.length ? issues.map((i, k) => { const tg = issueTarget(i, snap.floor, snap.placements); return <div key={k} className={`issue ${i.severity}`} role={tg ? 'button' : undefined} tabIndex={tg ? 0 : undefined} style={tg ? { cursor: 'pointer' } : undefined} onClick={tg ? () => onFocusIssue(i) : undefined} onKeyDown={tg ? e => { if (e.key === 'Enter' || e.key === ' '){ e.preventDefault(); onFocusIssue(i); } } : undefined}>{iT(i)}</div>; }) : <div className="issue PASS">{t('editor.positionValid')}</div>}
    <div className="grid2">
      <label className="f"><span>{t('editor.xField', { u })}</span><input type="number" value={inField(p.x)} onChange={e => num(e.target.value, (x: number) => onMove(x / 100, p.z), -1e9)} /></label>
      <label className="f"><span>{t('editor.zField', { u })}</span><input type="number" value={inField(p.z)} onChange={e => num(e.target.value, (z: number) => onMove(p.x, z / 100), -1e9)} /></label>
    </div>
    <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}><button className="btn" onClick={onRotate}>{t('editor.rotate')}</button><button className="btn" onClick={onDuplicate} title={commandHint('duplicate', { mac: typeof navigator !== 'undefined' && /Mac|iPhone|iPad/.test(navigator.platform || navigator.userAgent) })}>{t('editor.duplicate')}</button>{rv.offer && out?.targets[`o:${rv.offer.id}`] && <a className="btn" href={`/go/o/${rv.offer.id}`} target="_blank" rel={relFor(out.targets[`o:${rv.offer.id}`])}>{t('editor.viewProduct')}{out.targets[`o:${rv.offer.id}`] === 'affiliate' ? ` ${t('editor.affiliateTag')}` : ''}</a>}<button className="btn danger" onClick={onDelete}>{t('common.delete')}</button></div>
    <h4>{t('editor.variantsOnly', { n: g.variants.length })}</h4>
    {g.variants.map((v: any) => { const o = catalog.offers.find(x => x.variantId === v.id), dm = v.dimensionsCm;
      return <button key={v.id} className="var" aria-pressed={v.id === p.variantId} onClick={() => onVariant(v.id)}><span>{v.name}</span><span className="mono">{o ? lei(o.price) : '—'}</span><small>{dm ? formatDimsCm(dm, units, lang) : t('common.unknownDims')}{v.dimensionsConfidence === 'MEDIUM' ? ` ${t('common.approx')}` : ''}</small></button>; })}
  </>);
}

function DoorPanel({ op, snap, mc, cur, onChange }: { op: Opening; snap: Snapshot; mc: MaterialsCatalog; cur: string; onChange(p: DoorChoice): void }){
  const { t, lang } = usePrefs(), c = snap.doors?.[op.id] ?? {}, by = (k: string) => mc.materials.filter(m => m.category === k), type = doorTypeOf(op) ?? 'swing';
  // sistemul (șina, caseta, kitul pliant) doar pe tipurile care îl cer și doar cu produsele făcute pentru tipul golului
  const systems = by('door_system').filter(m => m.specs?.system === type);
  const iss = doorIssues(snap, mc).filter(i => i.openingId === op.id);
  return (<>
    <h4>{t('door.title')}</h4>
    {iss.map((i, k) => <div key={k} className="issue WARNING">{t(i.key, i.key === 'door.typeMismatch' ? { ...i.vars, type: String(i.vars?.type ?? '').split(', ').map(x => t(`door.type.${x}`)).join(', '), gap: t(`door.type.${i.vars?.gap}`) } : i.key === 'door.techInSlide' ? { ...i.vars, point: TECH_KINDS[i.vars?.point as TechKind]?.label[lang === 'en' ? 'en' : 'ro'] ?? String(i.vars?.point) } : i.vars)}</div>)}
    <label className="f"><span>{t('door.product')}</span><MaterialPicker cur={cur} value={c.product} options={by('door')} none={t('door.none')} onChange={v => onChange({ product: v || null })} /></label>
    <label className="f"><span>{t('door.handle')}</span><MaterialPicker cur={cur} value={c.handle} options={by('door_handle')} none={t('door.none')} onChange={v => onChange({ handle: v || null })} /></label>
    {(type === 'sliding' || type === 'pocket' || type === 'folding') && (systems.length > 0 || !!c.system) && <label className="f"><span>{t('door.system')}</span><MaterialPicker cur={cur} value={c.system} options={systems} none={t('door.none')} onChange={v => onChange({ system: v || null })} /></label>}
  </>);
}

/** Sub titlul etapei: faza, câte etape sunt gata și ce face etapa aceasta „gata” (aceeași regulă ca bulina din bara de etape). */
function StageMeta({ stage, done }: { stage: Stage; done: Record<Stage, boolean> | null }){
  const { t } = usePrefs(); if (!done) return null; const ok = done[stage];
  return (<div className="stage-meta">
    <p className="sm-phase">{t('stage.meta', { phase: t(`phase.${phaseOf(stage)}`), n: doneCount(done), total: STAGE_IDS.length })}</p>
    <p className={`sm-why ${ok ? 'is-done' : ''}`}><span className={`st-dot ${ok ? 'is-done' : ''}`} aria-hidden="true" /><b>{ok ? t('stage.isDone') : t('stage.isTodo')}</b> · {t(`stage.why.${stage}`)}</p>
  </div>);
}
/** Etapa Finisaje fără selecție: camerele ca listă de lucru, cu pardoseala aleasă și starea finisajelor. */
function RoomPicker({ snap, mc, onPick, onDraw }: { snap: Snapshot; mc: MaterialsCatalog; onPick(id: string): void; onDraw(): void }){
  const { t, lang, units } = usePrefs();
  if (!snap.floor.rooms.length) return <EmptyState title={t('empty.rooms.title')} text={t('empty.rooms.text')} action={{ label: t('empty.rooms.action'), onClick: onDraw }} />;
  return (<div className="roompick">
    <p className="muted">{t('stage.finishesPick')}</p>
    {snap.floor.rooms.map(r => { const f = finishesOf(snap, r), fl = materialOf(mc, f.floor), feats = (f.wallFeatures || []).length;
      return <button key={r.id} className="roomrow" onClick={() => onPick(r.id)}>
        <i style={{ background: fl?.specs?.color ?? '#d9d2c4' }} aria-hidden />
        <span><b>{r.name}</b><small>{formatArea(area(r.rect), units, lang)} · {t(`roomType.${r.type}`)}</small></span>
        <small>{fl ? fl.name.split(',')[0] : '—'}{feats ? ` · ${t('stage.accents', { n: feats })}` : ''}</small></button>; })}
  </div>);
}
/** Etapa Planșe: setul de documentație pentru client și echipe. */
function DocsPanel({ id, lang, onSchedule }: { id: string; lang: string; onSchedule(): void }){
  const { t } = usePrefs();
  return (<div className="docs">
    <a className="docrow" href={`/p/${id}/print`} target="_blank" rel="noopener"><b>{t('docs.pack')}</b><small>{t('docs.packSub')}</small></a>
    <button className="docrow" onClick={onSchedule}><b>{t('docs.schedule')}</b><small>{t('docs.scheduleSub')}</small></button>
    <a className="docrow" href={`/api/projects/${id}/export.dxf?lang=${lang}`} download><b>{t('docs.dxf')}</b><small>{t('docs.dxfSub')}</small></a>
  </div>);
}

/** Randările fotorealiste făcute pe acest calculator (Blender Cycles), cele mai noi întâi; se reîmprospătează cât timp lucrează. */
// Linkul pentru client și părerile venite pe el: un buton care salvează versiunea și copiază linkul; părerile grupate pe camere, de bifat.
type ClientFeedback = { id: string; roomId: string | null; verdict: 'like' | 'change' | 'question'; author: string; text: string; createdAt: string; resolvedAt: string | null; revisionNumber: number; item?: { key: string; label: string; role: string | null } | null; anchor?: { surface: 'room' | 'overview'; u: number; v: number } | null; token?: string };
// PIN-ul pentru pinii pe imagine, pe linkul activ cel mai nou: Generează / Regenerează (cookie-urile vechi ale clientului mor) / Dezactivează.
// PIN-ul se arată o singură dată (serverul ține doar hash-ul).
function SharePinAdmin({ id, tick, say }: { id: string; tick: number; say(s: string): void }){
  const { t } = usePrefs(), [share, setShare] = useState<{ token: string; pinEnabled: boolean } | null | undefined>(undefined), [shown, setShown] = useState<string | null>(null), [busy, setBusy] = useState(false);
  useEffect(() => { let alive = true; fetch(`/api/projects/${id}/shares`).then(r => r.ok ? r.json() : []).then((xs: any[]) => { if (!alive) return; const s = xs.find(x => x.active);
    setShare(s ? { token: s.token, pinEnabled: !!s.pinEnabled } : null); setShown(null); }).catch(() => { if (alive) setShare(null); }); return () => { alive = false; }; }, [id, tick]);
  async function act(method: 'POST' | 'DELETE'){ if (!share || busy) return; setBusy(true);
    try { const r = await fetch(`/api/projects/${id}/shares/pin`, { method, headers: { 'content-type': 'application/json' }, body: JSON.stringify({ token: share.token }) }), j = await r.json().catch(() => null);
      if (!r.ok){ say(j?.error || t('pin.failed')); return; }
      setShare({ ...share, pinEnabled: method === 'POST' }); setShown(method === 'POST' ? String(j?.pin ?? '') : null); if (method === 'DELETE') say(t('pin.disabled')); }
    catch { say(t('pin.failed')); } finally { setBusy(false); } }
  if (share === undefined) return null;
  return (<div className="pin-admin"><h3>{t('pin.adminTitle')}</h3><p className="muted small">{t('pin.adminSub')}</p>
    {!share ? <p className="muted small">{t('pin.noLink')}</p> : <>
      {share.pinEnabled && !shown && <p className="small">{t('pin.enabled')}</p>}
      {shown && <p className="notice" role="status"><b>{t('pin.shown', { pin: shown })}</b></p>}
      <div className="row"><button type="button" className="btn" disabled={busy} onClick={() => act('POST')}>{share.pinEnabled ? t('pin.regenerate') : t('pin.generate')}</button>
        {share.pinEnabled && <button type="button" className="btn" disabled={busy} onClick={() => act('DELETE')}>{t('pin.disable')}</button>}</div></>}
  </div>);
}
function ClientPanel({ id, say, onLinked, flush, roomNames, cur }: { id: string; say(s: string): void; onLinked(): void; flush(): Promise<boolean>; roomNames: Record<string, string>; cur: string }){
  const { t, lang } = usePrefs(), [link, setLink] = useState<{ url: string; local: boolean } | null>(null), [busy, setBusy] = useState(false), [items, setItems] = useState<ClientFeedback[] | null>(null), [tick, setTick] = useState(0);
  useEffect(() => { let alive = true; const load = () => fetch(`/api/projects/${id}/feedback`).then(r => r.ok ? r.json() : null).then(j => { if (alive && j) setItems(j); }).catch(() => {});
    load(); const iv = setInterval(load, 30000); return () => { alive = false; clearInterval(iv); }; }, [id]);
  const copy = async (url: string) => { try { await navigator.clipboard.writeText(url); say(t('client.copied')); } catch { /* fără clipboard: linkul rămâne afișat */ } };
  const making = useRef(false);
  async function make(){ if (making.current) return; making.current = true; setBusy(true); try { if (!(await flush())) return; const r = await fetch(`/api/projects/${id}/client-link`, { method: 'POST' }), j = await r.json();
      if (!r.ok){ say(j.error || t('client.failed')); return; }
      const url = j.url ?? `${location.origin}${j.path}`, local = j.publicUrl === false && isLocalHostname(location.hostname);
      setLink({ url, local }); setTick(n => n + 1); if (j.newRevision) say(t('client.newRev', { n: j.revisionNumber })); await copy(url); onLinked();
      prepareShareImages(id, j.revisionNumber).then(k => { if (k) say(t('client.imagesReady')); }); }
    catch { say(t('client.failed')); } finally { making.current = false; setBusy(false); } }
  async function toggle(f: ClientFeedback){ const r = await fetch(`/api/projects/${id}/feedback`, { method: 'PATCH', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ id: f.id, resolved: !f.resolvedAt }) });
    if (r.ok){ const j = await r.json(); setItems(xs => (xs ?? []).map(x => x.id === f.id ? { ...x, resolvedAt: j.resolvedAt } : x)); } }
  const open = (items ?? []).filter(f => !f.resolvedAt).length, groups = new Map<string, ClientFeedback[]>();
  for (const f of items ?? []){ const k = f.roomId ?? ''; groups.set(k, [...(groups.get(k) ?? []), f]); }
  return (<div className="client">
    <div className="docs"><button className="docrow" onClick={make} disabled={busy}><b>{t('client.link')}</b><small>{t('client.linkSub')}</small></button></div>
    {link && <div className="client-link"><input readOnly value={link.url} onFocus={e => e.currentTarget.select()} aria-label={t('client.link')} /><button className="btn" onClick={() => copy(link.url)}>{t('client.copy')}</button>
      {link.local && <div className="notice warn" role="alert"><Icon name="alert" /><div><b>{t('client.localTitle')}</b><p>{t('client.localOnly')}</p></div></div>}</div>}
    <SharePinAdmin id={id} tick={tick} say={say} />
    <DesignerChanges id={id} cur={cur} say={say} />
    <h3>{t('client.feedback')}{open > 0 && <span className="pill">{t('client.open', { n: open })}</span>}</h3>
    {items && items.length === 0 && <EmptyState title={t('empty.feedback.title')} text={t('empty.feedback.text')} action={{ label: t('client.link'), onClick: make }} />}
    {[...groups.entries()].map(([room, fs]) => <div key={room} className="fbg"><h4>{room ? roomNames[room] ?? room : t('client.general')}</h4>
      {fs.map(f => <div key={f.id} className={`fbi ${f.resolvedAt ? 'done' : ''}`}><span className={`fb-tag fb-${f.verdict}`}>{f.item && f.verdict === 'like' ? t('fb.isApproved') : t(`fb.${f.verdict}`)}{f.item ? ` · ${f.item.label}` : ''}</span>{f.anchor && <span className="fb-tag pin-tag">{t('pin.onImage')}</span>}{f.text && <q>{f.text}</q>}
        {f.anchor && f.token && <a className="linkish small" href={`/share/${f.token}#pin-${f.id}`} target="_blank" rel="noopener">{t('pin.viewOnImage')}</a>}
        <small>{f.author} · {new Date(f.createdAt).toLocaleString(intlLocale(lang), { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' })} · {t('client.rev', { n: f.revisionNumber })}</small>
        <button className="btn" onClick={() => toggle(f)}>{f.resolvedAt ? t('client.reopen') : t('client.resolve')}</button></div>)}</div>)}
  </div>);
}

function RenderGallery({ id }: { id: string }){
  const { t, lang } = usePrefs(), [data, setData] = useState<{ available: boolean; jobs: { job: string; status: string; images: string[]; startedAt: string }[] } | null>(null);
  useEffect(() => { let alive = true, timer: any; const load = () => fetch(`/api/projects/${id}/renders`).then(r => r.ok ? r.json() : null).then(j => { if (!alive) return; setData(j);
      if (j?.jobs?.some((x: any) => x.status === 'running')) timer = setTimeout(load, 5000); }).catch(() => {});
    load(); return () => { alive = false; clearTimeout(timer); }; }, [id]);
  if (!data) return null;
  return (<div className="renders"><h3>{t('render.title')}</h3>
    {!data.available && <p className="muted">{t('render.notConfigured')}</p>}
    {data.available && data.jobs.length === 0 && <EmptyState title={t('empty.renders.title')} text={t('render.none')} />}
    {data.jobs.slice(0, 3).map(j => <div key={j.job} className="render-job"><small>{new Date(j.startedAt).toLocaleString(intlLocale(lang))} · {t(`render.status.${j.status}`)}</small>
      <div className="render-grid">{j.images.map(f => <a key={f} href={`/api/projects/${id}/renders/${j.job}/${f}`} target="_blank" rel="noopener"><img src={`/api/projects/${id}/renders/${j.job}/${f}`} alt={f} loading="lazy" /></a>)}</div></div>)}
  </div>);
}

/** Tabletă pe verticală și telefon (sub 1024 px): planul și 3D-ul ocupă tot ecranul, ca file (Plan / 3D / Panou), iar
 *  panoul e o foaie care urcă de jos. Aceeași limită ca în globals.css (--bp-compact). */
const COMPACT_Q = '(max-width: 1023px)';
const isCompact = () => typeof matchMedia === 'function' && matchMedia(COMPACT_Q).matches;
function useCompact(){
  const [c, setC] = useState(false);
  useEffect(() => { if (typeof matchMedia !== 'function') return; const m = matchMedia(COMPACT_Q), on = () => setC(m.matches); on();
    m.addEventListener?.('change', on); return () => m.removeEventListener?.('change', on); }, []);
  return c;
}
/** Starea salvării, vizibilă pe orice ecran (pe telefon doar pictograma și cuvântul): salvat, se salvează, eroare. */
function SaveChip({ save, rev }: { save: string; rev: number }){
  const { t } = usePrefs();
  const label = save === 'saved' ? t('editor.saved') : save === 'saving' ? t('editor.saving') : save === 'dirty' ? t('editor.dirty') : save === 'conflict' ? t('editor.conflictShort') : save === 'offline' ? t('editor.offline') : save === 'auth' ? t('editor.authLost') : t('editor.saveError');
  const kind = save === 'saved' ? 'ok' : save === 'saving' || save === 'dirty' ? 'busy' : 'err';
  return (<span className={`status save-${kind}`} role="status" aria-live="polite" title={`${label} · ${rev ? t('editor.revisionN', { n: rev }) : t('editor.noRevisions')}`}>
    <Icon name={kind === 'ok' ? 'cloudCheck' : kind === 'busy' ? 'spinner' : save === 'offline' ? 'cloudOff' : 'alert'} className={kind === 'busy' ? 'spin' : ''} />
    <span className="status-main">{label}</span><span className="status-rev"> · {rev ? t('editor.revisionN', { n: rev }) : t('editor.noRevisions')}</span></span>);
}
