'use client';
import { useEffect, useMemo, useRef, useState } from 'react';
import type { Catalog, Floor, Snapshot, Severity, Wall } from '@/core/types';
import { footprintOf } from '@/core/validate';
import { resolve } from '@/core/catalog';
import { r3, snapPoint, wallLength } from '@/core/geometry';
import { roomArea, roomBoundaryPath, roomCentroid, transformBoundary, pointInRoom } from '@/core/room-geometry';
import { measure } from '@/core/edit-ops';
import { formatArea, formatLength } from '@/core/format';
import { snapDraw, pointAtLength, SEGMENT_THICKNESS, type SnapResult } from '@/core/snap';
import { contextCommands, groupContextCommands, commandById, commandHint, type ContextKind } from '@/lib/commands';
import { groupIds, toggleMulti, addToGroup, piecesInRect } from '@/core/arrange';
import { LONG_PRESS_MS, LONG_PRESS_MOVE_PX, MARQUEE_OVER, isLongPress, pressTravel, longPressAction, groupAfterKeyToggle, isGroupToggleKey, tabMovesCursor, type KeyCursor, toggleInGroup } from '@/core/multi-select';
import PlanContextMenu from './PlanContextMenu';
import { usePrefs } from '@/lib/prefs';
import type { Calib } from './UnderlayPanel';
import { TECH_SYMBOL } from './TechPanel';
import { pinchViewBox, midpoint, distance, type Pt } from '@/core/gesture';
import { stairGeometry, SLAB } from '@/core/levels';
import { planSymbol } from '@/core/plan-symbols';
import { symbolTransform } from '@/core/mirror';
import { doorSwing } from '@/core/doors';
import { doorSymbol } from '@/core/door-symbols';
import { nicheGeometry, nicheSide } from '@/core/niches';
import { wallItemGeometry, wallItemDepth } from '@/core/wall-items';
import { formatLevelValue, dimGeometry, dimOffsetAt, dimOk, textHeight, leaderGeometry, levelMarkGeometry, textAnchor, textAnchorAt, moveAnnotation, angleGeometry, angleArcPath, angleLabelAt, angleOk, angleRadiusAt, formatAngle, cloudPath, cloudLabelAt, cloudOk, CLOUD_LABEL_M } from '@/core/annotations';
import { PLAN_DEFAULTS, PLAN_LIMITS, cleanPlanText, cleanCloudLabel, stripPlanText } from '@/core/plan-limits';
import type { AngleAnnotation, DimAnnotation } from '@/core/types';
import { allFixturePoints } from '@/core/fixtures';
import { MEP_SYSTEMS, MEP_LIMITS } from '@/core/mep';
import { appendRunPoint, dropLastRunPoint, orthoPoint } from '@/core/mep-draw';
import { finishesOf } from '@/core/boq';
import { cladTarget, featurePlanRect, featureMarks, materialOf } from '@/core/finishes';
import type { MaterialsCatalog, WallFeature } from '@/core/types';
import { planItems, planKeyAction, cycleSelection, type PlanKeyAction } from '@/core/plan-keys';
import { pickable, type LayerId, type LayerState } from '@/core/layers';
import { resizeRoom, RoomResizeError } from '@/core/room-resize';
import { MIN_ROOM_M, type EdgeKey } from '@/core/room-neighbours';
import { edgeHandleAt, edgeHandlePoint, splitAxisFor, canSplitRoom } from '@/core/room-edit';
import { computeLightingWiringArcs } from '@/core/technical';
import { parseTypedLength, parseTypedSize, typedRect, acceptsTypedKey } from '@/core/typed-dimension';
import { selectionBox, frameViewBox } from '@/core/frame';
import { radiatorRect, isSizedRadiator } from '@/core/tech-geometry';
import { PLAN_LIMITS as LIM } from '@/core/plan-limits';
import { railingHeight, railingKind } from '@/core/railings';
import { roomRugs, rugSize, rugRectAt } from '@/core/textiles';

export type Tool = 'select' | 'wall' | 'room' | 'door' | 'window' | 'passage' | 'measure' | 'shaft' | 'niche' | 'dim' | 'text' | 'tech' | 'column' | 'beam' | 'glass_wall' | 'stair' | 'clad' | 'wall_item' | 'demolish' | 'floor_zone' | 'ceiling_zone' | 'railing' | 'pan' | 'zoom_area' | 'rug' | 'style_copy' | 'split' | 'leader' | 'level_mark' | 'angle_dim' | 'rev_cloud' | 'mep_run';
export type Sel = { kind: 'wall' | 'room' | 'placement' | 'stair' | 'column' | 'beam' | 'glass_wall' | 'niche' | 'annotation' | 'wall_item' | 'floor_zone' | 'ceiling_zone' | 'railing' | 'mep_run'; id: string } | { kind: 'opening'; id: string; wallId: string } | null;
type Phase = 'start' | 'move' | 'end';
export type TextExtra = { leader?: [number, number]; symbol?: 'level' };
/** uneltele care deschid câmpul de text de pe plan */
const TEXT_TOOLS: Tool[] = ['text', 'leader', 'level_mark'];
interface Props { snap: Snapshot; catalog: Catalog; sel: Sel; tool: Tool; severities: Record<string, Severity>;
  /** cerere de încadrare din afară (clic pe o problemă din „Sănătate”): `n` crește la fiecare cerere */
  frame?: { sel: Sel; n: number; at: number };
  /** selecția comună cu 3D (UI/UX P2-10): piesa de sub mouse (aici sau în 3D) și „Arată în 3D” din bara piesei */
  hoverId?: string | null; onHover?(id: string | null): void; onFrameOther?(id: string): void;
  onSelect(s: Sel): void; onEdit(fn: (s: Snapshot) => void, phase: Phase): void; onAddWall(a: [number, number], b: [number, number]): void;
  onAddRoom(r: { x0: number; z0: number; x1: number; z1: number }): void; onAddOpening(wallId: string, offset: number, kind: 'door' | 'window' | 'passage'): void;
  onAddNiche?(wallId: string, offset: number, side: 1 | -1): void;
  /** unealta „Traseu instalații”: culoarea sistemului ales și dacă se poate desena (cota traseului declarată); punctele sunt
   *  cele din plan, în ordinea clicurilor. `mepSelectable`: traseele se pot alege cu clic (doar în etapa Instalații). */
  mepDraw?: { color: string; ready: boolean }; onAddMepRun?(pts: [number, number][]): boolean | void; onMepNotReady?(): void; mepSelectable?: boolean;
  /** unealta „Placare”: latura camerei și porțiunea (m de la colțul x0/z0); fără `from`/`to` = toată latura */
  onAddClad?(roomId: string, side: WallFeature['side'], from?: number, to?: number): void;
  /** stratul de finisaje de perete (benzile placărilor cu marcajul lor), în etapele Finisaje și Planșe */
  showFinishes?: boolean; mc?: MaterialsCatalog | null;
  /** unealta „Pe perete”: clic lângă perete, pe fața pe care stă obiectul; `showWallItems` = false le ascunde (Releveu) */
  onAddWallItem?(wallId: string, offset: number, side: 1 | -1): void; showWallItems?: boolean;
  onAddDim?(a: [number, number], b: [number, number], offset: number, mode: 'aligned' | 'ortho'): void;
  /** cota unghiulară: vârful, câte un punct pe fiecare braț, raza arcului (m) */
  onAddAngle?(v: [number, number], a: [number, number], b: [number, number], r: number): void;
  /** norul de revizie: dreptunghiul tras și eticheta scurtă (lipsă = fără etichetă) */
  onAddCloud?(rect: { x0: number; z0: number; x1: number; z1: number }, label?: string): void;
  /** `extra`: vârful săgeții („Săgeată cu notă”) sau simbolul cotei de nivel; lipsă = text simplu */
  onAddText?(at: [number, number], text: string, extra?: TextExtra): void;
  /** unealta „Demolare”: clic pe perete comută starea lui (existent ↔ de demolat) */
  onToggleDemolish?(wallId: string): void;
  /** unealta „Zonă pardoseală”: dreptunghiul tras (sau clic-clic / dimensiuni tastate), ca la cameră */
  onAddZone?(r: { x0: number; z0: number; x1: number; z1: number }): void;
  /** unealta „Tavan fals pe zonă”: dreptunghiul, ca la zona de pardoseală; unealta „Balustradă”: clic-clic în lanț, ca la grindă */
  onAddCeilingZone?(r: { x0: number; z0: number; x1: number; z1: number }): void;
  onAddRailing?(a: [number, number], b: [number, number]): void;
  onAddColumn?(x: number, z: number, shape?: 'rect' | 'round', role?: 'shaft'): void;
  onAddBeam?(a: [number, number], b: [number, number]): void;
  onAddGlassWall?(a: [number, number], b: [number, number]): void;
  onAddStairAt?(x: number, z: number): void;
  onRotateSelected?(): void;
  onDuplicateSelected?(): void;
  /** oglindește piesa sau piesele selectate (core/mirror.ts) */
  onMirrorSelected?(): void;
  onDeleteSelected?(): void;
  onToggleColumnShape?(): void;
  onAutoFurnishRoom?(roomId: string): void;
  onSplitRoom?(roomId: string, axis: 'x' | 'z'): void;
  /** unealta „Împarte camera”: clic în cameră, peretele nou la poziția clicului (`at` = x pentru axa x, z pentru axa z) */
  onSplitRoomAt?(roomId: string, axis: 'x' | 'z', at: number): void;
  /** unealta „Covor”: clic în plan, covorul cu centrul la punctul clicului; `roomId` = null când clicul e în afara camerelor */
  onAddRugAt?(roomId: string | null, x: number, z: number): void;
  /** unealta „Pipetă de finisaje”: camera pe care s-a dat clic (editorul ține sursa); null = clic în afara camerelor */
  onStylePick?(roomId: string | null): void;
  /** lungimea tastată pe cota peretelui; false = refuzată (editorul anunță motivul), câmpul rămâne deschis */
  onSetWallLength?(wallId: string, m: number): boolean;
  calib?: Calib | null; onCalibPick?(a: [number, number], b: [number, number]): void;
  showTech?: boolean; onTechAt?(x: number, z: number): void;
  /** straturile planului (core/layers.ts): `false` = ascuns de utilizator. Elementele unui strat ascuns nu se desenează și nu se
   *  pot alege din plan (clic, Tab, dreptunghi); cel ales din Structură sau din paletă se vede cât timp rămâne ales. */
  layers?: Partial<LayerState>;
  /** golurile din placă ale acestui nivel (scările care urcă de dedesubt) */
  voids?: { x0: number; z0: number; x1: number; z1: number }[];
  /** tastatura cu planul în focus: mută / rotește / șterge / renunță (Tab alege direct, prin onSelect) */
  onKey?(a: PlanKeyAction): void;
  // selecția multiplă de piese: multi = piesele alese pe lângă cea principală (sel); onGroup primește grupul nou,
  // cu principala pe primul loc (gol = nimic selectat); onArrange = comanda arrange.* aleasă din meniul contextual
  multi?: string[]; onGroup?(ids: string[]): void; onArrange?(id: string): void;
  /** cursorul tastaturii cât grupul e „prins” (Shift+Space, apoi Tab): editorul îl anunță cititorului de ecran; null = fără cursor */
  onCursor?(c: KeyCursor): void; }
/** peste ce se poate porni dreptunghiul de selecție cu Shift: locul gol și suprafețele (camera, zonele, planul de fundal) */
const ROOM_FILL: Record<string, string> = { baie: '#e4e8e6', bucatarie: '#e6e8e3', hol: '#efeae1', living: '#f0e8da', dormitor: '#efe6dc' };
const SEV: Record<Severity, string> = { PASS: '#8B6A45', WARNING: '#B7791F', ERROR: '#B3261E' };
const G = .05;
/** Culoarea benzii de placare în plan, pe tip (vopseaua și lambriul iau culoarea aleasă). */
const FIN_FILL: Partial<Record<WallFeature['kind'], string>> = { wallpaper: '#e8e2f0', slats: '#d8c8ae', plaster: '#e3ddd3', brick: '#c98f74', stone: '#d6d0c4', tile: '#dfe6ea' };
const snapG = (v: number) => r3(Math.round(v / G) * G);

