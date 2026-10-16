'use client';
// Afișează dialogurile cerute prin lib/dialog.ts (askConfirm / askPrompt): <dialog> modal, titlu și text legate prin
// aria-labelledby / aria-describedby, focusul ținut înăuntru, Escape anulează, focusul revine unde era înainte de primul
// dialog din coadă după ce s-a închis ultimul. La schimbarea paginii toate dialogurile în așteptare se anulează.
import { useEffect, useRef, useState, useSyncExternalStore } from 'react';
import { usePathname } from 'next/navigation';
import { currentDialog, subscribeDialogs, answerDialog, cancelAllDialogs, canConfirm, trapFocusIndex, dialogKey, focusMemory } from '@/lib/dialog';
import { usePrefs } from '@/lib/prefs';

const FOCUSABLE = 'button:not([disabled]),input:not([disabled]),textarea:not([disabled]),select:not([disabled]),a[href]';
export default function DialogHost(){
  const d = useSyncExternalStore(subscribeDialogs, currentDialog, () => null), { t } = usePrefs();
  const ref = useRef<HTMLDialogElement>(null), back = useRef(focusMemory<Element>()), [text, setText] = useState('');
  // pagina s-a schimbat (link, înapoi, router.push): dialogurile paginii vechi se anulează, nu rămân peste pagina nouă
  const pathname = usePathname(), shownPath = useRef(pathname);
  useEffect(() => { if (shownPath.current === pathname) return; shownPath.current = pathname; cancelAllDialogs(); }, [pathname]);
  useEffect(() => { const el = ref.current; if (!el) return;
    if (d){ back.current.show(document.activeElement); setText(d.input?.initial ?? ''); try { if (!el.open) el.showModal?.(); } catch { /* fără showModal: rămâne afișat ca bloc */ }
      requestAnimationFrame(() => { const first = el.querySelector<HTMLElement>(d.input ? 'input,textarea' : d.danger ? '[data-cancel]' : '[data-ok]'); first?.focus(); }); }
    else { if (el.open) el.close?.(); const prev = back.current.hide() as HTMLElement | null; if (prev?.isConnected) prev.focus?.(); } }, [d?.id]); // eslint-disable-line
  if (!d) return <dialog ref={ref} className="dlg" aria-hidden="true" />;
  const ok = canConfirm(d, text), done = (v: boolean | string | null) => answerDialog(v);
  const accept = () => { if (!ok) return; done(d.kind === 'prompt' ? text : true); };
  const onKey = (e: React.KeyboardEvent) => { const single = (e.target as HTMLElement).tagName === 'INPUT', act = dialogKey(e.key, single);
    if (act === 'cancel'){ e.preventDefault(); done(d.kind === 'prompt' ? null : false); }
    else if (act === 'confirm'){ e.preventDefault(); accept(); }
    else if (act === 'trap'){ const els = [...ref.current!.querySelectorAll<HTMLElement>(FOCUSABLE)], i = trapFocusIndex(els.length, els.indexOf(document.activeElement as HTMLElement), e.shiftKey);
      if (i >= 0){ e.preventDefault(); els[i]!.focus(); } } };
  const tid = `dlg-t-${d.id}`, bid = `dlg-b-${d.id}`;
  return (<dialog ref={ref} className="dlg" role={d.danger ? 'alertdialog' : 'dialog'} aria-modal="true" aria-labelledby={tid} aria-describedby={d.body ? bid : undefined}
    onKeyDown={onKey} onCancel={e => { e.preventDefault(); done(d.kind === 'prompt' ? null : false); }} onClick={e => { if (e.target === ref.current) done(d.kind === 'prompt' ? null : false); }}>
    <form className="dlg-card" method="dialog" onSubmit={e => { e.preventDefault(); accept(); }}>
      <h2 id={tid}>{d.title}</h2>
      {d.body && <p id={bid}>{d.body}</p>}
      {d.input && <label className="f"><span>{d.input.label}</span>{d.input.multiline
        ? <textarea value={text} rows={3} maxLength={d.input.maxLength} placeholder={d.input.placeholder} onChange={e => setText(e.target.value)} />
        : <input type="text" value={text} maxLength={d.input.maxLength} placeholder={d.input.placeholder} autoComplete="off" onChange={e => setText(e.target.value)} aria-invalid={d.input.mustEqual != null && text.length > 0 && !ok} />}</label>}
      <div className="dlg-act">
        <button type="button" className="btn" data-cancel onClick={() => done(d.kind === 'prompt' ? null : false)}>{d.cancelLabel ?? t('dlg.cancel')}</button>
        <button type="submit" className={`btn primary ${d.danger ? 'danger' : ''}`} data-ok disabled={!ok}>{d.confirmLabel ?? t('dlg.ok')}</button>
      </div>
    </form>
  </dialog>);
}
