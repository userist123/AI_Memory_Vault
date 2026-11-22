// Straturile planului 2D (ca în programele CAD): categorii întregi de elemente pe care designerul le ascunde ca să lucreze
// curat. Pereții, golurile și conturul camerelor nu sunt straturi: rămân mereu vizibile. Starea e a utilizatorului, per
// proiect, în localStorage; planșele tipărite și exporturile (DXF, IFC) nu o citesc niciodată: documentul rămâne complet.
// Regula: stratul se vede doar dacă îl lasă și etapa, și utilizatorul. Utilizatorul poate ascunde peste regula etapei,
// dar nu poate arăta ce ascunde etapa.

export const LAYER_IDS = ['furniture', 'annotations', 'tech', 'mep', 'finishes', 'wallItems', 'underlay', 'roomLabels'] as const;
export type LayerId = typeof LAYER_IDS[number];
/** Alegerea utilizatorului: true = vrea stratul afișat. */
export type LayerState = Record<LayerId, boolean>;

export const LAYERS_VERSION = 1;
export const layersKey = (projectId: string): string => `casa3d.layers.${projectId}`;
export const defaultLayers = (): LayerState => ({ furniture: true, annotations: true, tech: true, mep: true, finishes: true, wallItems: true, underlay: true, roomLabels: true });
export const isLayerId = (x: unknown): x is LayerId => typeof x === 'string' && (LAYER_IDS as readonly string[]).includes(x);

/** Regula etapei (ce se vedea și înainte de panoul de straturi): obiectele de pe perete nu apar la Releveu. */
export function stageAllows(layer: LayerId, stage: string): boolean {
  return layer === 'wallItems' ? stage !== 'survey' : true;
}
/** Marcajele de finisaj (benzile de placare cu codul lor, covoarele) se desenează doar în etapele Finisaje și Documente;
 *  zonele de pardoseală și tavanele false, din același strat, se văd în toate etapele. */
export const finishMarksInStage = (stage: string): boolean => stage === 'finishes' || stage === 'docs';

/** Stratul se vede dacă îl lasă etapa și utilizatorul nu l-a ascuns. */
export function visible(layer: LayerId, stage: string, user: Partial<LayerState> | null | undefined): boolean {
  return stageAllows(layer, stage) && user?.[layer] !== false;
}

/** Citește starea salvată; orice abatere (JSON stricat, altă versiune, tipuri greșite, straturi necunoscute) se ignoră:
 *  ce nu se poate citi rămâne vizibil. Nu aruncă. */
export function parseLayers(raw: string | null | undefined): LayerState {
  const out = defaultLayers();
  if (!raw) return out;
  let o: unknown;
  try { o = JSON.parse(raw); } catch { return out; }
  if (typeof o !== 'object' || o === null || (o as { v?: unknown }).v !== LAYERS_VERSION) return out;
  const hidden = (o as { hidden?: unknown }).hidden;
  if (!Array.isArray(hidden)) return out;
  for (const id of hidden) if (isLayerId(id)) out[id] = false;
  return out;
}
/** Se salvează doar straturile ascunse: un strat adăugat mai târziu pornește vizibil. */
export const serializeLayers = (s: Partial<LayerState>): string => JSON.stringify({ v: LAYERS_VERSION, hidden: LAYER_IDS.filter(id => s[id] === false) });

export const toggleLayer = (s: LayerState, id: LayerId): LayerState => ({ ...s, [id]: !s[id] });
export const hiddenLayers = (s: Partial<LayerState> | null | undefined): LayerId[] => LAYER_IDS.filter(id => s?.[id] === false);

/** Stratul din care face parte un element selectabil din plan; null = mereu vizibil (pereți, goluri, camere, structură). */
export function layerOfKind(kind: string): LayerId | null {
  switch (kind){
    case 'placement': return 'furniture';
    case 'annotation': return 'annotations';
    case 'wall_item': return 'wallItems';
    case 'floor_zone': case 'ceiling_zone': return 'finishes';
    case 'mep_run': return 'mep';
    default: return null;
  }
}
/** Elementul poate fi ales din plan (clic, Tab, dreptunghi de selecție)? Cele din straturile ascunse de utilizator nu. */
export const pickable = (kind: string, user: Partial<LayerState> | null | undefined): boolean => { const l = layerOfKind(kind); return !l || user?.[l] !== false; };

/** Stratul în care desenează o unealtă: când omul o alege cu stratul ascuns, editorul îl arată la loc (altfel ar desena orb). */
export function layerOfTool(tool: string): LayerId | null {
  switch (tool){
    case 'dim': case 'text': case 'leader': case 'level_mark': case 'angle_dim': case 'rev_cloud': return 'annotations';
    case 'tech': return 'tech';
    case 'mep_run': return 'mep';
    case 'clad': case 'floor_zone': case 'ceiling_zone': case 'rug': return 'finishes';
    case 'wall_item': return 'wallItems';
    default: return null;
  }
}