export default function PlanView(p: Props){
  const { t, lang, units } = usePrefs();
  // cota de nivel se scrie în forma de planșă (+2,80, -0,15, ±0,00) dacă omul a tastat un număr; altfel rămâne textul lui
  const addNote = (at: [number, number], v: string, extra?: TextExtra) => p.onAddText?.(at, extra?.symbol === 'level' ? formatLevelValue(v, lang === 'en' ? '.' : ',') : v, extra);
  // etichete de lungime: metric = cm fără unitate pe plan (aglomerat), imperial = picioare și inci
  const wl = (m: number) => units === 'imperial' ? formatLength(m, 'imperial', lang) : String(Math.round(m * 100));
  const svg = useRef<SVGSVGElement>(null);
  const floor = p.snap.floor;
  const bounds = useMemo(() => { const xs = floor.walls.flatMap(w => [w.a[0], w.b[0]]).concat(floor.rooms.flatMap(r => [r.rect.x0, r.rect.x1])), zs = floor.walls.flatMap(w => [w.a[1], w.b[1]]).concat(floor.rooms.flatMap(r => [r.rect.z0, r.rect.z1]));
    return xs.length ? { x0: Math.min(...xs), x1: Math.max(...xs), z0: Math.min(...zs), z1: Math.max(...zs) } : { x0: 0, x1: 6, z0: 0, z1: 5 }; }, []); // eslint-disable-line
  const [vb, setVb] = useState(() => { const pad = 1.2; return { x: bounds.x0 - pad, y: bounds.z0 - pad, w: bounds.x1 - bounds.x0 + pad * 2, h: bounds.z1 - bounds.z0 + pad * 2 }; });
  const [preview, setPreview] = useState<{ a: [number, number]; b: [number, number] } | null>(null);
  // pending: camera pornită cu un clic (fără tragere) așteaptă al doilea clic sau dimensiunile tastate (WCAG 2.2 2.5.7)
  const [roomDraft, setRoomDraft] = useState<{ a: [number, number]; b: [number, number]; pending?: boolean } | null>(null);
  // cota tastată în timpul desenării (UI/UX P1-6): cifrele merg în chipul de lângă cursor, Enter construiește exact
  const [typed, setTyped] = useState('');
  const [cursor, setCursor] = useState<[number, number] | null>(null);
  const [rulerHover, setRulerHover] = useState<[number, number] | null>(null);
  // desenarea precisă (core/snap.ts): ce a prins magnetul și ultimul cursor brut (direcția pentru Enter)
  const [hit, setHit] = useState<SnapResult | null>(null), raw = useRef<{ pt: [number, number]; free: boolean; ortho?: boolean } | null>(null);
  // traseul de instalații în lucru: punctele puse deja (ultimul = preview.a); ref-ul le ține la zi pentru tastatură
  const [mepPts, setMepPtsRaw] = useState<[number, number][]>([]), mepRef = useRef<[number, number][]>([]), mepClick = useRef<{ at: number; x: number; y: number } | null>(null);
  const setMepPts = (v: [number, number][]) => { mepRef.current = v; setMepPtsRaw(v); };
  const [aspect, setAspect] = useState(1), [calA, setCalA] = useState<[number, number] | null>(null);
  const ul = p.layers?.underlay === false && !p.calib ? undefined : p.snap.underlay;   // stratul „Plan de fundal”; calibrarea îl arată oricum
  useEffect(() => { if (!ul) return; const i = new Image(); i.onload = () => i.naturalWidth && setAspect(i.naturalHeight / i.naturalWidth); i.src = ul.dataUrl; }, [ul?.dataUrl]); // eslint-disable-line
  useEffect(() => { if (p.calib?.stage !== 'pick') setCalA(null); }, [p.calib?.stage]);
  const [ruler, setRuler] = useState<{ a: [number, number]; b: [number, number] | null } | null>(null);
  // cota persistentă în lucru: clic pe a, clic pe b, apoi clic (sau Enter) pentru distanța liniei; O comută aliniată / ortogonală
  // „Placare”: primul clic fixează camera, latura și un capăt; al doilea clic (pe aceeași latură) celălalt capăt
  const [cladDraft, setCladDraft] = useState<{ roomId: string; side: WallFeature['side']; along: number } | null>(null);
  useEffect(() => { setCladDraft(null); }, [p.tool]);
  const [dimDraft, setDimDraft] = useState<{ a: [number, number]; b?: [number, number]; mode: 'aligned' | 'ortho' } | null>(null);
  // textul în lucru: caseta de editare stă peste plan, la locul clicului (Enter confirmă, Esc renunță)
  const [textDraft, setTextDraft] = useState<{ at: [number, number]; left: number; top: number; extra?: TextExtra } | null>(null), [textVal, setTextVal] = useState(''), textInput = useRef<HTMLInputElement>(null), textClosed = useRef(false);
  useEffect(() => { if (textDraft) textInput.current?.focus(); }, [textDraft]);
  // „Săgeată cu notă”: primul clic = vârful săgeții (pe obiectul adnotat); al doilea deschide câmpul de text în locul notei
  const [leaderTip, setLeaderTip] = useState<[number, number] | null>(null);
  // „Cotă unghiulară”: clic pe vârf, pe primul braț, pe al doilea; al patrulea clic (sau Enter) fixează raza arcului
  const [angleDraft, setAngleDraft] = useState<{ v: [number, number]; a?: [number, number]; b?: [number, number] } | null>(null);
  // „Nor de revizie”: după dreptunghi se deschide câmpul etichetei (gol; Enter fără text = nor fără etichetă, Esc renunță)
  const [cloudDraft, setCloudDraft] = useState<{ rect: { x0: number; z0: number; x1: number; z1: number }; left: number; top: number } | null>(null), [cloudVal, setCloudVal] = useState(''), cloudInput = useRef<HTMLInputElement>(null), cloudClosed = useRef(false);
  useEffect(() => { if (cloudDraft) cloudInput.current?.focus(); }, [cloudDraft]);
  useEffect(() => { setLeaderTip(null); setAngleDraft(null); setCloudDraft(null); }, [p.tool]);
  // altă unealtă = alt gest: rigla, segmentul în lucru, camera pornită cu un clic și cota tastată nu trec dintr-o unealtă
  // în alta (altfel ciorna rămâne după cursor și blochează tragerea în Selecție)
  useEffect(() => { setRuler(null); setPreview(null); setTyped(''); setHit(null); setRoomDraft(null); setDimDraft(null); setTextDraft(null); setMenu(null); setDim(null); setMepPts([]); mepClick.current = null; }, [p.tool]);
  // meniul contextual (registru: contextCommands) și cota editată pe loc; ambele în coordonate de ecran
  const [menu, setMenu] = useState<{ x: number; y: number; kind: ContextKind } | null>(null), menuByKey = useRef(0);
  const [dim, setDim] = useState<{ wallId: string; text: string; orig: string; x: number; y: number; bad: boolean } | null>(null), dimClick = useRef<{ id: string; at: number } | null>(null);
  useEffect(() => { setMenu(null); setDim(null); }, [vb]);   // poziția lor pe ecran nu mai corespunde după zoom / deplasare
  const mac = typeof navigator !== 'undefined' && /Mac|iPhone|iPad/.test(navigator.platform || navigator.userAgent);

  const wiringArcs = useMemo(() => {
    if (!p.showTech) return [];
    return computeLightingWiringArcs(p.snap, p.catalog);
  }, [p.showTech, p.snap, p.catalog]);

  const hud = useMemo(() => {
    if (p.tool !== 'select' || !p.sel) return null;
    const { kind, id } = p.sel;
    if (kind === 'placement') {
      const pl = p.snap.placements.find(x => x.id === id);
      if (!pl) return null;
      const fp = footprintOf(p.catalog, pl);
      const w = fp ? fp.x1 - fp.x0 : 0.8, d = fp ? fp.z1 - fp.z0 : 0.8;
      const tag = `${Math.round(w * 100)}×${Math.round(d * 100)} cm`;
      return { x: pl.x, z: (fp ? fp.z0 : pl.z - d / 2) - 0.28, kind, id, tag, canRotate: true, canDuplicate: true, canDelete: true };
    }
    if (kind === 'column') {
      const col = floor.columns?.find(x => x.id === id);
      if (!col) return null;
      const tag = `${Math.round(col.w * 100)}×${Math.round(col.d * 100)} cm`;
      return { x: col.x, z: col.z - col.d / 2 - 0.28, kind, id, tag, canRotate: true, canToggleShape: !col.role || col.role === 'structural', canDelete: true };
    }
    if (kind === 'stair') {
      const st = floor.stairs?.find(x => x.id === id);
      if (!st) return null;
      const tag = `${Math.round(st.width * 100)}×${Math.round(st.length * 100)} cm`;
      return { x: st.x, z: st.z - st.length / 2 - 0.28, kind, id, tag, canRotate: true, canDelete: true };
    }
    if (kind === 'beam') {
      const bm = floor.beams?.find(x => x.id === id);
      if (!bm) return null;
      const L = wallLength(bm.a, bm.b);
      const tag = `${Math.round(L * 100)} cm`;
      return { x: (bm.a[0] + bm.b[0]) / 2, z: (bm.a[1] + bm.b[1]) / 2 - 0.28, kind, id, tag, canDelete: true };
    }
    if (kind === 'glass_wall') {
      const gw = floor.glassWalls?.find(x => x.id === id);
      if (!gw) return null;
      const L = wallLength(gw.a, gw.b);
      const tag = `${Math.round(L * 100)} cm`;
      return { x: (gw.a[0] + gw.b[0]) / 2, z: (gw.a[1] + gw.b[1]) / 2 - 0.28, kind, id, tag, canDelete: true };
    }
    if (kind === 'railing') {
      const rl = floor.railings?.find(x => x.id === id);
      if (!rl) return null;
      const tag = `${Math.round(wallLength(rl.a, rl.b) * 100)} cm · h ${Math.round(railingHeight(rl) * 100)}`;
      return { x: (rl.a[0] + rl.b[0]) / 2, z: (rl.a[1] + rl.b[1]) / 2 - 0.28, kind, id, tag, canDelete: true };
    }
    if (kind === 'room') {
      const rm = floor.rooms.find(x => x.id === id);
      if (!rm) return null;
      const tag = formatArea(roomArea(rm), units, lang);
      return { x: (rm.rect.x0 + rm.rect.x1) / 2, z: rm.rect.z0 + 0.32, kind, id, tag, canAutoFurnish: true, canSplit: true, canDelete: true };
    }
    return null;
  }, [p.tool, p.sel, p.snap, p.catalog, floor, units, lang]);
  const drag = useRef<any>(null), pan = useRef<any>(null);
  // selecția multiplă: grupul (piesa principală + `multi`) și dreptunghiul de selecție tras cu Shift, în coordonatele planului
  const group = useMemo(() => groupIds(p.sel, p.multi ?? []), [p.sel, p.multi]), inMulti = (id: string) => group.length > 1 && group.includes(id);
  // straturile ascunse: elementul se desenează dacă stratul lui e vizibil sau dacă e chiar cel ales (ori în grupul ales)
  const layerOff = (l: LayerId) => p.layers?.[l] === false;
  const drawn = (l: LayerId, kind: string, id: string) => !layerOff(l) || (p.sel?.kind === kind && p.sel.id === id) || (kind === 'placement' && group.includes(id));
  const placements = layerOff('furniture') ? p.snap.placements.filter(pl => drawn('furniture', 'placement', pl.id)) : p.snap.placements;
  // ce se poate alege de la tastatură (Tab, Ctrl+A, săgeți): fără elementele din straturile ascunse, în afară de cel ales acum
  const keyItems = () => planItems(p.snap).filter(i => pickable(i.kind, p.layers) || (p.sel?.kind === i.kind && p.sel.id === i.id));
  const [marquee, setMarquee] = useState<{ a: [number, number]; b: [number, number] } | null>(null);
  // atingerea lungă (ecran tactil, core/multi-select.ts): pe o piesă o adaugă în grup / o scoate, pe gol pornește dreptunghiul de
  // selecție. `lpBase` = grupul de dinaintea atingerii (atingerea simplă a schimbat deja selecția până trece jumătatea de secundă).
  const lp = useRef<{ id: number; x: number; y: number; far: number; timer: ReturnType<typeof setTimeout> } | null>(null), lpFiredAt = useRef(0), lpBase = useRef<string[] | null>(null);
  const cancelLp = () => { if (lp.current){ clearTimeout(lp.current.timer); lp.current = null; } };
  useEffect(() => cancelLp, []);
  // cursorul tastaturii: după primul Shift+Space grupul e „prins”, iar Tab mută doar cursorul (selecția și grupul rămân);
  // orice schimbare a grupului făcută altfel (mouse, Esc, ștergere) îl desființează
  const [kbCur, setKbCur] = useState<KeyCursor>(null), kbExpect = useRef(''), gKey = group.join('|');
  const setCursorKb = (c: KeyCursor) => { setKbCur(c); p.onCursor?.(c); };
  useEffect(() => { if (kbCur && gKey !== kbExpect.current) setCursorKb(null); }, [gKey]); // eslint-disable-line react-hooks/exhaustive-deps
  useEffect(() => { setMarquee(null); cancelLp(); lpBase.current = null; if (kbCur) setCursorKb(null); }, [p.tool]); // eslint-disable-line react-hooks/exhaustive-deps
  // uneltele de navigare: Mâna (cursor grab / grabbing cât se trage) și Zoom pe zonă (dreptunghiul tras, în coordonatele planului)
  const [panning, setPanning] = useState(false), [zoomDraft, setZoomDraft] = useState<{ a: [number, number]; b: [number, number] } | null>(null);
  // „Împarte camera”: Shift ținut la ultima mișcare schimbă axa peretelui nou
  const [splitFlip, setSplitFlip] = useState(false);
  useEffect(() => { setZoomDraft(null); setPanning(false); pan.current = null; }, [p.tool]);
  // camera de sub un punct (conturul propriu, dacă are; altfel dreptunghiul); ultima desenată e deasupra
  const roomAt = (pt: [number, number]) => [...floor.rooms].reverse().find(r => pointInRoom(r, pt[0], pt[1])) ?? null;
  const splitAt = (pt: [number, number], flip: boolean) => { const room = roomAt(pt); if (!room) return null; const axis = splitAxisFor(room.rect, flip); return { room, axis, at: snapG(axis === 'x' ? pt[0] : pt[1]) }; };
  // atingeri: două degete = zoom și deplasare; un deget pe un loc gol (la selecție) = deplasare, atingere scurtă = deselectare
  const touches = useRef(new Map<number, Pt>()), pinch = useRef<{ vb: typeof vb; ctm: { a: number; d: number; e: number; f: number }; mid: Pt; dist: number } | null>(null);
  const startPinch = () => { const [a, b] = [...touches.current.values()] as [Pt, Pt], m = svg.current!.getScreenCTM()!;
    if (drag.current?.moved) p.onEdit(() => {}, 'end'); drag.current = null; pan.current = null; setRoomDraft(null);
    pinch.current = { vb, ctm: { a: m.a, d: m.d, e: m.e, f: m.f }, mid: midpoint(a, b), dist: distance(a, b) }; };

  const toWorld = (e: { clientX: number; clientY: number }): [number, number] => { const s = svg.current!, pt = s.createSVGPoint(); pt.x = e.clientX; pt.y = e.clientY; const w = pt.matrixTransform(s.getScreenCTM()!.inverse()); return [w.x, w.y]; };
  useEffect(() => { const el = svg.current!; const wheel = (e: WheelEvent) => { e.preventDefault(); const [wx, wz] = toWorld(e), k = e.deltaY > 0 ? 1.12 : 1 / 1.12;
    setVb(v => { const w = Math.min(80, Math.max(1.5, v.w * k)), h = v.h * (w / v.w); return { x: wx - (wx - v.x) * (w / v.w), y: wz - (wz - v.y) * (w / v.w), w, h }; }); };
    el.addEventListener('wheel', wheel, { passive: false }); return () => el.removeEventListener('wheel', wheel); }, []);
  // a drag cancelled by Escape or by the plan unmounting (layout change, view switch) still closes the editor's history
  // transaction, otherwise the editor keeps waiting for 'end' and ignores undo/redo
  const onEditRef = useRef(p.onEdit); onEditRef.current = p.onEdit;
  const cancelDrag = () => { const d = drag.current; drag.current = null; if (d?.moved) onEditRef.current(() => {}, 'end'); };
  useEffect(() => { const k = (e: KeyboardEvent) => { if (e.key === 'Escape'){ setCladDraft(null); setCalA(null); setRuler(null); setPreview(null); setRoomDraft(null); setDimDraft(null); setTextDraft(null); setLeaderTip(null); setAngleDraft(null); setCloudDraft(null); setTyped(''); setZoomDraft(null); setMarquee(null); mepRef.current = []; setMepPtsRaw([]); cancelDrag(); } }; addEventListener('keydown', k); return () => { removeEventListener('keydown', k); cancelDrag(); }; }, []);

  function nearestWall(pt: [number, number], maxD = .35){ let best: { id: string; offset: number; d: number } | null = null;
    for (const w of floor.walls){ const L = wallLength(w.a, w.b); if (L < .01) continue; const ux = (w.b[0] - w.a[0]) / L, uz = (w.b[1] - w.a[1]) / L, s = Math.max(0, Math.min(L, (pt[0] - w.a[0]) * ux + (pt[1] - w.a[1]) * uz)), d = Math.hypot(pt[0] - (w.a[0] + ux * s), pt[1] - (w.a[1] + uz * s));
      if (d < maxD && (!best || d < best.d)) best = { id: w.id, offset: s, d }; } return best; }
  // un singur deget, unealta Selecție: pornește ceasul atingerii lungi. Cât ceasul merge, mișcările sub prag nu trag și nu
  // panoramează (move); peste prag ceasul se oprește și gestul rămâne tragere / panoramare, ca înainte.
  function armLongPress(e: React.PointerEvent){
    cancelLp(); lpBase.current = null; if (p.tool !== 'select' || !p.onGroup) return;
    const el = (e.target as Element).closest('[data-k]') as HTMLElement | null, act = longPressAction(el?.dataset.k); if (!act) return;
    const base = group, pid = el?.dataset.id, start = toWorld(e), onGroup = p.onGroup, downAt = performance.now();
    const timer = setTimeout(() => { const g = lp.current; if (!g || g.timer !== timer) return; lp.current = null;
      // temporizatorul garantează durata; funcția pură hotărăște după cât s-a mișcat degetul
      if (pinch.current || drag.current?.moved || !isLongPress(downAt, Math.max(performance.now(), downAt + LONG_PRESS_MS), g.far)) return;
      drag.current = null; pan.current = null; lpFiredAt.current = Date.now(); navigator.vibrate?.(15);
      if (act === 'toggle'){ if (pid) onGroup(toggleInGroup(base, pid)); }   // ca din tastatură: singura piesă aleasă nu se scoate
      else { lpBase.current = base; setMarquee({ a: start, b: start }); } }, LONG_PRESS_MS);
    lp.current = { id: e.pointerId, x: e.clientX, y: e.clientY, far: 0, timer };
  }
  function down(e: React.PointerEvent){
    // un câmp din inspector încă focalizat se aplică înainte ca planul să schimbe selecția (altfel câmpul e demontat cu tot cu text)
    const ae = document.activeElement as HTMLElement | null; if (ae?.tagName === 'INPUT' && !ae.classList.contains('plan-dim-input')) ae.blur();
    if (e.pointerType === 'touch'){ touches.current.set(e.pointerId, { x: e.clientX, y: e.clientY }); svg.current!.setPointerCapture(e.pointerId);
      if (touches.current.size === 2){ cancelLp(); lpBase.current = null; setMarquee(null); startPinch(); return; } if (touches.current.size > 2 || pinch.current) return;
      armLongPress(e); }
    // Mâna: tragerea cu butonul stâng mută vederea, exact ca Alt+tragere / butonul din mijloc; nu atinge proiectul (fără onEdit)
    if (e.button === 1 || (e.button === 0 && (e.altKey || p.tool === 'pan'))){ pan.current = { x: e.clientX, y: e.clientY, vb }; if (p.tool === 'pan') setPanning(true); (e.target as Element).setPointerCapture?.(e.pointerId); return; }
    if (e.button !== 0) return; const pt = toWorld(e);
    // Zoom pe zonă: dreptunghiul tras devine vederea la eliberare (up); doar vederea se schimbă
    if (p.tool === 'zoom_area'){ setZoomDraft({ a: pt, b: pt }); svg.current!.setPointerCapture(e.pointerId); return; }
    if (p.calib?.stage === 'pick'){ const q: [number, number] = [r3(pt[0]), r3(pt[1])]; if (!calA) setCalA(q); else if (Math.hypot(q[0] - calA[0], q[1] - calA[1]) > .05){ p.onCalibPick?.(calA, q); setCalA(null); } return; }
    if (p.tool === 'tech'){ p.onTechAt?.(pt[0], pt[1]); return; }
    if (p.tool === 'column'){ const s = snapPoint(pt, floor); p.onAddColumn?.(s[0], s[1]); return; }
    if (p.tool === 'shaft'){ const s = snapPoint(pt, floor); p.onAddColumn?.(s[0], s[1], 'rect', 'shaft'); return; }
    // traseul de instalații: clic = punct; al doilea clic pe același loc (dublu-clic) termină traseul. Fără cota declarată nu pornește
    if (p.tool === 'mep_run'){ svg.current!.focus({ preventScroll: true }); if (!p.mepDraw?.ready){ p.onMepNotReady?.(); return; }
      const lc = mepClick.current; mepClick.current = { at: e.timeStamp, x: e.clientX, y: e.clientY };
      if (preview && lc && e.timeStamp - lc.at < 500 && Math.hypot(e.clientX - lc.x, e.clientY - lc.y) < 6){ finishMep(); return; }
      const r = drawSnap(pt, preview?.a, e.shiftKey); raw.current = { pt, free: false, ortho: e.shiftKey };
      if (!preview){ setTyped(''); setMepPts([r.p]); setPreview({ a: r.p, b: r.p }); setHit(r); } else addSegment(preview.a, r.p); return; }
    if (SEGMENT_TOOLS.includes(p.tool)){ const r = snapDraw(pt, { floor, anchor: preview?.a, free: e.shiftKey }); svg.current!.focus({ preventScroll: true }); if (!preview){ setTyped(''); raw.current = { pt: r.p, free: e.shiftKey }; setPreview({ a: r.p, b: r.p }); setHit(snapDraw(pt, { floor, anchor: r.p, free: e.shiftKey })); } else addSegment(preview.a, r.p); return; }
    if (p.tool === 'stair'){ const s = snapPoint(pt, floor); p.onAddStairAt?.(s[0], s[1]); return; }
    if (p.tool === 'dim'){ const s = snapPoint(pt, floor); svg.current!.focus({ preventScroll: true });
      if (!dimDraft){ setDimDraft({ a: s, mode: 'aligned' }); setTyped(''); }
      else if (!dimDraft.b){ if (dimOk(dimDraft.a, s, dimDraft.mode)){ setDimDraft({ ...dimDraft, b: s }); setTyped(''); } }
      else finishDim(dimOffsetAt(dimDraft.a, dimDraft.b, dimDraft.mode, pt)); return; }
    // cota unghiulară: vârful, primul braț, al doilea braț (fiecare de cel puțin 1 cm), apoi raza arcului prin punctul clicului
    if (p.tool === 'angle_dim'){ const s = snapPoint(pt, floor); svg.current!.focus({ preventScroll: true });
      if (!angleDraft) setAngleDraft({ v: s });
      else if (!angleDraft.a){ if (angleOk(angleDraft.v, s, s)) setAngleDraft({ ...angleDraft, a: s }); }
      else if (!angleDraft.b){ if (angleOk(angleDraft.v, angleDraft.a, s)) setAngleDraft({ ...angleDraft, b: s }); }
      else finishAngle(angleRadiusAt(angleDraft.v, pt)); return; }
    // norul de revizie cu câmpul etichetei deschis: clicul pe plan l-a închis deja (blur = confirmă), nu pornește alt nor
    if (p.tool === 'rev_cloud' && cloudDraft) return;
    // preventDefault oprește blur-ul inputului: un text deja tastat se plasează aici (clicul nu-l pierde), apoi unealta revine la selecție
    if (TEXT_TOOLS.includes(p.tool) && textDraft && cleanPlanText(textVal)){ e.preventDefault(); textClosed.current = true; addNote(textDraft.at, cleanPlanText(textVal), textDraft.extra); setTextDraft(null); setLeaderTip(null); svg.current?.focus(); return; }
    // săgeata cu notă: primul clic e vârful; fără text tastat, un clic nou mută doar locul notei (vârful rămâne)
    if (p.tool === 'leader' && !leaderTip){ e.preventDefault(); setTextDraft(null); setLeaderTip([r3(pt[0]), r3(pt[1])]); svg.current!.focus({ preventScroll: true }); return; }
    // câmpul pornește gol și la cota de nivel: valoarea o tastează omul (nimic precompletat)
    if (TEXT_TOOLS.includes(p.tool)){ e.preventDefault(); const par = svg.current!.parentElement!.getBoundingClientRect(); setTextVal('');
      // un vârf sub 2 cm de notă nu s-ar vedea: nota rămâne text simplu, fără o săgeată invizibilă
      const extra: TextExtra | undefined = p.tool === 'leader' && leaderTip && Math.hypot(leaderTip[0] - pt[0], leaderTip[1] - pt[1]) >= .02 ? { leader: leaderTip } : p.tool === 'level_mark' ? { symbol: 'level' } : undefined;
      setTextDraft({ at: [r3(pt[0]), r3(pt[1])], left: e.clientX - par.left, top: e.clientY - par.top, ...(extra ? { extra } : {}) }); return; }
    if (p.tool === 'measure'){ const s = snapPoint(pt, floor); setRuler(r => r && !r.b ? { a: r.a, b: s } : { a: s, b: null }); return; }
    if (p.tool === 'demolish'){ const w = nearestWall(pt); if (w) p.onToggleDemolish?.(w.id); return; }
    if (p.tool === 'rug'){ p.onAddRugAt?.(roomAt(pt)?.id ?? null, Math.round(pt[0] * 100) / 100, Math.round(pt[1] * 100) / 100); return; }
    if (p.tool === 'style_copy'){ p.onStylePick?.(roomAt(pt)?.id ?? null); return; }
    if (p.tool === 'split'){ const h = splitAt(pt, e.shiftKey); if (h) p.onSplitRoomAt?.(h.room.id, h.axis, h.at); return; }
    if (p.tool === 'room' || p.tool === 'floor_zone' || p.tool === 'ceiling_zone' || p.tool === 'rev_cloud'){ const s: [number, number] = [snapG(pt[0]), snapG(pt[1])];
      if (roomDraft?.pending){ finishRoom(roomDraft.a, s); return; }   // al doilea clic: colțul opus
      setRoomDraft({ a: s, b: s }); setTyped(''); svg.current!.setPointerCapture(e.pointerId); svg.current!.focus({ preventScroll: true }); return; }
    // placare: clic pe fața peretelui, apoi clic unde se termină; al doilea clic în același loc (dublu-clic) = toată latura
    if (p.tool === 'clad'){ const h = cladTarget(floor, pt); svg.current!.focus({ preventScroll: true }); if (!h){ setCladDraft(null); return; }
      if (cladDraft && cladDraft.roomId === h.roomId && cladDraft.side === h.side){ const a = Math.min(cladDraft.along, h.along), b = Math.max(cladDraft.along, h.along); setCladDraft(null);
        if (b - a < .05) p.onAddClad?.(h.roomId, h.side); else p.onAddClad?.(h.roomId, h.side, a, b); }
      else setCladDraft({ roomId: h.roomId, side: h.side, along: h.along }); return; }
    if (p.tool === 'wall_item'){ const w = nearestWall(pt), wl = w && floor.walls.find(x => x.id === w.id); if (w && wl) p.onAddWallItem?.(w.id, w.offset, nicheSide(wl, pt)); return; }
    if (p.tool === 'niche'){ const w = nearestWall(pt), wl = w && floor.walls.find(x => x.id === w.id); if (w && wl) p.onAddNiche?.(w.id, w.offset, nicheSide(wl, pt)); return; }
    if (p.tool === 'door' || p.tool === 'window' || p.tool === 'passage'){ const w = nearestWall(pt); if (w) p.onAddOpening(w.id, w.offset, p.tool); return; }
    let t = (e.target as Element).closest('[data-k]') as HTMLElement | null;
    // ușile și ferestrele au prioritate față de mânerul laturii (zona lui de 44 px poate atinge un gol pe un plan mic), cu excepția cazului când golul acoperă tot peretele și mânerul e decalat în afară
    if (t?.dataset.k === 'redge' && !t.dataset.offset){ const op = document.elementsFromPoint?.(e.clientX, e.clientY).find(x => (x as HTMLElement).dataset?.k === 'opening') as HTMLElement | undefined; if (op) t = op; }
    // selecția multiplă (doar mouse / creion): Shift+clic pe o piesă o adaugă în grup sau o scoate; Shift+tragere pe gol
    // (sau peste o cameră / zonă) = dreptunghi de selecție. Mânerele, pereții și golurile își păstrează gestul cu Shift.
    if (e.shiftKey && p.onGroup && e.pointerType !== 'touch'){
      if (t?.dataset.k === 'placement'){ p.onGroup(toggleMulti(group, t.dataset.id!)); return; }
      if (!t || MARQUEE_OVER.has(t.dataset.k!)){ setMarquee({ a: pt, b: pt }); svg.current!.setPointerCapture(e.pointerId); return; } }
    if (!t){ p.onSelect(null); if (e.pointerType === 'touch') pan.current = { x: e.clientX, y: e.clientY, vb }; return; }
    const kind = t.dataset.k!, id = t.dataset.id!;
    // dublu clic pe cota peretelui: câmpul de lungime. Numărat aici (pointerdown.detail e 0 în Chrome, iar captura pointerului
    // mută ținta lui dblclick pe svg); deschis după eveniment, altfel focusul dat planului la mousedown închide câmpul pe loc
    if (t.dataset.dim){ const last = dimClick.current; dimClick.current = { id, at: e.timeStamp };
      if (last && last.id === id && e.timeStamp - last.at < 500){ dimClick.current = null; p.onSelect({ kind: 'wall', id }); setTimeout(() => openDim(id), 0); return; } }
    if (kind === 'underlay'){ p.onSelect(null); drag.current = { kind, start: pt, moved: false, orig: null }; svg.current!.setPointerCapture(e.pointerId); return; }
    if (kind === 'handle'){ drag.current = { kind, wallId: t.dataset.w, end: t.dataset.end, start: pt, moved: false }; }
    // vârful săgeții cu notă: se trage separat de text; selecția rămâne pe adnotare, iar up / cancelDrag trimit 'end'
    else if (kind === 'leadertip'){ drag.current = { kind, id, start: pt, moved: false, orig: null }; }
    else if (kind === 'redge'){ drag.current = { kind, id, edge: t.dataset.edge, start: pt, moved: false, orig: null }; }
    else if (kind === 'opening'){ p.onSelect({ kind: 'opening', id, wallId: t.dataset.w! }); drag.current = { kind, id, wallId: t.dataset.w, start: pt, moved: false }; }
    // o piesă din grup: selecția rămâne, tragerea mută tot grupul; un clic fără tragere o alege doar pe ea (în up)
    // piesa blocată (sau un grup care conține una) se alege, dar nu se trage (core/lock.ts)
    else if (kind === 'placement' && (p.snap.placements.find(q => q.id === id)?.locked || (inMulti(id) && group.some(g => p.snap.placements.find(q => q.id === g)?.locked)))){ if (!inMulti(id)) p.onSelect({ kind: 'placement', id }); return; }
    else if (kind === 'placement' && inMulti(id)){ drag.current = { kind, id, start: pt, moved: false, orig: null, group }; }
    // traseul de instalații se alege, dar nu se trage (punctele lui au și cotă; se editează în panou)
    else if (kind === 'mep_run'){ p.onSelect({ kind: 'mep_run', id }); return; }
    else { p.onSelect({ kind: kind as any, id }); drag.current = { kind, id, start: pt, moved: false, orig: null }; }
    svg.current!.setPointerCapture(e.pointerId);
  }
  function move(e: React.PointerEvent){
    if (e.pointerType === 'touch' && touches.current.has(e.pointerId)){ touches.current.set(e.pointerId, { x: e.clientX, y: e.clientY });
      if (pinch.current){ if (touches.current.size >= 2){ const [a, b] = [...touches.current.values()] as [Pt, Pt], g = pinch.current; setVb(pinchViewBox(g.vb, g.ctm, g.mid, g.dist, midpoint(a, b), distance(a, b))); } return; }
      // atingerea lungă în așteptare: tremurul degetului (sub prag) nu pornește tragerea; peste prag e tragere, nu atingere lungă
      const g = lp.current; if (g && g.id === e.pointerId){ g.far = Math.max(g.far, pressTravel(g, { x: e.clientX, y: e.clientY })); if (g.far > LONG_PRESS_MOVE_PX) cancelLp(); else return; } }
    const pt = toWorld(e); setCursor(pt);
    if (p.tool === 'split' && splitFlip !== e.shiftKey) setSplitFlip(e.shiftKey);
    if (zoomDraft){ setZoomDraft({ a: zoomDraft.a, b: pt }); return; }
    if (marquee){ setMarquee({ a: marquee.a, b: pt }); return; }
    if (pan.current){ const s = svg.current!.getScreenCTM()!, k = 1 / s.a; setVb({ ...pan.current.vb, x: pan.current.vb.x - (e.clientX - pan.current.x) * k, y: pan.current.vb.y - (e.clientY - pan.current.y) * k }); return; }
    if (ruler && !ruler.b) { setRulerHover(snapPoint(pt, floor)); }
    if (SEGMENT_TOOLS.includes(p.tool)){ const mep = p.tool === 'mep_run'; raw.current = { pt, free: !mep && e.shiftKey, ortho: mep && e.shiftKey }; const r = drawSnap(pt, preview?.a, e.shiftKey); setHit(r); if (preview){ setPreview({ a: preview.a, b: r.p }); return; } }
    if (roomDraft){ setRoomDraft({ ...roomDraft, b: [snapG(pt[0]), snapG(pt[1])] }); return; }   // păstrează „pending” (camera clic-clic)
    const d = drag.current; if (!d) return;
    const dx = pt[0] - d.start[0], dz = pt[1] - d.start[1]; if (!d.moved && Math.hypot(dx, dz) < .03) return;
    const phase: Phase = d.moved ? 'move' : 'start'; d.moved = true;
    if (!d.orig) d.orig = JSON.parse(JSON.stringify(p.snap));
    const o = d.orig as Snapshot;
    p.onEdit(s => {
      if (d.kind === 'underlay'){ const a = o.underlay, b = s.underlay; if (a && b && !a.locked){ b.x = r3(a.x + dx); b.z = r3(a.z + dz); } }
      // grupul se mută întreg cu deplasarea (prinsă la grilă) a piesei trase: distanțele dintre piese rămân
      else if (d.kind === 'placement' && d.group){ const a0 = o.placements.find(x => x.id === d.id); if (!a0) return; const ddx = snapG(a0.x + dx) - a0.x, ddz = snapG(a0.z + dz) - a0.z;
        for (const gid of d.group as string[]){ const a = o.placements.find(x => x.id === gid), b = s.placements.find(x => x.id === gid); if (!a || !b) continue; b.x = r3(a.x + ddx); b.z = r3(a.z + ddz);
          const room = s.floor.rooms.find(r => b.x >= r.rect.x0 && b.x <= r.rect.x1 && b.z >= r.rect.z0 && b.z <= r.rect.z1); if (room) b.roomId = room.id; b.source = 'manual'; } }
      else if (d.kind === 'placement'){ const a = o.placements.find(x => x.id === d.id)!, b = s.placements.find(x => x.id === d.id)!; b.x = snapG(a.x + dx); b.z = snapG(a.z + dz);
        const room = s.floor.rooms.find(r => b.x >= r.rect.x0 && b.x <= r.rect.x1 && b.z >= r.rect.z0 && b.z <= r.rect.z1); if (room) b.roomId = room.id; b.source = 'manual'; }
      else if (d.kind === 'stair'){ const a = o.floor.stairs?.find(x => x.id === d.id), b = s.floor.stairs?.find(x => x.id === d.id); if (a && b){ b.x = snapG(a.x + dx); b.z = snapG(a.z + dz); } }
      else if (d.kind === 'floor_zone'){ const a = o.floor.zones?.find(x => x.id === d.id), b = s.floor.zones?.find(x => x.id === d.id); if (a && b){ const ddx = snapG(dx), ddz = snapG(dz); b.rect = { x0: r3(a.rect.x0 + ddx), x1: r3(a.rect.x1 + ddx), z0: r3(a.rect.z0 + ddz), z1: r3(a.rect.z1 + ddz) }; } }
      else if (d.kind === 'ceiling_zone'){ const a = o.floor.ceilingZones?.find(x => x.id === d.id), b = s.floor.ceilingZones?.find(x => x.id === d.id); if (a && b){ const ddx = snapG(dx), ddz = snapG(dz); b.rect = { x0: r3(a.rect.x0 + ddx), x1: r3(a.rect.x1 + ddx), z0: r3(a.rect.z0 + ddz), z1: r3(a.rect.z1 + ddz) }; } }
      else if (d.kind === 'railing'){ const a = o.floor.railings?.find(x => x.id === d.id), b = s.floor.railings?.find(x => x.id === d.id); if (a && b){ const ddx = snapG(dx), ddz = snapG(dz); b.a = [r3(a.a[0] + ddx), r3(a.a[1] + ddz)]; b.b = [r3(a.b[0] + ddx), r3(a.b[1] + ddz)]; } }
      else if (d.kind === 'column'){ const a = o.floor.columns?.find(x => x.id === d.id), b = s.floor.columns?.find(x => x.id === d.id); if (a && b){ b.x = snapG(a.x + dx); b.z = snapG(a.z + dz); } }
      else if (d.kind === 'beam'){ const a = o.floor.beams?.find(x => x.id === d.id), b = s.floor.beams?.find(x => x.id === d.id); if (a && b){ const ddx = snapG(dx), ddz = snapG(dz); b.a = [r3(a.a[0] + ddx), r3(a.a[1] + ddz)]; b.b = [r3(a.b[0] + ddx), r3(a.b[1] + ddz)]; } }
      else if (d.kind === 'glass_wall'){ const a = o.floor.glassWalls?.find(x => x.id === d.id), b = s.floor.glassWalls?.find(x => x.id === d.id); if (a && b){ const ddx = snapG(dx), ddz = snapG(dz); b.a = [r3(a.a[0] + ddx), r3(a.a[1] + ddz)]; b.b = [r3(a.b[0] + ddx), r3(a.b[1] + ddz)]; } }
      else if (d.kind === 'room'){ const a = o.floor.rooms.find(x => x.id === d.id)!.rect, b = s.floor.rooms.find(x => x.id === d.id)!; const ddx = snapG(dx), ddz = snapG(dz);
        b.rect = { x0: r3(a.x0 + ddx), x1: r3(a.x1 + ddx), z0: r3(a.z0 + ddz), z1: r3(a.z1 + ddz) }; if (a && b.boundary) b.boundary = b.boundary.map(([x, z]) => [r3(x + ddx), r3(z + ddz)]); }
      // latura camerei alese: camera se redimensionează din dreptunghiul de la începutul tragerii, cu vecinii care împart latura
      // (core/room-resize.ts, neighbours); camera nu scade sub 60 cm, iar o poziție refuzată lasă ultima poziție validă
      else if (d.kind === 'redge'){ const a = o.floor.rooms.find(x => x.id === d.id)?.rect; if (!a) return; const k = d.edge as EdgeKey, ax = k[0] as 'x' | 'z';
        let v = snapG(a[k] + (ax === 'x' ? dx : dz)); v = k.endsWith('0') ? Math.min(v, r3(a[`${ax}1`] - MIN_ROOM_M)) : Math.max(v, r3(a[`${ax}0`] + MIN_ROOM_M));
        try { s.floor = resizeRoom(o.floor, d.id, { ...a, [k]: v }, () => `wall-${crypto.randomUUID().slice(0, 8)}`, { neighbours: true }); } catch (err){ if (!(err instanceof RoomResizeError)) throw err; } }
      else if (d.kind === 'wall'){ const a = o.floor.walls.find(x => x.id === d.id)!, b = s.floor.walls.find(x => x.id === d.id)!; const ddx = snapG(dx), ddz = snapG(dz);
        b.a = [r3(a.a[0] + ddx), r3(a.a[1] + ddz)]; b.b = [r3(a.b[0] + ddx), r3(a.b[1] + ddz)]; }
      else if (d.kind === 'handle'){ const b = s.floor.walls.find(x => x.id === d.wallId)!; const other = d.end === 'a' ? b.b : b.a; const np = snapDraw(pt, { floor: { walls: s.floor.walls.filter(w => w.id !== d.wallId) }, anchor: other, free: e.shiftKey }).p;
        if (d.end === 'a') b.a = np; else b.b = np; }
      // adnotarea se mută întreagă: cota cu ambele capete, textul cu vârful săgeții lui (core/annotations.ts)
      else if (d.kind === 'annotation'){ const a = o.floor.annotations?.find(x => x.id === d.id), b = s.floor.annotations?.find(x => x.id === d.id); if (a && b) moveAnnotation(b, a, snapG(dx), snapG(dz)); }
      else if (d.kind === 'leadertip'){ const b = s.floor.annotations?.find(x => x.id === d.id); if (b?.kind === 'text' && b.leader) b.leader = [r3(pt[0]), r3(pt[1])]; }
      else if (d.kind === 'wall_item'){ const it = s.floor.wallItems?.find(x => x.id === d.id), g = it && wallItemGeometry(s.floor, it); if (it && g){ const sAlong = (pt[0] - g.wall.a[0]) * g.ux + (pt[1] - g.wall.a[1]) * g.uz; it.offset = r3(Math.max(it.width / 2, Math.min(g.L - it.width / 2, snapG(sAlong)))); } }
      else if (d.kind === 'niche'){ const n = s.floor.niches?.find(x => x.id === d.id), g = n && nicheGeometry(s.floor, n); if (n && g){ const sAlong = (pt[0] - g.wall.a[0]) * g.ux + (pt[1] - g.wall.a[1]) * g.uz; n.offset = r3(Math.max(0, Math.min(g.L - n.width, snapG(sAlong - n.width / 2)))); } }
      else if (d.kind === 'opening'){ const w = s.floor.walls.find(x => x.id === d.wallId)!, op = w.openings.find(x => x.id === d.id)!, L = wallLength(w.a, w.b), ux = (w.b[0] - w.a[0]) / L, uz = (w.b[1] - w.a[1]) / L;
        const sAlong = (pt[0] - w.a[0]) * ux + (pt[1] - w.a[1]) * uz; op.offset = r3(Math.max(0, Math.min(L - op.width, snapG(sAlong - op.width / 2)))); }
    }, phase);
  }
  function up(e: React.PointerEvent){
    if (e.pointerType === 'touch'){ cancelLp(); touches.current.delete(e.pointerId); if (pinch.current){ if (touches.current.size < 2) pinch.current = null; return; } }
    if (pan.current){ pan.current = null; setPanning(false); return; }
    // Zoom pe zonă: sub 10 cm pe ambele laturi e un clic, nu o zonă (vederea rămâne); un gest întrerupt nu schimbă vederea
    if (zoomDraft){ const { a, b } = zoomDraft, el = svg.current; setZoomDraft(null);
      if (e.type !== 'pointercancel' && (Math.abs(b[0] - a[0]) >= .1 || Math.abs(b[1] - a[1]) >= .1)) setVb(frameViewBox({ x0: Math.min(a[0], b[0]), x1: Math.max(a[0], b[0]), z0: Math.min(a[1], b[1]), z1: Math.max(a[1], b[1]) }, el && el.clientHeight > 0 ? el.clientWidth / el.clientHeight : 1.5));
      return; }
    if (roomDraft && !roomDraft.pending){ const { a, b } = roomDraft;
      if (Math.abs(b[0] - a[0]) < .6 && Math.abs(b[1] - a[1]) < .6){ setRoomDraft({ a, b, pending: true }); return; }   // clic fără tragere: așteaptă al doilea clic
      finishRoom(a, b); return; }
    // dreptunghiul de selecție: piesele cu centrul în el intră în grup (cele deja alese rămân); sub 5 cm e un clic, nu o zonă
    // pornit cu atingerea lungă: grupul de bază e cel de dinaintea atingerii (atingerea pe gol a deselectat deja), deci se reface și el
    if (marquee){ const { a, b } = marquee, base = lpBase.current; lpBase.current = null; setMarquee(null);
      if (e.type !== 'pointercancel' && (Math.abs(b[0] - a[0]) >= .05 || Math.abs(b[1] - a[1]) >= .05)){ const ids = piecesInRect(placements, a, b); if (ids.length || base?.length) p.onGroup?.(addToGroup(base ?? group, ids)); }
      else if (base?.length) p.onGroup?.([...base]);   // atingere lungă pe gol fără tragere (sau anulată): grupul de dinainte revine
      return; }
    const d = drag.current; drag.current = null; if (d?.moved) p.onEdit(() => {}, 'end');
    else if (d?.group && e.type !== 'pointercancel') p.onSelect({ kind: 'placement', id: d.id });
  }
  // un segment terminat (perete, grindă, perete de sticlă) pornește următorul din capătul lui; sub 20 cm nu se adaugă
  // magnetul desenării: la traseul de instalații Shift blochează segmentul pe orizontală / verticală (core/mep-draw.ts);
  // la celelalte unelte de segment Shift rămâne „fără magnet”
  const drawSnap = (pt: [number, number], anchor: [number, number] | null | undefined, shift: boolean): SnapResult => p.tool !== 'mep_run' ? snapDraw(pt, { floor, anchor, free: shift })
    : shift && anchor ? snapDraw(orthoPoint(anchor, pt), { floor: { walls: [] }, anchor, stepDeg: 90 }) : snapDraw(pt, { floor, anchor });
  // traseul se salvează o singură dată, la final (un singur pas de anulare); sub două puncte desenul doar se închide
  // salvarea refuzată (cota sau diametrul invalide) păstrează punctele desenate: omul corectează bara și termină din nou
  const finishMep = (pts: [number, number][] = mepRef.current) => { mepClick.current = null; if (pts.length >= 2 && p.onAddMepRun?.(pts) === false) return; setMepPts([]); setPreview(null); setTyped(''); setHit(null); };
  // Esc: ultimul punct pus dispare (segmentul lui e anulat); fără niciun punct rămas, desenul se închide
  const undoMepPoint = () => { const n = dropLastRunPoint(mepRef.current), l = n[n.length - 1]; setMepPts(n); setTyped('');
    if (!l){ setPreview(null); setHit(null); return; } const r = raw.current ? drawSnap(raw.current.pt, l, !!raw.current.ortho) : null; setPreview({ a: l, b: r ? r.p : l }); setHit(r); };
  const addSegment = (a: [number, number], b: [number, number]) => {
    if (p.tool === 'mep_run'){ const next = appendRunPoint(mepRef.current, b); if (next === mepRef.current) return; const l = next[next.length - 1]!;
      // la numărul maxim de puncte al unui traseu, traseul se termină singur
      if (next.length >= MEP_LIMITS.maxPoints){ finishMep(next); return; }
      setMepPts(next); setPreview({ a: l, b: l }); setTyped(''); setHit(raw.current ? drawSnap(raw.current.pt, l, !!raw.current.ortho) : null); return; }
    if (wallLength(a, b) >= .2){ if (p.tool === 'wall') p.onAddWall(a, b); else if (p.tool === 'beam') p.onAddBeam?.(a, b); else if (p.tool === 'railing') p.onAddRailing?.(a, b); else p.onAddGlassWall?.(a, b); }
    setPreview({ a: b, b }); setTyped(''); setHit(raw.current ? snapDraw(raw.current.pt, { floor, anchor: b, free: raw.current.free }) : null); };

  // Tab / Shift+Tab aleg camerele, piesele și scările pe rând (după ultimul, Tab iese din plan); restul tastelor merg la editor
  function finishRoom(a: [number, number], b: [number, number]){ setRoomDraft(null); setTyped('');
    const r = { x0: Math.min(a[0], b[0]), x1: Math.max(a[0], b[0]), z0: Math.min(a[1], b[1]), z1: Math.max(a[1], b[1]) };
    if (p.tool === 'floor_zone'){ if (r.x1 - r.x0 >= LIM.zone.minM && r.z1 - r.z0 >= LIM.zone.minM) p.onAddZone?.(r); return; }
    if (p.tool === 'rev_cloud'){ openCloud(r); return; }
    if (p.tool === 'ceiling_zone'){ if (r.x1 - r.x0 >= LIM.ceilingZone.minM && r.z1 - r.z0 >= LIM.ceilingZone.minM) p.onAddCeilingZone?.(r); return; }
    if (r.x1 - r.x0 >= .6 && r.z1 - r.z0 >= .6) p.onAddRoom(r); }
  // cota tastată: valabilă doar cât se desenează un segment (perete, grindă, sticlă) sau o cameră; tastele prinse aici
  // (cifre, unitățile după o cifră, Backspace, Escape, Enter) nu ajung la scurtăturile editorului
  const finishAngle = (r: number) => { if (angleDraft?.a && angleDraft.b) p.onAddAngle?.(angleDraft.v, angleDraft.a, angleDraft.b, r); setAngleDraft(null); };
  // norul: dreptunghiul e gata, se cere eticheta (câmp gol, nimic precompletat) lângă colțul din stânga-sus
  const openCloud = (r: { x0: number; z0: number; x1: number; z1: number }) => { if (!cloudOk(r)) return; const el = svg.current; if (!el) return;
    const par = el.parentElement!.getBoundingClientRect(), s = toScreen([r.x0, r.z0]); cloudClosed.current = false; setCloudVal('');
    setCloudDraft({ rect: { x0: r3(r.x0), z0: r3(r.z0), x1: r3(r.x1), z1: r3(r.z1) }, left: Math.max(0, s.x - par.left), top: Math.max(20, s.y - par.top - 24) }); };
  const closeCloud = (add: boolean) => { if (cloudClosed.current) return; cloudClosed.current = true; const d = cloudDraft; setCloudDraft(null);
    if (add && d){ const label = cleanCloudLabel(cloudVal); p.onAddCloud?.(d.rect, label || undefined); } };
  const finishDim = (off: number) => { if (dimDraft?.b) p.onAddDim?.(dimDraft.a, dimDraft.b, Math.max(-PLAN_LIMITS.annotation.offsetMaxM, Math.min(PLAN_LIMITS.annotation.offsetMaxM, off)), dimDraft.mode); setDimDraft(null); setTyped(''); };
  const rectTool = p.tool === 'room' || p.tool === 'floor_zone' || p.tool === 'ceiling_zone' || p.tool === 'rev_cloud';
  const segment = SEGMENT_TOOLS.includes(p.tool), dimming = p.tool === 'dim' && !!dimDraft, typing = (segment && !!preview) || (rectTool && !!roomDraft) || dimming;
  const typedValue = !typed ? null : segment || dimming ? parseTypedLength(typed, units) : parseTypedSize(typed, units);
  function typedKey(e: React.KeyboardEvent<SVGSVGElement>): boolean {
    if (!typing) return false;
    // cât timp se desenează, Delete și Backspace (și cu Ctrl/⌘, ⌘⌫ pe Mac) nu ajung la „șterge selecția” din editor
    if ((e.key === 'Backspace' || e.key === 'Delete') && (e.ctrlKey || e.metaKey || e.altKey)) return true;
    if (e.ctrlKey || e.metaKey || e.altKey) return false;
    // traseul de instalații: Enter fără lungime tastată termină traseul; Esc scoate ultimul punct
    if (p.tool === 'mep_run' && preview && !typed){ if (e.key === 'Enter'){ finishMep(); return true; } if (e.key === 'Escape'){ undoMepPoint(); return true; } }
    // cota: O comută aliniată / ortogonală; Enter cu o lungime pune al doilea punct, apoi Enter pune linia (decalajul tastat sau cel de la cursor)
    if (dimming && dimDraft){ if (e.key === 'o' || e.key === 'O'){ setDimDraft({ ...dimDraft, mode: dimDraft.mode === 'ortho' ? 'aligned' : 'ortho' }); return true; }
      if (e.key === 'Enter'){ const tv = typeof typedValue === 'number' ? typedValue : null;
        if (!dimDraft.b){ if (tv){ const a = dimDraft.a, toward: [number, number] = cursor && wallLength(a, cursor) > 1e-3 ? cursor : [a[0] + 1, a[1]], b = pointAtLength(a, toward, tv, 15, false); if (dimOk(a, b, dimDraft.mode)) setDimDraft({ ...dimDraft, b }); setTyped(''); } return true; }
        const atCur = cursor ? dimOffsetAt(dimDraft.a, dimDraft.b, dimDraft.mode, cursor) : PLAN_DEFAULTS.dimOffset;
        finishDim(tv != null ? tv * (atCur < 0 ? -1 : 1) : atCur || PLAN_DEFAULTS.dimOffset); return true; } }
    if (e.key === 'Enter' && typed){ if (typedValue == null) return true;
      // segmentul păstrează direcția magnetului (pas de 15°; Shift = liberă); fără mișcare de mouse, spre dreapta
      if (segment && preview && typeof typedValue === 'number'){ const a = preview.a, free = !!raw.current?.free;
        const toward: [number, number] = wallLength(a, preview.b) > 1e-3 ? preview.b : raw.current && wallLength(a, raw.current.pt) > 1e-3 ? raw.current.pt : [a[0] + 1, a[1]];
        addSegment(a, pointAtLength(a, toward, typedValue, raw.current?.ortho ? 90 : 15, free)); }
      else if (rectTool && roomDraft && typeof typedValue === 'object'){ const r = typedRect(roomDraft.a, roomDraft.b, typedValue); setRoomDraft(null); setTyped(''); if (p.tool === 'floor_zone') p.onAddZone?.(r); else if (p.tool === 'rev_cloud') openCloud(r); else if (p.tool === 'ceiling_zone') p.onAddCeilingZone?.(r); else p.onAddRoom(r); }
      return true; }
    if (e.key === 'Backspace' && typed){ setTyped(x => x.slice(0, -1)); return true; }
    // cât timp se desenează, Delete și Backspace nu ajung la „șterge selecția” din editor
    if (e.key === 'Backspace' || e.key === 'Delete') return true;
    if (e.key === 'Escape' && typed){ setTyped(''); return true; }
    if (acceptsTypedKey(e.key, typed)){ setTyped(x => (x + e.key).slice(0, 24)); return true; }
    return false;
  }
  function keyDown(e: React.KeyboardEvent<SVGSVGElement>){
    if (typedKey(e)){ e.preventDefault(); e.stopPropagation(); return; }
    // cota unghiulară în lucru: Enter (după al doilea braț) = raza implicită; Esc anulează desenul (ascultătorul de pe window),
    // fără să deselecteze; Delete / Backspace nu ajung la „șterge selecția”
    if (p.tool === 'angle_dim' && angleDraft && !e.ctrlKey && !e.metaKey && !e.altKey){
      if (e.key === 'Enter'){ e.preventDefault(); e.stopPropagation(); if (angleDraft.b) finishAngle(PLAN_DEFAULTS.angleRadius); return; }
      if (e.key === 'Escape') return;
      if (e.key === 'Backspace' || e.key === 'Delete'){ e.preventDefault(); e.stopPropagation(); return; } }
    // Esc în timpul desenării anulează desenul (ascultătorii de pe window), chiar dacă e ceva selectat: nu deselectează întâi
    if (e.key === 'Escape' && typing) return;
    // uneltele noi (pipetă, covor, împărțire, navigare): Esc ajunge la editor și revine la Selecție, nu doar deselectează
    if (e.key === 'Escape' && (p.tool === 'style_copy' || p.tool === 'rug' || p.tool === 'split' || p.tool === 'pan' || p.tool === 'zoom_area')) return;
    // Shift+F10 / tasta Menu: meniul contextual al selecției, la centrul ei pe ecran
    if ((e.key === 'ContextMenu' || (e.key === 'F10' && e.shiftKey)) && !typing && p.tool === 'select' && p.sel){ e.preventDefault(); e.stopPropagation();
      const box = selectionBox(p.snap, p.catalog, p.sel), at = box ? toScreen([(box.x0 + box.x1) / 2, (box.z0 + box.z1) / 2]) : null;
      const r = svg.current!.getBoundingClientRect(), q = at ?? { x: r.left + r.width / 2, y: r.top + r.height / 2 };
      menuByKey.current = Date.now(); setMenu({ ...q, kind: p.sel.kind }); return; }
    // Ctrl/⌘+A: toate piesele nivelului intră în grup (piesa principală rămâne cea aleasă, dacă e una)
    if ((e.key === 'a' || e.key === 'A') && (e.ctrlKey || e.metaKey) && !e.altKey && !e.shiftKey && !typing && p.tool === 'select' && p.onGroup){ e.preventDefault(); e.stopPropagation();
      const ids = keyItems().filter(x => x.kind === 'placement').map(x => x.id); if (ids.length) p.onGroup(addToGroup(group, ids)); return; }
    // Shift+Space: obiectul curent (cursorul tastaturii sau, fără el, selecția) intră în grup sau iese, dacă e piesă; de aici
    // grupul e „prins” și Tab mută doar cursorul, ca selecția să nu se piardă (core/multi-select.ts)
    if (isGroupToggleKey(e) && !typing && p.tool === 'select' && p.onGroup){ e.preventDefault(); e.stopPropagation();
      const cur: KeyCursor = kbCur ?? (p.sel ? { kind: p.sel.kind, id: p.sel.id } : null), g = groupAfterKeyToggle(group, cur); if (!g || !cur) return;
      kbExpect.current = g.join('|'); setCursorKb(cur); p.onGroup(g); return; }
    if (e.key === 'Tab' && !e.ctrlKey && !e.metaKey && !e.altKey && !typing && p.tool === 'select' && tabMovesCursor(!!kbCur, group.length)){
      const n = cycleSelection(keyItems(), kbCur, e.shiftKey ? -1 : 1); if (!n) return;   // după ultimul, Tab iese din plan
      e.preventDefault(); e.stopPropagation(); setCursorKb({ kind: n.kind, id: n.id }); return; }
    // placarea începută: Enter = toată latura
    if (p.tool === 'clad' && cladDraft && e.key === 'Enter'){ e.preventDefault(); e.stopPropagation(); const d = cladDraft; setCladDraft(null); p.onAddClad?.(d.roomId, d.side); return; }
    // F: încadrează selecția (ca în Figma / Archicad); fără selecție, tot planul
    if ((e.key === 'f' || e.key === 'F') && !e.ctrlKey && !e.metaKey && !e.altKey){ e.preventDefault(); e.stopPropagation(); if (!frameSel(p.sel)) fitAll(); return; }
    const a = planKeyAction(e, keyItems(), p.sel); if (!a) return;
    if (a.type === 'leave') return;
    e.preventDefault(); e.stopPropagation();
    if (a.type === 'select') p.onSelect({ kind: a.item.kind, id: a.item.id }); else p.onKey?.(a);
  }
  const fs = Math.max(.12, vb.w / 70), isSel = (k: string, id: string) => p.sel && p.sel.kind === k && p.sel.id === id;
  // zona de apucare a mânerelor de latură: cel puțin 44 px în diametru pe ecran, oricât de mic e planul (tabletă, telefon)
  const pxPerM = Math.min((svg.current?.clientWidth ?? 0) / vb.w, (svg.current?.clientHeight ?? 0) / vb.h), hitR = Math.max(fs * 2.2, pxPerM > 0 ? 22 / pxPerM : 0);
  // WCAG 2.2 2.5.7: zoom-ul (rotiță/ciupire) și deplasarea (Alt+tragere) au și butoane cu un singur clic
  const zoomBy = (k: number) => setVb(v => { const w = Math.min(80, Math.max(1.5, v.w * k)), h = v.h * (w / v.w), cx = v.x + v.w / 2, cy = v.y + v.h / 2; return { x: cx - w / 2, y: cy - h / 2, w, h }; });
  const panBy = (fx: number, fy: number) => setVb(v => ({ ...v, x: v.x + v.w * fx, y: v.y + v.h * fy }));
  // încadrarea obiectului selectat, la proporția reală a planului de pe ecran; false dacă obiectul nu există pe acest nivel
  const frameSel = (s: Sel) => { const box = selectionBox(p.snap, p.catalog, s), el = svg.current; if (!box) return false;
    setVb(frameViewBox(box, el && el.clientHeight > 0 ? el.clientWidth / el.clientHeight : 1.5)); return true; };
  // doar cererile proaspete: la remontare (alt nivel, ieșirea din previzualizare) planul nu sare la o țintă veche
  useEffect(() => { if (p.frame && Date.now() - p.frame.at < 4000) frameSel(p.frame.sel); }, [p.frame?.n]); // eslint-disable-line
  const toScreen = (q: [number, number]) => { const s = svg.current!, pt = s.createSVGPoint(); pt.x = q[0]; pt.y = q[1]; const r = pt.matrixTransform(s.getScreenCTM()!); return { x: r.x, y: r.y }; };
  // cota peretelui editată pe loc: câmpul stă peste eticheta cotei, cu lungimea curentă selectată
  const openDim = (wallId: string) => { const w = floor.walls.find(x => x.id === wallId); if (!w || !p.onSetWallLength || wallLength(w.a, w.b) < .01) return;
    const text = wl(wallLength(w.a, w.b)); setDim({ wallId, text, orig: text, ...toScreen(dimLabelAt(w, fs)), bad: false }); };
  // textul neatins (și forma imperială 12′ 6″, pe care parserul n-o citește) închide câmpul fără modificare
  const closeDim = () => { setDim(null); svg.current?.focus({ preventScroll: true }); };
  const applyDim = () => { if (!dim) return; if (dim.text.trim() === dim.orig){ closeDim(); return; } const m = parseTypedLength(dim.text, units);
    if (m == null || !p.onSetWallLength?.(dim.wallId, m)){ setDim({ ...dim, bad: true }); return; } setDim(null); svg.current?.focus({ preventScroll: true }); };
  // comenzile meniului: aceleași acțiuni ca bara piesei și tastele (lib/commands.ts dă eticheta și scurtătura)
  const runCommand = (id: string) => { const s = p.sel; setMenu(null); svg.current?.focus({ preventScroll: true }); if (!s) return;
    if (id === 'rotate') p.onRotateSelected?.(); else if (id === 'mirror') p.onMirrorSelected?.(); else if (id === 'duplicate') p.onDuplicateSelected?.(); else if (id === 'delete') p.onDeleteSelected?.();
    else if (id === 'frame'){ if (!frameSel(s)) fitAll(); } else if (id === 'view3d') p.onFrameOther?.(s.id); else if (id === 'column.shape') p.onToggleColumnShape?.();
    else if (id.startsWith('arrange.')) p.onArrange?.(id);
    else if (id === 'room.furnish') p.onAutoFurnishRoom?.(s.id); else if (id === 'room.split') p.onSplitRoom?.(s.id, 'x'); else if (id === 'wall.length') openDim(s.id); };
  // ce oferă meniul acum: comenzile fără acțiune legată (ex. 3D în vederea doar-plan) nu apar
  const available = (id: string) => ({ view3d: !!p.onFrameOther, 'wall.length': !!p.onSetWallLength, 'room.furnish': !!p.onAutoFurnishRoom, 'room.split': !!p.onSplitRoom,
    'column.shape': !!p.onToggleColumnShape, rotate: !!p.onRotateSelected, mirror: !!p.onMirrorSelected, duplicate: !!p.onDuplicateSelected, delete: !!p.onDeleteSelected } as Record<string, boolean>)[id] ?? true;
  // clic dreapta: în timpul desenării anulează segmentul; în Selecție alege obiectul de sub cursor și deschide meniul
  function contextMenu(e: React.MouseEvent){ e.preventDefault();
    // pe ecran tactil apăsarea lungă pe o piesă sau pe gol e selecție multiplă (down / armLongPress), nu meniu
    if (lp.current || Date.now() - lpFiredAt.current < 1000) return;
    if (p.tool === 'mep_run' && preview){ finishMep(); return; }   // clic dreapta termină traseul (ce s-a desenat se păstrează)
    if (preview || roomDraft){ setPreview(null); setRoomDraft(null); setTyped(''); return; }
    if (p.tool !== 'select' || Date.now() - menuByKey.current < 600) return;
    const t0 = (e.target as Element).closest('[data-k]') as HTMLElement | null, k = t0?.dataset.k;
    if (!t0 || !k || !['wall', 'room', 'placement', 'stair', 'column', 'beam', 'glass_wall', 'opening', 'niche', 'wall_item', 'annotation', 'floor_zone', 'ceiling_zone', 'railing', 'mep_run'].includes(k)){ setMenu(null); return; }
    const s: Sel = k === 'opening' ? { kind: 'opening', id: t0.dataset.id!, wallId: t0.dataset.w! } : { kind: k as Exclude<ContextKind, 'opening'>, id: t0.dataset.id! };
    // clic dreapta pe o piesă din grup: grupul rămâne (meniul oferă și alinierea / distribuirea)
    if (!(s.kind === 'placement' && inMulti(s.id))) p.onSelect(s); setMenu({ x: e.clientX, y: e.clientY, kind: s.kind }); }
  const fitAll = () => { const pad = 1.2; setVb({ x: bounds.x0 - pad, y: bounds.z0 - pad, w: bounds.x1 - bounds.x0 + pad * 2, h: bounds.z1 - bounds.z0 + pad * 2 }); };
  return (<><svg ref={svg} className="plan" viewBox={`${vb.x} ${vb.y} ${vb.w} ${vb.h}`} onPointerDown={down} onPointerMove={move} onPointerUp={up} onPointerCancel={up} onBlur={e => { if (kbCur && !svg.current?.contains(e.relatedTarget as Node | null)) setCursorKb(null); }} onContextMenu={contextMenu}
    style={{ cursor: p.tool === 'select' ? 'default' : p.tool === 'pan' ? (panning ? 'grabbing' : 'grab') : p.tool === 'zoom_area' ? 'zoom-in' : 'crosshair', color: '#1A141C' }} role="application" aria-label={t('plan.aria')} tabIndex={0} onKeyDown={keyDown}>
    <defs><pattern id="g" width="1" height="1" patternUnits="userSpaceOnUse"><path d="M1 0H0V1" fill="none" stroke="#e4e5e0" strokeWidth=".01" /></pattern>
      {/* hașura ghenei: diagonale subțiri la 6 cm */}
      <pattern id="shaft-hatch" width=".06" height=".06" patternUnits="userSpaceOnUse" patternTransform="rotate(45)"><path d="M0 0V.06" stroke="#5E636B" strokeWidth=".008" /></pattern>
      {/* finisaje de perete: tapet = hașură fină, riflaj = linii perpendiculare pe perete la 3 cm, tencuială = puncte */}
      <pattern id="fin-hatch" width=".02" height=".02" patternUnits="userSpaceOnUse" patternTransform="rotate(45)"><path d="M0 0V.02" stroke="#6b5a45" strokeWidth=".003" /></pattern>
      <pattern id="fin-slats-h" width=".03" height=".04" patternUnits="userSpaceOnUse"><path d="M.015 0V.04" stroke="#5a4430" strokeWidth=".005" /></pattern>
      <pattern id="fin-slats-v" width=".04" height=".03" patternUnits="userSpaceOnUse"><path d="M0 .015H.04" stroke="#5a4430" strokeWidth=".005" /></pattern>
      <pattern id="fin-dots" width=".02" height=".02" patternUnits="userSpaceOnUse"><circle cx=".01" cy=".01" r=".003" fill="#6b5a45" /></pattern>
      {/* peretele nou: hașură diagonală la 5 cm; zona de pardoseală: hașură ușoară la 12 cm */}
      <pattern id="wall-new-hatch" width=".05" height=".05" patternUnits="userSpaceOnUse" patternTransform="rotate(45)"><rect width=".05" height=".05" fill="#FFFDF8" /><path d="M0 0V.05" stroke="#23262B" strokeWidth=".012" /></pattern>
      <pattern id="zone-hatch" width=".12" height=".12" patternUnits="userSpaceOnUse" patternTransform="rotate(-45)"><rect width=".12" height=".12" fill="#e7dccb" fillOpacity=".55" /><path d="M0 0V.12" stroke="#8B6A45" strokeWidth=".008" strokeOpacity=".7" /></pattern></defs>
    <rect x={vb.x} y={vb.y} width={vb.w} height={vb.h} fill="url(#g)" />
    {ul && <image data-k="underlay" href={ul.dataUrl} x={ul.x} y={ul.z} width={ul.widthM} height={ul.widthM * aspect} preserveAspectRatio="none" opacity={ul.opacity} pointerEvents={ul.locked || p.calib?.stage === 'pick' ? 'none' : 'auto'} style={{ cursor: ul.locked ? 'default' : 'move' }} />}
    {floor.rooms.map(r => <g key={r.id}>
      <path data-k="room" data-id={r.id} d={roomBoundaryPath(r)} fill={ROOM_FILL[r.type] || '#f2eee6'} fillOpacity={ul ? .45 : 1} stroke={isSel('room', r.id) ? '#8B6A45' : 'none'} strokeWidth={.04} strokeLinejoin="round" />
    </g>)}
    {/* zonele de pardoseală: hașură ușoară, contur subțire și suprafața (eticheta zonei, dacă are) */}
    {(floor.zones || []).filter(z => drawn('finishes', 'floor_zone', z.id)).map(z => { const r = z.rect, on = isSel('floor_zone', z.id), w = r.x1 - r.x0, d = r.z1 - r.z0;
      return (<g key={z.id} data-k="floor_zone" data-id={z.id} style={{ cursor: 'move' }}><title>{t('tool.floor_zone')}{z.label ? ` · ${z.label}` : ''} ({wl(w)}×{wl(d)})</title>
        <rect x={r.x0} y={r.z0} width={w} height={d} fill="url(#zone-hatch)" stroke={on ? '#8B6A45' : '#6b5a45'} strokeWidth={on ? .03 : .012} strokeDasharray={on ? '.08 .05' : undefined} /></g>); })}
    {(p.voids || []).map((v, k) => <g key={`void-${k}`} pointerEvents="none">
      <rect x={v.x0} y={v.z0} width={v.x1 - v.x0} height={v.z1 - v.z0} fill="#FBFBF9" stroke="#5E636B" strokeWidth={.02} strokeDasharray=".08 .05" />
      <path d={`M${v.x0} ${v.z0}L${v.x1} ${v.z1}M${v.x1} ${v.z0}L${v.x0} ${v.z1}`} stroke="#8A8F96" strokeWidth={.012} />
      <text x={(v.x0 + v.x1) / 2} y={v.z0 - fs * .3} textAnchor="middle" fontSize={fs * .7} fontFamily="Instrument Sans, Segoe UI, sans-serif" fill="#5E636B">{t('stair.void')}</text></g>)}
    {(floor.stairs || []).map(st => { const g = stairGeometry(st, floor.ceilingHeight + SLAB), r = g.rect, s = isSel('stair', st.id), [dx, dz] = g.dir, px = -dz, pz = dx, hw = st.width / 2;
      // trepte perpendiculare pe direcția de urcare, săgeata de la prima treaptă spre ultima, „SUS” la pornire
      const treads = Array.from({ length: g.steps - 1 }, (_, k) => { const cx = g.bottom[0] + dx * g.going * (k + 1), cz = g.bottom[1] + dz * g.going * (k + 1); return `M${cx - px * hw} ${cz - pz * hw}L${cx + px * hw} ${cz + pz * hw}`; }).join('');
      const ah = Math.min(.25, st.width * .3), tip = g.top, arrow = `M${g.bottom[0] + dx * .1} ${g.bottom[1] + dz * .1}L${tip[0] - dx * .05} ${tip[1] - dz * .05}M${tip[0] - dx * ah - px * ah * .6} ${tip[1] - dz * ah - pz * ah * .6}L${tip[0] - dx * .05} ${tip[1] - dz * .05}L${tip[0] - dx * ah + px * ah * .6} ${tip[1] - dz * ah + pz * ah * .6}`;
      return (<g key={st.id}><rect data-k="stair" data-id={st.id} x={r.x0} y={r.z0} width={r.x1 - r.x0} height={r.z1 - r.z0} fill="#e9e2d5" stroke={s ? '#8B6A45' : '#5E636B'} strokeWidth={s ? .035 : .018} style={{ cursor: 'move' }} />
        <path d={treads} stroke="#8A8F96" strokeWidth={.01} pointerEvents="none" /><path d={arrow} fill="none" stroke="#23262B" strokeWidth={.02} pointerEvents="none" />
        <text x={g.bottom[0] - dx * fs * .5} y={g.bottom[1] - dz * fs * .5 + fs * .3} textAnchor="middle" fontSize={fs * .7} fontFamily="Instrument Sans, Segoe UI, sans-serif" fill="#23262B" pointerEvents="none">{t('stair.up')}</text></g>); })}
    {placements.map(pl => { const fp = footprintOf(p.catalog, pl); if (!fp) return null; const sv = p.severities[pl.id] || 'PASS', rv = resolve(p.catalog, pl.variantId), on = isSel('placement', pl.id), also = !on && inMulti(pl.id), hov = p.hoverId === pl.id;
      // simbolul de arhitectură al piesei, la dimensiunile ei reale, rotit spre fața ei; dreptunghiul transparent rămâne pentru selecție și tragere
      const w = pl.size ? pl.size.w / 100 : rv?.w ?? fp.x1 - fp.x0, d = pl.size ? pl.size.d / 100 : rv?.d ?? fp.z1 - fp.z0, sym = planSymbol(pl.group, w, d, rv?.variant.chairs ?? 4);
      const ink = sv === 'PASS' ? (on || also ? '#8B6A45' : '#3a352f') : SEV[sv], sw = on ? .03 : also ? .022 : .015;
      return (<g key={pl.id}><title>{rv ? `${rv.product.name} — ${rv.variant.name}` : pl.group}</title>
        <g transform={symbolTransform(pl)} pointerEvents="none" fill="none" stroke={ink} strokeLinejoin="round">
          {sym.outline && <path d={sym.outline} fill={sym.fill === 'soft' ? '#fffdf8' : '#fffdfa'} fillOpacity={.92} strokeWidth={sw * 1.4} />}
          {sym.detail && <path d={sym.detail} strokeWidth={sw * .8} />}</g>
        <rect data-k="placement" data-id={pl.id} x={fp.x0} y={fp.z0} width={fp.x1 - fp.x0} height={fp.z1 - fp.z0} fill={on || also ? '#8B6A45' : sv === 'PASS' ? 'transparent' : SEV[sv]} fillOpacity={on ? .12 : also ? .07 : sv === 'PASS' ? 0 : .12} stroke={on || also ? '#8B6A45' : hov ? '#C63C29' : 'none'} strokeOpacity={also ? .75 : hov && !on ? .7 : 1} strokeWidth={also ? .014 : .02} strokeDasharray={on || also || hov ? '.06 .04' : undefined} style={{ cursor: pl.locked ? 'default' : 'move' }}
          onPointerEnter={p.onHover ? () => p.onHover!(pl.id) : undefined} onPointerLeave={p.onHover ? () => p.onHover!(null) : undefined} />
        {/* lacătul piesei blocate (core/lock.ts): în colțul din dreapta-sus al amprentei, nu prinde clicuri */}
        {pl.locked && (() => { const s = Math.min(.22, (fp.x1 - fp.x0) / 3, (fp.z1 - fp.z0) / 3), x = fp.x1 - s * 1.2, z = fp.z0 + s * .2;
          return <g pointerEvents="none" aria-hidden="true"><rect x={x} y={z + s * .45} width={s} height={s * .6} rx={s * .1} fill="#5a4a3a" /><path d={`M${x + s * .22} ${z + s * .45}V${z + s * .25}a${s * .28} ${s * .28} 0 0 1 ${s * .56} 0V${z + s * .45}`} fill="none" stroke="#5a4a3a" strokeWidth={s * .12} /></g>; })()}</g>); })}
    {/* covoarele camerelor (stratul de finisaje): dreptunghi textil punctat sub piese, cu numărul covorului când sunt mai multe */}
    {p.showFinishes && p.mc && floor.rooms.flatMap(r => { const rugs = roomRugs(finishesOf(p.snap, r));
      return rugs.map((c, i) => { const m = p.mc!.materials.find(x => x.id === c.material), s = m ? rugSize(r, m, c.rotate) : null; if (!s) return null; const q = rugRectAt(r, s, c);
        return <g key={`rug-${r.id}-${i}`} pointerEvents="none" data-rug={i + 1}><rect x={q.x0} y={q.z0} width={q.x1 - q.x0} height={q.z1 - q.z0} rx={.04} fill={m!.specs?.color ?? '#c8b79a'} fillOpacity={.18} stroke="#8B6A45" strokeWidth={.012} strokeDasharray=".04 .03" />
          {rugs.length > 1 && <text x={q.x0 + fs * .4} y={q.z1 - fs * .35} fontSize={fs * .55} fontFamily="Instrument Sans, Segoe UI, sans-serif" fill="#6b5a45" stroke="#FFFDF8" strokeWidth={fs * .12} paintOrder="stroke">{t('tex.rugN', { n: i + 1 })}</text>}</g>; }); })}
    {/* camera aleasă: chenar punctat în interiorul pereților, vizibil și când e aleasă de la tastatură */}
    {floor.rooms.filter(r => isSel('room', r.id)).map(r => <path key={`sel-${r.id}`} pointerEvents="none" d={roomBoundaryPath(r)} fill="#7A5A36" fillOpacity={.07} stroke="#7A5A36" strokeWidth={.045} strokeDasharray=".14 .08" />)}
    {/* eticheta zonei de pardoseală (nume · suprafață) în colțul ei, deasupra mobilierului, ca să nu fie acoperită de masă */}
    {(floor.zones || []).filter(z => drawn('finishes', 'floor_zone', z.id)).map(z => { const r = z.rect, on = isSel('floor_zone', z.id);
      return <text key={`zl-${z.id}`} x={r.x0 + fs * .6} y={r.z0 + fs * 1.5} fontSize={fs * .66} fontWeight={600} fontFamily="Instrument Sans, Segoe UI, sans-serif" fill={on ? '#8B6A45' : '#5a4a38'} stroke="#FFFDF8" strokeWidth={fs * .16} paintOrder="stroke" pointerEvents="none">{z.label ? `${z.label} · ` : ''}{formatArea((r.x1 - r.x0) * (r.z1 - r.z0), units, lang)}</text>; })}
    {/* etichetele camerelor deasupra mobilierului, cu contur de hârtie ca să rămână lizibile */}
    {!layerOff('roomLabels') && floor.rooms.map(r => { const w = r.rect.x1 - r.rect.x0, d = r.rect.z1 - r.rect.z0, [cx, cz] = roomCentroid(r);
      return (<g key={`lbl-${r.id}`} pointerEvents="none" stroke="#FFFDF8" strokeWidth={fs * .25} strokeLinejoin="round" paintOrder="stroke">
        <text x={cx} y={cz - fs * .2} textAnchor="middle" fontSize={fs * 1.15} fontFamily="Newsreader, Georgia, serif" fill="#1A141C" stroke="#FFFDF8" strokeWidth={fs * .2} strokeLinejoin="round" paintOrder="stroke">{r.name}</text>
        <text x={cx} y={cz + fs * 1.1} textAnchor="middle" fontSize={fs * .78} fontFamily="Instrument Sans, Segoe UI, sans-serif" fill="#3D343B" stroke="#FFFDF8" strokeWidth={fs * .18} strokeLinejoin="round" paintOrder="stroke">{formatArea(roomArea(r), units, lang)} · {wl(w)}×{wl(d)}</text></g>); })}
    {floor.walls.map(w => { const L = wallLength(w.a, w.b); if (L < .01) return null; const ux = (w.b[0] - w.a[0]) / L, uz = (w.b[1] - w.a[1]) / L, nx = -uz, nz = ux, sel = isSel('wall', w.id);
      const mid = dimLabelAt(w, fs), dimOn = p.tool === 'select' && !!p.onSetWallLength;
      // starea peretelui: existent = plin; nou = hașură diagonală în contur; de demolat = doar conturul punctat (golurile lui nu se mai desenează)
      const st = w.status, h = w.thickness / 2, band = `M${w.a[0] - ux * h + nx * h} ${w.a[1] - uz * h + nz * h}L${w.b[0] + ux * h + nx * h} ${w.b[1] + uz * h + nz * h}L${w.b[0] + ux * h - nx * h} ${w.b[1] + uz * h - nz * h}L${w.a[0] - ux * h - nx * h} ${w.a[1] - uz * h - nz * h}Z`;
      return (<g key={w.id} data-wall-status={st}>
        <line data-k="wall" data-id={w.id} x1={w.a[0]} y1={w.a[1]} x2={w.b[0]} y2={w.b[1]} stroke={st === 'demolish' ? 'transparent' : st === 'new' ? '#FFFDF8' : sel ? '#8B6A45' : '#23262B'} strokeWidth={w.thickness} strokeLinecap="square" style={{ cursor: 'pointer' }} />
        {st === 'new' && <path d={band} fill="url(#wall-new-hatch)" stroke={sel ? '#8B6A45' : '#23262B'} strokeWidth={sel ? .03 : .015} pointerEvents="none" />}
        {st === 'demolish' && <path d={band} fill="#FBFBF9" fillOpacity={.6} stroke={sel ? '#8B6A45' : '#B3261E'} strokeWidth={sel ? .03 : .018} strokeDasharray=".08 .05" pointerEvents="none" />}
        {st !== 'demolish' && w.openings.map(o => { const a = [w.a[0] + ux * o.offset, w.a[1] + uz * o.offset], b = [w.a[0] + ux * (o.offset + o.width), w.a[1] + uz * (o.offset + o.width)], os = p.sel?.kind === 'opening' && p.sel.id === o.id;
          return (<g key={o.id}><line data-k="opening" data-id={o.id} data-w={w.id} x1={a[0]} y1={a[1]} x2={b[0]} y2={b[1]} stroke={os ? '#efe5d8' : '#FBFBF9'} strokeWidth={w.thickness + .01} style={{ cursor: 'ew-resize' }} />
            {o.kind === 'window' ? <><line x1={a[0]} y1={a[1]} x2={b[0]} y2={b[1]} stroke="#6E8798" strokeWidth={.025} pointerEvents="none" /><line x1={a[0] + nx * w.thickness * .3} y1={a[1] + nz * w.thickness * .3} x2={b[0] + nx * w.thickness * .3} y2={b[1] + nz * w.thickness * .3} stroke="#6E8798" strokeWidth={.012} pointerEvents="none" /></>
              // golul de trecere: fără foaie și fără arc, doar tocurile (linii scurte peste grosime) și buiandrugul punctat
              : o.passage ? <g pointerEvents="none" stroke="#3a352f"><path d={`M${a[0] - nx * w.thickness / 2} ${a[1] - nz * w.thickness / 2}L${a[0] + nx * w.thickness / 2} ${a[1] + nz * w.thickness / 2}M${b[0] - nx * w.thickness / 2} ${b[1] - nz * w.thickness / 2}L${b[0] + nx * w.thickness / 2} ${b[1] + nz * w.thickness / 2}`} strokeWidth={.02} />
                <line x1={a[0]} y1={a[1]} x2={b[0]} y2={b[1]} strokeWidth={.012} strokeDasharray=".08 .05" /></g>
              : (() => { const sw = doorSwing(floor, floor.walls.indexOf(w), w.openings.indexOf(o));
                // simbolul pe tip (core/door-symbols.ts): foaia și arcul la batantă, șina la glisantă, caseta punctată, zigzagul, pivotul
                const sym = doorSymbol({ a: a as [number, number], b: b as [number, number], thickness: w.thickness, hinge: sw.hinge, swing: sw.swing, type: o.doorType, flush: o.flush });
                return <g pointerEvents="none" data-door-type={sym.type}>{sym.paths.map((q, i) => <path key={i} d={q.d} fill={q.fill ? '#3a352f' : 'none'} fillOpacity={q.fill ? .35 : undefined} stroke="#3a352f" strokeWidth={.012} strokeDasharray={q.dash ? '.05 .035' : undefined} />)}
                  {sym.label && <text x={sym.label.x} y={sym.label.z + fs * .25} textAnchor="middle" fontSize={fs * .7} fontFamily="Instrument Sans, Segoe UI, sans-serif" fill="#3a352f">{sym.label.text}</text>}</g>; })()}</g>); })}
        <text x={mid[0]} y={mid[1] + fs * .3} textAnchor="middle" fontSize={fs * .78} fontFamily="Instrument Sans, Segoe UI, sans-serif" fill={sel ? '#B8432C' : '#3D343B'} stroke="#FFFDF8" strokeWidth={fs * .18} strokeLinejoin="round" paintOrder="stroke" pointerEvents="none">{wl(L)}</text>
        {/* zona de apucare a cotei: cel puțin 44 × 24 px pe ecran (WCAG 2.5.8), clic = peretele, dublu clic = lungimea */}
        {dimOn && (() => { const hw = Math.max(fs * 1.6, pxPerM > 0 ? 22 / pxPerM : 0), hh = Math.max(fs * .7, pxPerM > 0 ? 12 / pxPerM : 0);
          return <rect data-k="wall" data-id={w.id} data-dim="1" x={mid[0] - hw} y={mid[1] - hh} width={hw * 2} height={hh * 2} fill="transparent" style={{ cursor: 'text' }}><title>{t('plan.dimEditHint')}</title></rect>; })()}
        {sel && ([['a', w.a], ['b', w.b]] as const).map(([end, pt]) => <circle key={end} data-k="handle" data-w={w.id} data-end={end} data-id={w.id} cx={pt[0]} cy={pt[1]} r={fs * .6} fill="#fff" stroke="#8B6A45" strokeWidth={.03} style={{ cursor: 'grab' }} />)}
      </g>); })}
    {/* Placările de perete (stratul de finisaje): bandă de 4 cm pe fața interioară, pe porțiunea ei, cu marcajul „R1”, „T1”… */}
    {p.showFinishes && (() => { const marks = featureMarks(p.snap, floor, finishesOf);
      return marks.map(mk => { const room = floor.rooms.find(r => r.id === mk.roomId)!, w = finishesOf(p.snap, room).wallFeatures![mk.index]!, rc = featurePlanRect(floor, room, w); if (!rc) return null;
        const horiz = w.side === 'N' || w.side === 'S', inward = w.side === 'N' || w.side === 'W' ? 1 : -1, cx = (rc.x0 + rc.x1) / 2, cz = (rc.z0 + rc.z1) / 2;
        // marcajul stă la capătul de început al benzii (la mijloc ar acoperi eticheta unui obiect de pe perete centrat pe ea)
        const k0 = Math.min(fs * 1.2, (horiz ? rc.x1 - rc.x0 : rc.z1 - rc.z0) / 2), lx = horiz ? rc.x0 + k0 : cx + inward * fs * 1.1, lz = horiz ? cz + inward * fs * 1.1 : rc.z0 + k0, code = `${t(`fin.code.${w.kind}`)}${mk.n}`;
        const fill = w.kind === 'paint' || w.kind === 'panel' ? (w.color ?? (p.mc ? materialOf(p.mc, w.material)?.specs?.color : undefined) ?? '#d9cbb5') : FIN_FILL[w.kind] ?? '#cbbfae';
        const pat = w.kind === 'wallpaper' ? 'url(#fin-hatch)' : w.kind === 'slats' ? `url(#fin-slats-${horiz ? 'h' : 'v'})` : w.kind === 'plaster' ? 'url(#fin-dots)' : null;
        return (<g key={`${mk.roomId}:${mk.index}`} pointerEvents="none" data-finish={code}><title>{code} · {t(`fin.kind.${w.kind}`)}</title>
          <rect x={rc.x0} y={rc.z0} width={rc.x1 - rc.x0} height={rc.z1 - rc.z0} fill={fill} stroke="#6b5a45" strokeWidth={.006} />
          {pat && <rect x={rc.x0} y={rc.z0} width={rc.x1 - rc.x0} height={rc.z1 - rc.z0} fill={pat} />}
          <text x={lx} y={lz + fs * .22} textAnchor="middle" fontSize={fs * .55} fontWeight={600} fontFamily="Instrument Sans, Segoe UI, sans-serif" fill="#6b5a45" stroke="#FFFDF8" strokeWidth={fs * .12} paintOrder="stroke">{code}</text></g>); }); })()}
    {cladDraft && (() => { const room = floor.rooms.find(r => r.id === cladDraft.roomId); if (!room) return null; const h = cursor ? cladTarget(floor, cursor) : null;
      const b = h && h.roomId === cladDraft.roomId && h.side === cladDraft.side ? h.along : cladDraft.along, rc = featurePlanRect(floor, room, { side: cladDraft.side, kind: 'slats', material: '', alongFromM: Math.min(cladDraft.along, b), alongToM: Math.max(cladDraft.along, b) + (Math.abs(b - cladDraft.along) < .05 ? .05 : 0) }, .08);
      return rc ? <rect pointerEvents="none" x={rc.x0} y={rc.z0} width={rc.x1 - rc.x0} height={rc.z1 - rc.z0} fill="#B8432C" fillOpacity={.35} stroke="#B8432C" strokeWidth={.012} strokeDasharray=".05 .03" /> : null; })()}
    {/* Nișe: scobitura punctată în grosimea peretelui, pe fața ei, cu eticheta „N lățime×înălțime” */}
    {(floor.niches || []).map(n => { const g = nicheGeometry(floor, n); if (!g) return null; const ns = isSel('niche', n.id), lx = g.center[0] + g.nx * fs * 1.1, lz = g.center[1] + g.nz * fs * 1.1;
      return (<g key={n.id} data-k="niche" data-id={n.id} style={{ cursor: 'ew-resize' }}><title>{t('tool.niche')} ({Math.round(n.width * 100)}×{Math.round(n.height * 100)}×{Math.round(n.depth * 100)} cm)</title>
        <path d={`M${g.quad.map(q => q.join(' ')).join('L')}Z`} fill={ns ? '#efe5d8' : '#FBFBF9'} stroke={ns ? '#8B6A45' : '#3a352f'} strokeWidth={ns ? .02 : .012} strokeDasharray=".05 .03" />
        <text x={lx} y={lz + fs * .25} textAnchor="middle" fontSize={fs * .6} fontFamily="Instrument Sans, Segoe UI, sans-serif" fill={ns ? '#8B6A45' : '#3D343B'} stroke="#FFFDF8" strokeWidth={fs * .15} paintOrder="stroke" pointerEvents="none">N {Math.round(n.width * 100)}×{Math.round(n.height * 100)}</text></g>); })}
    {/* Obiecte pe perete (deasupra planului de secțiune): linie dublă punctată pe fața peretelui, de lățimea obiectului, cu eticheta T/O/R/TV */}
    {p.showWallItems !== false && (floor.wallItems || []).filter(it => drawn('wallItems', 'wall_item', it.id)).map(it => { const g = wallItemGeometry(floor, it); if (!g) return null; const on = isSel('wall_item', it.id), d = Math.max(.03, Math.min(.2, wallItemDepth(it)));
      const [a, b] = g.face, off = (q: [number, number], k: number) => `${q[0] + g.nx * k} ${q[1] + g.nz * k}`, ink = on ? '#8B6A45' : '#3a352f', lx = g.center[0] + g.nx * (d + fs * .9), lz = g.center[1] + g.nz * (d + fs * .9);
      return (<g key={it.id} data-k="wall_item" data-id={it.id} data-wall-item={it.kind} style={{ cursor: 'ew-resize' }}><title>{it.label || t(`wallItem.kind.${it.kind}`)} ({Math.round(it.width * 100)}×{Math.round(it.height * 100)} cm, {Math.round(it.elevation * 100)} cm)</title>
        <path d={`M${off(a, 0)}L${off(b, 0)}L${off(b, d)}L${off(a, d)}Z`} fill={on ? '#efe5d8' : '#fff'} fillOpacity={.85} stroke="transparent" strokeWidth={fs * .4} />
        <path d={`M${off(a, .01)}L${off(b, .01)}M${off(a, d)}L${off(b, d)}`} fill="none" stroke={ink} strokeWidth={on ? .018 : .01} strokeDasharray=".05 .03" />
        <text x={lx} y={lz + fs * .22} textAnchor="middle" fontSize={fs * .55} fontFamily="Instrument Sans, Segoe UI, sans-serif" fill={ink} stroke="#FFFDF8" strokeWidth={fs * .12} paintOrder="stroke" pointerEvents="none">{t(`wallItem.code.${it.kind}`)}</text></g>); })}
    {/* Cote persistente și texte de pe planșă */}
    {(floor.annotations || []).filter(an => drawn('annotations', 'annotation', an.id)).map(an => { const on = isSel('annotation', an.id), ink = on ? '#8B6A45' : DIM;
      if (an.kind === 'text'){ const h = textHeight(an), ta = textAnchorAt(an), lg = leaderGeometry(an), rot = an.rotation ? `rotate(${an.rotation} ${an.at[0]} ${an.at[1]})` : undefined, col = on ? '#8B6A45' : '#1A141C';
        // săgeata cu notă (linie + vârf plin, cu o zonă de prindere mai lată) și simbolul cotei de nivel fac parte din aceeași adnotare
        return (<g key={an.id} data-k="annotation" data-id={an.id} style={{ cursor: 'move' }}>
          {lg && <><line x1={lg.from[0]} y1={lg.from[1]} x2={lg.tip[0]} y2={lg.tip[1]} stroke="transparent" strokeWidth={Math.max(h * .6, fs * .5)} />
            <line x1={lg.from[0]} y1={lg.from[1]} x2={lg.tip[0]} y2={lg.tip[1]} stroke={col} strokeWidth={h * .08} /><path d={`M${lg.head.map(q => q.join(' ')).join('L')}Z`} fill={col} /></>}
          {an.symbol === 'level' && <path d={`M${levelMarkGeometry(an).outline.map(q => q.join(' ')).join('L')}Z`} fill={HALO} stroke={col} strokeWidth={h * .08} strokeLinejoin="round" transform={rot} />}
          <text x={ta[0]} y={ta[1]} fontSize={h} transform={rot} textAnchor={textAnchor(an) === 'end' ? 'end' : undefined} fontFamily="Instrument Sans, Segoe UI, sans-serif"
            fill={col} stroke={HALO} strokeWidth={h * .18} paintOrder="stroke" textDecoration={on ? 'underline' : undefined}>{an.text}</text>
          {on && an.leader && <circle data-k="leadertip" data-id={an.id} cx={an.leader[0]} cy={an.leader[1]} r={fs * .45} fill="#fff" stroke="#8B6A45" strokeWidth={.03} style={{ cursor: 'grab' }}><title>{t('leader.tipHint')}</title></circle>}</g>); }
      if (an.kind === 'angle') return <AngleMark key={an.id} d={an} fs={fs} ink={ink} label={formatAngle(angleGeometry(an).deg, lang === 'en' ? '.' : ',')} hit={{ 'data-k': 'annotation', 'data-id': an.id }} />;
      // norul de revizie: conturul cu arce (prins pe linie, nu pe interior, ca planul de sub el să rămână accesibil) și eticheta
      if (an.kind === 'cloud'){ const d = cloudPath(an.rect), lb = cloudLabelAt(an.rect);
        return (<g key={an.id} data-k="annotation" data-id={an.id} style={{ cursor: 'move' }}><title>{t('tool.rev_cloud')}{an.label ? ` · ${an.label}` : ''}</title>
          <path d={d} fill="none" stroke="transparent" strokeWidth={fs * .8} /><path d={d} fill="none" stroke={ink} strokeWidth={fs * .07} strokeLinejoin="round" pointerEvents="none" />
          {an.label && <text x={lb[0]} y={lb[1]} fontSize={CLOUD_LABEL_M} fontWeight={600} fontFamily="Instrument Sans, Segoe UI, sans-serif" fill={ink} stroke={HALO} strokeWidth={CLOUD_LABEL_M * .18} paintOrder="stroke">{an.label}</text>}</g>); }
      return <DimMark key={an.id} d={an} fs={fs} ink={ink} label={wl(dimGeometry(an).value)} hit={{ 'data-k': 'annotation', 'data-id': an.id }} />; })}
    {/* cota unghiulară în lucru: brațele până la cursor, apoi arcul cu raza de la cursor */}
    {angleDraft && (() => { const c = cursor ?? angleDraft.v, { v, a, b } = angleDraft, dash = { stroke: DIM, strokeWidth: fs * .07, strokeDasharray: '.08 .05' };
      const dec = lang === 'en' ? '.' : ',', dot = <circle cx={v[0]} cy={v[1]} r={fs * .25} fill={DIM} />;
      if (!a) return <g pointerEvents="none"><line x1={v[0]} y1={v[1]} x2={c[0]} y2={c[1]} {...dash} />{dot}</g>;
      if (!b){ const d = { v, a, b: c, r: Math.max(LIM.annotation.angleRadiusMinM, Math.min(PLAN_DEFAULTS.angleRadius, wallLength(v, a), wallLength(v, c))) }, ok = angleOk(v, a, c);
        return <g pointerEvents="none"><line x1={v[0]} y1={v[1]} x2={a[0]} y2={a[1]} {...dash} /><line x1={v[0]} y1={v[1]} x2={c[0]} y2={c[1]} {...dash} />{dot}
          {ok && <g opacity={.75}><AngleMark d={d} fs={fs} ink={DIM} label={formatAngle(angleGeometry(d).deg, dec)} /></g>}</g>; }
      const d = { v, a, b, r: cursor ? angleRadiusAt(v, cursor) : PLAN_DEFAULTS.angleRadius };
      return <g pointerEvents="none" opacity={.75}><AngleMark d={d} fs={fs} ink={DIM} label={formatAngle(angleGeometry(d).deg, dec)} />{dot}</g>; })()}
    {/* norul de revizie care își așteaptă eticheta */}
    {cloudDraft && <path d={cloudPath(cloudDraft.rect)} fill="none" stroke={DIM} strokeWidth={fs * .07} strokeLinejoin="round" opacity={.75} pointerEvents="none" />}
    {/* săgeata cu notă în lucru: de la vârful ales până la cursor, apoi până la locul notei */}
    {(() => { const d = textDraft?.extra?.leader ? { at: textDraft.at, leader: textDraft.extra.leader } : p.tool === 'leader' && leaderTip ? { at: cursor ?? leaderTip, leader: leaderTip } : null, lg = d && leaderGeometry(d);
      return d && (<g pointerEvents="none">{lg && <><line x1={lg.from[0]} y1={lg.from[1]} x2={lg.tip[0]} y2={lg.tip[1]} stroke={DIM} strokeWidth={fs * .07} strokeDasharray=".08 .05" /><path d={`M${lg.head.map(q => q.join(' ')).join('L')}Z`} fill={DIM} /></>}
        <circle cx={d.leader[0]} cy={d.leader[1]} r={fs * .2} fill={DIM} /></g>); })()}
    {dimDraft && (() => { const c = cursor ?? dimDraft.a;
      if (!dimDraft.b){ const L = wallLength(dimDraft.a, c); return (<g pointerEvents="none"><line x1={dimDraft.a[0]} y1={dimDraft.a[1]} x2={c[0]} y2={c[1]} stroke={DIM} strokeWidth={fs * .07} strokeDasharray=".08 .05" /><circle cx={dimDraft.a[0]} cy={dimDraft.a[1]} r={fs * .25} fill={DIM} />
        <text x={(dimDraft.a[0] + c[0]) / 2} y={(dimDraft.a[1] + c[1]) / 2 - fs * .5} textAnchor="middle" fontSize={fs} fontFamily="Instrument Sans, Segoe UI, sans-serif" fill={DIM} stroke={HALO} strokeWidth={fs * .22} paintOrder="stroke">{typed ? `${typed}▏` : formatLength(L, units, lang)}</text></g>); }
      const d = { a: dimDraft.a, b: dimDraft.b, offset: cursor ? dimOffsetAt(dimDraft.a, dimDraft.b, dimDraft.mode, cursor) : PLAN_DEFAULTS.dimOffset, mode: dimDraft.mode };
      return <g pointerEvents="none" opacity={.75}><DimMark d={d} fs={fs} ink={DIM} label={typed ? `${typed}▏` : wl(dimGeometry(d).value)} /></g>; })()}
    {/* Grinzi sub tavan */}
    {(floor.beams || []).map(bm => {
      const bs = isSel('beam', bm.id);
      const L = wallLength(bm.a, bm.b);
      const mid = [(bm.a[0] + bm.b[0]) / 2, (bm.a[1] + bm.b[1]) / 2];
      return (<g key={bm.id} data-k="beam" data-id={bm.id} style={{ cursor: 'pointer' }}>
        <title>{t('tool.beam')} ({Math.round(bm.width * 100)}×{Math.round(bm.depth * 100)} cm)</title>
        <line x1={bm.a[0]} y1={bm.a[1]} x2={bm.b[0]} y2={bm.b[1]} stroke={bs ? '#8B6A45' : '#718096'} strokeWidth={bm.width} strokeDasharray=".12 .06" strokeOpacity={.75} />
        <text x={mid[0]} y={mid[1] - fs * .3} textAnchor="middle" fontSize={fs * .65} fontFamily="Instrument Sans, Segoe UI, sans-serif" fill={bs ? '#8B6A45' : '#4A5568'} pointerEvents="none">{t('tool.beam')} {Math.round(bm.width * 100)}×{Math.round(bm.depth * 100)}</text>
      </g>);
    })}
    {/* Ghidul BIM (Faza 4): pereții structurali au axa marcată, fundațiile conturul punctat, traseele culoarea sistemului;
        diametrul nedeclarat apare „necunoscut” în titlu, nu e inventat (core/mep.ts) */}
    {floor.walls.filter(w => w.structural).map(w => <line key={`st-${w.id}`} data-k="structural" x1={w.a[0]} y1={w.a[1]} x2={w.b[0]} y2={w.b[1]} stroke="#c2410c" strokeWidth={fs * .09} strokeDasharray=".3 .08 .05 .08" pointerEvents="none"><title>{t('bim.structuralWall')}</title></line>)}
    {(floor.footings || []).map(fd => <rect key={fd.id} data-k="footing" data-id={fd.id} x={fd.x - fd.w / 2} y={fd.z - fd.d / 2} width={fd.w} height={fd.d} fill="none" stroke="#4A5568" strokeWidth={fs * .06} strokeDasharray=".1 .06" pointerEvents="none"><title>{t('bim.footing')} {Math.round(fd.w * 100)}×{Math.round(fd.d * 100)} · {Math.round(fd.depth * 100)} cm</title></rect>)}
    {/* contur deschis sub fiecare traseu: traseele merg des pe lângă pereți, iar linia colorată s-ar pierde pe peretele închis */}
    {(floor.mepRuns || []).filter(r => drawn('mep', 'mep_run', r.id)).map(r => <polyline key={`halo-${r.id}`} points={r.points.map(p => `${p[0]},${p[2]}`).join(' ')} fill="none" stroke="#FFFDF8" strokeWidth={Math.max(fs * .22, (r.diameterMm ?? 0) / 1000) + fs * .18} strokeLinejoin="round" strokeLinecap="round" strokeOpacity={.9} pointerEvents="none" />)}
    {(floor.mepRuns || []).filter(r => drawn('mep', 'mep_run', r.id)).map(r => <polyline key={r.id} data-k="mep" data-id={r.id} points={r.points.map(p => `${p[0]},${p[2]}`).join(' ')} fill="none" stroke={MEP_SYSTEMS[r.system]?.color ?? '#555'} strokeWidth={Math.max(fs * .22, (r.diameterMm ?? 0) / 1000)} strokeDasharray={r.diameterMm ? undefined : `${fs * .6} ${fs * .35}`} strokeLinejoin="round" strokeOpacity={.85} pointerEvents="none">
      <title>{MEP_SYSTEMS[r.system]?.label[lang === 'en' ? 'en' : 'ro']} · {r.diameterMm ? `Ø${r.diameterMm} mm` : t('bim.diameterUnknown')}{r.material ? ` · ${r.material}` : ''}</title></polyline>)}
    {/* zona de apucare a traseului (mai lată decât linia) și conturul celui ales; doar cu unealta Selecție, în etapa Instalații */}
    {p.mepSelectable && p.tool === 'select' && (floor.mepRuns || []).filter(r => drawn('mep', 'mep_run', r.id)).map(r => { const pts = r.points.map(q => `${q[0]},${q[2]}`).join(' '), w = Math.max(fs * .22, (r.diameterMm ?? 0) / 1000);
      return (<g key={`pick-${r.id}`}>{isSel('mep_run', r.id) && <polyline data-mep-selected={r.id} points={pts} fill="none" stroke="#1D4ED8" strokeWidth={w + fs * .2} strokeOpacity={.35} strokeLinejoin="round" strokeLinecap="round" pointerEvents="none" />}
        <polyline data-k="mep_run" data-id={r.id} points={pts} fill="none" stroke="transparent" strokeWidth={Math.max(fs * .9, w)} strokeLinejoin="round" pointerEvents="stroke" style={{ cursor: 'pointer' }} /></g>); })}
    {/* Balustrade: linie dublă (sticlă: albastră, metal: gri închis) cu stâlpii; zona de apucare e mai lată decât desenul */}
    {(floor.railings || []).map(rl => { const on = isSel('railing', rl.id), L = wallLength(rl.a, rl.b); if (L < .01) return null; const nx = -(rl.b[1] - rl.a[1]) / L * .03, nz = (rl.b[0] - rl.a[0]) / L * .03, glass = railingKind(rl) === 'glass', ink = on ? '#8B6A45' : glass ? '#2563EB' : '#3a352f', n = Math.max(1, Math.ceil(L / 1.2 - 1e-9));
      return (<g key={rl.id} data-k="railing" data-id={rl.id} data-railing={railingKind(rl)} style={{ cursor: 'pointer' }}><title>{t('tool.railing')} · {t(`railing.kind.${railingKind(rl)}`)} ({wl(L)} · h {Math.round(railingHeight(rl) * 100)})</title>
        <line x1={rl.a[0]} y1={rl.a[1]} x2={rl.b[0]} y2={rl.b[1]} stroke="transparent" strokeWidth={Math.max(.12, fs * .5)} />
        <path d={`M${rl.a[0] + nx} ${rl.a[1] + nz}L${rl.b[0] + nx} ${rl.b[1] + nz}M${rl.a[0] - nx} ${rl.a[1] - nz}L${rl.b[0] - nx} ${rl.b[1] - nz}`} stroke={ink} strokeWidth={on ? .02 : .012} pointerEvents="none" />
        {Array.from({ length: n + 1 }, (_, k) => { const f = k / n, x = rl.a[0] + (rl.b[0] - rl.a[0]) * f, z = rl.a[1] + (rl.b[1] - rl.a[1]) * f; return <rect key={k} x={x - .025} y={z - .025} width={.05} height={.05} fill={ink} pointerEvents="none" />; })}</g>); })}
    {/* Pereți vitrați / Compartimentări sticlă */}
    {(floor.glassWalls || []).map(gw => {
      const gs = isSel('glass_wall', gw.id);
      const L = wallLength(gw.a, gw.b);
      const ux = L > 0.01 ? (gw.b[0] - gw.a[0]) / L : 1, uz = L > 0.01 ? (gw.b[1] - gw.a[1]) / L : 0, nx = -uz, nz = ux;
      const mullions = gw.mullions ?? Math.max(1, Math.round(L / 0.9));
      return (<g key={gw.id} data-k="glass_wall" data-id={gw.id} style={{ cursor: 'pointer' }}>
        <title>{t('tool.glass_wall')}</title>
        <line x1={gw.a[0]} y1={gw.a[1]} x2={gw.b[0]} y2={gw.b[1]} stroke="#93C5FD" strokeWidth={gw.thickness} strokeOpacity={.65} />
        <line x1={gw.a[0] + nx * gw.thickness * .45} y1={gw.a[1] + nz * gw.thickness * .45} x2={gw.b[0] + nx * gw.thickness * .45} y2={gw.b[1] + nz * gw.thickness * .45} stroke={gs ? '#2563EB' : '#1E293B'} strokeWidth={.015} pointerEvents="none" />
        <line x1={gw.a[0] - nx * gw.thickness * .45} y1={gw.a[1] - nz * gw.thickness * .45} x2={gw.b[0] - nx * gw.thickness * .45} y2={gw.b[1] - nz * gw.thickness * .45} stroke={gs ? '#2563EB' : '#1E293B'} strokeWidth={.015} pointerEvents="none" />
        {Array.from({ length: mullions + 1 }, (_, mi) => {
          const fr = mi / mullions;
          const px = gw.a[0] + (gw.b[0] - gw.a[0]) * fr, pz = gw.a[1] + (gw.b[1] - gw.a[1]) * fr;
          return <line key={mi} x1={px - nx * gw.thickness * .45} y1={pz - nz * gw.thickness * .45} x2={px + nx * gw.thickness * .45} y2={pz + nz * gw.thickness * .45} stroke={gs ? '#2563EB' : '#1E293B'} strokeWidth={.018} pointerEvents="none" />;
        })}
      </g>);
    })}
    {/* Stâlpi / Coloane structurale */}
    {(floor.columns || []).map(col => {
      const cs = isSel('column', col.id);
      // ghena: dreptunghi hașurat; casetarea: contur dublu; stâlpul structural rămâne plin
      if (col.role === 'shaft' || col.role === 'boxing'){ const x0 = col.x - col.w / 2, z0 = col.z - col.d / 2, k = Math.min(.03, col.w / 6, col.d / 6), ink = cs ? '#8B6A45' : '#3D343B';
        return (<g key={col.id} data-k="column" data-id={col.id} style={{ cursor: 'move' }}>
          <title>{t(col.role === 'shaft' ? 'tool.shaft' : 'column.boxing')} ({Math.round(col.w * 100)}×{Math.round(col.d * 100)} cm{col.height ? ` · h ${Math.round(col.height * 100)}` : ''})</title>
          <rect x={x0} y={z0} width={col.w} height={col.d} fill={col.role === 'shaft' ? 'url(#shaft-hatch)' : '#f4f1ea'} stroke={ink} strokeWidth={cs ? .035 : .02} />
          {col.role === 'shaft' ? <rect x={x0} y={z0} width={col.w} height={col.d} fill="#fbfaf7" fillOpacity={.35} pointerEvents="none" />
            : <rect x={x0 + k} y={z0 + k} width={col.w - 2 * k} height={col.d - 2 * k} fill="none" stroke={ink} strokeWidth={.012} pointerEvents="none" />}</g>); }
      return (<g key={col.id} data-k="column" data-id={col.id} style={{ cursor: 'move' }}>
        <title>{t('tool.column')} ({Math.round(col.w * 100)}×{Math.round(col.d * 100)} cm)</title>
        {col.shape === 'round' ? (
          <circle cx={col.x} cy={col.z} r={col.w / 2} fill="#3D343B" stroke={cs ? '#8B6A45' : '#1A141C'} strokeWidth={cs ? .04 : .02} />
        ) : (
          <rect x={col.x - col.w / 2} y={col.z - col.d / 2} width={col.w} height={col.d} fill="#3D343B" stroke={cs ? '#8B6A45' : '#1A141C'} strokeWidth={cs ? .04 : .02} />
        )}
        <line x1={col.x - col.w * .35} y1={col.z - col.d * .35} x2={col.x + col.w * .35} y2={col.z + col.d * .35} stroke="#E2E8F0" strokeWidth={.015} pointerEvents="none" />
        <line x1={col.x - col.w * .35} y1={col.z + col.d * .35} x2={col.x + col.w * .35} y2={col.z - col.d * .35} stroke="#E2E8F0" strokeWidth={.015} pointerEvents="none" />
      </g>);
    })}
    {/* Tavane false pe zonă (deasupra planului de secțiune): contur linie-punct, fără umplere, cu „TF −15” în colț; se apucă de contur */}
    {(floor.ceilingZones || []).filter(z => drawn('finishes', 'ceiling_zone', z.id)).map(z => { const r = z.rect, on = isSel('ceiling_zone', z.id), w = r.x1 - r.x0, d = r.z1 - r.z0, ink = on ? '#8B6A45' : '#5a3d6b';
      return (<g key={z.id} data-k="ceiling_zone" data-id={z.id} data-ceiling-zone={z.dropCm} style={{ cursor: 'move' }}><title>{t('tool.ceiling_zone')}{z.label ? ` · ${z.label}` : ''} ({wl(w)}×{wl(d)}, −{Math.round(z.dropCm)} cm)</title>
        <rect x={r.x0} y={r.z0} width={w} height={d} fill="none" stroke="transparent" strokeWidth={Math.max(.12, fs * .5)} />
        <rect x={r.x0} y={r.z0} width={w} height={d} fill={on ? '#5a3d6b' : 'none'} fillOpacity={on ? .06 : 0} stroke={ink} strokeWidth={on ? .025 : .014} strokeDasharray=".16 .05 .03 .05" pointerEvents="none" />
        {z.coveCm ? <rect x={r.x0 + .06} y={r.z0 + .06} width={Math.max(.01, w - .12)} height={Math.max(.01, d - .12)} fill="none" stroke={ink} strokeWidth={.008} strokeDasharray=".03 .03" pointerEvents="none" /> : null}
        <text x={r.x1 - fs * .4} y={r.z0 + fs * 1.1} textAnchor="end" fontSize={fs * .6} fontWeight={600} fontFamily="Instrument Sans, Segoe UI, sans-serif" fill={ink} stroke="#FFFDF8" strokeWidth={fs * .14} paintOrder="stroke">{z.label ? `${z.label} · ` : ''}TF −{Math.round(z.dropCm)}{z.led ? ' · LED' : ''}</text></g>); })}
    {/* mânerele laturilor camerei alese (deasupra pereților); zona de apucare are ~44 px, pentru degete */}
    {p.tool === 'select' && floor.rooms.filter(r => isSel('room', r.id)).map(r => (['x0', 'x1', 'z0', 'z1'] as const).map(k => {
      // mânerul stă pe cea mai lungă porțiune a laturii fără uși / ferestre; dacă golurile acoperă toată latura, e decalat perpendicular în afara camerei
      const v = k[0] === 'x', hp = edgeHandlePoint(floor, r.rect, k, hitR, hitR * 1.15), cx = hp.x, cz = hp.z, len = fs * 1.6, th = fs * .55;
      return (<g key={`re-${r.id}-${k}`} data-k="redge" data-id={r.id} data-edge={k} data-offset={hp.offset ? '1' : undefined} style={{ cursor: v ? 'ew-resize' : 'ns-resize', touchAction: 'none' }}>
        <title>{t('plan.resizeEdge')}</title>
        <circle cx={cx} cy={cz} r={hitR} fill="transparent" />
        <rect x={cx - (v ? th : len) / 2} y={cz - (v ? len : th) / 2} width={v ? th : len} height={v ? len : th} rx={th / 2} fill="#fff" stroke="#7A5A36" strokeWidth={.03} /></g>); }))}
    {/* segmentul în lucru la grosimea lui reală și cu același capăt pătrat ca rezultatul: colțul în L / T arată cum va fi */}
    {preview && p.tool === 'mep_run' && <polyline data-mep-draft={mepPts.length} points={[...mepPts, preview.b].map(q => `${q[0]},${q[1]}`).join(' ')} fill="none" stroke={p.mepDraw?.color ?? '#555'} strokeWidth={fs * .22} strokeLinejoin="round" strokeOpacity={.75} pointerEvents="none" />}
    {preview && segment && p.tool !== 'mep_run' && <line x1={preview.a[0]} y1={preview.a[1]} x2={preview.b[0]} y2={preview.b[1]} stroke="#6E8798" strokeWidth={SEGMENT_THICKNESS[p.tool as keyof typeof SEGMENT_THICKNESS]} strokeLinecap="square" strokeOpacity={.55} pointerEvents="none" />}
    {segment && <DrawHud preview={preview} hit={hit} typed={typed} fs={fs} label={m => formatLength(m, units, lang)} invalid={!!typed && typedValue == null} />}
    {typing && rectTool && roomDraft && (() => { const at = roomDraft.b, bad = !!typed && typedValue == null, w = Math.max(fs * 7, fs * .62 * ((typed || t('plan.typeHint')).length + 2));
      return <g className="typed-chip" aria-hidden="true" transform={`translate(${at[0] + fs * .8} ${at[1] + fs * .8})`}>
        <rect width={w} height={fs * 1.9} rx={fs * .45} fill={bad ? '#FCEBEA' : '#fff'} stroke={bad ? '#B3261E' : '#7A5A36'} strokeWidth={fs * .08} />
        <text x={fs * .6} y={fs * 1.3} fontSize={fs} fontFamily="Instrument Sans, Segoe UI, sans-serif" fill={typed ? (bad ? '#B3261E' : '#1A141C') : '#7A6F66'}>{typed || t('plan.typeHint')}</text></g>; })()}
    {/* Zoom pe zonă: dreptunghiul tras; Împarte camera: peretele nou, punctat, la cursor (camerele cu contur propriu nu se pot împărți) */}
    {kbCur && (() => { const b = selectionBox(p.snap, p.catalog, kbCur as Sel), pad = fs * .5; return b && <rect data-kbcursor={kbCur.id} x={b.x0 - pad} y={b.z0 - pad} width={b.x1 - b.x0 + pad * 2} height={b.z1 - b.z0 + pad * 2} rx={pad}
      fill="none" stroke="#1F5FBF" strokeWidth={vb.w / 320} strokeDasharray={`${vb.w / 160} ${vb.w / 240}`} pointerEvents="none" />; })()}
    {marquee && <rect data-marquee="" x={Math.min(marquee.a[0], marquee.b[0])} y={Math.min(marquee.a[1], marquee.b[1])} width={Math.abs(marquee.b[0] - marquee.a[0])} height={Math.abs(marquee.b[1] - marquee.a[1])} fill="#8B6A45" fillOpacity={.08} stroke="#8B6A45" strokeWidth={vb.w / 500} strokeDasharray={`${vb.w / 120} ${vb.w / 200}`} pointerEvents="none" />}
    {zoomDraft && <rect data-zoom-area="" x={Math.min(zoomDraft.a[0], zoomDraft.b[0])} y={Math.min(zoomDraft.a[1], zoomDraft.b[1])} width={Math.abs(zoomDraft.b[0] - zoomDraft.a[0])} height={Math.abs(zoomDraft.b[1] - zoomDraft.a[1])} fill="#6E8798" fillOpacity={.1} stroke="#6E8798" strokeWidth={vb.w / 500} strokeDasharray={`${vb.w / 120} ${vb.w / 200}`} pointerEvents="none" />}
    {p.tool === 'split' && cursor && (() => { const h = splitAt(cursor, splitFlip); if (!h || !canSplitRoom(h.room)) return null; const r = h.room.rect;
      return <line data-split-preview={h.axis} x1={h.axis === 'x' ? h.at : r.x0} y1={h.axis === 'x' ? r.z0 : h.at} x2={h.axis === 'x' ? h.at : r.x1} y2={h.axis === 'x' ? r.z1 : h.at} stroke="#8B6A45" strokeWidth={.03} strokeDasharray=".1 .07" pointerEvents="none" />; })()}
    {roomDraft && <rect x={Math.min(roomDraft.a[0], roomDraft.b[0])} y={Math.min(roomDraft.a[1], roomDraft.b[1])} width={Math.abs(roomDraft.b[0] - roomDraft.a[0])} height={Math.abs(roomDraft.b[1] - roomDraft.a[1])} fill="#6E8798" fillOpacity={.12} stroke="#6E8798" strokeWidth={.02} strokeDasharray=".08 .05" />}
    {!layerOff('tech') && allFixturePoints(p.snap, p.catalog, floor.rooms, finishesOf).map((f, i) => { const r = fs * .45, c = '#8A5A00';
      // simbolurile de pe planul de iluminat: pendul = cerc cu cruce, aplică = semicerc lipit de perete, șină = linie cu spoturi
      if (f.kind === 'track'){ const h = (f.len ?? 2) / 2, [x0, z0, x1, z1] = f.alongX ? [f.x - h, f.z, f.x + h, f.z] : [f.x, f.z - h, f.x, f.z + h], n = Math.max(2, Math.round((f.len ?? 2) / .5));
        return <g key={`fx${i}`} pointerEvents="none"><line x1={x0} y1={z0} x2={x1} y2={z1} stroke={c} strokeWidth={.03} />{Array.from({ length: n }, (_, k) => { const o = (k + .5) / n; return <circle key={k} cx={x0 + (x1 - x0) * o} cy={z0 + (z1 - z0) * o} r={r * .45} fill="#fff" stroke={c} strokeWidth={.015} />; })}</g>; }
      if (f.kind === 'sconce'){ const a = Math.atan2(f.nz, f.nx) * 180 / Math.PI; return <path key={`fx${i}`} pointerEvents="none" transform={`translate(${f.x} ${f.z}) rotate(${a})`} d={`M0 ${-r} A${r} ${r} 0 0 1 0 ${r} Z`} fill="#fff" stroke={c} strokeWidth={.02} />; }
      return <g key={`fx${i}`} pointerEvents="none"><circle cx={f.x} cy={f.z} r={r} fill="#fff" stroke={c} strokeWidth={.02} /><path d={`M${f.x - r * .7} ${f.z - r * .7}L${f.x + r * .7} ${f.z + r * .7}M${f.x - r * .7} ${f.z + r * .7}L${f.x + r * .7} ${f.z - r * .7}`} stroke={c} strokeWidth={.015} /></g>; })}
    {p.showTech && wiringArcs.map((arc, i) => (
      <g key={`warc-${i}`} pointerEvents="none">
        <path
          className="wiring-arc"
          d={`M ${arc.from[0]} ${arc.from[1]} Q ${arc.ctrl[0]} ${arc.ctrl[1]} ${arc.to[0]} ${arc.to[1]}`}
          fill="none"
          stroke="#D97706"
          strokeWidth={fs * 0.12}
          strokeDasharray={`${fs * 0.4} ${fs * 0.2}`}
          strokeOpacity={0.88}
        />
        {arc.circuitLabel && (
          <text
            x={arc.ctrl[0]}
            y={arc.ctrl[1]}
            fontSize={fs * 0.55}
            fill="#B45309"
            stroke="#fff"
            strokeWidth={fs * 0.15}
            paintOrder="stroke"
            textAnchor="middle"
            dominantBaseline="central"
            fontFamily="Instrument Sans, Segoe UI, sans-serif"
            fontWeight={600}
          >
            {arc.circuitLabel}
          </text>
        )}
      </g>
    ))}
    {p.showTech && (p.snap.tech || []).map(tp => { const sym = TECH_SYMBOL[tp.kind], r = fs * (sym.letter.length > 1 ? .62 : .5);
      // caloriferul nou (cu mărimi): dreptunghi subțire pe fața peretelui, cu nervurile lui; punctul vechi de racord rămâne cerc
      const rr = isSizedRadiator(tp) ? radiatorRect(floor, tp) : null;
      if (rr){ const horiz = rr.x1 - rr.x0 >= rr.z1 - rr.z0, n = Math.max(2, Math.round((horiz ? rr.x1 - rr.x0 : rr.z1 - rr.z0) / .1));
        return <g key={tp.id} pointerEvents="none" data-tech="radiator"><title>{t('radiator.title')} ({Math.round((tp.widthM ?? 0) * 100)}×{Math.round((tp.heightM ?? 0) * 100)} cm)</title>
          <rect x={rr.x0} y={rr.z0} width={rr.x1 - rr.x0} height={rr.z1 - rr.z0} fill="#fff" stroke={sym.color} strokeWidth={.015} />
          <path d={Array.from({ length: n - 1 }, (_, k) => { const f = (k + 1) / n; return horiz ? `M${rr.x0 + (rr.x1 - rr.x0) * f} ${rr.z0}V${rr.z1}` : `M${rr.x0} ${rr.z0 + (rr.z1 - rr.z0) * f}H${rr.x1}`; }).join('')} stroke={sym.color} strokeWidth={.006} />
          <text x={(rr.x0 + rr.x1) / 2} y={(rr.z0 + rr.z1) / 2 + (horiz ? -fs * .45 : fs * .2)} textAnchor="middle" fontSize={fs * .55} fontFamily="Instrument Sans, Segoe UI, sans-serif" fill={sym.color} stroke="#FFFDF8" strokeWidth={fs * .12} paintOrder="stroke">{sym.letter} {Math.round((tp.widthM ?? 0) * 100)}</text></g>; }
      return <g key={tp.id} pointerEvents="none"><circle cx={tp.x} cy={tp.z} r={r} fill={sym.color} stroke="#fff" strokeWidth={fs * .08} opacity={.92} />
        <text x={tp.x} y={tp.z + fs * .2} textAnchor="middle" fontSize={fs * (sym.letter.length > 1 ? .5 : .6)} fontFamily="Instrument Sans, Segoe UI, sans-serif" fill="#fff">{sym.letter}</text></g>; })}
    {ruler && (() => { const b = ruler.b || rulerHover || ruler.a, d = measure(ruler.a, b); return (<g pointerEvents="none">
      <line x1={ruler.a[0]} y1={ruler.a[1]} x2={b[0]} y2={b[1]} stroke="#B3261E" strokeWidth={.03} strokeDasharray=".1 .05" />
      <circle cx={ruler.a[0]} cy={ruler.a[1]} r={fs * .3} fill="#B3261E" /><circle cx={b[0]} cy={b[1]} r={fs * .3} fill="#B3261E" />
      <text x={(ruler.a[0] + b[0]) / 2} y={(ruler.a[1] + b[1]) / 2 - fs * .6} textAnchor="middle" fontSize={fs} fontFamily="Instrument Sans, Segoe UI, sans-serif" fill="#B3261E" stroke="#fff" strokeWidth={fs * .25} paintOrder="stroke">{formatLength(d, units, lang)}</text></g>); })()}
    {p.calib && (() => { const a = p.calib.stage === 'enter' ? p.calib.a : calA, b = p.calib.stage === 'enter' ? p.calib.b : (calA && cursor) || undefined; if (!a) return null; return (<g pointerEvents="none">
      {b && <line x1={a[0]} y1={a[1]} x2={b[0]} y2={b[1]} stroke="#7A4A7F" strokeWidth={.03} strokeDasharray=".1 .05" />}
      <circle cx={a[0]} cy={a[1]} r={fs * .3} fill="#7A4A7F" />{b && <circle cx={b[0]} cy={b[1]} r={fs * .3} fill="#7A4A7F" />}</g>); })()}
    {hud && (() => {
      const hx = hud.x, hz = hud.z;
      const bRad = fs * 0.75, bDia = bRad * 2, gap = fs * 0.35;
      const btns: { key: string; label: string; icon: string; title: string; act(): void; danger?: boolean }[] = [];
      if (hud.canRotate) btns.push({ key: 'rot', label: 'R', icon: '⟳', title: t('hud.rotate'), act: () => p.onRotateSelected?.() });
      if (hud.canToggleShape) btns.push({ key: 'shape', label: '◯', icon: '◯', title: t('hud.toggleShape'), act: () => p.onToggleColumnShape?.() });
      if (hud.canDuplicate) btns.push({ key: 'dup', label: '📋', icon: '⧉', title: t('hud.duplicate'), act: () => p.onDuplicateSelected?.() });
      if (hud.kind === 'placement' && p.onFrameOther) btns.push({ key: 'f3d', label: '3D', icon: '3D', title: t('hud.frame3d'), act: () => p.onFrameOther?.(hud.id) });
      if (hud.canAutoFurnish) btns.push({ key: 'auto', label: '⚡', icon: '⚡', title: t('hud.autoFurnish'), act: () => p.onAutoFurnishRoom?.(hud.id) });
      if (hud.canSplit) btns.push({ key: 'split', label: '✂', icon: '✂', title: t('hud.splitRoom'), act: () => p.onSplitRoom?.(hud.id, 'x') });
      if (hud.canDelete) btns.push({ key: 'del', label: '✕', icon: '✕', title: t('hud.delete'), act: () => p.onDeleteSelected?.(), danger: true });
      const tagW = fs * (hud.tag.length * 0.55 + 1.2);
      const totalW = btns.length * (bDia + gap) + (hud.tag ? tagW + gap : 0);
      const startX = hx - totalW / 2;
      return (
        <g className="plan-hud" data-testid="plan-hud" style={{ pointerEvents: 'all' }}>
          <rect
            x={startX - gap}
            y={hz - bRad - fs * 0.25}
            width={totalW + gap}
            height={bDia + fs * 0.5}
            rx={bRad + fs * 0.25}
            fill="#0F172A"
            fillOpacity={0.94}
            stroke="#334155"
            strokeWidth={fs * 0.08}
            filter="drop-shadow(0 4px 10px rgba(0,0,0,0.35))"
          />
          {btns.map((btn, bi) => {
            const bx = startX + bi * (bDia + gap) + bRad;
            return (
              <g
                key={btn.key}
                data-testid={`hud-btn-${btn.key}`}
                style={{ cursor: 'pointer' }}
                onPointerDown={e => { e.stopPropagation(); }}
                onClick={e => { e.stopPropagation(); btn.act(); }}
              >
                <title>{btn.title}</title>
                <circle cx={bx} cy={hz} r={bRad} fill={btn.danger ? '#7F1D1D' : '#1E293B'} stroke={btn.danger ? '#DC2626' : '#475569'} strokeWidth={fs * 0.08} />
                <text x={bx} y={hz + fs * 0.05} textAnchor="middle" dominantBaseline="central" fontSize={fs * 0.85} fill={btn.danger ? '#FCA5A5' : '#F8FAFC'} fontWeight={600} fontFamily="Instrument Sans, Segoe UI, sans-serif" pointerEvents="none">{btn.icon}</text>
              </g>
            );
          })}
          {hud.tag && (() => {
            const tx = startX + btns.length * (bDia + gap);
            return (
              <g key="hud-tag" pointerEvents="none">
                <rect x={tx} y={hz - bRad * 0.75} width={tagW} height={bRad * 1.5} rx={bRad * 0.75} fill="#334155" stroke="#475569" strokeWidth={fs * 0.05} />
                <text x={tx + tagW / 2} y={hz + fs * 0.04} textAnchor="middle" dominantBaseline="central" fontSize={fs * 0.65} fill="#E2E8F0" fontWeight={500} fontFamily="Instrument Sans, Segoe UI, sans-serif">{hud.tag}</text>
              </g>
            );
          })()}
        </g>
      );
    })()}
    {cursor && <text x={vb.x + vb.w - fs * .6} y={vb.y + vb.h - fs * .6} textAnchor="end" fontSize={fs * .8} fontFamily="Instrument Sans, Segoe UI, sans-serif" fill="#5E636B" pointerEvents="none">{units === 'imperial' ? `x ${formatLength(cursor[0], 'imperial', lang)} · z ${formatLength(cursor[1], 'imperial', lang)}` : `x ${cursor[0].toFixed(2)} · z ${cursor[1].toFixed(2)} m`}</text>}
  </svg>
    {textDraft && <input ref={textInput} className="plan-text-input" style={{ position: 'absolute', left: textDraft.left, top: textDraft.top, zIndex: 3 }} aria-label={t(textDraft.extra?.symbol ? 'level.inputAria' : 'text.inputAria')} placeholder={t(textDraft.extra?.symbol ? 'level.placeholder' : 'text.placeholder')}
      maxLength={PLAN_LIMITS.annotation.textMax} value={textVal} onChange={e => setTextVal(e.target.value)}
      // clic în afară păstrează textul tastat (ca Enter); după Enter / Escape focusul revine pe plan, nu pe <body>
      // (textClosed: focus() pe plan declanșează sincron blur-ul inputului, care altfel ar adăuga textul a doua oară)
      onFocus={() => { textClosed.current = false; }}
      onBlur={() => { if (textClosed.current) return; textClosed.current = true; const v = cleanPlanText(textVal); if (v) addNote(textDraft.at, v, textDraft.extra); setTextDraft(null); setLeaderTip(null); }}
      onKeyDown={e => { e.stopPropagation(); if (e.key !== 'Enter' && e.key !== 'Escape') return; e.preventDefault(); textClosed.current = true;
        const v = e.key === 'Enter' ? cleanPlanText(textVal) : ''; if (v) addNote(textDraft.at, v, textDraft.extra); setTextDraft(null); setLeaderTip(null); svg.current?.focus(); }} />}
    {cloudDraft && <input ref={cloudInput} className="plan-text-input" style={{ position: 'absolute', left: cloudDraft.left, top: cloudDraft.top, zIndex: 3 }} aria-label={t('cloud.inputAria')} placeholder={t('cloud.placeholder')}
      maxLength={PLAN_LIMITS.annotation.cloudLabelMax} value={cloudVal} onChange={e => setCloudVal(stripPlanText(e.target.value))}
      // Enter sau clic în afară = norul se adaugă (cu eticheta tastată sau fără); Esc renunță la nor; focusul revine pe plan
      onFocus={() => { cloudClosed.current = false; }} onBlur={() => closeCloud(true)}
      onKeyDown={e => { e.stopPropagation(); if (e.key !== 'Enter' && e.key !== 'Escape') return; e.preventDefault(); closeCloud(e.key === 'Enter'); svg.current?.focus(); }} />}
    {<div className="plan-nudge" role="group" aria-label={t('plan.nudgeAria')}>
      {([['in', '+', () => zoomBy(1 / 1.25)], ['out', '−', () => zoomBy(1.25)], ['fit', '⤢', fitAll], ['left', '◀', () => panBy(-.2, 0)], ['up', '▲', () => panBy(0, -.2)], ['down', '▼', () => panBy(0, .2)], ['right', '▶', () => panBy(.2, 0)]] as const).map(([k, sym, fn]) =>
        <button key={k} type="button" className="btn" aria-label={t(`plan.nudge.${k}`)} title={t(`plan.nudge.${k}`)} onClick={fn}>{sym}</button>)}
    </div>}
    {menu && p.sel && <PlanContextMenu x={menu.x} y={menu.y} label={t('ctx.aria')} onClose={() => { setMenu(null); svg.current?.focus({ preventScroll: true }); }} onPick={runCommand}
      items={(menu.kind === 'placement' && group.length > 1 && p.onArrange ? groupContextCommands(group.length) : contextCommands(menu.kind)).filter(available).map(id => { const c = commandById(id)!; return { id, label: t(c.menuLabel ?? c.label), hint: commandHint(id, { mac }), danger: id === 'delete' }; })} />}
    {dim && <input className="plan-dim-input" autoFocus aria-label={t('plan.dimEdit')} aria-invalid={dim.bad} value={dim.text} style={{ left: dim.x, top: dim.y }}
      onFocus={e => e.currentTarget.select()} onChange={e => setDim({ ...dim, text: e.target.value, bad: false })} onBlur={() => setDim(null)}
      onKeyDown={e => { e.stopPropagation(); if (e.key === 'Enter'){ e.preventDefault(); applyDim(); } else if (e.key === 'Escape'){ e.preventDefault(); setDim(null); svg.current?.focus({ preventScroll: true }); } }} />}</>);
}

