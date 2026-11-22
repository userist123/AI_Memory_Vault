'use client';
// Oferta meseriașului, pagina publică (/oferta/[token]): lista de cantități cu preț unitar pe fiecare rând (sau „nu ofertez”),
// incluziunile, datele firmei și termenii, cu totaluri pe loc și salvare automată cu versiune (două file deschise nu se
// suprascriu). După trimitere: rezumat doar de citit. Fără cont; pagina nu arată nimic despre client sau despre alte oferte.
import { useCallback, useEffect, useRef, useState } from 'react';
import { usePrefs } from '@/lib/prefs';
import { intlLocale } from '@/lib/i18n';
import { formatPrice, formatNumber } from '@/core/format';
import { INCLUSIONS, INCLUSION_MODES, COMPANY_FORMS, CUI_RE, RFQ_LIMITS, offerMissing, offerTotals, offerCoverage, type RfqOffer, type PublicLine, type Inclusion, type InclusionMode } from '@/core/rfq';

type Initial = { studio: string | null; title: string; city: string | null; dueDate: string | null; note: string; lines: PublicLine[]; offer: RfqOffer; rev: number; submitted: boolean; submittedAt: string | null; closed: boolean; vatRate: number };
type SaveState = 'saved' | 'dirty' | 'saving' | 'error' | 'conflict';
const SAVE_DELAY = 1500, CUR = 'RON';
const unitL = (u: string) => u === 'm2' ? 'm²' : u;
const toNum = (s: string) => { const v = Number(s.replace(/\s/g, '').replace(',', '.')); return s.trim() === '' || !Number.isFinite(v) ? undefined : v; };
const r2 = (n: number) => Math.round((n + Number.EPSILON) * 100) / 100;

/** Câmp numeric cu text propriu (virgulă sau punct): valoarea pleacă mai departe doar când e un număr în limite. */
function NumInput({ value, onNum, min, max, int, ...rest }: { value: number | undefined; onNum(v: number | undefined): void; min: number; max: number; int?: boolean } & Omit<React.InputHTMLAttributes<HTMLInputElement>, 'value' | 'onChange' | 'min' | 'max'>){
  const [txt, setTxt] = useState(value != null ? String(value).replace('.', ',') : '');
  useEffect(() => { setTxt(p => toNum(p) === value ? p : value != null ? String(value).replace('.', ',') : ''); }, [value]);
  return <input type="text" inputMode={int ? 'numeric' : 'decimal'} autoComplete="off" {...rest} value={txt} onChange={e => { setTxt(e.target.value); const v = toNum(e.target.value);
    onNum(v == null || v < min || v > max || (int && !Number.isInteger(v)) ? undefined : int ? v : r2(v)); }} />;
}

