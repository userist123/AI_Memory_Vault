'use client';
// Panoul „Cereri de ofertă către meseriași” din etapa Ofertă (doar pentru designer): cerere nouă pe meserii (lista de cantități
// se îngheață la creare), câte un link pe meseriaș, starea invitațiilor, compararea ofertelor trimise pe aceeași bază
// (persoană fizică = cu TVA / firmă = fără TVA) și „Atribuie”, care scrie prețurile contractate pe proiect.
// Nimic de aici nu ajunge pe linkul clientului; meseriașii nu văd clientul, bugetul sau ofertele celorlalți.
import { useEffect, useMemo, useState } from 'react';
import { createPortal } from 'react-dom';
import { ListSkeleton } from './Skeleton';
import { usePrefs } from '@/lib/prefs';
import { intlLocale } from '@/lib/i18n';
import { formatMoney, formatNumber, formatPrice } from '@/core/format';
import { askConfirm } from '@/lib/dialog';
import { FEE_EVENT } from './ChainTable';
import { TRADES, INCLUSIONS, RFQ_LIMITS, compareOffers, type Trade, type RfqLine, type RfqOffer, type Basis, type LaborContract, type OfferWarning } from '@/core/rfq';

type Invite = { key: string; label: string; active: boolean; token?: string; path?: string; url?: string | null; publicUrl: boolean; createdAt: string; updatedAt: string; submittedAt: string | null;
  expiresAt: string | null; revokedAt: string | null; started: boolean; offer: RfqOffer | null };
type Req = { id: string; title: string; trades: Trade[]; lines: RfqLine[]; note: string; siteCity: string | null; dueDate: string | null; createdAt: string; closedAt: string | null; awarded: string | null; invites: Invite[] };
type State = { requests: Req[]; available: RfqLine[]; laborContract: LaborContract; projectRev: number; vatRate: number };
const unitL = (u: string) => u === 'm2' ? 'm²' : u;