const SEGMENT_TOOLS: Tool[] = ['wall', 'beam', 'glass_wall', 'railing', 'mep_run'];
/** Centrul etichetei de cotă a peretelui: la mijloc, decalată spre normala din stânga, în afara grosimii. */
function dimLabelAt(w: Wall, fs: number): [number, number] {
  const L = wallLength(w.a, w.b), nx = -(w.b[1] - w.a[1]) / L, nz = (w.b[0] - w.a[0]) / L, off = w.thickness / 2 + fs * .9;
  return [(w.a[0] + w.b[0]) / 2 + nx * off, (w.a[1] + w.b[1]) / 2 + nz * off];
}
const DIM = '#B8432C', HALO = '#FFFDF8';
/** Cota unghiulară: cele două brațe scurte din vârf, arcul dintre ele și valoarea pe bisectoare, în afara arcului. */
function AngleMark({ d, fs, ink, label, hit }: { d: Pick<AngleAnnotation, 'v' | 'a' | 'b' | 'r'>; fs: number; ink: string; label: string; hit?: Record<string, string> }){
  const g = angleGeometry(d), sw = fs * .06, arc = angleArcPath(d), lb = angleLabelAt(d, fs * .9);
  const arms = g.arms.map(([p, q]) => `M${p[0]} ${p[1]}L${q[0]} ${q[1]}`).join('');
  return (<g {...hit} style={hit ? { cursor: 'move' } : undefined}>
    {hit && <path d={`${arc}${arms}`} fill="none" stroke="transparent" strokeWidth={fs * .8} />}
    <path d={arms} fill="none" stroke={ink} strokeWidth={sw * .7} pointerEvents="none" />
    <path d={arc} fill="none" stroke={ink} strokeWidth={sw} pointerEvents="none" />
    <text x={lb[0]} y={lb[1]} textAnchor="middle" dominantBaseline="central" fontSize={fs * .85} fontFamily="Instrument Sans, Segoe UI, sans-serif" fontWeight={600}
      fill={ink} stroke={HALO} strokeWidth={fs * .2} strokeLinejoin="round" paintOrder="stroke" pointerEvents="none">{label}</text></g>);
}
/** Cota persistentă: linii de extensie de la punctele măsurate, linia de cotă cu tick-uri la 45° și valoarea pe linie. */
function DimMark({ d, fs, ink, label, hit }: { d: Pick<DimAnnotation, 'a' | 'b' | 'offset' | 'mode'>; fs: number; ink: string; label: string; hit?: Record<string, string> }){
  const g = dimGeometry(d), [ux, uz] = g.u, [nx, nz] = g.n, tk = fs * .3, sw = fs * .06, s = Math.sign(d.offset) || 1;
  const tick = (P: [number, number]) => `M${P[0] - (ux + nx) * tk * .7} ${P[1] - (uz + nz) * tk * .7}L${P[0] + (ux + nx) * tk * .7} ${P[1] + (uz + nz) * tk * .7}`;
  const ext = (q: [number, number], P: [number, number]) => `M${q[0] + nx * s * fs * .15} ${q[1] + nz * s * fs * .15}L${P[0] + nx * s * fs * .3} ${P[1] + nz * s * fs * .3}`;
  const m: [number, number] = [g.mid[0] + nx * s * fs * .5, g.mid[1] + nz * s * fs * .5];
  return (<g {...hit} style={hit ? { cursor: 'move' } : undefined}>
    {hit && <path d={`M${g.p1[0]} ${g.p1[1]}L${g.p2[0]} ${g.p2[1]}`} stroke="transparent" strokeWidth={fs * .8} />}
    <path d={`${ext(d.a, g.p1)}${ext(d.b, g.p2)}`} stroke={ink} strokeWidth={sw * .7} pointerEvents="none" />
    <path d={`M${g.p1[0]} ${g.p1[1]}L${g.p2[0]} ${g.p2[1]}${tick(g.p1)}${tick(g.p2)}`} stroke={ink} strokeWidth={sw} pointerEvents="none" />
    <text x={m[0]} y={m[1]} transform={`rotate(${g.rot} ${m[0]} ${m[1]})`} textAnchor="middle" dominantBaseline="central" fontSize={fs * .85} fontFamily="Instrument Sans, Segoe UI, sans-serif" fontWeight={600}
      fill={ink} stroke={HALO} strokeWidth={fs * .2} strokeLinejoin="round" paintOrder="stroke" pointerEvents="none">{label}</text></g>);
}
/** Cota segmentului în lucru, în limbajul planșei: linie de cotă paralelă, decalată, cu tick-uri la 45°, lungimea pe linie
 *  (sau ce se tastează), unghiul lângă capătul liber; la punctul prins de magnet un glif: pătrat = capăt de perete,
 *  cruce = grilă, arc = unghi, cerc gol = liber (Shift). Fără animație. */