export default function RfqOfferForm({ token, initial }: { token: string; initial: Initial }){
  const { t, tp, lang } = usePrefs(), api = `/api/oferta/${token}`, lines = initial.lines, readOnly = initial.submitted || initial.closed;
  const [data, setData] = useState<RfqOffer>(initial.offer), [save, setSave] = useState<SaveState>('saved'), [end, setEnd] = useState<'' | 'done' | 'gone' | 'closed'>(initial.submitted ? 'done' : initial.closed ? 'closed' : '');
  const [sentAt, setSentAt] = useState<string | null>(initial.submittedAt), [busy, setBusy] = useState(false), [msg, setMsg] = useState(''), [missing, setMissing] = useState<string[]>([]), [cui, setCui] = useState(initial.offer.company.cui ?? '');
  const rev = useRef(initial.rev), latest = useRef(data), dirty = useRef(false), flight = useRef<Promise<boolean> | null>(null), timer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined), errRef = useRef<HTMLDivElement>(null);
  const money = (v: number) => formatPrice(v, CUR, lang), qf = (v: number) => formatNumber(v, lang, { max: 2 });
  const day = (s: string) => new Date(s.slice(0, 10) + 'T12:00:00').toLocaleDateString(intlLocale(lang), { day: 'numeric', month: 'long', year: 'numeric' });

  // o singură salvare în zbor; dacă formularul s-a schimbat între timp, urmează încă una cu versiunea nouă
  const flush = useCallback(async (): Promise<boolean> => {
    clearTimeout(timer.current); if (flight.current) await flight.current; if (!dirty.current) return true;
    const run = (async () => { const sent = latest.current; dirty.current = false; setSave('saving');
      try { const r = await fetch(api, { method: 'PUT', keepalive: true, headers: { 'content-type': 'application/json' }, body: JSON.stringify({ rev: rev.current, data: sent }) }), j = await r.json().catch(() => ({}));
        if (r.ok){ rev.current = j.rev; setSave(dirty.current ? 'dirty' : 'saved'); return true; }
        if (r.status === 404){ setEnd('gone'); return false; } if (r.status === 409 && j.submitted){ setEnd('done'); return false; } if (r.status === 409 && j.closed){ setEnd('closed'); return false; }
        if (r.status === 409){ setSave('conflict'); return false; }
        dirty.current = true; setSave('error'); return false; }
      catch { dirty.current = true; setSave('error'); timer.current = setTimeout(() => { void flush(); }, 5000); return false; } })();
    flight.current = run; const ok = await run; flight.current = null;
    if (ok && dirty.current) return flush(); return ok;
  }, [api]);
  const change = (fn: (o: RfqOffer) => void) => { const next: RfqOffer = JSON.parse(JSON.stringify(latest.current)); fn(next);
    latest.current = next; setData(next); setMissing(m => m.length ? m.filter(k => missingOf(next).includes(k)) : m); dirty.current = true; if (save !== 'conflict') setSave('dirty');
    clearTimeout(timer.current); timer.current = setTimeout(() => { void flush(); }, SAVE_DELAY); };
  useEffect(() => { const leave = (e: BeforeUnloadEvent) => { if (dirty.current) e.preventDefault(); }; window.addEventListener('beforeunload', leave); return () => { window.removeEventListener('beforeunload', leave); clearTimeout(timer.current); }; }, []);
  // pe telefon fila se închide sau trece în fundal fără „beforeunload”: ce s-a scris chiar înainte se trimite atunci (keepalive)
  useEffect(() => { const hide = () => { if (document.visibilityState === 'hidden' && dirty.current) void flush(); }, gone = () => { if (dirty.current) void flush(); };
    document.addEventListener('visibilitychange', hide); window.addEventListener('pagehide', gone);
    return () => { document.removeEventListener('visibilitychange', hide); window.removeEventListener('pagehide', gone); }; }, [flush]);

  // ce lipsește pentru trimitere: aceleași reguli ca pe server, plus suma incluziunilor „cu preț separat”
  function missingOf(o: RfqOffer){ return [...offerMissing(o, lines), ...INCLUSIONS.filter(k => o.inclusions[k]?.mode === 'separate' && o.inclusions[k]!.amount == null).map(k => `inclusion:${k}`)]; }
  const missLabel = (k: string) => k.startsWith('price:') ? t('rfq.p.missing.price', { label: lines.find(l => l.rateId === k.slice(6))?.label ?? '' }) : k === 'prices' ? t('rfq.p.missing.prices')
    : k.startsWith('inclusion:') ? t(`rfq.inc.${k.slice(10)}`) : t(({ form: 'rfq.p.form', vatPayer: 'rfq.p.vat', advancePct: 'rfq.p.advance', startDate: 'rfq.p.start', durationDays: 'rfq.p.duration', warrantyMonths: 'rfq.p.warranty', validDays: 'rfq.p.valid' } as Record<string, string>)[k] ?? 'rfq.p.none');
  async function submit(){ if (busy) return; const miss = [...new Set(missingOf(latest.current))]; setMissing(miss); setMsg('');
    if (miss.length){ requestAnimationFrame(() => errRef.current?.focus()); return; }
    setBusy(true);
    try { if (!(await flush())){ if (save === 'error') setMsg(t('rfq.p.saveError')); return; }
      const r = await fetch(`${api}/submit`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ rev: rev.current }) }), j = await r.json().catch(() => ({}));
      if (r.ok || (r.status === 409 && j.submitted)){ setSentAt(s => s ?? new Date().toISOString()); setEnd('done'); window.scrollTo({ top: 0 }); } else if (r.status === 404) setEnd('gone'); else if (r.status === 409 && j.closed) setEnd('closed');
      else if (r.status === 409) setSave('conflict'); else if (Array.isArray(j.missing)){ setMissing(j.missing); requestAnimationFrame(() => errRef.current?.focus()); } else setMsg(j.error || t('rfq.p.submitFailed')); }
    catch { setMsg(t('rfq.p.submitFailed')); } finally { setBusy(false); } }

  if (end === 'gone') return (<main className="rof rof-end"><h1>{t('rfq.p.gone.title')}</h1><p>{t('rfq.p.gone.text')}</p></main>);
  const locked = readOnly || end !== '', tot = offerTotals(data, lines, initial.vatRate), cov = offerCoverage(data, lines), payer = data.company.vatPayer;
  const miss = (k: string) => missing.includes(k);
  const head = (<header className="rof-head">
    {initial.studio && <p className="rof-studio">{t('rfq.p.from', { studio: initial.studio })}</p>}
    <h1>{t('rfq.p.pageTitle')}: {initial.title}</h1>
    {(initial.city || initial.dueDate) && <p className="rof-meta">{[initial.city ? t('rfq.p.city', { city: initial.city }) : '', initial.dueDate ? t('rfq.p.due', { date: day(initial.dueDate) }) : ''].filter(Boolean).join(' · ')}</p>}
    {initial.note && <div className="rof-note"><b>{t('rfq.p.noteTitle')}</b><p>{initial.note}</p></div>}
  </header>);
  const totals = (<section className="rof-sec rof-totals" aria-labelledby="rof-tot-h"><h2 id="rof-tot-h">{t('rfq.p.s.totals')}</h2>
    <dl><div><dt>{t('rfq.p.tot.net')}</dt><dd>{money(r2(tot.net + tot.separate))}</dd></div>
      {tot.separate > 0 && <div className="sub"><dt>{t('rfq.p.tot.sep')}</dt><dd>{money(tot.separate)}</dd></div>}
      {payer === true && <><div><dt>{t('rfq.p.tot.vat', { pct: Math.round(initial.vatRate * 100) })}</dt><dd>{money(tot.vat)}</dd></div><div className="strong"><dt>{t('rfq.p.tot.gross')}</dt><dd>{money(tot.gross)}</dd></div></>}</dl>
    {payer === false && <p className="rof-help">{t('rfq.p.tot.noVat')}</p>}{payer == null && <p className="rof-help">{t('rfq.p.tot.pending')}</p>}
    {data.negotiatedTotal != null && <p className="rof-help"><b>{t('rfq.p.negotiated')}: {money(data.negotiatedTotal)}</b></p>}
    <p className="rof-help">{t('rfq.p.fiscalNote')}</p></section>);
  const privacy = <details className="rof-privacy" open={!locked}><summary>{t('rfq.p.privacy.title')}</summary><p>{t('rfq.p.privacy.text')}</p></details>;

  // după trimitere (sau cu cererea închisă): doar rezumatul, de citit
  if (locked){ const val = (v: string | undefined | null) => v || t('rfq.p.none');
    return (<main className="rof">
      <div className={`rof-banner${end === 'closed' ? ' closed' : ''}`} role="status"><h2>{t(end === 'closed' ? 'rfq.p.closed.title' : 'rfq.p.done.title')}</h2><p>{t(end === 'closed' ? 'rfq.p.closed.text' : 'rfq.p.done.text')}</p>
        {end !== 'closed' && sentAt && <p><small>{t('rfq.p.sentAt', { when: new Date(sentAt).toLocaleString(intlLocale(lang), { dateStyle: 'long', timeStyle: 'short' }) })}</small></p>}</div>
      {head}
      <section className="rof-sec" aria-labelledby="rof-l-h"><h2 id="rof-l-h">{t('rfq.p.s.lines')}</h2>
        <ul className="rof-sum">{lines.map(l => { const p = data.prices[l.rateId]; return <li key={l.rateId}><span><b>{l.label}</b><small>{qf(l.qty)} {unitL(l.unit)}</small></span>
          <span className="n">{typeof p === 'number' ? <><b>{money(r2(p * l.qty))}</b><small>{money(p)}/{unitL(l.unit)}</small></> : <em>{t(p === null ? 'rfq.p.lineDeclined' : 'rfq.p.none')}</em>}</span></li>; })}</ul></section>
      <section className="rof-sec" aria-labelledby="rof-i-h"><h2 id="rof-i-h">{t('rfq.p.s.inclusions')}</h2>
        <ul className="rof-sum">{INCLUSIONS.map(k => { const inc = data.inclusions[k]; return <li key={k}><span>{t(`rfq.inc.${k}`)}</span><span className="n">{!inc ? t('rfq.p.none') : inc.mode === 'separate' ? `${t('rfq.mode.separate')}: ${inc.amount != null ? money(inc.amount) : t('rfq.p.none')}` : t(`rfq.mode.${inc.mode}`)}</span></li>; })}</ul></section>
      <section className="rof-sec" aria-labelledby="rof-c-h"><h2 id="rof-c-h">{t('rfq.p.s.company')} · {t('rfq.p.s.terms')}</h2>
        <ul className="rof-sum">
          <li><span>{t('rfq.p.form')}</span><span className="n">{data.company.form ? t(`rfq.form.${data.company.form}`) : t('rfq.p.none')}</span></li>
          <li><span>{t('rfq.p.vat')}</span><span className="n">{payer == null ? t('rfq.p.none') : t(payer ? 'rfq.vat.yes' : 'rfq.vat.no')}</span></li>
          {data.company.name && <li><span>{t('rfq.p.name')}</span><span className="n">{data.company.name}</span></li>}
          {data.company.cui && <li><span>{t('rfq.p.cui')}</span><span className="n">{data.company.cui}</span></li>}
          <li><span>{t('rfq.p.advance')}</span><span className="n">{data.advancePct != null ? `${qf(data.advancePct)}%` : t('rfq.p.none')}</span></li>
          <li><span>{t('rfq.p.start')}</span><span className="n">{data.startDate ? day(data.startDate) : t('rfq.p.none')}</span></li>
          <li><span>{t('rfq.p.duration')}</span><span className="n">{val(data.durationDays?.toString())}</span></li>
          <li><span>{t('rfq.p.warranty')}</span><span className="n">{val(data.warrantyMonths?.toString())}</span></li>
          <li><span>{t('rfq.p.valid')}</span><span className="n">{val(data.validDays?.toString())}</span></li>
          {data.notes && <li className="col"><span>{t('rfq.p.notes')}</span><span className="rof-pre">{data.notes}</span></li>}</ul></section>
      {totals}{privacy}
    </main>); }

  const setInc = (k: Inclusion, mode: InclusionMode) => change(o => { const cur = o.inclusions[k]; if (cur?.mode === mode) delete o.inclusions[k]; else o.inclusions[k] = mode === 'separate' ? { mode, ...(cur?.amount != null ? { amount: cur.amount } : {}) } : { mode }; });
  const setTerm = (k: 'advancePct' | 'durationDays' | 'warrantyMonths' | 'validDays' | 'negotiatedTotal', v: number | undefined) => change(o => { if (v == null) delete o[k]; else o[k] = v; });
  const onCui = (s: string) => { setCui(s); const c = s.replace(/\s/g, '').toUpperCase(); if (!c || CUI_RE.test(c)) change(o => { if (c) o.company.cui = c; else delete o.company.cui; }); };
  const cuiBad = (() => { const c = cui.replace(/\s/g, '').toUpperCase(); return !!c && !CUI_RE.test(c); })();
  const saveKey = save === 'saved' ? 'rfq.p.saved' : save === 'saving' ? 'rfq.p.saving' : save === 'error' ? 'rfq.p.saveError' : save === 'conflict' ? 'rfq.p.conflict' : 'rfq.p.dirty';

  return (<main className="rof">
    {head}
    <p className="rof-intro">{t('rfq.p.intro')}</p>
    <p className={`rof-save ${save}`} role="status">{t(saveKey)}</p>
    {save === 'conflict' && <div className="issue WARNING" role="alert">{t('rfq.p.conflict')} <button type="button" className="btn sm" onClick={() => location.reload()}>{t('rfq.p.reload')}</button></div>}

    <section className="rof-sec" aria-labelledby="rof-l-h"><h2 id="rof-l-h">{t('rfq.p.s.lines')}</h2>
      <p className="rof-help">{t('rfq.p.priceHelp')}</p>
      {lines.map(l => { const p = data.prices[l.rateId], declined = p === null, pid = `rof-p-${l.rateId}`;
        return (<div key={l.rateId} className={`rof-line${miss(`price:${l.rateId}`) ? ' miss' : ''}${declined ? ' off' : ''}`}>
          <div className="rof-line-h"><b>{l.label}</b><span>{t('rfq.p.qty', { qty: `${qf(l.qty)} ${unitL(l.unit)}` })}</span></div>
          {l.rooms.length > 1 && <details className="rof-rooms"><summary>{t('rfq.p.rooms')}</summary><ul>{l.rooms.map((x, i) => <li key={i}>{x.name}: {qf(x.qty)} {unitL(l.unit)}</li>)}</ul></details>}
          <div className="rof-line-in">
            <label htmlFor={pid}>{t('rfq.p.priceLabel', { unit: unitL(l.unit) })}<span className="sr-only"> — {l.label}</span></label>
            <NumInput id={pid} value={typeof p === 'number' ? p : undefined} min={0.01} max={RFQ_LIMITS.priceMax} disabled={declined} aria-invalid={miss(`price:${l.rateId}`) || undefined} placeholder="0,00"
              onNum={v => change(o => { if (v == null) delete o.prices[l.rateId]; else o.prices[l.rateId] = v; })} onBlur={() => { void flush(); }} />
            <output aria-live="polite">{declined ? t('rfq.p.lineDeclined') : t('rfq.p.lineTotal', { total: typeof p === 'number' ? money(r2(p * l.qty)) : t('rfq.p.none') })}</output></div>
          <label className="rof-check"><input type="checkbox" checked={declined} aria-label={t('rfq.p.declineAria', { label: l.label })} onChange={e => change(o => { if (e.target.checked) o.prices[l.rateId] = null; else delete o.prices[l.rateId]; })} /> {t('rfq.p.decline')}</label>
        </div>); })}
      {cov.declined.length > 0 && <p className="rof-help">{tp('rfq.p.partialNote', cov.declined.length)}</p>}
    </section>

    <section className="rof-sec" aria-labelledby="rof-i-h"><h2 id="rof-i-h">{t('rfq.p.s.inclusions')}</h2>
      <p className="rof-help">{t('rfq.p.incIntro')}</p>
      {INCLUSIONS.map(k => { const inc = data.inclusions[k], gid = `rof-inc-${k}`;
        return (<div key={k} className={`rof-f${miss(`inclusion:${k}`) ? ' miss' : ''}`} role="group" aria-labelledby={gid}><span className="rof-q" id={gid}>{t(`rfq.inc.${k}`)}</span>
          <div className="rof-opts">{INCLUSION_MODES.map(m => <button key={m} type="button" className="rof-opt" aria-pressed={inc?.mode === m} onClick={() => setInc(k, m)}>{t(`rfq.mode.${m}`)}</button>)}</div>
          {inc?.mode === 'separate' && <label className="rof-amount">{t('rfq.p.incAmount')}<NumInput value={inc.amount} min={0.01} max={RFQ_LIMITS.amountMax} aria-label={t('rfq.p.incAmountAria', { label: t(`rfq.inc.${k}`) })} placeholder="0,00"
            onNum={v => change(o => { const x = o.inclusions[k]; if (!x) return; if (v == null) delete x.amount; else x.amount = v; })} onBlur={() => { void flush(); }} /></label>}
        </div>); })}
    </section>

    <section className="rof-sec" aria-labelledby="rof-c-h"><h2 id="rof-c-h">{t('rfq.p.s.company')}</h2>
      <div className={`rof-f${miss('form') ? ' miss' : ''}`} role="group" aria-labelledby="rof-form"><span className="rof-q" id="rof-form">{t('rfq.p.form')}</span>
        <div className="rof-opts">{COMPANY_FORMS.map(f => <button key={f} type="button" className="rof-opt" aria-pressed={data.company.form === f} onClick={() => change(o => { if (o.company.form === f) delete o.company.form; else o.company.form = f; })}>{t(`rfq.form.${f}`)}</button>)}</div></div>
      <div className={`rof-f${miss('vatPayer') ? ' miss' : ''}`} role="group" aria-labelledby="rof-vat"><span className="rof-q" id="rof-vat">{t('rfq.p.vat')}</span>
        <div className="rof-opts">{[true, false].map(b => <button key={String(b)} type="button" className="rof-opt" aria-pressed={payer === b} onClick={() => change(o => { if (o.company.vatPayer === b) delete o.company.vatPayer; else o.company.vatPayer = b; })}>{t(b ? 'rfq.vat.yes' : 'rfq.vat.no')}</button>)}</div></div>
      <label className="rof-f"><span className="rof-q">{t('rfq.p.name')}</span><input type="text" maxLength={RFQ_LIMITS.name} autoComplete="organization" value={data.company.name ?? ''} onChange={e => change(o => { if (e.target.value.trim()) o.company.name = e.target.value; else delete o.company.name; })} onBlur={() => { void flush(); }} /></label>
      <label className="rof-f"><span className="rof-q">{t('rfq.p.cui')}</span><input type="text" maxLength={RFQ_LIMITS.cui} inputMode="text" autoComplete="off" value={cui} aria-invalid={cuiBad || undefined} aria-describedby={cuiBad ? 'rof-cui-err' : undefined} onChange={e => onCui(e.target.value)} onBlur={() => { void flush(); }} />
        {cuiBad && <small id="rof-cui-err" className="rof-err">{t('rfq.p.cuiBad')}</small>}</label>
    </section>

    <section className="rof-sec" aria-labelledby="rof-t-h"><h2 id="rof-t-h">{t('rfq.p.s.terms')}</h2>
      <div className="rof-grid">
        <label className={`rof-f${miss('advancePct') ? ' miss' : ''}`}><span className="rof-q">{t('rfq.p.advance')}</span><NumInput value={data.advancePct} min={0} max={100} aria-invalid={miss('advancePct') || undefined} onNum={v => setTerm('advancePct', v)} onBlur={() => { void flush(); }} /></label>
        <label className={`rof-f${miss('startDate') ? ' miss' : ''}`}><span className="rof-q">{t('rfq.p.start')}</span><input type="date" value={data.startDate ?? ''} aria-invalid={miss('startDate') || undefined} onChange={e => change(o => { if (e.target.value) o.startDate = e.target.value; else delete o.startDate; })} onBlur={() => { void flush(); }} /></label>
        <label className={`rof-f${miss('durationDays') ? ' miss' : ''}`}><span className="rof-q">{t('rfq.p.duration')}</span><NumInput int value={data.durationDays} min={1} max={RFQ_LIMITS.days} aria-invalid={miss('durationDays') || undefined} onNum={v => setTerm('durationDays', v)} onBlur={() => { void flush(); }} /></label>
        <label className={`rof-f${miss('warrantyMonths') ? ' miss' : ''}`}><span className="rof-q">{t('rfq.p.warranty')}</span><NumInput int value={data.warrantyMonths} min={0} max={RFQ_LIMITS.months} aria-invalid={miss('warrantyMonths') || undefined} onNum={v => setTerm('warrantyMonths', v)} onBlur={() => { void flush(); }} /></label>
        <label className={`rof-f${miss('validDays') ? ' miss' : ''}`}><span className="rof-q">{t('rfq.p.valid')}</span><NumInput int value={data.validDays} min={1} max={RFQ_LIMITS.validDays} aria-invalid={miss('validDays') || undefined} onNum={v => setTerm('validDays', v)} onBlur={() => { void flush(); }} /></label>
      </div>
      <label className="rof-f"><span className="rof-q">{t('rfq.p.negotiated')}</span><NumInput value={data.negotiatedTotal} min={0.01} max={RFQ_LIMITS.amountMax * 10} aria-describedby="rof-neg-h" onNum={v => setTerm('negotiatedTotal', v)} onBlur={() => { void flush(); }} /><small id="rof-neg-h">{t('rfq.p.negotiatedHelp')}</small></label>
      <label className="rof-f"><span className="rof-q">{t('rfq.p.notes')}</span><textarea rows={3} maxLength={RFQ_LIMITS.notes} value={data.notes ?? ''} onChange={e => change(o => { if (e.target.value.trim()) o.notes = e.target.value; else delete o.notes; })} onBlur={() => { void flush(); }} /></label>
    </section>

    {totals}{privacy}
    {msg && <div className="issue WARNING" role="alert">{msg}</div>}
    {missing.length > 0 && <div className="issue ERROR" role="alert" tabIndex={-1} ref={errRef}><b>{t('rfq.p.missing')}</b><ul>{missing.map(k => <li key={k}>{missLabel(k)}</li>)}</ul></div>}
    <nav className="rof-nav"><span className="rof-nav-tot">{payer === true ? t('rfq.p.tot.gross') : t('rfq.p.tot.net')}: <b>{money(payer === true ? tot.gross : r2(tot.net + tot.separate))}</b></span>
      <button type="button" className="btn lg primary" disabled={busy || save === 'conflict'} onClick={submit}>{busy ? t('rfq.p.submitting') : t('rfq.p.submit')}</button></nav>
  </main>);
}
