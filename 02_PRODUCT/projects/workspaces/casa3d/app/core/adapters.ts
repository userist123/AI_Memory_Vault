// Registrul de adaptoare (docs/bim/GHID_BIM.md §4.2 și faza 7; contractul e descris în docs/bim/ADAPTERS.md).
// Un adaptor transformă proiectul într-un format extern (export) sau un fișier extern în candidați (import). Nucleul (Snapshot,
// elementIndex, proveniență, versiuni, validare) rămâne neschimbat; adaptoarele nu scriu direct în proiect: importul întoarce
// candidați, iar aplicarea trece prin validarea serverului (checkSnapshot). Fiecare adaptor își declară implementarea și licența,
// ce păstrează (id-uri, discipline, proveniență) și ce pierde, ca utilizatorul să nu presupună o fidelitate pe care nu o are.
import type { Catalog, Snapshot } from './types';
import { planToDxf } from './dxf';
import { snapshotToIfc } from './ifc-export';
import { parseDxf, wallCandidates, type WallCandidate, type DxfUnit } from './dxf-import';
import { runCoordination } from './coordination-review';
import { coordinationReport } from './issues';

export type AdapterCategory = 'cad' | 'bim' | 'model3d' | 'render' | 'report';
export interface AdapterInfo {
  id: string; direction: 'export' | 'import'; category: AdapterCategory; format: string; extension: string; status: 'stable' | 'experimental';
  /** cine implementează formatul și sub ce licență (dependențele externe trebuie verificate înainte de adăugare, ghidul §4.2) */
  implementation: string;
  preserves: string[]; loses: string[];
  /** unde rulează: în browser, pe server, sau în aplicația externă (Blender) */
  runsIn: 'browser' | 'server' | 'external';
}
export interface ExportAdapter extends AdapterInfo { direction: 'export'; run(s: Snapshot, cat: Catalog, o: { lang: 'ro' | 'en'; name: string; timestamp: string }): string }
export interface ImportAdapter extends AdapterInfo { direction: 'import'; run(text: string, o: { unit?: DxfUnit; layers?: string[] }): WallCandidate[] }
export type Adapter = ExportAdapter | ImportAdapter;

const OWN = 'cod propriu Casa3D, fără dependențe externe';
export const ADAPTERS: Adapter[] = [
  { id: 'dxf-export', direction: 'export', category: 'cad', format: 'DXF R12 ASCII', extension: 'dxf', status: 'stable', implementation: OWN, runsIn: 'server',
    preserves: ['geometria în plan (mm)', 'straturi pe categorie și pe sistem de instalații', 'niveluri alăturate'], loses: ['cotele pe verticală', 'id-urile elementelor (doar traseele au eticheta cu id)', 'proprietățile'],
    run: (s, cat, o) => planToDxf(s, cat, { lang: o.lang }) },
  { id: 'ifc4-export', direction: 'export', category: 'bim', format: 'IFC4 (STEP)', extension: 'ifc', status: 'experimental', implementation: OWN, runsIn: 'server',
    preserves: ['ierarhia proiect–teren–clădire–niveluri', 'GlobalId stabil derivat din id', 'disciplina și proveniența în Casa3D_Element', 'golurile din pereți', 'diametrul declarat al traseelor'],
    loses: ['scările, nișele, obiectele de pe perete', 'finisajele și materialele', 'conectivitatea traseelor (IfcDistributionPort)'],
    run: (s, cat, o) => snapshotToIfc(s, cat, { fileName: `${o.name}.ifc`, timestamp: o.timestamp }).text },
  { id: 'coordination-report', direction: 'export', category: 'report', format: 'Markdown', extension: 'md', status: 'experimental', implementation: OWN, runsIn: 'browser',
    preserves: ['id-ul stabil al constatărilor', 'regulile rulate și nerulate', 'statusul și justificarea'], loses: ['vederea 3D a constatării'],
    run: (s, cat, o) => coordinationReport(runCoordination(s, cat), s.issues, { project: o.name, generatedAt: o.timestamp }) },
  { id: 'dxf-import', direction: 'import', category: 'cad', format: 'DXF ASCII (LINE, LWPOLYLINE, POLYLINE)', extension: 'dxf', status: 'experimental', implementation: OWN, runsIn: 'browser',
    preserves: ['unitatea declarată ($INSUNITS)', 'stratul sursă', 'grosimea măsurată din perechi de linii', 'amprenta SHA-256 a fișierului'],
    loses: ['arcele, blocurile, textele, cotele', 'golurile (se adaugă manual)', 'DWG (format proprietar, nesuportat)'],
    run: (text, o) => wallCandidates(parseDxf(text), o) },
];
/** Adaptoarele care rulează în afara aplicației (fără funcție `run` aici): Blender (scriptul tools/blender/casa3d_render.py) și GLB din vederea 3D. */
export const EXTERNAL_ADAPTERS: AdapterInfo[] = [
  { id: 'blender-scene', direction: 'export', category: 'render', format: 'casa3d-scene JSON → Blender 4.2+ (bpy)', extension: 'casa3d.json', status: 'stable',
    implementation: 'cod propriu (vederea 3D + scriptul Python); Blender e GPL, rulat separat de utilizator', runsIn: 'external',
    preserves: ['id-ul elementului pe obiect (c3d_id)', 'colecții pe disciplină', 'materialele și lumina'], loses: ['proprietățile tehnice'] },
  { id: 'glb-export', direction: 'export', category: 'model3d', format: 'glTF binar (GLB)', extension: 'glb', status: 'stable', implementation: 'three.js GLTFExporter (MIT)', runsIn: 'browser',
    preserves: ['geometria și materialele PBR'], loses: ['id-urile și disciplinele', 'proprietățile'] },
];
export function adapter(id: string): Adapter | undefined { return ADAPTERS.find(a => a.id === id); }
