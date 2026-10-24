// Butoanele care creează ceva (proiect, copie, link pentru client, revizie): un dublu sau triplu click în același „tick”
// trece de `disabled={busy}` (starea React nu s-a re-randat încă). Garda e sincronă: a doua cerere cu aceeași cheie, cât
// timp prima e în lucru, nu mai pornește. `hold` ține cheia ocupată și după reușită (ex. pagina urmează să se schimbe).
export function singleFlight(){
  const busy = new Set<string>();
  return {
    has: (key: string) => busy.has(key),
    async run<T>(key: string, fn: () => Promise<T>, o: { hold?: (r: T) => boolean } = {}): Promise<T | undefined> {
      if (busy.has(key)) return undefined; busy.add(key); let keep = false;
      try { const r = await fn(); keep = !!o.hold?.(r); return r; } finally { if (!keep) busy.delete(key); }
    },
  };
}
