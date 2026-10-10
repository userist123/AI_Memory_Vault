// Șabloane de pornire, independente de piață. Dimensiunile sunt tipice și orientative, nu copiate dintr-o clădire reală.
import demoFloor from '../data/floor.v1.json';
import studio from '../data/templates/studio.json';
import oneBedroom from '../data/templates/one-bedroom.json';
import twoBedroom from '../data/templates/two-bedroom.json';
import threeBedroom from '../data/templates/three-bedroom.json';
import type { Floor } from './types';

export interface Localized { ro: string; en: string }
export interface Template { id: string; name: Localized; description: Localized; floor: Floor }
export interface TemplateSummary { id: string; name: Localized; description: Localized; rooms: number; area: number }

export const TEMPLATES: Template[] = [
  { id: 'demo', name: { ro: 'Apartament demo', en: 'Demo apartment' },
    description: { ro: 'Apartament cu un dormitor, folosit ca exemplu. Dimensiuni tipice, orientative.', en: 'One-bedroom apartment used as an example. Typical, approximate dimensions.' }, floor: demoFloor as Floor },
  { id: 'studio', name: { ro: 'Garsonieră / studio', en: 'Studio' },
    description: { ro: 'Cameră de zi cu dormit, bucătărie, baie și hol. Dimensiuni tipice, orientative.', en: 'Living and sleeping room, kitchen, bathroom and hall. Typical, approximate dimensions.' }, floor: studio as Floor },
  { id: 'one-bedroom', name: { ro: 'Apartament cu 2 camere', en: 'One-bedroom apartment' },
    description: { ro: 'Living, dormitor, bucătărie, baie și hol; toate camerele se deschid din hol. Dimensiuni tipice, orientative.', en: 'Living room, bedroom, kitchen, bathroom and hall; every room opens from the hall. Typical, approximate dimensions.' }, floor: oneBedroom as Floor },
  { id: 'two-bedroom', name: { ro: 'Apartament cu 3 camere', en: 'Two-bedroom apartment' },
    description: { ro: 'Living, două dormitoare, bucătărie, baie și hol-coridor. Dimensiuni tipice, orientative.', en: 'Living room, two bedrooms, kitchen, bathroom and a corridor hall. Typical, approximate dimensions.' }, floor: twoBedroom as Floor },
  { id: 'three-bedroom', name: { ro: 'Apartament familial cu 4 camere', en: 'Three-bedroom family apartment' },
    description: { ro: 'Living, trei dormitoare, bucătărie, baie, WC separat și hol. Dimensiuni tipice, orientative.', en: 'Living room, three bedrooms, kitchen, bathroom, separate WC and hall. Typical, approximate dimensions.' }, floor: threeBedroom as Floor },
];
export const templateIds = (): string[] => TEMPLATES.map(t => t.id);
export const getTemplate = (id: string): Template | undefined => TEMPLATES.find(t => t.id === id);
export const floorArea = (f: Floor): number => Math.round(f.rooms.reduce((s, r) => s + (r.rect.x1 - r.rect.x0) * (r.rect.z1 - r.rect.z0), 0) * 100) / 100;
export function templateSummary(): TemplateSummary[] {
  return TEMPLATES.map(t => ({ id: t.id, name: t.name, description: t.description, rooms: t.floor.rooms.length, area: floorArea(t.floor) }));
}

/** Numele camerelor după tip, pe limbi; folosite când un proiect nou pornește dintr-un șablon. */
export const ROOM_NAMES: Record<'ro' | 'en', Record<string, string>> = {
  ro: { hol: 'Hol', baie: 'Baie', bucatarie: 'Bucătărie', living: 'Living', dormitor: 'Dormitor' },
  en: { hol: 'Hall', baie: 'Bathroom', bucatarie: 'Kitchen', living: 'Living room', dormitor: 'Bedroom' },
};
/** Traduce numele implicite ale camerelor (de ex. „Dormitor 2” → „Bedroom 2”); numele personalizate (de ex. „WC”) rămân. */
export function localizeRoomNames(floor: Floor, lang: 'ro' | 'en'): Floor {
  if (lang === 'ro') return floor;
  return { ...floor, rooms: floor.rooms.map(r => { const base = ROOM_NAMES.ro[r.type], m = base ? new RegExp(`^${base}( \\d+)?$`).exec(r.name) : null;
    return m ? { ...r, name: ROOM_NAMES[lang][r.type] + (m[1] ?? '') } : r; }) };
}
