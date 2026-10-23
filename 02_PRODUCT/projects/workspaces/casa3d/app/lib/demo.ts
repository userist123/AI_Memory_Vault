// Demo public (CASA3D_DEMO=1): o instanță separată, deschisă oricui prin tunel, ca potențialii cumpărători să încerce aplicația.
// Fiecare vizitator are proiectele lui (cookie anonim), cel mult câteva; proiectele mai vechi de o zi se șterg la următoarea
// creare, iar totalul instanței e plafonat (memoria serverului). Randarea Blender și administrarea nu există în demo.
import { HttpError } from './repo';

export const DEMO_LIMITS = { perOwner: 5, total: 2000, ttlHours: 24 };
export const isDemo = () => process.env.CASA3D_DEMO === '1';
type Q = (sql: string, params?: unknown[]) => Promise<{ rows: any[] }>;
/** Înainte de a crea un proiect în demo: curăță proiectele expirate și verifică limitele. În afara demo-ului nu face nimic. */
export async function demoBeforeCreate(q: Q, owner: string){
  if (!isDemo()) return;
  await q(`delete from projects where created_at < now() - ($1::int * interval '1 hour')`, [DEMO_LIMITS.ttlHours]);
  const { rows: [c] } = await q('select count(*)::int as total, count(*) filter (where owner=$1)::int as mine from projects', [owner]);
  if (c.mine >= DEMO_LIMITS.perOwner) throw new HttpError(429, `În demo poți avea cel mult ${DEMO_LIMITS.perOwner} proiecte. Șterge unul ca să faci altul.`);
  if (c.total >= DEMO_LIMITS.total) throw new HttpError(503, 'Demo-ul e plin acum. Încearcă din nou mai târziu.');
}
