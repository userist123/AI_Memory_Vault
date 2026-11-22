'use client';
// „Cursuri valutare”: apare doar când proiectul are produse la comandă în altă monedă. Un rând pe monedă: cursul (lei pentru
// 1 unitate), data, sursa, „Preia cursul BNR” și editare de mână. Cursurile stau în proiect (Snapshot.fx), deci o revizie le îngheață.
import { useEffect, useState } from 'react';
import { usePrefs } from '@/lib/prefs';
import { intlLocale } from '@/lib/i18n';
import { cleanRate, formatRate, validFxDate, currencyLabel, FX_LIMITS, type FxRates } from '@/core/fx';

const todayIso = () => new Date().toISOString().slice(0, 10);
type Bnr = { date: string; rates: Record<string, number> };
export default function FxRatesBox({ projectId, used, rates, cur, onChange, say }: { projectId: string; used: string[]; rates: FxRates | undefined; cur: string; onChange(next: FxRates): void | Promise<void>; say?(s: string): void }){
  const { t, lang } = usePrefs(), loc = intlLocale(lang), has = (c: string) => !!rates && Object.hasOwn(rates, c);
  const [draft, setDraft] = useState<Record<string, string>>({}), [busy, setBusy] = useState<string | null>(null), [msg, setMsg] = useState<{ c: string; text: string; bad: boolean } | null>(null);
  // un curs schimbat din altă parte (BNR, anulare) înlocuiește ce era tastat
  useEffect(() => { setDraft({}); }, [rates]);
  if (!used.length) return null;
  const missing = used.filter(c => !has(c)), label = currencyLabel(cur, loc);
  const put = async (c: string, r: FxRates[string] | null) => { const next: FxRates = { ...(rates ?? {}) }; if (r) next[c] = r; else delete next[c]; await onChange(next); };
  async function commitRate(c: string){
    const raw = draft[c]; if (raw === undefined) return; const txt = raw.trim().replace(/\s/g, '').replace(',', '.');
    if (txt === ''){ setMsg(null); if (has(c)) await put(c, null); return; }
    const rate = /^\d+(\.\d+)?$/.test(txt) ? cleanRate(Number(txt)) : null;
    if (rate == null){ setMsg({ c, text: t('fx.invalid', { max: FX_LIMITS.rateMax }), bad: true }); return; }
    setMsg(null); if (has(c) && rates![c]!.rate === rate){ setDraft(d => { const { [c]: _x, ...rest } = d; return rest; }); return; }
    await put(c, { rate, date: todayIso(), source: 'manual' });
  }
  async function setDate(c: string, date: string){ if (!has(c) || !validFxDate(date)) return; await put(c, { ...rates![c]!, date, source: 'manual' }); }
  async function fromBnr(c: string){ setBusy(c); setMsg(null);
    try { const res = await fetch(`/api/projects/${projectId}/fx/bnr`), j = await res.json().catch(() => null) as (Bnr & { error?: string }) | null;
      if (!res.ok || !j?.rates){ setMsg({ c, text: j?.error || t('fx.bnrError'), bad: true }); return; }
      const rate = Object.hasOwn(j.rates, c) ? cleanRate(j.rates[c]) : null;
      if (rate == null || !validFxDate(j.date)){ setMsg({ c, text: t('fx.bnrNoCur', { c }), bad: true }); return; }
      await put(c, { rate, date: j.date, source: 'bnr' });
      const text = t('fx.bnrOk', { c, r: formatRate(rate, loc), d: new Date(j.date + 'T12:00:00').toLocaleDateString(loc, { day: 'numeric', month: 'long', year: 'numeric' }) }); setMsg({ c, text, bad: false }); say?.(text);
    } catch { setMsg({ c, text: t('fx.bnrError'), bad: true }); } finally { setBusy(null); } }
  return (<section className="fxbox" aria-label={t('fx.title')}>
    <h2>{t('fx.title')}</h2>
    <p className="muted small">{t('fx.hint', { cur: label })}</p>
    {missing.length > 0 && <p className="issue WARNING" role="alert">{t('fx.missing', { list: missing.join(', ') })}</p>}
    <ul>{used.map(c => { const r = has(c) ? rates![c]! : null, val = draft[c] ?? (r ? formatRate(r.rate, loc) : '');
      return <li key={c} className={r ? '' : 'is-missing'}>
        <label className="fx-rate"><span>{t('fx.rate', { c })}</span><input inputMode="decimal" value={val} placeholder={t('fx.ratePlaceholder')} aria-label={t('fx.rateAria', { c, cur: label })} aria-invalid={msg?.c === c && msg.bad}
          onChange={e => setDraft(d => ({ ...d, [c]: e.target.value }))} onBlur={() => void commitRate(c)} onKeyDown={e => { if (e.key === 'Enter'){ e.preventDefault(); void commitRate(c); } }} /><em>{label}</em></label>
        <label className="fx-date"><span className="sr-only">{t('fx.dateAria', { c })}</span><input type="date" value={r?.date ?? ''} disabled={!r} max={todayIso()} aria-label={t('fx.dateAria', { c })} onChange={e => void setDate(c, e.target.value)} /></label>
        <span className={`fx-src${r ? '' : ' late'}`}>{t(r ? `fx.src.${r.source}` : 'fx.src.none')}</span>
        <button type="button" className="btn" disabled={busy != null} onClick={() => void fromBnr(c)}>{busy === c ? t('fx.bnrBusy') : t('fx.bnr')}</button>
        {msg?.c === c && <small className={msg.bad ? 'late' : 'muted'} role={msg.bad ? 'alert' : 'status'}>{msg.text}</small>}
      </li>; })}</ul>
    <p className="muted small">{t('fx.note')}</p>
  </section>);
}
