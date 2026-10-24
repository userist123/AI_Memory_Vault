// Grupele uneltelor din bara din stânga (components/ToolRail.tsx): date și ordinea locurilor, fără React (tests/plan-tools.test.ts).
import type { Tool } from '@/components/PlanView';

export const TOOL_GROUPS: { id: 'openings' | 'structure' | 'notes' | 'decor'; tools: Tool[] }[] = [
  { id: 'openings', tools: ['door', 'window', 'passage', 'niche'] },
  // „Demolare” (starea peretelui) stă cu structura: nu adaugă un loc nou în bara Releveului
  { id: 'structure', tools: ['column', 'shaft', 'beam', 'glass_wall', 'demolish'] },
  { id: 'notes', tools: ['measure', 'dim', 'text'] },
  // decorul (Finisaje, Concept, Mobilier): placarea pe porțiune, obiectele de pe perete și zona de pardoseală
  { id: 'decor', tools: ['clad', 'wall_item', 'floor_zone'] },
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
  const many = tools.length > GROUP_OVER, out: Slot[] = [], seen = new Set<string>();
  for (const tk of tools){ const g = TOOL_GROUPS.find(x => x.tools.includes(tk)), members = g ? g.tools.filter(x => tools.includes(x)) : [];
    if (!g || members.length < (many ? 2 : GROUP_MIN_FOLD)){ out.push({ tool: tk }); continue; }
    if (!seen.has(g.id)){ seen.add(g.id); out.push({ group: g.id, tools: members }); } }
  return out;
}
