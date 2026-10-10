// Texte pentru utilizator derivate din codurile tehnice ale motorului (fără dependențe de UI, testabile).
/** Motivul tehnic al solver-ului, spus pe înțelesul utilizatorului (codul rămâne în paranteză pentru diagnostic). */
export function failText(reason?: string): string {
  const m = /^([A-Z_]+):\s*(.*)$/.exec(reason ?? ''); const code = m?.[1], rest = m?.[2] ?? reason ?? '';
  if (code === 'DOES_NOT_FIT') return `O piesă nu încape în cameră cu spațiile libere cerute. Încearcă alt magazin, fără „Accesibilitate” sau fără înlocuirea mobilei existente. (${code})`;
  return code ? `${rest} (${code})` : rest || 'Piesa nu a putut fi așezată.';
}