function DrawHud({ preview, hit, typed, fs, label, invalid }: { preview: { a: [number, number]; b: [number, number] } | null; hit: SnapResult | null; typed: string; fs: number; label(m: number): string; invalid: boolean }){
  const g = fs * .32, sw = fs * .07, q = hit?.p;
  const glyph = q && (hit!.kind === 'endpoint' ? <rect x={q[0] - g} y={q[1] - g} width={g * 2} height={g * 2} fill="none" stroke={DIM} strokeWidth={sw} />
    : hit!.kind === 'wall' ? <path d={`M${q[0] - g} ${q[1] - g}H${q[0] + g}M${q[0]} ${q[1] - g}V${q[1] + g}`} stroke={DIM} strokeWidth={sw} />
    : hit!.kind === 'grid' ? <path d={`M${q[0] - g} ${q[1]}H${q[0] + g}M${q[0]} ${q[1] - g}V${q[1] + g}`} stroke={DIM} strokeWidth={sw} />
    : hit!.kind === 'angle' ? <path d={`M${q[0] - g} ${q[1] + g * .6}A${g * 1.4} ${g * 1.4} 0 0 1 ${q[0] + g} ${q[1] + g * .6}`} fill="none" stroke={DIM} strokeWidth={sw} />
    : <circle cx={q[0]} cy={q[1]} r={g} fill="none" stroke={DIM} strokeWidth={sw} />);
  if (!preview) return glyph ? <g pointerEvents="none" aria-hidden="true">{glyph}</g> : null;
  const { a, b } = preview, L = Math.hypot(b[0] - a[0], b[1] - a[1]);
  if (L < 1e-3 && !typed) return glyph ? <g pointerEvents="none" aria-hidden="true">{glyph}</g> : null;
  const ux = L > 1e-3 ? (b[0] - a[0]) / L : 1, uz = L > 1e-3 ? (b[1] - a[1]) / L : 0, nx = uz, nz = -ux, off = fs * 1.4, ext = fs * .35, tk = fs * .3;
  const A: [number, number] = [a[0] + nx * off, a[1] + nz * off], B: [number, number] = [b[0] + nx * off, b[1] + nz * off];
  let rot = Math.atan2(uz, ux) * 180 / Math.PI; if (rot > 90) rot -= 180; if (rot <= -90) rot += 180;
  const mid: [number, number] = [(A[0] + B[0]) / 2 + nx * fs * .45, (A[1] + B[1]) / 2 + nz * fs * .45];
  const tick = (P: [number, number]) => `M${P[0] - (ux + nx) * tk * .7} ${P[1] - (uz + nz) * tk * .7}L${P[0] + (ux + nx) * tk * .7} ${P[1] + (uz + nz) * tk * .7}`;
  const text = typed ? `${typed}▏` : label(L), ang = hit?.angleDeg;
  return (<g pointerEvents="none" aria-hidden="true">
    <path d={`M${a[0] + nx * fs * .2} ${a[1] + nz * fs * .2}L${A[0] + nx * ext} ${A[1] + nz * ext}M${b[0] + nx * fs * .2} ${b[1] + nz * fs * .2}L${B[0] + nx * ext} ${B[1] + nz * ext}`} stroke={DIM} strokeWidth={sw * .7} />
    <path d={`M${A[0]} ${A[1]}L${B[0]} ${B[1]}${tick(A)}${tick(B)}`} stroke={DIM} strokeWidth={sw} />
    <text x={mid[0]} y={mid[1]} transform={`rotate(${rot} ${mid[0]} ${mid[1]})`} textAnchor="middle" dominantBaseline="central" fontSize={fs} fontFamily="Instrument Sans, Segoe UI, sans-serif" fontWeight={600}
      fill={DIM} stroke={HALO} strokeWidth={fs * .22} strokeLinejoin="round" paintOrder="stroke" textDecoration={invalid ? 'line-through' : undefined}>{text}</text>
    {ang != null && L > 1e-3 && <text x={b[0] - nx * fs * 1.1 + ux * fs * .6} y={b[1] - nz * fs * 1.1 + uz * fs * .6} textAnchor="middle" dominantBaseline="central" fontSize={fs * .72} fontFamily="Instrument Sans, Segoe UI, sans-serif"
      fill="#5E636B" stroke={HALO} strokeWidth={fs * .18} strokeLinejoin="round" paintOrder="stroke">{`${Math.round(ang * 10) / 10}°`}</text>}
    {glyph}</g>);
}
