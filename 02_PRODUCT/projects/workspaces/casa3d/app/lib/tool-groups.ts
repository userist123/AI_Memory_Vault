// Grupele uneltelor din bara din stânga (components/ToolRail.tsx): date și ordinea locurilor, fără React (tests/plan-tools.test.ts).
import type { Tool } from '@/components/PlanView';

/** `minFold`: de la câți membri în etapă se strânge grupa sub pragul GROUP_OVER (lipsă = GROUP_MIN_FOLD). */
export const TOOL_GROUPS: { id: 'view' | 'openings' | 'structure' | 'notes' | 'decor'; tools: Tool[]; minFold?: number; onlyCrowded?: boolean }[] = [
  // navigarea (Mână, Zoom pe zonă) e în toate etapele cu plan: un singur loc, chiar dacă are doar doi membri
  { id: 'view', tools: ['pan', 'zoom_area'], minFold: 2 },
  { id: 'openings', tools: ['door', 'window', 'passage', 'niche'] },
  // „Demolare” (starea peretelui) stă cu structura: nu adaugă un loc nou în bara Releveului
  // balustrada (lotul 4) intră tot aici: Releveul rămâne cu 7 locuri
  { id: 'structure', tools: ['column', 'shaft', 'beam', 'glass_wall', 'railing', 'demolish', 'split'] },
  // săgeata cu notă și cota de nivel sunt tot adnotări: intră la coadă, fără un loc nou în bară
  // onlyCrowded: adnotările se strâng doar în etapele aglomerate (Releveu); în Planșe, unde sunt uneltele principale, rămân butoane separate
  { id: 'notes', tools: ['measure', 'dim', 'text', 'leader', 'level_mark'], onlyCrowded: true },
  // decorul (Finisaje, Concept, Mobilier): placarea pe porțiune, obiectele de pe perete și zona de pardoseală
  // tavanul fals pe zonă (lotul 4) intră în aceeași grupă: Finisaje rămâne cu 3 locuri
  { id: 'decor', tools: ['clad', 'wall_item', 'floor_zone', 'ceiling_zone', 'rug', 'style_copy'] },
];
/** Peste câte unelte se grupează (pe ecran lat); sub prag bara rămâne plată, ca înainte. */
export const GROUP_OVER = 8;
/** O grupă cu cel puțin atâția membri în etapă se strânge și sub prag: o unealtă nouă intră în grupa ei fără un buton în plus
 *  (Finisaje: Placare, Pe perete, Zonă pardoseală = un singur loc). Grupele de doi rămân desfăcute, ca înainte. */
export const GROUP_MIN_FOLD = 3;
/** O etapă cu cel mult atâtea unelte rămâne plată, ca înainte de grupe (Planșe: Selectează, Măsoară, Cotă, Text = 4 butoane);
 *  plierea grupelor de trei începe de la o unealtă în plus (Finisaje: 5 unelte → 3 locuri). */
export const FLAT_MAX = 4;
type Slot = { tool: Tool } | { group: typeof TOOL_GROUPS[number]['id']; tools: Tool[] };
/** Ordinea din bară: uneltele etapei, cu membrii unei grupe (cel puțin doi în etapă) strânși în locul primului dintre ei. */
export function railSlots(tools: Tool[], grouped: boolean): Slot[] {
  if (!grouped || tools.length <= FLAT_MAX) return tools.map(tool => ({ tool }));
  // grupele cu `minFold` (navigarea, prezentă în toate etapele) nu se numără la pragul FLAT_MAX: Planșe rămâne cu
  // Măsoară, Cotă, Text ca butoane separate și după ce a primit Mâna și Zoom pe zonă (care ocupă un singur loc)
  const flat = tools.filter(tk => TOOL_GROUPS.find(x => x.tools.includes(tk))?.minFold === undefined).length <= FLAT_MAX;
  const many = tools.length > GROUP_OVER, out: Slot[] = [], seen = new Set<string>();
  for (const tk of tools){ const g = TOOL_GROUPS.find(x => x.tools.includes(tk)), members = g ? g.tools.filter(x => tools.includes(x)) : [];
    if (!g || (g.onlyCrowded && !many) || members.length < (g.minFold !== undefined ? (many ? Math.min(2, g.minFold) : g.minFold) : flat ? Infinity : many ? 2 : GROUP_MIN_FOLD)){ out.push({ tool: tk }); continue; }
    if (!seen.has(g.id)){ seen.add(g.id); out.push({ group: g.id, tools: members }); } }
  return out;
}
