'use client';
import { useState } from 'react';
import type { Snapshot } from '@/core/types';
import { STYLES, PRIORITIES, DEFAULT_BRIEF, type DesignBrief } from '@/core/brief';
import { usePrefs } from '@/lib/prefs';
import { issueText } from '@/lib/i18n';
import { formatMoney } from '@/core/format';
const STATUS: Record<string, string> = { PASS: 'design.status.PASS', WARNING: 'design.status.WARNING', ERROR: 'design.status.ERROR' };

export default function DesignPanel({ id, snap, onPreview, onApplied, say, cur = 'RON' }: { id: string; snap: Snapshot; onPreview(v: any | null, pid: string | null): void; onApplied(s: Snapshot, rev: number): void; say(t: string): void; cur?: string }){
  const { t, lang, units } = usePrefs(), iT = (i: any) => issueText(lang, i, units), lei = (v: number) => formatMoney(Math.round(v), cur, lang);
  const [b, setB] = useState<DesignBrief>({ ...DEFAULT_BRIEF, ...(snap.brief || {}) }), [busy, setBusy] = useState(false), [res, setRes] = useState<any>(null);
  const [open, setOpen] = useState<Record<string, string>>({}), [ack, setAck] = useState<Record<string, boolean>>({}), [done, setDone] = useState<Record<string, string>>({});
  const set = (p: Partial<DesignBrief>) => setB({ ...b, ...p }), toggle = <T,>(arr: T[], v: T) => arr.includes(v) ? arr.filter(x => x !== v) : [...arr, v];
  async function generate(){ setBusy(true); onPreview(null, null); setRes(null); setDone({}); setAck({});
    const r = await fetch(`/api/projects/${id}/proposals`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ brief: b }) }); const j = await r.json(); setBusy(false);
    if (!r.ok){ say(j.error || t('design.generateFailed')); return; } setRes(j); }
  async function act(tier: string, action: 'apply' | 'reject'){
    const r = await fetch(`/api/projects/${id}/proposals/${res.id}`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ tier, action, confirmWarnings: !!ack[tier] }) }); const j = await r.json();
    if (!r.ok){ say(j.error || t('design.actionFailed')); return; }
    setDone({ ...done, [tier]: action }); onPreview(null, null);
    if (action === 'apply'){ onApplied(j.snapshot, j.revision); say(t('design.applied', { tier: t(`design.tier.${tier}`), n: j.revision })); } else say(t('design.rejectedNote')); }
  return (<div className="design">
    <h3>{t('design.title')}</h3>
    <p className="prov" style={{ margin: 0 }}>{t('design.intro')}</p>
    <div className="grid2">
      <label className="f"><span>{t('design.style')}</span><select value={b.style} onChange={e => set({ style: e.target.value as any })}>{Object.keys(STYLES).map(k => <option key={k} value={k}>{t(`design.styles.${k}`)}</option>)}</select></label>
      <label className="f"><span>{t('design.property')}</span><select value={b.propertyType} onChange={e => set({ propertyType: e.target.value as any })}><option value="apartament">{t('design.property.apartament')}</option><option value="casa">{t('design.property.casa')}</option><option value="studio">{t('design.property.studio')}</option></select></label>
      <label className="f"><span>{t('design.occupants')}</span><input type="number" min={1} max={12} value={b.occupants} onChange={e => set({ occupants: Number(e.target.value) || 1 })} /></label>
      <label className="f"><span>{t('design.totalBudget', { cur })}</span><input type="number" min={0} value={b.budget ?? ''} placeholder={t('design.noLimit')} onChange={e => set({ budget: e.target.value === '' ? null : Number(e.target.value) })} /></label>
    </div>
    <div className="chips">{([['children', 'design.children'], ['pets', 'design.pets'], ['accessibility', 'design.accessibility']] as const).map(([k, l]) => <label key={k} className="chip"><input type="checkbox" checked={b[k]} onChange={e => set({ [k]: e.target.checked } as any)} />{t(l)}</label>)}</div>
    <div className="chips">{Object.keys(PRIORITIES).map(k => <label key={k} className="chip"><input type="checkbox" checked={b.priorities.includes(k as any)} onChange={() => set({ priorities: toggle(b.priorities, k as any) })} />{t(`design.priorities.${k}`)}</label>)}</div>
    <div className="chips">{(['IKEA', 'Dedeman'] as const).map(s => <label key={s} className="chip"><input type="checkbox" checked={b.suppliers.includes(s)} onChange={() => set({ suppliers: toggle(b.suppliers, s) })} />{s}</label>)}</div>
    <label className="f"><span>{t('design.colors')}</span><input type="text" maxLength={120} value={b.colors} placeholder={t('design.colorsPh')} onChange={e => set({ colors: e.target.value })} /></label>
    <label className="f"><span>{t('design.avoid')}</span><input type="text" maxLength={200} value={b.avoid} placeholder={t('design.avoidPh')} onChange={e => set({ avoid: e.target.value })} /></label>
    <label className="f"><span>{t('design.notes')}</span><textarea maxLength={600} rows={3} value={b.notes} onChange={e => set({ notes: e.target.value })} style={{ width: '100%', border: '1px solid var(--rule)', borderRadius: 6, padding: 8 }} /></label>
    <button className="btn primary" disabled={busy || !b.suppliers.length} onClick={generate}>{busy ? t('design.generating') : t('design.generate')}</button>
    {res && <>
      <div className={`issue ${res.aiGenerated ? 'PASS' : 'WARNING'}`}>{res.aiGenerated ? t('design.byAi', { model: res.model }) : t('design.byRules')} {res.notes.join(' ')}</div>
      {res.variants.map((v: any) => <div key={v.tier} className={`vcard ${v.status}`}>
        <div className="vhead"><b>{v.title}</b><span className={`tag ${v.status}`}>{done[v.tier] === 'apply' ? t('design.done.apply') : done[v.tier] === 'reject' ? t('design.done.reject') : t(STATUS[v.status]!)}</span></div>
        <div className="vtotal"><b className="mono">{lei(v.total)}</b><span className="mono">{t('design.delta', { sign: v.delta >= 0 ? '+' : '−', amount: lei(Math.abs(v.delta)) })}</span></div>
        {v.palette.length > 0 && <div className="pal">{v.palette.map((c: string) => <i key={c} style={{ background: c }} title={c} />)}</div>}
        <p className="prov" style={{ margin: 0 }}>{v.summary}</p>
        {v.issues.map((i: any, k: number) => <div key={k} className={`issue ${i.severity}`}>{iT(i)}</div>)}
        <button className="acc" aria-expanded={open[v.tier] === 'chg'} onClick={() => setOpen({ ...open, [v.tier]: open[v.tier] === 'chg' ? '' : 'chg' })}>{t('design.changes', { n: v.changes.length })}</button>
        {open[v.tier] === 'chg' && <div className="tbl">{v.changes.map((c: any, k: number) => <div key={k} className="trow"><span>{c.label}</span><b className="mono">{c.priceDelta == null ? '' : `${c.priceDelta >= 0 ? '+' : '−'}${lei(Math.abs(c.priceDelta))}`}</b><small>{c.from} → {c.to}{c.note ? ` · ${c.note}` : ''}</small></div>)}</div>}
        <button className="acc" aria-expanded={open[v.tier] === 'why'} onClick={() => setOpen({ ...open, [v.tier]: open[v.tier] === 'why' ? '' : 'why' })}>{t('design.reasons', { n: v.reasons.length })}</button>
        {open[v.tier] === 'why' && <div className="tbl">{v.reasons.map((r: any, k: number) => <div key={k} className="trow"><span>{r.target}</span><small>{r.reason}</small></div>)}</div>}
        {v.status === 'WARNING' && !done[v.tier] && <label className="f" style={{ display: 'flex', gap: 8, alignItems: 'center' }}><input type="checkbox" checked={!!ack[v.tier]} onChange={e => setAck({ ...ack, [v.tier]: e.target.checked })} /> {t('design.ack')}</label>}
        {!done[v.tier] && <div className="vbtns">
          <button className="btn" onClick={() => onPreview(v, res.id)}>{t('design.preview')}</button>
          <button className="btn primary" disabled={v.status === 'ERROR' || (v.status === 'WARNING' && !ack[v.tier])} onClick={() => act(v.tier, 'apply')}>{t('design.apply')}</button>
          <button className="btn" onClick={() => act(v.tier, 'reject')}>{t('design.reject')}</button></div>}
      </div>)}
    </>}
  </div>);
}
