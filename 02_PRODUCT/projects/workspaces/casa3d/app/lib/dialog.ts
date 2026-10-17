// Dialogurile aplicației în locul lui confirm() / prompt() din browser (audit UI F-22): butoane în română, numite după
// acțiune („Șterge varianta”, „Anulează”), focus ținut în dialog, Escape = anulare. Aici e coada de cereri și logica fără
// DOM; components/DialogHost.tsx le afișează. Oricine poate cere un dialog și primește un Promise cu răspunsul.

export interface DialogRequest {
  kind: 'confirm' | 'prompt';
  title: string;
  body?: string;
  /** eticheta butonului principal; implicit „Confirmă” / „OK” din dicționar */
  confirmLabel?: string;
  cancelLabel?: string;
  /** acțiune distructivă: butonul principal e roșu, iar focusul pornește pe „Anulează” */
  danger?: boolean;
  /** pentru prompt: câmpul de text */
  input?: { label: string; placeholder?: string; initial?: string; maxLength?: number; multiline?: boolean;
    /** confirmarea prin tastare: butonul principal se activează doar când textul e exact acesta */
    mustEqual?: string };
}
type Pending = DialogRequest & { id: number; resolve(v: boolean | string | null): void };
let queue: Pending[] = [], seq = 0;
const listeners = new Set<() => void>();
const emit = () => { for (const l of listeners) l(); };

/** Dialogul afișat acum (primul din coadă) sau null. */
export const currentDialog = (): Pending | null => queue[0] ?? null;
export function subscribeDialogs(l: () => void){ listeners.add(l); return () => { listeners.delete(l); }; }
function open(r: DialogRequest): Promise<boolean | string | null> { return new Promise(resolve => { queue = [...queue, { ...r, id: ++seq, resolve }]; emit(); }); }
/** Răspunsul la dialogul curent: true/false la confirmare, textul sau null la prompt. Trece la următorul din coadă. */
export function answerDialog(value: boolean | string | null){ const c = queue[0]; if (!c) return; queue = queue.slice(1);
  c.resolve(c.kind === 'confirm' ? value === true : typeof value === 'string' ? value : null); emit(); }

/**
 * Anulează tot ce e în coadă (confirmare = false, prompt = null), de ex. la schimbarea paginii: un dialog cerut de pagina
 * veche nu mai are voie să rămână deschis și să-i execute acțiunea pe pagina nouă.
 */
export function cancelAllDialogs(){ const all = queue; if (!all.length) return; queue = [];
  for (const c of all) c.resolve(c.kind === 'confirm' ? false : null); emit(); }

/**
 * Unde revine focusul după dialoguri: elementul activ dinaintea primului dialog din coadă, ținut minte o singură dată
 * (nu la fiecare dialog din coadă, când focusul e deja în dialogul anterior) și redat doar când coada s-a golit.
 */
export function focusMemory<T>(){ let saved: T | null = null, open = false;
  return {
    /** un dialog e afișat; `active` = elementul cu focus chiar acum */
    show(active: T | null){ if (!open){ saved = active; open = true; } },
    /** coada s-a golit: elementul la care revine focusul (o singură dată) */
    hide(): T | null { if (!open) return null; open = false; const s = saved; saved = null; return s; },
  };
}

/** În locul lui confirm(): true doar dacă omul a ales butonul principal. Escape, clic pe fundal sau „Anulează” = false. */
export const askConfirm = (r: Omit<DialogRequest, 'kind' | 'input'>) => open({ ...r, kind: 'confirm' }) as Promise<boolean>;
/** În locul lui prompt(): textul scris sau null dacă s-a anulat. */
export const askPrompt = (r: Omit<DialogRequest, 'kind'> & { input: NonNullable<DialogRequest['input']> }) => open({ ...r, kind: 'prompt' }) as Promise<string | null>;

/** Butonul principal e activ? La confirmarea prin tastare, doar când textul se potrivește exact (fără spațiile de la capete). */
export function canConfirm(r: DialogRequest, text: string){ return !(r.kind === 'prompt' && r.input?.mustEqual != null && text.trim() !== r.input.mustEqual.trim()); }

/** Capcana de focus: indexul elementului care primește focusul la Tab (Shift+Tab), cu trecere de la ultimul la primul. */
export function trapFocusIndex(count: number, current: number, shift: boolean): number {
  if (count <= 0) return -1;
  if (current < 0 || current >= count) return shift ? count - 1 : 0;
  return shift ? (current - 1 + count) % count : (current + 1) % count;
}
/** Ce face o tastă în dialog: Escape anulează, Tab rămâne în dialog, Enter confirmă (doar în câmpul de o linie). */
export function dialogKey(key: string, inSingleLineInput: boolean): 'cancel' | 'trap' | 'confirm' | null {
  return key === 'Escape' ? 'cancel' : key === 'Tab' ? 'trap' : key === 'Enter' && inSingleLineInput ? 'confirm' : null;
}
