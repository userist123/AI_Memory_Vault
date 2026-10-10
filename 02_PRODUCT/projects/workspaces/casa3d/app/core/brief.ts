// Design Brief structurat. Toate câmpurile au valori finite și controlate (fără text liber în logica de decizie).
export const STYLES = { modern: 'Modern', scandinav: 'Scandinav', natural: 'Natural / Japandi', clasic: 'Clasic' } as const;
export const PRIORITIES = { depozitare: 'Mult spațiu de depozitare', spatiu: 'Spațiu liber de circulație', birou: 'Birou acasă', musafiri: 'Primesc des musafiri' } as const;
export type Style = keyof typeof STYLES; export type Priority = keyof typeof PRIORITIES;
export interface DesignBrief { propertyType: 'apartament' | 'casa' | 'studio'; occupants: number; children: boolean; pets: boolean; accessibility: boolean;
  style: Style; colors: string; priorities: Priority[]; budget: number | null; suppliers: ('IKEA' | 'Dedeman')[]; avoid: string; notes: string }
export const DEFAULT_BRIEF: DesignBrief = { propertyType: 'apartament', occupants: 2, children: false, pets: false, accessibility: false, style: 'scandinav', colors: '', priorities: [], budget: null, suppliers: ['IKEA', 'Dedeman'], avoid: '', notes: '' };
export function checkBrief(b: any): DesignBrief {
  const err = (m: string) => { throw Object.assign(new Error(m), { status: 400 }); };
  if (!b || typeof b !== 'object') err('Brief invalid.');
  const out: DesignBrief = { ...DEFAULT_BRIEF };
  if (['apartament', 'casa', 'studio'].includes(b.propertyType)) out.propertyType = b.propertyType;
  out.occupants = Math.max(1, Math.min(12, Math.round(Number(b.occupants) || 1)));
  out.children = !!b.children; out.pets = !!b.pets; out.accessibility = !!b.accessibility;
  if (!(b.style in STYLES)) err('Stil necunoscut.'); out.style = b.style;
  out.priorities = Array.isArray(b.priorities) ? b.priorities.filter((p: string) => p in PRIORITIES) : [];
  out.budget = b.budget == null || b.budget === '' ? null : Math.max(0, Number(b.budget) || 0) || null;
  out.suppliers = Array.isArray(b.suppliers) ? b.suppliers.filter((s: string) => s === 'IKEA' || s === 'Dedeman') : [];
  if (!out.suppliers.length) err('Alege cel puțin un magazin.');
  const clean = (v: unknown, n: number) => String(v ?? '').replace(/[\u0000-\u001f<>]/g, ' ').slice(0, n);
  out.colors = clean(b.colors, 120); out.avoid = clean(b.avoid, 200); out.notes = clean(b.notes, 600);
  return out;
}
