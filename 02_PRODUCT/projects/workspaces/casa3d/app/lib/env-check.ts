// Validarea configurației la pornire (security-lockdown, verificarea 2): o variabilă setată greșit oprește serverul cu
// un mesaj clar, în loc să dezactiveze tăcut o protecție (ex. ADMIN_TOKEN prea scurt) sau să pornească într-un mod
// nedorit. Variabilele lipsă rămân permise: fiecare are un comportament implicit sigur (vezi .env.example).
type Env = Record<string, string | undefined>;
export function envProblems(env: Env = process.env): string[] {
  const out: string[] = [], set = (k: string) => env[k] != null && env[k] !== '';
  if (set('ADMIN_TOKEN') && env.ADMIN_TOKEN!.length < 16) out.push('ADMIN_TOKEN trebuie să aibă cel puțin 16 caractere (sau lipsește: administrarea rămâne închisă).');
  if (set('ANTHROPIC_API_KEY') && !/^sk-ant-[\w-]{10,}$/.test(env.ANTHROPIC_API_KEY!)) out.push('ANTHROPIC_API_KEY nu are forma unei chei Anthropic (sk-ant-...).');
  if (set('DATABASE_URL') && !/^postgres(ql)?:\/\//.test(env.DATABASE_URL!)) out.push('DATABASE_URL trebuie să înceapă cu postgres:// sau postgresql://.');
  for (const k of ['CASA3D_LOCAL_TRUST', 'CASA3D_DEMO', 'PGLITE_MEMORY', 'DATABASE_SSL_NO_VERIFY']) if (set(k) && env[k] !== '1' && env[k] !== '0') out.push(`${k} acceptă doar 1 sau 0 (acum: „${String(env[k]).slice(0, 20)}”).`);
  if (set('CASA3D_PUBLIC_URL')){ let ok = false; try { const u = new URL(env.CASA3D_PUBLIC_URL!); ok = /^https?:$/.test(u.protocol) && !u.username && !u.password && (u.pathname === '/' || u.pathname === '') && !u.search && !u.hash; } catch { ok = false; }
    if (!ok) out.push('CASA3D_PUBLIC_URL trebuie să fie doar o origine http(s), ex. https://casa.exemplu.ro.'); }
  if (set('CASA3D_CLIENT_IP_HEADER') && !/^[a-z0-9-]{1,64}$/i.test(env.CASA3D_CLIENT_IP_HEADER!)) out.push('CASA3D_CLIENT_IP_HEADER trebuie să fie numele unui antet HTTP.');
  if (env.CASA3D_LOCAL_TRUST === '1' && env.CASA3D_DEMO === '1') out.push('CASA3D_LOCAL_TRUST=1 și CASA3D_DEMO=1 împreună: demo-ul public nu are voie să aibă încredere locală. Pornește demo-ul cu `npm run start:hosted`, nu cu `npm start`.');
  return out;
}
