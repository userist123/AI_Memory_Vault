'use client';
import { useState } from 'react';
import type { Snapshot } from '@/core/types';
import { STYLES, PRIORITIES, DEFAULT_BRIEF, type DesignBrief } from '@/core/brief';
import { TIER_LABEL } from '@/core/proposal';
const lei = (v: number) => Math.round(v).toLocaleString('ro-RO') + ' lei';
const STATUS: Record<string, string> = { PASS: 'Validă', WARNING: 'Cu avertismente', ERROR: 'Nu poate fi aplicată' };

export default function DesignPanel({ id, snap, onPreview, onApplied, say }: { id: string; snap: Snapshot; onPreview(v: any | null, pid: string | null): void; onApplied(s: Snapshot, rev: number): void; say(t: string): void }){
  const [b, setB] = useState<DesignBrief>({ ...DEFAULT_BRIEF, ...(snap.brief || {}) }), [busy, setBusy] = useState(false), [res, setRes] = useState<any>(null);
  const [open, setOpen] = useState<Record<string, string>>({}), [ack, setAck] = useState<Record<string, boolean>>({}), [done, setDone] = useState<Record<string, string>>({});
  const set = (p: Partial<DesignBrief>) => setB({ ...b, ...p }), toggle = <T,>(arr: T[], v: T) => arr.includes(v) ? arr.filter(x => x !== v) : [...arr, v];
  async function generate(){ setBusy(true); onPreview(null, null); setRes(null); setDone({}); setAck({});
    const r = await fetch(`/api/projects/${id}/proposals`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ brief: b }) }); const j = await r.json(); setBusy(false);
    if (!r.ok){ say(j.error || 'Generarea a eșuat.'); return; } setRes(j); }
  async function act(tier: string, action: 'apply' | 'reject'){
    const r = await fetch(`/api/projects/${id}/proposals/${res.id}`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ tier, action, confirmWarnings: !!ack[tier] }) }); const j = await r.json();
    if (!r.ok){ say(j.error || 'Operația a eșuat.'); return; }
    setDone({ ...done, [tier]: action }); onPreview(null, null);
    if (action === 'apply'){ onApplied(j.snapshot, j.revision); say(`Varianta ${TIER_LABEL[tier as keyof typeof TIER_LABEL]} a fost aplicată și salvată ca revizia ${j.revision}.`); } else say('Varianta a fost respinsă; proiectul a rămas neschimbat.'); }
  return (<div className="design">
    <h3>Design: brief și variante</h3>
    <p className="prov" style={{ margin: 0 }}>Propunerile aleg doar produse și materiale din catalog. Pozițiile le calculează motorul geometric, iar fiecare variantă trece prin verificarea de încadrare, circulație și buget înainte să o poți aplica.</p>
    <div className="grid2">
      <label className="f"><span>Stil</span><select value={b.style} onChange={e => set({ style: e.target.value as any })}>{Object.entries(STYLES).map(([k, l]) => <option key={k} value={k}>{l}</option>)}</select></label>
      <label className="f"><span>Locuință</span><select value={b.propertyType} onChange={e => set({ propertyType: e.target.value as any })}><option value="apartament">Apartament</option><option value="casa">Casă</option><option value="studio">Garsonieră</option></select></label>
      <label className="f"><span>Persoane</span><input type="number" min={1} max={12} value={b.occupants} onChange={e => set({ occupants: Number(e.target.value) || 1 })} /></label>
      <label className="f"><span>Buget total (lei)</span><input type="number" min={0} value={b.budget ?? ''} placeholder="fără limită" onChange={e => set({ budget: e.target.value === '' ? null : Number(e.target.value) })} /></label>
    </div>
    <div className="chips">{([['children', 'Copii'], ['pets', 'Animale'], ['accessibility', 'Accesibilitate']] as const).map(([k, l]) => <label key={k} className="chip"><input type="checkbox" checked={b[k]} onChange={e => set({ [k]: e.target.checked } as any)} />{l}</label>)}</div>
    <div className="chips">{Object.entries(PRIORITIES).map(([k, l]) => <label key={k} className="chip"><input type="checkbox" checked={b.priorities.includes(k as any)} onChange={() => set({ priorities: toggle(b.priorities, k as any) })} />{l}</label>)}</div>
    <div className="chips">{(['IKEA', 'Dedeman'] as const).map(s => <label key={s} className="chip"><input type="checkbox" checked={b.suppliers.includes(s)} onChange={() => set({ suppliers: toggle(b.suppliers, s) })} />{s}</label>)}</div>
    <label className="f"><span>Culori preferate</span><input type="text" maxLength={120} value={b.colors} placeholder="ex.: alb, stejar, verde salvie" onChange={e => set({ colors: e.target.value })} /></label>
    <label className="f"><span>De evitat (cuvinte din numele produselor)</span><input type="text" maxLength={200} value={b.avoid} placeholder="ex.: sticlă, negru" onChange={e => set({ avoid: e.target.value })} /></label>
    <label className="f"><span>Alte detalii</span><textarea maxLength={600} rows={3} value={b.notes} onChange={e => set({ notes: e.target.value })} style={{ width: '100%', border: '1px solid var(--rule)', borderRadius: 6, padding: 8 }} /></label>
    <button className="btn primary" disabled={busy || !b.suppliers.length} onClick={generate}>{busy ? 'Se generează…' : 'Generează 3 variante'}</button>
    {res && <>
      <div className={`issue ${res.aiGenerated ? 'PASS' : 'WARNING'}`}>{res.aiGenerated ? `Variante propuse de AI (${res.model}), validate de aplicație.` : 'Variante generate de motorul de reguli al aplicației (fără AI).'} {res.notes.join(' ')}</div>
      {res.variants.map((v: any) => <div key={v.tier} className={`vcard ${v.status}`}>
        <div className="vhead"><b>{v.title}</b><span className={`tag ${v.status}`}>{done[v.tier] === 'apply' ? 'Aplicată' : done[v.tier] === 'reject' ? 'Respinsă' : STATUS[v.status]}</span></div>
        <div className="vtotal"><b className="mono">{lei(v.total)}</b><span className="mono">{v.delta >= 0 ? '+' : '−'}{lei(Math.abs(v.delta))} față de acum</span></div>
        {v.palette.length > 0 && <div className="pal">{v.palette.map((c: string) => <i key={c} style={{ background: c }} title={c} />)}</div>}
        <p className="prov" style={{ margin: 0 }}>{v.summary}</p>
        {v.issues.map((i: any, k: number) => <div key={k} className={`issue ${i.severity}`}>{i.message}</div>)}
        <button className="acc" aria-expanded={open[v.tier] === 'chg'} onClick={() => setOpen({ ...open, [v.tier]: open[v.tier] === 'chg' ? '' : 'chg' })}>Ce se schimbă ({v.changes.length})</button>
        {open[v.tier] === 'chg' && <div className="tbl">{v.changes.map((c: any, k: number) => <div key={k} className="trow"><span>{c.label}</span><b className="mono">{c.priceDelta == null ? '' : `${c.priceDelta >= 0 ? '+' : '−'}${lei(Math.abs(c.priceDelta))}`}</b><small>{c.from} → {c.to}{c.note ? ` · ${c.note}` : ''}</small></div>)}</div>}
        <button className="acc" aria-expanded={open[v.tier] === 'why'} onClick={() => setOpen({ ...open, [v.tier]: open[v.tier] === 'why' ? '' : 'why' })}>De ce aceste alegeri ({v.reasons.length})</button>
        {open[v.tier] === 'why' && <div className="tbl">{v.reasons.map((r: any, k: number) => <div key={k} className="trow"><span>{r.target}</span><small>{r.reason}</small></div>)}</div>}
        {v.status === 'WARNING' && !done[v.tier] && <label className="f" style={{ display: 'flex', gap: 8, alignItems: 'center' }}><input type="checkbox" checked={!!ack[v.tier]} onChange={e => setAck({ ...ack, [v.tier]: e.target.checked })} /> Am citit avertismentele și vreau să aplic varianta</label>}
        {!done[v.tier] && <div className="vbtns">
          <button className="btn" onClick={() => onPreview(v, res.id)}>Previzualizează</button>
          <button className="btn primary" disabled={v.status === 'ERROR' || (v.status === 'WARNING' && !ack[v.tier])} onClick={() => act(v.tier, 'apply')}>Aplică</button>
          <button className="btn" onClick={() => act(v.tier, 'reject')}>Respinge</button></div>}
      </div>)}
    </>}
  </div>);
}
