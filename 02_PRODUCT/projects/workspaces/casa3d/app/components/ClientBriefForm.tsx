'use client';
// Chestionarul clientului, pagina publică (/brief/[token]): câte o secțiune pe ecran, răspunsuri ca butoane mari, salvare
// automată cu versiune (două file deschise nu se suprascriu), poze de inspirație la final și trimitere. Fără cont.
import { useCallback, useEffect, useRef, useState } from 'react';
import { usePrefs } from '@/lib/prefs';
import { SECTIONS, UNSURE, missingRequired, type ClientBrief, type Question } from '@/core/client-brief';
import { optionLabel, questionLabel, questionsOf } from '@/lib/client-brief-labels';
import { shrinkPhoto } from '@/lib/image-resize';

type Photo = { id: string; verdict: 'love' | 'ok' | 'dislike' | null; note: string };
type Initial = { submitted: true } | { submitted: false; data: Partial<ClientBrief>; rev: number; currency: string; photos: Photo[] };
type SaveState = 'saved' | 'dirty' | 'saving' | 'error' | 'conflict';
const MAX_PHOTOS = 12, MAX_PHOTO_BYTES = 1.5 * 1024 * 1024, SAVE_DELAY = 1500;
const VERDICTS = ['love', 'ok', 'dislike'] as const, PH = ['colorsLike', 'colorsAvoid', 'keepItems', 'dislikes', 'notes'];
const STEPS = [...SECTIONS, 'photos'] as const;

