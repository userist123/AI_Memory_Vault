// Jurnalizarea erorilor fără date: obiectul de eroare al bazei (PGlite/pg) poartă `query` și `params`, adică tokenuri
// de link, hash-uri de PIN, nume și notițe ale clienților. În jurnal ajung doar locul, tipul, codul și mesajul scurt.
export function errorSummary(e: unknown){
  if (!(e instanceof Error)) return { type: typeof e };
  const code = (e as { code?: unknown }).code;
  return { name: e.name, ...(typeof code === 'string' || typeof code === 'number' ? { code } : {}), message: e.message.replace(/[\r\n]+/g, ' ').slice(0, 200) };
}
export function logError(where: string, e: unknown){ console.error(`[casa3d] ${where}`, errorSummary(e)); }
