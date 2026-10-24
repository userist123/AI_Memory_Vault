// Protecția comună a exporturilor CSV împotriva injecției de formule (Excel, LibreOffice, Google Sheets).
// O celulă text care începe cu = + - @ sau cu tab / CR primește un apostrof în față, ca să fie citită ca text.
// Folosită de TOATE exporturile CSV: produsele proprii, comenzi.csv, deviz.csv, proforma și exportul comenzilor din interfață.
export const FORMULA_START = /^[=+\-@\t\r]/;
/** Celulă de export protejată; null/undefined → gol. */
export const safeCell = (v: unknown): string => { if (v == null) return ''; const s = String(v); return FORMULA_START.test(s) ? "'" + s : s; };
/** Protejează doar textul; numerele rămân numere (un -3 numeric nu e formulă). */
export const safeValue = (v: unknown): string => typeof v === 'number' ? (Number.isFinite(v) ? String(v) : '') : safeCell(v);
/** Ghilimele în jurul celulei dacă are separatorul, ghilimele sau rând nou (inclusiv CR). */
export const quoteCell = (s: string, sep = ';') => s.includes(sep) || /["\r\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s;