export default function ClientBriefForm({ token, initial }: { token: string; initial: Initial }){
  const { t, lang } = usePrefs(), api = `/api/brief/${token}`;
  const [data, setData] = useState<Partial<ClientBrief>>(initial.submitted ? {} : initial.data), cur = initial.submitted ? 'RON' : initial.currency;
  const [photos, setPhotos] = useState<Photo[]>(initial.submitted ? [] : initial.photos), [step, setStep] = useState(0);
  const [save, setSave] = useState<SaveState>('saved'), [end, setEnd] = useState<'' | 'done' | 'gone'>(initial.submitted ? 'done' : '');
  const [busy, setBusy] = useState(false), [uploading, setUploading] = useState(0), [msgs, setMsgs] = useState<string[]>([]), [missing, setMissing] = useState<string[]>([]);
  const rev = useRef(initial.submitted ? 0 : initial.rev), latest = useRef(data), dirty = useRef(false), flight = useRef<Promise<boolean> | null>(null), timer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined), top = useRef<HTMLDivElement>(null);

  // o singură salvare în zbor; dacă formularul s-a schimbat între timp, urmează încă una cu versiunea nouă
  const flush = useCallback(async (): Promise<boolean> => {
    clearTimeout(timer.current); if (flight.current) await flight.current; if (!dirty.current) return true;
    const run = (async () => { const sent = latest.current; dirty.current = false; setSave('saving');
      try { const r = await fetch(api, { method: 'PUT', keepalive: true, headers: { 'content-type': 'application/json' }, body: JSON.stringify({ rev: rev.current, data: { v: 1, ...sent } }) }), j = await r.json().catch(() => ({}));
        if (r.ok){ rev.current = j.rev; setSave(dirty.current ? 'dirty' : 'saved'); return true; }
        if (r.status === 404){ setEnd('gone'); return false; } if (r.status === 409 && j.submitted){ setEnd('done'); return false; }
        if (r.status === 409){ setSave('conflict'); return false; }
        dirty.current = true; setSave('error'); return false; }
      catch { dirty.current = true; setSave('error'); timer.current = setTimeout(() => { void flush(); }, 5000); return false; } })();
    flight.current = run; const ok = await run; flight.current = null;
    if (ok && dirty.current) return flush(); return ok;
  }, [api]);
  const change = (patch: Partial<ClientBrief>) => { const next: any = { ...latest.current, ...patch };
    for (const k of Object.keys(next)) if (next[k] === undefined || next[k] === '' || (Array.isArray(next[k]) && !next[k].length)) delete next[k];
    // întrebări condiționate: fără copii nu există vârste; camera cea mai importantă e una dintre cele alese
    if (!next.children) delete next.childAges;
    if (next.priorityRoom && next.priorityRoom !== UNSURE && !(next.rooms ?? []).includes(next.priorityRoom)) delete next.priorityRoom;
    latest.current = next; setData(next); setMissing(m => m.filter(k => next[k] === undefined)); dirty.current = true; if (save !== 'conflict') setSave('dirty');
    clearTimeout(timer.current); timer.current = setTimeout(() => { void flush(); }, SAVE_DELAY); };
  useEffect(() => { const leave = (e: BeforeUnloadEvent) => { if (dirty.current) e.preventDefault(); }; window.addEventListener('beforeunload', leave); return () => { window.removeEventListener('beforeunload', leave); clearTimeout(timer.current); }; }, []);
  // pe telefon fila se închide sau trece în fundal fără „beforeunload”: răspunsul dat chiar înainte se trimite atunci (keepalive)
  useEffect(() => { const hide = () => { if (document.visibilityState === 'hidden' && dirty.current) void flush(); }, gone = () => { if (dirty.current) void flush(); };
    document.addEventListener('visibilitychange', hide); window.addEventListener('pagehide', gone);
    return () => { document.removeEventListener('visibilitychange', hide); window.removeEventListener('pagehide', gone); }; }, [flush]);
  const go = (n: number) => { setStep(Math.max(0, Math.min(STEPS.length - 1, n))); void flush(); requestAnimationFrame(() => top.current?.scrollIntoView({ block: 'start' })); };

  async function submit(){ if (busy) return; const miss = missingRequired(latest.current); setMissing(miss);
    if (miss.length){ const s = STEPS.findIndex(sec => questionsOf(sec).some(q => q.key === miss[0])); if (s >= 0) go(s); return; }
    setBusy(true); setMsgs([]);
    try { if (!(await flush())) return;
      const r = await fetch(`${api}/submit`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ rev: rev.current }) }), j = await r.json().catch(() => ({}));
      if (r.ok || (r.status === 409 && j.submitted)) setEnd('done'); else if (r.status === 404) setEnd('gone'); else if (r.status === 409) setSave('conflict');
      else if (Array.isArray(j.missing)) setMissing(j.missing); else setMsgs([j.error || t('cbrief.submitFailed')]); }
    catch { setMsgs([t('cbrief.submitFailed')]); } finally { setBusy(false); } }

  // pozele: micșorate și recodate JPEG în browser (orientarea aplicată); serverul păstrează doar imaginea, fără metadate
  async function addPhotos(files: FileList | null){ if (!files?.length) return; const list = [...files].slice(0, Math.max(0, MAX_PHOTOS - photos.length)), out: string[] = [];
    if (list.length < files.length) out.push(t('cbrief.photos.limit', { max: MAX_PHOTOS }));
    setMsgs([]); setUploading(list.length);
    for (const f of list){ const name = f.name.slice(0, 60);
      try { let blob = await shrinkPhoto(f, 2048, .82);
        if (blob === f){ out.push(t('cbrief.photos.unreadable', { name })); continue; }   // browserul nu a putut decoda imaginea (de obicei HEIC)
        if (blob.size > MAX_PHOTO_BYTES) blob = await shrinkPhoto(f, 1600, .72);
        if (blob.size > MAX_PHOTO_BYTES){ out.push(t('cbrief.photos.tooBig', { name })); continue; }
        const r = await fetch(`${api}/photos`, { method: 'POST', headers: { 'content-type': 'image/jpeg' }, body: blob }), j = await r.json().catch(() => ({}));
        if (r.ok) setPhotos(p => [...p, { id: j.id, verdict: j.verdict ?? null, note: j.note ?? '' }]);
        else if (r.status === 404){ setEnd('gone'); break; } else if (r.status === 409 && j.submitted){ setEnd('done'); break; }
        else if (r.status === 409 && j.limit){ out.push(t('cbrief.photos.limit', { max: MAX_PHOTOS })); break; } else out.push(j.error || t('cbrief.photos.failed', { name })); }
      catch { out.push(t('cbrief.photos.failed', { name })); }
      finally { setUploading(n => Math.max(0, n - 1)); } }
    setUploading(0); setMsgs(out); }
  async function patchPhoto(id: string, patch: Partial<Photo>){ setPhotos(p => p.map(x => x.id === id ? { ...x, ...patch } : x));
    const r = await fetch(`${api}/photos/${id}`, { method: 'PATCH', headers: { 'content-type': 'application/json' }, body: JSON.stringify(patch) }).catch(() => null);
    if (r?.status === 404) setEnd('gone'); }
  async function removePhoto(id: string){ const r = await fetch(`${api}/photos/${id}`, { method: 'DELETE' }).catch(() => null); if (r?.ok || r?.status === 404) setPhotos(p => p.filter(x => x.id !== id)); }

  if (end) return (<main className="cbf cbf-end"><h1>{t(end === 'done' ? 'cbrief.done.title' : 'cbrief.gone.title')}</h1><p>{t(end === 'done' ? 'cbrief.done.text' : 'cbrief.gone.text')}</p></main>);
  const sec = STEPS[step]!, last = step === STEPS.length - 1, val = (k: string) => (data as Record<string, any>)[k];
  const visible = (q: Question) => q.key !== 'childAges' || (data.children ?? 0) > 0;
  const optionsOf = (q: Question) => q.key === 'priorityRoom' ? (q.options ?? []).filter(o => o === UNSURE || (data.rooms ?? []).includes(o)) : q.options ?? [];
  const pick = (q: Question, o: string) => { const cur0: string[] = val(q.key) ?? [];
    if (q.kind === 'enum') return change({ [q.key]: val(q.key) === o ? undefined : o } as any);
    if (cur0.includes(o)) return change({ [q.key]: cur0.filter(x => x !== o) } as any);
    if (o === UNSURE) return change({ [q.key]: [o] } as any);
    const base = cur0.filter(x => x !== UNSURE), max = q.kind === 'multi' && q.max ? q.max : Infinity;
    change({ [q.key]: [...(base.length >= max ? base.slice(1) : base), o] } as any); };
  const field = (q: Question) => { const id = `cbf-${q.key}`, miss = missing.includes(q.key);
    const head = <span className="cbf-q" id={id}>{questionLabel(t, q.key)}{q.required && <em> · {t('cbrief.required')}</em>}{q.kind === 'multi' && <small>{q.max ? t('cbrief.pickUpTo', { n: q.max }) : t('cbrief.pickAny')}</small>}</span>;
    if (q.kind === 'enum' || q.kind === 'multi'){ const opts = optionsOf(q); if (!opts.length) return null;
      return (<div key={q.key} className={`cbf-f${miss ? ' miss' : ''}`} role="group" aria-labelledby={id}>{head}
        <div className="cbf-opts">{opts.map(o => { const on = q.kind === 'enum' ? val(q.key) === o : (val(q.key) ?? []).includes(o);
          return <button key={o} type="button" className={`cbf-opt${o === UNSURE ? ' unsure' : ''}`} aria-pressed={on} onClick={() => pick(q, o)}>{optionLabel(t, lang, q.key, o, cur)}</button>; })}</div></div>); }
    if (q.kind === 'bool') return (<div key={q.key} className="cbf-f" role="group" aria-labelledby={id}>{head}
      <div className="cbf-opts">{[true, false].map(b => <button key={String(b)} type="button" className="cbf-opt" aria-pressed={val(q.key) === b} onClick={() => change({ [q.key]: val(q.key) === b ? undefined : b } as any)}>{t(b ? 'cbrief.yes' : 'cbrief.no')}</button>)}</div></div>);
    if (q.kind === 'int'){ const n: number | undefined = val(q.key), set = (v: number) => change({ [q.key]: Math.max(q.min!, Math.min(q.max!, v)) } as any);
      return (<div key={q.key} className={`cbf-f${miss ? ' miss' : ''}`} role="group" aria-labelledby={id}>{head}
        <div className="cbf-step"><button type="button" className="btn" aria-label={`${t('cbrief.less')}: ${questionLabel(t, q.key)}`} disabled={n === undefined ? q.min! > 0 : n <= q.min!} onClick={() => set(n === undefined ? q.min! : n - 1)}>−</button>
          <output aria-live="polite">{n ?? '–'}</output>
          <button type="button" className="btn" aria-label={`${t('cbrief.more')}: ${questionLabel(t, q.key)}`} disabled={n !== undefined && n >= q.max!} onClick={() => set(n === undefined ? Math.max(q.min!, 1) : n + 1)}>+</button></div></div>); }
    const ph = PH.includes(q.key) ? t(`cbrief.ph.${q.key}`) : undefined, on = (e: { target: { value: string } }) => change({ [q.key]: e.target.value } as any);
    return (<label key={q.key} className="cbf-f">{head}{q.long ? <textarea rows={3} maxLength={q.max} value={val(q.key) ?? ''} placeholder={ph} onChange={on} onBlur={() => { void flush(); }} />
      : <input type="text" maxLength={q.max} value={val(q.key) ?? ''} placeholder={ph} onChange={on} onBlur={() => { void flush(); }} />}</label>); };

  return (<main className="cbf" ref={top}>
    <header className="cbf-head"><h1>{t('cbrief.pageTitle')}</h1>{step === 0 && <p>{t('cbrief.intro')}</p>}
      <div className="cbf-prog"><span>{t('cbrief.step', { n: step + 1, total: STEPS.length })}</span>
        <span className={`cbf-save ${save}`} role="status">{t(save === 'saved' ? 'cbrief.saved' : save === 'saving' ? 'cbrief.saving' : save === 'error' ? 'cbrief.saveError' : save === 'conflict' ? 'cbrief.conflict' : 'cbrief.dirty')}</span></div>
      <progress max={STEPS.length} value={step + 1} aria-label={t('cbrief.step', { n: step + 1, total: STEPS.length })} /></header>
    {save === 'conflict' && <div className="issue WARNING" role="alert">{t('cbrief.conflict')} <button type="button" className="btn sm" onClick={() => location.reload()}>{t('cbrief.reload')}</button></div>}
    <section className="cbf-sec" aria-labelledby="cbf-sec-h"><h2 id="cbf-sec-h">{t(`cbrief.s.${sec}`)}</h2>
      {sec !== 'photos' ? questionsOf(sec).filter(visible).map(field) : <>
        <p className="cbf-note">{t('cbrief.photos.intro', { n: MAX_PHOTOS })}</p>
        <div className="cbf-photos">{photos.map((p, i) => <figure key={p.id} className="cbf-photo">
          <img src={`${api}/photos/${p.id}`} alt={t('cbrief.photos.alt', { n: i + 1 })} loading="lazy" />
          <div className="cbf-opts" role="group" aria-label={t('cbrief.photos.verdict')}>{VERDICTS.map(v => <button key={v} type="button" className={`cbf-opt sm v-${v}`} aria-pressed={p.verdict === v} onClick={() => patchPhoto(p.id, { verdict: p.verdict === v ? null : v })}>{t(`cbrief.photos.${v}`)}</button>)}</div>
          <input type="text" maxLength={200} defaultValue={p.note} placeholder={t('cbrief.photos.note')} aria-label={t('cbrief.photos.note')} onBlur={e => { const n = e.target.value.trim(); if (n !== p.note) void patchPhoto(p.id, { note: n }); }} />
          <button type="button" className="btn sm ghost" onClick={() => removePhoto(p.id)}>{t('cbrief.photos.remove')}</button></figure>)}</div>
        <div className="cbf-add"><label className={`btn lg${photos.length >= MAX_PHOTOS || uploading ? ' off' : ''}`}>{uploading ? t('cbrief.photos.uploading') : t('cbrief.photos.add')}
          <input type="file" accept="image/*" multiple className="sr-only" disabled={photos.length >= MAX_PHOTOS || uploading > 0} onChange={e => { const f = e.target.files; void addPhotos(f).then(() => { e.target.value = ''; }); }} /></label>
          <span>{t('cbrief.photos.count', { n: photos.length, max: MAX_PHOTOS })}</span></div>
        <details className="cbf-privacy" open><summary>{t('cbrief.privacy.title')}</summary><p>{t('cbrief.privacy.text')}</p></details></>}
    </section>
    {msgs.map((m, i) => <div key={i} className="issue WARNING" role="alert">{m}</div>)}
    {missing.length > 0 && <div className="issue ERROR" role="alert">{t('cbrief.missing')} {missing.map(k => questionLabel(t, k)).join(' · ')}</div>}
    <nav className="cbf-nav"><button type="button" className="btn lg" disabled={step === 0} onClick={() => go(step - 1)}>{t('cbrief.back')}</button>
      {last ? <button type="button" className="btn lg primary" disabled={busy || uploading > 0 || save === 'conflict'} onClick={submit}>{busy ? t('cbrief.submitting') : t('cbrief.submit')}</button>
        : <button type="button" className="btn lg primary" onClick={() => go(step + 1)}>{t('cbrief.next')}</button>}</nav>
  </main>);
}
