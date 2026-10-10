// Texte pentru utilizator derivate din codurile tehnice ale motorului (fără dependențe de UI, testabile).
import { t, type Lang } from '../lib/i18n';
/** Motivul tehnic al solver-ului, spus pe înțelesul utilizatorului în limba aleasă (codul rămâne în paranteză pentru diagnostic). */
export function failText(reason?: string, lang: Lang = 'ro'): string {
  const m = /^([A-Z_]+):\s*(.*)$/.exec(reason ?? ''); const code = m?.[1], rest = m?.[2] ?? reason ?? '';
  if (code === 'DOES_NOT_FIT') return `${t(lang, 'msg.doesNotFit')} (${code})`;
  return code ? `${rest} (${code})` : rest || t(lang, 'msg.pieceFailed');
}
