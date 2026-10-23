// Grupele uneltelor din bara din stânga (components/ToolRail.tsx): date și ordinea locurilor, fără React (tests/plan-tools.test.ts).
import type { Tool } from '@/components/PlanView';

export const TOOL_GROUPS: { id: 'openings' | 'structure' | 'notes'; tools: Tool[] }[] = [
  { id: 'openings', tools: ['door', 'window', 'passage', 'niche'] },
  { id: 'structure', tools: ['column', 'shaft', 'beam', 'glass_wall'] },
  { id: 'notes', tools: ['measure', 'dim', 'text'] },
];
/** Peste câte unelte se grupează (pe ecran lat); sub prag bara rămâne plată, ca înainte. */
export const GROUP_OVER = 8;
type Slot = { tool: Tool } | { group: typeof TOOL_GROUPS[number]['id']; tools: Tool[] };
/** Ordinea din bară: uneltele etapei, cu membrii unei grupe (cel puțin doi în etapă) strânși în locul primului dintre ei. */
export function railSlots(tools: Tool[], grouped: boolean): Slot[] {
  if (!grouped || tools.length <= GROUP_OVER) return tools.map(tool => ({ tool }));
  const out: Slot[] = [], seen = new Set<string>();
  for (const tk of tools){ const g = TOOL_GROUPS.find(x => x.tools.includes(tk)), members = g ? g.tools.filter(x => tools.includes(x)) : [];
    if (!g || members.length < 2){ out.push({ tool: tk }); continue; }
    if (!seen.has(g.id)){ seen.add(g.id); out.push({ group: g.id, tools: members }); } }
  return out;
}