export default function RfqPanel({ id, cur, say, flush, onContract }: { id: string; cur: string; say(s: string): void; flush?(): Promise<boolean>; onContract?(c: LaborContract, draftRev: number): void }){
  const { t, tp, lang } = usePrefs(), api = `/api/projects/${id}/rfq`;
  const [st, setSt] = useState<State | null>(null), [loadErr, setLoadErr] = useState<string | null>(null), [busy, setBusy] = useState(false);
  const [form, setForm] = useState<{ title: string; trades: Trade[]; city: string; due: string; note: string } | null>(null), [names, setNames] = useState<Record<string, string>>({}), [basis, setBasis] = useState<Basis>('individual'), [wide, setWide] = useState<string | null>(null);
  const money = (v: number | null | undefined) => formatMoney(v, cur, lang, '—'), price = (v: number | null | undefined) => formatPrice(v, cur, lang), qf = (v: number) => formatNumber(v, lang, { max: 2 });
  const day = (s: string) => new Date(s.slice(0, 10) + 'T12:00:00').toLocaleDateString(intlLocale(lang), { day: 'numeric', month: 'short', year: 'numeric' });
  const when = (s: string) => new Date(s).toLocaleString(intlLocale(lang), { dateStyle: 'medium', timeStyle: 'short' });
  const load = () => fetch(api).then(async x => { const j = await x.json().catch(() => null); if (!x.ok){ setLoadErr(j?.error || t('rfq.d.loadError')); return null; } setSt(j); setLoadErr(null); return j as State; }).catch(() => { setLoadErr(t('rfq.d.loadError')); return null; });
  useEffect(() => { setSt(null); setForm(null); void load(); }, [id]); // eslint-disable-line
  // tabelul de comparare mărit pe tot ecranul (panoul lateral e îngust): Esc îl închide
  useEffect(() => { if (!wide) return; const on = (e: KeyboardEvent) => { if (e.key === 'Escape') setWide(null); }; window.addEventListener('keydown', on); return () => window.removeEventListener('keydown', on); }, [wide]);
  async function call(url: string, method: string, body?: unknown): Promise<any | null> {
    if (busy) return null; setBusy(true);
    try { const res = await fetch(url, { method, headers: body === undefined ? undefined : { 'content-type': 'application/json' }, body: body === undefined ? undefined : JSON.stringify(body) }), j = await res.json().catch(() => ({}));
      if (!res.ok){ say(j.error || t('rfq.d.failed')); if (res.status === 409 || res.status === 404) void load(); return null; } setSt(j); return j;
    } catch { say(t('rfq.d.failed')); return null; } finally { setBusy(false); }
  }
  // rândurile de manoperă de acum, pe meserii: ce ar îngheța o cerere creată în acest moment
  const byTrade = useMemo(() => TRADES.map(tr => ({ trade: tr, lines: (st?.available ?? []).filter(l => l.trade === tr) })).filter(x => x.lines.length), [st]);
  // denumirea și unitatea unui tarif: din deviz sau din rândurile înghețate ale cererilor
  const rateInfo = useMemo(() => { const m = new Map<string, { label: string; unit: string }>(); for (const l of [...(st?.requests.flatMap(r => r.lines) ?? []), ...(st?.available ?? [])]) m.set(l.rateId, { label: l.label, unit: l.unit }); return m; }, [st]);
  if (loadErr && !st) return <section className="rfq"><h3>{t('rfq.d.title')}</h3><p className="bad" role="alert">{loadErr}</p></section>;
  if (!st) return <section className="rfq"><h3>{t('rfq.d.title')}</h3><ListSkeleton label={t('print.loading')} rows={2} /></section>;

  const linkOf = (i: Invite) => i.url || `${location.origin}${i.path}`;
  const copy = async (i: Invite) => { const url = linkOf(i); try { await navigator.clipboard.writeText(url); say(t('rfq.d.invite.copied', { label: i.label })); } catch { say(t('rfq.d.invite.linkShown', { label: i.label, url })); } };
  async function create(){ if (!form) return;
    const j = await call(api, 'POST', { title: form.title, trades: form.trades, ...(form.city.trim() ? { siteCity: form.city } : {}), ...(form.due ? { dueDate: form.due } : {}), ...(form.note.trim() ? { note: form.note } : {}) });
    if (j){ setForm(null); say(t('rfq.d.created')); } }
  async function addInvite(r: Req){ const label = (names[r.id] ?? '').trim(); if (!label) return;
    const j = await call(`${api}/${r.id}/invites`, 'POST', { label }); if (!j) return; setNames(n => ({ ...n, [r.id]: '' }));
    const inv = (j as State).requests.find(x => x.id === r.id)?.invites.find(x => x.key === j.key); if (inv?.active) void copy(inv); }
  const revoke = async (r: Req, i: Invite) => { if (await askConfirm({ title: t('rfq.d.confirmRevoke', { label: i.label }), body: t('rfq.d.confirmRevokeBody'), confirmLabel: t('rfq.d.revoke'), danger: true })) void call(`${api}/${r.id}/invites/${i.key}`, 'PATCH', { action: 'revoke' }); };
  const removeInvite = async (r: Req, i: Invite) => { if (await askConfirm({ title: t('rfq.d.confirmDelete', { label: i.label }), body: t('rfq.d.confirmDeleteBody'), confirmLabel: t('rfq.d.delete'), danger: true })) void call(`${api}/${r.id}/invites/${i.key}`, 'DELETE'); };
  const removeReq = async (r: Req) => { if (await askConfirm({ title: t('rfq.d.confirmDeleteReq', { title: r.title }), body: t('rfq.d.confirmDeleteReqBody'), confirmLabel: t('rfq.d.deleteReq'), danger: true })) void call(`${api}/${r.id}`, 'DELETE'); };
  // scrierea prețurilor contractate: ciorna din editor se salvează întâi, apoi se citește versiunea proiectului (409 dacă s-a schimbat între timp)
  async function withRev(fn: (rev: number) => Promise<any | null>){
    if (flush && !(await flush())){ say(t('rfq.d.failed')); return null; }
    const fresh = await load(); if (!fresh) return null;
    const j = await fn(fresh.projectRev); if (!j) return null;
    onContract?.(j.laborContract, j.draftRev); window.dispatchEvent(new CustomEvent(FEE_EVENT, { detail: { id } })); return j; }
  async function award(r: Req, i: Invite){ const n = i.offer ? r.lines.filter(l => typeof i.offer!.prices[l.rateId] === 'number').length : 0;
    if (!await askConfirm({ title: t('rfq.d.awardConfirm', { label: i.label }), body: t('rfq.d.awardBody', { n }), confirmLabel: t('rfq.d.award') })) return;
    const j = await withRev(rev => call(`${api}/${r.id}`, 'PATCH', { action: 'award', invite: i.key, rev }));
    if (j) say(`${t('rfq.d.awardedDone', { n: j.applied.length, label: i.label })}${j.partial ? ` ${t('rfq.d.awardedPartial')}` : ''}`); }
  async function unaward(r: Req){ if (!await askConfirm({ title: t('rfq.d.unawardConfirm'), body: t('rfq.d.unawardBody'), confirmLabel: t('rfq.d.unaward'), danger: true })) return;
    if (await withRev(rev => call(`${api}/${r.id}`, 'PATCH', { action: 'unaward', rev }))) say(t('rfq.d.unawarded')); }
  async function clearContract(){ if (!await askConfirm({ title: t('rfq.d.contract.clearConfirm'), body: t('rfq.d.contract.clearBody'), confirmLabel: t('rfq.d.contract.clear'), danger: true })) return;
    if (await withRev(rev => call(api, 'PATCH', { action: 'clearContract', rev }))) say(t('rfq.d.contract.cleared')); }

  const statusOf = (i: Invite) => i.submittedAt ? t('rfq.d.st.submitted', { when: when(i.submittedAt) }) : i.started ? t('rfq.d.st.draft', { when: when(i.updatedAt) }) : t('rfq.d.st.empty');
  const linkState = (i: Invite) => i.active ? (i.expiresAt ? t('rfq.d.st.expires', { when: day(i.expiresAt) }) : '') : t(i.revokedAt ? 'rfq.d.st.revoked' : 'rfq.d.st.expired');
  const warnText = (w: OfferWarning, r: Req) => w.code === 'BELOW_MARKET' ? t('rfq.d.warn.BELOW_MARKET', { label: r.lines.find(l => l.rateId === w.rateId)?.label ?? w.rateId, pct: w.pct })
    : w.code === 'EXCLUDED' ? t('rfq.d.warn.EXCLUDED', { what: t(`rfq.inc.${w.key}`) }) : tp(`rfq.d.warn.${w.code}`, w.n);
  const contract = Object.entries(st.laborContract);
  const toggleTrade = (tr: Trade) => setForm(f => f && ({ ...f, trades: f.trades.includes(tr) ? f.trades.filter(x => x !== tr) : [...f.trades, tr] }));

  return (<section className="rfq" aria-labelledby="rfq-h">
    <h3 id="rfq-h">{t('rfq.d.title')}</h3>
    <p className="muted small">{t('rfq.d.intro')}</p>

    {contract.length > 0 && <div className="rfq-contract"><h4>{t('rfq.d.contract.title')}</h4>
      <ul>{contract.map(([rateId, c]) => { const info = rateInfo.get(rateId); return <li key={rateId}><b>{info?.label ?? rateId}</b>
        <span>{t('rfq.d.contract.row', { price: price(c.unitPrice), unit: unitL(info?.unit ?? ''), vat: c.vatPayer ? t('rfq.contracted.vat') : '', team: c.label })}</span></li>; })}</ul>
      <p className="muted small">{t('rfq.d.contract.note')}</p>
      <button className="btn sm danger" disabled={busy} onClick={clearContract}>{t('rfq.d.contract.clear')}</button></div>}

    {!form ? (byTrade.length ? <p><button className="btn" disabled={busy} onClick={() => setForm({ title: '', trades: [], city: '', due: '', note: '' })}>{t('rfq.d.new')}</button></p> : <p className="muted small">{t('rfq.d.noLabor')}</p>)
      : <form className="rfq-form" onSubmit={e => { e.preventDefault(); void create(); }}>
        <label className="rfq-f"><span>{t('rfq.d.f.title')}</span><input value={form.title} maxLength={RFQ_LIMITS.title} required placeholder={t('rfq.d.f.titlePh')} onChange={e => setForm({ ...form, title: e.target.value })} /></label>
        <fieldset className="rfq-trades"><legend>{t('rfq.d.f.trades')}</legend>
          {byTrade.map(x => <label key={x.trade} className="rfq-trade"><input type="checkbox" checked={form.trades.includes(x.trade)} onChange={() => toggleTrade(x.trade)} />
            <span><b>{t(`rfq.trade.${x.trade}`)}</b> <em>{tp('rfq.d.tradeRows', x.lines.length)}</em>
              <small>{x.lines.map(l => `${l.label} — ${qf(l.qty)} ${unitL(l.unit)}`).join(' · ')}</small></span></label>)}</fieldset>
        <div className="rfq-row2">
          <label className="rfq-f"><span>{t('rfq.d.f.city')}</span><input value={form.city} maxLength={RFQ_LIMITS.city} onChange={e => setForm({ ...form, city: e.target.value })} /></label>
          <label className="rfq-f"><span>{t('rfq.d.f.due')}</span><input type="date" value={form.due} onChange={e => setForm({ ...form, due: e.target.value })} /></label></div>
        <p className="muted small">{t('rfq.d.f.cityHelp')}</p>
        <label className="rfq-f"><span>{t('rfq.d.f.note')}</span><textarea rows={3} value={form.note} maxLength={RFQ_LIMITS.note} placeholder={t('rfq.d.f.notePh')} onChange={e => setForm({ ...form, note: e.target.value })} /></label>
        <p className="muted small">{t('rfq.d.frozen')}</p>
        <div className="rfq-btns"><button type="submit" className="btn primary" disabled={busy || !form.title.trim() || !form.trades.length}>{t('rfq.d.create')}</button><button type="button" className="btn" onClick={() => setForm(null)}>{t('rfq.d.cancel')}</button></div>
      </form>}

    {st.requests.map(r => { const sent = r.invites.filter(i => i.offer), winner = r.invites.find(i => i.key === r.awarded);
      const cmp = sent.length ? compareOffers(r.lines, sent.map(i => ({ id: i.key, label: i.label, offer: i.offer! })), basis, st.vatRate) : null;
      const offerOf = (key: string) => sent.find(i => i.key === key)!.offer!;
      // același tabel în panou (cu derulare) și, mărit, peste tot ecranul (în .ws, ca să păstreze tema)
      const table = cmp && (<table className="rfq-cmp">
            <thead><tr><th scope="col">{t('rfq.d.col.work')}</th>{cmp.offers.map(o => <th key={o.id} scope="col" className={`n${o.id === r.awarded ? ' won' : ''}`}>{o.label}{o.id === r.awarded && <small>{t('rfq.d.awardedMark')}</small>}</th>)}</tr></thead>
            <tbody>
              {cmp.rows.map(row => <tr key={row.rateId}><th scope="row">{row.label}<small>{qf(row.qty)} {unitL(row.unit)} · {t('rfq.d.marketRange', { low: price(row.ref.low), high: price(row.ref.expected), unit: unitL(row.unit) })}</small></th>
                {cmp.offers.map(o => { const c = row.cells[o.id]!; return <td key={o.id} className={`n${c.min ? ' min' : ''}${c.below ? ' below' : ''}${c.unit == null ? ' none' : ''}`}>
                  {c.unit == null ? <em>{t(c.declined ? 'rfq.d.cell.declined' : 'rfq.d.cell.missing')}</em> : <><b>{t('rfq.d.cell.price', { price: price(c.basisUnit), unit: unitL(row.unit) })}</b>{c.min && <span className="rfq-tag">{t('rfq.d.cell.min')}</span>}
                    <small>{money(c.value)}</small>{c.below && <small className="rfq-warn">{t('rfq.d.cell.below', { pct: Math.round(-(c.refPct ?? 0)) })}</small>}</>}</td>; })}</tr>)}
              <tr className="sum"><th scope="row">{t('rfq.d.row.net')}</th>{cmp.offers.map(o => <td key={o.id} className="n">{money(o.net)}</td>)}</tr>
              {cmp.offers.some(o => o.separate > 0) && <tr><th scope="row">{t('rfq.d.row.separate')}</th>{cmp.offers.map(o => <td key={o.id} className="n">{o.separate > 0 ? money(o.separate) : '—'}</td>)}</tr>}
              <tr><th scope="row">{t('rfq.d.row.vat')}</th>{cmp.offers.map(o => <td key={o.id} className="n">{o.vatPayer ? money(o.vat) : t('rfq.vat.no')}</td>)}</tr>
              <tr className="total"><th scope="row">{t('rfq.d.row.total')}<small>{t(`rfq.d.basis.${basis}`)}</small></th>{cmp.offers.map(o => <td key={o.id} className="n"><b>{money(o.total)}</b></td>)}</tr>
              <tr><th scope="row">{t('rfq.d.row.rank')}</th>{cmp.offers.map(o => <td key={o.id} className={`n${o.rank === 1 ? ' min' : ''}`}>{o.rank != null ? t('rfq.d.rank', { n: o.rank }) : <em>{t(o.partial ? 'rfq.d.rank.partial' : 'rfq.d.rank.incomplete')}</em>}</td>)}</tr>
              {cmp.offers.some(o => o.negotiated != null) && <tr><th scope="row">{t('rfq.d.row.negotiated')}</th>{cmp.offers.map(o => <td key={o.id} className="n">{o.negotiated != null ? money(o.negotiated) : '—'}</td>)}</tr>}
              <tr><th scope="row">{t('rfq.d.row.company')}</th>{cmp.offers.map(o => { const c = offerOf(o.id).company; return <td key={o.id} className="n">{o.form ? t(`rfq.form.${o.form}`) : '—'} · {t(o.vatPayer ? 'rfq.vat.yes' : 'rfq.vat.no')}{(c.name || c.cui) && <small>{[c.name, c.cui].filter(Boolean).join(' · ')}</small>}</td>; })}</tr>
              <tr><th scope="row">{t('rfq.d.row.advance')}</th>{cmp.offers.map(o => <td key={o.id} className="n">{offerOf(o.id).advancePct != null ? `${qf(offerOf(o.id).advancePct!)}%` : '—'}</td>)}</tr>
              <tr><th scope="row">{t('rfq.d.row.start')}</th>{cmp.offers.map(o => <td key={o.id} className="n">{offerOf(o.id).startDate ? day(offerOf(o.id).startDate!) : '—'}</td>)}</tr>
              <tr><th scope="row">{t('rfq.d.row.duration')}</th>{cmp.offers.map(o => <td key={o.id} className="n">{offerOf(o.id).durationDays != null ? tp('rfq.d.days', offerOf(o.id).durationDays!) : '—'}</td>)}</tr>
              <tr><th scope="row">{t('rfq.d.row.warranty')}</th>{cmp.offers.map(o => <td key={o.id} className="n">{offerOf(o.id).warrantyMonths != null ? tp('rfq.d.months', offerOf(o.id).warrantyMonths!) : '—'}</td>)}</tr>
              <tr><th scope="row">{t('rfq.d.row.valid')}</th>{cmp.offers.map(o => <td key={o.id} className="n">{offerOf(o.id).validDays != null ? tp('rfq.d.validDays', offerOf(o.id).validDays!) : '—'}</td>)}</tr>
              {INCLUSIONS.map(k => <tr key={k}><th scope="row">{t(`rfq.inc.${k}`)}</th>{cmp.offers.map(o => { const inc = offerOf(o.id).inclusions[k];
                return <td key={o.id} className={`n${inc?.mode === 'excluded' ? ' below' : ''}`}>{!inc ? '—' : inc.mode === 'separate' ? `${t('rfq.mode.separate')}: ${money(inc.amount)}` : t(`rfq.mode.${inc.mode}`)}</td>; })}</tr>)}
              {cmp.offers.some(o => offerOf(o.id).notes) && <tr><th scope="row">{t('rfq.d.row.notes')}</th>{cmp.offers.map(o => <td key={o.id} className="rfq-notes">{offerOf(o.id).notes ?? '—'}</td>)}</tr>}
              <tr><th scope="row">{t('rfq.d.row.warn')}</th>{cmp.offers.map(o => <td key={o.id} className="rfq-warns">{o.warnings.length ? <ul>{o.warnings.map((w, k) => <li key={k}>{warnText(w, r)}</li>)}</ul> : <span className="muted">{t('rfq.d.warn.none')}</span>}</td>)}</tr>
              <tr className="act"><th scope="row">{t('rfq.d.row.action')}</th>{cmp.offers.map(o => { const inv = sent.find(i => i.key === o.id)!;
                return <td key={o.id} className="n">{r.awarded === o.id ? <b>{t('rfq.d.awardedMark')}</b> : !r.awarded ? <button className="btn sm primary" disabled={busy} onClick={() => award(r, inv)}>{t('rfq.d.award')}<span className="sr-only">: {o.label}</span></button> : '—'}</td>; })}</tr>
            </tbody></table>);
      return (<article key={r.id} className="rfq-req" aria-label={r.title}>
        <header><h4>{r.title}</h4>
          <p className="rfq-meta">{[r.trades.map(tr => t(`rfq.trade.${tr}`)).join(', '), t('rfq.d.meta.created', { date: day(r.createdAt) }), r.dueDate ? t('rfq.d.meta.due', { date: day(r.dueDate) }) : '', r.siteCity ?? ''].filter(Boolean).join(' · ')}</p>
          {winner && <p className="rfq-awarded"><b>{t('rfq.d.awarded', { label: winner.label })}</b></p>}
          {r.note && <p className="rfq-note">{r.note}</p>}</header>
        <details className="rfq-lines"><summary>{tp('rfq.d.lines', r.lines.length)}</summary>
          <div className="rfq-scroll"><table><thead><tr><th>{t('rfq.d.col.work')}</th><th className="n">{t('rfq.d.col.qty')}</th><th className="n">{t('rfq.d.col.market')}</th></tr></thead>
            <tbody>{r.lines.map(l => <tr key={l.rateId}><td>{l.label}<small>{l.rooms.map(x => `${x.name} ${qf(x.qty)}`).join(' · ')}</small></td><td className="n">{qf(l.qty)} {unitL(l.unit)}</td>
              <td className="n">{t('rfq.d.marketRange', { low: price(l.ref.low), high: price(l.ref.expected), unit: unitL(l.unit) })}</td></tr>)}</tbody></table></div>
          <p className="muted small">{t('rfq.d.marketNote')}</p></details>

        <ul className="rfq-invites">{r.invites.length === 0 && <li className="muted small">{t('rfq.d.invite.none')}</li>}
          {r.invites.map(i => <li key={i.key} className={i.key === r.awarded ? 'won' : ''}>
            <span className="rfq-who"><b>{i.label}</b>{i.key === r.awarded && <em> · {t('rfq.d.awardedMark')}</em>}<small>{[statusOf(i), linkState(i)].filter(Boolean).join(' · ')}</small></span>
            <span className="rfq-btns">{i.active && <button className="btn sm" onClick={() => copy(i)}>{t('rfq.d.copy')}</button>}
              {i.active && <button className="btn sm" disabled={busy} onClick={() => revoke(r, i)}>{t('rfq.d.revoke')}</button>}
              {i.submittedAt && !r.closedAt && <button className="btn sm" disabled={busy} onClick={() => call(`${api}/${r.id}/invites/${i.key}`, 'PATCH', { action: 'reopen' })}>{t('rfq.d.reopen')}</button>}
              {i.key !== r.awarded && <button className="btn sm danger" disabled={busy} onClick={() => removeInvite(r, i)}>{t('rfq.d.delete')}</button>}</span></li>)}</ul>
        {r.invites.some(i => i.active && !i.publicUrl) && <p className="muted small">{t('rfq.d.localOnly')}</p>}
        {!r.closedAt && <form className="rfq-add" onSubmit={e => { e.preventDefault(); void addInvite(r); }}>
          <input value={names[r.id] ?? ''} maxLength={RFQ_LIMITS.label} aria-label={t('rfq.d.invite.label')} placeholder={t('rfq.d.invite.ph')} onChange={e => setNames({ ...names, [r.id]: e.target.value })} />
          <button type="submit" className="btn" disabled={busy || !(names[r.id] ?? '').trim()}>{t('rfq.d.invite.add')}</button></form>}

        <h5>{t('rfq.d.compare')}</h5>
        {!cmp ? <p className="muted small">{t('rfq.d.noOffers')}</p> : <>
          <fieldset className="rfq-basis"><legend>{t('rfq.d.basis')}</legend>
            {(['individual', 'company'] as const).map(b => <label key={b}><input type="radio" name={`rfq-basis-${r.id}`} checked={basis === b} onChange={() => setBasis(b)} /> {t(`rfq.d.basis.${b}`)}</label>)}</fieldset>
          <p><button type="button" className="btn sm" onClick={() => setWide(r.id)}>{t('rfq.d.wide')}</button></p>
          <div className="rfq-scroll">{table}</div>
          {wide === r.id && createPortal(<div className="rfq rfq-wide" role="dialog" aria-modal="true" aria-label={`${t('rfq.d.compare')}: ${r.title}`}>
            <div className="rfq-wide-h"><b>{t('rfq.d.compare')}: {r.title}</b><span>{t(`rfq.d.basis.${basis}`)}</span><button type="button" className="btn sm" autoFocus onClick={() => setWide(null)}>{t('rfq.d.wideClose')}</button></div>
            {table}</div>, document.querySelector('.ws') ?? document.body)}
          <p className="muted small">{t('rfq.d.basisNote')}</p></>}
        <div className="rfq-btns">{r.awarded ? <button className="btn sm danger" disabled={busy} onClick={() => unaward(r)}>{t('rfq.d.unaward')}</button>
          : <button className="btn sm danger" disabled={busy} onClick={() => removeReq(r)}>{t('rfq.d.deleteReq')}</button>}</div>
      </article>); })}
  </section>);
}
