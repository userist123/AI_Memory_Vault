'use client';
// Fișa proiectului în editor: clientul (cu sunat / e-mail / hartă dintr-o atingere), adresa lucrării, etapa, termenele și onorariul.
// Se salvează singură, separat de plan (nu intră în revizii și nu ajunge pe linkul clientului).
import { useEffect, useRef, useState } from 'react';
import { PROJECT_STATUSES, daysLeft, type ProjectMeta } from '@/core/project-meta';
import { FEE_EVENT } from './ChainTable';
import { formatMoney } from '@/core/format';
import { usePrefs } from '@/lib/prefs';
import { askConfirm } from '@/lib/dialog';

const today = () => new Date().toISOString().slice(0, 10);
export default function ProjectCard({ id, initial, areaM2, cur, say, onSaved }: { id: string; initial: ProjectMeta; areaM2: number; cur: string; say(s: string): void; onSaved?(m: ProjectMeta): void }){
  const { t, lang } = usePrefs(), [m, setM] = useState<ProjectMeta>(initial), [state, setState] = useState<'saved' | 'saving' | 'error'>('saved'), timer = useRef<any>(null), first = useRef(true), [startOpen] = useState(!initial.client?.name);
  useEffect(() => { if (first.current){ first.current = false; return; } setState('saving'); clearTimeout(timer.current);
    // fără `fee`: onorariul are o singură sursă, contractul („Contract și ofertă”); serverul păstrează un onorariu vechi încă nemigrat
    timer.current = setTimeout(async () => { const { fee: _legacy, ...body } = m; const r = await fetch(`/api/projects/${id}/meta`, { method: 'PUT', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body) }).catch(() => null);
      if (r?.ok){ setState('saved'); onSaved?.(m); } else { setState('error'); const j = await r?.json().catch(() => null); say(j?.error || t('pcard.saveError')); } }, 700);
    return () => clearTimeout(timer.current); }, [m]); // eslint-disable-line
  const c = m.client ?? {}, setC = (k: keyof NonNullable<ProjectMeta['client']>, v: string) => setM(x => ({ ...x, client: { ...x.client, [k]: v } }));
  const left = daysLeft(m, today()), tel = c.phone?.replace(/[^+0-9]/g, '');
  // onorariul se citește din contract (sursa unică) și se actualizează când ContractPanel sau bugetul îl schimbă
  const [fee, setFee] = useState<{ gross: number | null; net: number | null } | null>(null);
  useEffect(() => { let alive = true; fetch(`/api/projects/${id}/contract`).then(r => r.ok ? r.json() : null).then(j => { if (alive && j) setFee(j.calc); }).catch(() => {});
    const on = (e: Event) => { const d = (e as CustomEvent).detail; if (d?.id === id && d.calc) setFee(d.calc); }; window.addEventListener(FEE_EVENT, on); return () => { alive = false; window.removeEventListener(FEE_EVENT, on); }; }, [id]);
  void areaM2;
  const map = (a?: string) => a ? `https://www.google.com/maps/search/?api=1&query=${encodeURIComponent(a)}` : null;
  return (<details className="pcard-ed" open={startOpen}>
    <summary><b>{c.name || t('pcard.title')}</b><span className={`st st-${m.status}`}>{t(`pstatus.${m.status}`)}</span>
      {left != null && <small className={left < 0 ? 'late' : left <= 7 ? 'soon' : ''}>{left < 0 ? t('pcard.late', { n: -left }) : t('pcard.left', { n: left })}</small>}</summary>
    <div className="pcard-body">
      <label className="f"><span>{t('pcard.status')}</span><select value={m.status} onChange={e => setM(x => ({ ...x, status: e.target.value as ProjectMeta['status'] }))}>{PROJECT_STATUSES.map(s => <option key={s} value={s}>{t(`pstatus.${s}`)}</option>)}</select></label>
      <label className="f"><span>{t('pcard.client')}</span><input value={c.name ?? ''} maxLength={120} autoComplete="off" onChange={e => setC('name', e.target.value)} /></label>
      <div className="grid2">
        <label className="f"><span>{t('pcard.phone')}</span><input type="tel" inputMode="tel" value={c.phone ?? ''} maxLength={40} onChange={e => setC('phone', e.target.value)} /></label>
        <label className="f"><span>{t('pcard.email')}</span><input type="email" inputMode="email" value={c.email ?? ''} maxLength={160} onChange={e => setC('email', e.target.value)} /></label></div>
      {(tel || c.email) && <div className="pcard-act">{tel && <a className="btn" href={`tel:${tel}`}>{t('pcard.call')}</a>}{tel && <a className="btn" href={`https://wa.me/${tel.replace(/^\+/, '').replace(/^0/, '40')}`} target="_blank" rel="noopener">WhatsApp</a>}{c.email && <a className="btn" href={`mailto:${c.email}`}>{t('pcard.mail')}</a>}</div>}
      <label className="f"><span>{t('pcard.address')}</span><input value={c.address ?? ''} maxLength={300} onChange={e => setC('address', e.target.value)} /></label>
      <label className="f"><span>{t('pcard.site')}</span><input value={m.siteAddress ?? ''} maxLength={300} placeholder={t('pcard.sameAddress')} onChange={e => setM(x => ({ ...x, siteAddress: e.target.value }))} /></label>
      {map(m.siteAddress || c.address) && <div className="pcard-act"><a className="btn" href={map(m.siteAddress || c.address)!} target="_blank" rel="noopener">{t('pcard.map')}</a></div>}
      <div className="grid2">
        <label className="f"><span>{t('pcard.start')}</span><input type="date" value={m.startDate ?? ''} onChange={e => setM(x => ({ ...x, startDate: e.target.value || undefined }))} /></label>
        <label className="f"><span>{t('pcard.due')}</span><input type="date" value={m.dueDate ?? ''} onChange={e => setM(x => ({ ...x, dueDate: e.target.value || undefined }))} /></label></div>
      <p className="prov">{fee?.net != null ? t('pcard.feeSummary', { v: formatMoney(fee.gross ?? fee.net, cur, lang) }) : t('pcard.feeNone')} {t('pcard.feeWhere')}</p>
      <label className="chk"><input type="checkbox" checked={!!m.shareOrders} onChange={e => setM(x => ({ ...x, shareOrders: e.target.checked || undefined }))} /><span>{t('pcard.shareOrders')}<small>{t('pcard.shareOrdersSub')}</small></span></label>
      <label className="f"><span>{t('pcard.notes')}</span><textarea rows={3} value={c.notes ?? ''} maxLength={4000} onChange={e => setC('notes', e.target.value)} /></label>
      <small className={`prov ${state === 'error' ? 'err' : ''}`}>{t(`pcard.${state}`)}</small>
      <div className="pcard-act"><a className="btn" href={`/api/projects/${id}/export.zip`} download title={t('pcard.exportHint')}>{t('pcard.export')}</a><button className="btn danger" onClick={async () => { if (!(await askConfirm({ title: t('dlg.trashTitle'), body: t('pcard.trashConfirm'), confirmLabel: t('pcard.trash'), danger: true }))) return; const r = await fetch(`/api/projects/${id}`, { method: 'DELETE' }); if (r.ok) location.href = '/'; else say(t('pcard.saveError')); }}>{t('pcard.trash')}</button></div>
    </div>
  </details>);
}
