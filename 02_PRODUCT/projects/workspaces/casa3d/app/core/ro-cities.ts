// Reședințele de județ din România și București, cu coordonatele centrului (date publice, rotunjite la 0,01° ≈ 1 km).
// Folosite doar ca scurtătură pentru declararea localității casei (core/north.ts SiteLocation): în proiect se păstrează
// numai coordonatele, nu numele. Ilfovul nu apare separat: reședința lui e București. Bucureștiul e primul, restul alfabetic.
import type { SiteLocation } from './north';
export interface RoCity extends SiteLocation { name: string }
export const RO_CITIES: readonly RoCity[] = [
  { name: 'București', lat: 44.43, lon: 26.1 }, { name: 'Alba Iulia', lat: 46.07, lon: 23.58 }, { name: 'Alexandria', lat: 43.97, lon: 25.33 }, { name: 'Arad', lat: 46.18, lon: 21.32 },
  { name: 'Bacău', lat: 46.57, lon: 26.91 }, { name: 'Baia Mare', lat: 47.66, lon: 23.58 }, { name: 'Bistrița', lat: 47.13, lon: 24.5 }, { name: 'Botoșani', lat: 47.75, lon: 26.67 },
  { name: 'Brașov', lat: 45.65, lon: 25.61 }, { name: 'Brăila', lat: 45.27, lon: 27.96 }, { name: 'Buzău', lat: 45.15, lon: 26.82 }, { name: 'Călărași', lat: 44.2, lon: 27.33 },
  { name: 'Cluj-Napoca', lat: 46.77, lon: 23.6 }, { name: 'Constanța', lat: 44.18, lon: 28.63 }, { name: 'Craiova', lat: 44.32, lon: 23.8 }, { name: 'Deva', lat: 45.88, lon: 22.9 },
  { name: 'Drobeta-Turnu Severin', lat: 44.63, lon: 22.66 }, { name: 'Focșani', lat: 45.7, lon: 27.18 }, { name: 'Galați', lat: 45.44, lon: 28.05 }, { name: 'Giurgiu', lat: 43.9, lon: 25.97 },
  { name: 'Iași', lat: 47.16, lon: 27.59 }, { name: 'Miercurea Ciuc', lat: 46.36, lon: 25.8 }, { name: 'Oradea', lat: 47.05, lon: 21.92 }, { name: 'Piatra Neamț', lat: 46.93, lon: 26.37 },
  { name: 'Pitești', lat: 44.86, lon: 24.87 }, { name: 'Ploiești', lat: 44.94, lon: 26.02 }, { name: 'Râmnicu Vâlcea', lat: 45.1, lon: 24.37 }, { name: 'Reșița', lat: 45.3, lon: 21.89 },
  { name: 'Satu Mare', lat: 47.79, lon: 22.89 }, { name: 'Sfântu Gheorghe', lat: 45.86, lon: 25.79 }, { name: 'Sibiu', lat: 45.8, lon: 24.15 }, { name: 'Slatina', lat: 44.43, lon: 24.37 },
  { name: 'Slobozia', lat: 44.56, lon: 27.37 }, { name: 'Suceava', lat: 47.65, lon: 26.26 }, { name: 'Târgoviște', lat: 44.93, lon: 25.46 }, { name: 'Târgu Jiu', lat: 45.04, lon: 23.27 },
  { name: 'Târgu Mureș', lat: 46.54, lon: 24.56 }, { name: 'Timișoara', lat: 45.76, lon: 21.23 }, { name: 'Tulcea', lat: 45.18, lon: 28.8 }, { name: 'Vaslui', lat: 46.64, lon: 27.73 },
  { name: 'Zalău', lat: 47.18, lon: 23.06 },
];
/** Orașul din listă cu exact aceste coordonate (la 0,01°), dacă localitatea a fost aleasă din listă. */
export const cityAt = (loc: SiteLocation | null | undefined): RoCity | null =>
  loc ? RO_CITIES.find(c => Math.abs(c.lat - loc.lat) < 0.005 && Math.abs(c.lon - loc.lon) < 0.005) ?? null : null;
