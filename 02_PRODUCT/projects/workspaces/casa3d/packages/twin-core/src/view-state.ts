// ViewerState: one renderer-neutral description of what the 2D plan and the 3D viewer show.
// Both renderers read it; neither owns it. Pure functions, serialisable, deterministic.
import type { Twin, Placement } from './twin.js';
import type { Issue } from './engine.js';

export type ViewMode = 'plan' | '3d';

export interface Camera3D { yaw: number; pitch: number; distance: number; target: { x: number; y: number; z: number } }

export interface ViewerState {
  mode: ViewMode;
  /** Current (persisted) twin fingerprint the view is showing. */
  twinFingerprint: string;
  /** Proposal shown as an overlay; absent when no preview is active. */
  previewProposalId?: string;
  selectedIds: string[];
  hoveredId?: string;
  /** Issues to highlight, keyed by element id. */
  highlights: Record<string, 'ERROR' | 'WARNING'>;
  plan: { zoom: number; pan: { x: number; y: number }; showGrid: boolean; showDimensions: boolean };
  camera: Camera3D;
  /** Touch/joystick input is translated to camera moves; the state only records the result. */
  input: { touch: boolean };
}

export const DEFAULT_CAMERA: Camera3D = { yaw: 45, pitch: 35, distance: 8, target: { x: 0, y: 0, z: 0 } };

export function initialViewerState(twinFingerprint: string): ViewerState {
  return {
    mode: 'plan', twinFingerprint, selectedIds: [], highlights: {},
    plan: { zoom: 1, pan: { x: 0, y: 0 }, showGrid: true, showDimensions: true },
    camera: { ...DEFAULT_CAMERA, target: { ...DEFAULT_CAMERA.target } },
    input: { touch: false },
  };
}

export type ViewerAction =
  | { type: 'setMode'; mode: ViewMode }
  | { type: 'select'; ids: string[]; additive?: boolean }
  | { type: 'hover'; id?: string }
  | { type: 'preview'; proposalId?: string }
  | { type: 'twinChanged'; fingerprint: string }
  | { type: 'highlight'; issues: Issue[] }
  | { type: 'zoom'; factor: number }
  | { type: 'pan'; dx: number; dy: number }
  | { type: 'orbit'; dYaw: number; dPitch: number }
  | { type: 'dolly'; factor: number }
  | { type: 'moveTarget'; dx: number; dy: number }
  | { type: 'joystick'; axes: { x: number; y: number }; dt: number }
  | { type: 'toggle'; key: 'showGrid' | 'showDimensions' }
  | { type: 'resetCamera' };

const clamp = (v: number, lo: number, hi: number): number => Math.min(hi, Math.max(lo, v));
const r3 = (v: number): number => Math.round(v * 1000) / 1000;

export const ZOOM_RANGE = { min: 0.25, max: 8 };
export const DISTANCE_RANGE = { min: 1.5, max: 40 };
export const JOYSTICK_SPEED = 2.5; // metres per second at full deflection

export function reduceViewer(state: ViewerState, action: ViewerAction): ViewerState {
  switch (action.type) {
    case 'setMode': return state.mode === action.mode ? state : { ...state, mode: action.mode };
    case 'select': {
      const ids = action.additive ? Array.from(new Set([...state.selectedIds, ...action.ids])) : [...action.ids];
      return { ...state, selectedIds: ids.sort() };
    }
    case 'hover': { const s = { ...state }; if (action.id === undefined) delete s.hoveredId; else s.hoveredId = action.id; return s; }
    case 'preview': { const s = { ...state }; if (action.proposalId === undefined) delete s.previewProposalId; else s.previewProposalId = action.proposalId; return s; }
    case 'twinChanged': {
      // A new twin invalidates any preview overlay and selections of elements that may no longer exist.
      const s: ViewerState = { ...state, twinFingerprint: action.fingerprint, selectedIds: [], highlights: {} };
      delete s.previewProposalId; delete s.hoveredId;
      return s;
    }
    case 'highlight': {
      const highlights: Record<string, 'ERROR' | 'WARNING'> = {};
      for (const i of action.issues) { const id = i.refs[0]; if (!id) continue; if (highlights[id] !== 'ERROR') highlights[id] = i.severity; }
      return { ...state, highlights };
    }
    case 'zoom': {
      const zoom = r3(clamp(state.plan.zoom * action.factor, ZOOM_RANGE.min, ZOOM_RANGE.max));
      return { ...state, plan: { ...state.plan, zoom } };
    }
    case 'pan': return { ...state, plan: { ...state.plan, pan: { x: r3(state.plan.pan.x + action.dx), y: r3(state.plan.pan.y + action.dy) } } };
    case 'orbit': return { ...state, camera: { ...state.camera, yaw: r3(((state.camera.yaw + action.dYaw) % 360 + 360) % 360), pitch: r3(clamp(state.camera.pitch + action.dPitch, 5, 89)) } };
    case 'dolly': return { ...state, camera: { ...state.camera, distance: r3(clamp(state.camera.distance * action.factor, DISTANCE_RANGE.min, DISTANCE_RANGE.max)) } };
    case 'moveTarget': return { ...state, camera: { ...state.camera, target: { ...state.camera.target, x: r3(state.camera.target.x + action.dx), y: r3(state.camera.target.y + action.dy) } } };
    case 'joystick': {
      // Walk the camera target in the camera's horizontal frame: y axis forward, x axis strafe. Deterministic.
      const ax = clamp(action.axes.x, -1, 1), ay = clamp(action.axes.y, -1, 1), dt = clamp(action.dt, 0, 0.5);
      const yaw = (state.camera.yaw * Math.PI) / 180;
      const fx = Math.sin(yaw), fy = Math.cos(yaw);
      const dx = (fx * ay + fy * ax) * JOYSTICK_SPEED * dt, dy = (fy * ay - fx * ax) * JOYSTICK_SPEED * dt;
      return { ...state, input: { touch: true }, camera: { ...state.camera, target: { ...state.camera.target, x: r3(state.camera.target.x + dx), y: r3(state.camera.target.y + dy) } } };
    }
    case 'toggle': return { ...state, plan: { ...state.plan, [action.key]: !state.plan[action.key] } };
    case 'resetCamera': return { ...state, camera: { ...DEFAULT_CAMERA, target: { ...DEFAULT_CAMERA.target } } };
  }
}

/** What a renderer needs for one placement: the footprint in plan units and the box in 3D, from the same source. */
export interface RenderItem { id: string; catalogId: string; x: number; y: number; w: number; d: number; h: number; rotation: number; selected: boolean; highlight?: 'ERROR' | 'WARNING'; preview: boolean }

export function renderItems(twin: Twin, state: ViewerState, previewTwin?: Twin): RenderItem[] {
  const base = new Map(twin.placements.map(p => [p.id, p]));
  const items: RenderItem[] = [];
  const push = (p: Placement, preview: boolean) => {
    const it: RenderItem = { id: p.id, catalogId: p.catalogId, x: p.x, y: p.y, w: p.w, d: p.d, h: p.h, rotation: p.rotation, selected: state.selectedIds.includes(p.id), preview };
    const h = state.highlights[p.id]; if (h) it.highlight = h;
    items.push(it);
  };
  if (previewTwin && state.previewProposalId) {
    for (const p of previewTwin.placements) push(p, !base.has(p.id) || JSON.stringify(base.get(p.id)) !== JSON.stringify(p));
  } else {
    for (const p of twin.placements) push(p, false);
  }
  return items.sort((a, b) => (a.id < b.id ? -1 : a.id > b.id ? 1 : 0));
}
