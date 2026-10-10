'use client';
import { useEffect, useState } from 'react';
import type { Catalog, Snapshot, FurniturePlacement } from '@/core/types';
import { groups } from '@/core/catalog';
const lei = (v: number) => Math.round(v).toLocaleString('ro-RO') + ' lei';
type Alt = { index: number; title: string; ok: boolean; outcomes: { op: string; ref: string; status: string; reason?: string }[]; measures: { items: number; rooms: { roomId: string; area: number; freeRatio: number; items: number }[]; warnings: number; errors: number };
  boq: { knownTotal: number; unknownCount: number; retailers: string[]; budget?: { target: number; delta: number; status: string } }; issues: { severity: string; code: string; message: string }[]; placements: FurniturePlacement[] };
const BUDGET: Record<string, string> = { UNDER: 'în buget', OVER: 'peste buget', UNKNOWN: 'verdict necunoscut (prețuri lipsă)' };

export default function TwinDesignPanel({ id, snap, catalog, onPreview, onApplied, say }: { id: string; snap: Snapshot; catalog: Catalog; onPreview(s: Snapshot | null, label: string | null): void; onApplied(s: Snapshot, rev: number): void; say(t: string): void }){
  const G = groups(catalog), groupKeys = Object.keys(G).filter(k => !G[k].includedWith);
  const [roomId, setRoomId] = useState(snap.floor.rooms[0]?.id ?? ''), [wants, setWants] = useState<string[]>(['canapea', 'masuta'].filter(k => groupKeys.includes(k)));
  const [budget, setBudget] = useState<string>(''), [retailers, setRetailers] = useState<('IKEA' | 'Dedeman')[]>(['IKEA', 'Dedeman']), [accessibility, setAccessibility] = useState(false), [replace, setReplace] = useState(true);
  const [busy, setBusy] = useState(false), [res, setRes] = useState<{ id: string; baseFingerprint: string; notes: string[]; alternatives: Alt[] } | null>(null), [ack, setAck] = useState<Record<number, boolean>>({}), [done, setDone] = useState<Record<number, string>>({});
  useEffect(() => { if (!snap.floor.rooms.some(r => r.id === roomId)) setRoomId(snap.floor.rooms[0]?.id ?? ''); }, [snap, roomId]);
  const toggle = <T,>(arr: T[], v: T) => arr.includes(v) ? arr.filter(x => x !== v) : [...arr, v];
  async function generate(){ setBusy(true); onPreview(null, null); setRes(null); setDone({}); setAck({});
    const r = await fetch(`/api/projects/${id}/design`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ brief: { roomId, wants, budget: budget === '' ? null : Number(budget), retailers, accessibility, replace } }) });
    const j = await r.json(); setBusy(false); if (!r.ok){ say(j.error || 'Generarea a eșuat.'); return; } setRes(j); }
  async function act(a: Alt, action: 'apply' | 'reject'){ if (!res) return;
    const r = await fetch(`/api/projects/${id}/design/${res.id}`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ index: a.index, action, confirmWarnings: !!ack[a.index] }) }); const j = await r.json();
    if (!r.ok){ say(`${j.error || 'Operația a eșuat.'} ${(j.details || []).slice(0, 2).map((d: any) => d.message).join(' ')}`); return; }
    setDone({ ...done, [a.index]: action }); onPreview(null, null);
    if (action === 'apply'){ onApplied(j.snapshot, j.revision); say(`Varianta ${a.title} a fost aplicată și salvată ca revizia ${j.revision}.`); } else say('Varianta a fost respinsă; proiectul a rămas neschimbat.'); }
  const previewSnap = (a: Alt): Snapshot => ({ ...snap, placements: [...(replace ? snap.placements.filter(p => p.roomId !== roomId) : snap.placements).filter(p => !a.placements.some(q => q.id === p.id)), ...a.placements] });
  return (<div className="design">
    <h3>Digital Twin: brief pe cameră, 3 variante</h3>
    <p className="prov" style={{ margin: 0 }}>Motorul de reguli propune doar produse din catalog, ca DSL fără coordonate. Solver-ul și Geometry Engine așază piesele determinist; fiecare variantă e validată (încadrare, uși, ferestre, circulație) și evaluată pe buget, cu UNKNOWN unde lipsesc prețuri. Aplicarea se revalidează pe proiectul curent și e refuzată dacă proiectul s-a schimbat între timp.</p>
    <div className="grid2">
      <label className="f"><span>Cameră</span><select value={roomId} onChange={e => setRoomId(e.target.value)}>{snap.floor.rooms.map(r => <option key={r.id} value={r.id}>{r.name}</option>)}</select></label>
      <label className="f"><span>Buget mobilier (lei)</span><input type="number" min={0} value={budget} placeholder="fără limită" onChange={e => setBudget(e.target.value)} /></label>
    </div>
    <div className="chips">{groupKeys.map(k => <label key={k} className="chip"><input type="checkbox" checked={wants.includes(k)} onChange={() => setWants(toggle(wants, k))} />{G[k].label}</label>)}</div>
    <div className="chips">{(['IKEA', 'Dedeman'] as const).map(s => <label key={s} className="chip"><input type="checkbox" checked={retailers.includes(s)} onChange={() => setRetailers(toggle(retailers, s))} />{s}</label>)}
      <label className="chip"><input type="checkbox" checked={accessibility} onChange={e => setAccessibility(e.target.checked)} />Accesibilitate (spații libere mai mari)</label>
      <label className="chip"><input type="checkbox" checked={replace} onChange={e => setReplace(e.target.checked)} />Înlocuiește mobila din cameră</label></div>
    <button className="btn primary" disabled={busy || !wants.length || !retailers.length || !roomId} onClick={generate}>{busy ? 'Se generează…' : 'Generează 3 variante pe Digital Twin'}</button>
    {res && <>
      <div className="issue WARNING">{res.notes.join(' ')}</div>
      {res.alternatives.map(a => { const st = !a.ok ? 'ERROR' : a.issues.some(i => i.severity === 'WARNING') ? 'WARNING' : 'PASS';
        return <div key={a.index} className={`vcard ${st}`}>
          <div className="vhead"><b>{a.title}</b><span className={`tag ${st}`}>{done[a.index] === 'apply' ? 'Aplicată' : done[a.index] === 'reject' ? 'Respinsă' : st === 'ERROR' ? 'Nu poate fi aplicată' : st === 'WARNING' ? 'Cu avertismente' : 'Validă'}</span></div>
          <div className="vtotal"><b className="mono">{lei(a.boq.knownTotal)}</b><span className="mono">{a.boq.unknownCount ? `+ ${a.boq.unknownCount} prețuri necunoscute` : 'toate prețurile cunoscute'}{a.boq.budget ? ` · ${BUDGET[a.boq.budget.status]}` : ''}</span></div>
          <p className="prov" style={{ margin: 0 }}>{a.measures.items} piese · {a.measures.rooms.filter(r => r.roomId === roomId).map(r => `${Math.round(r.freeRatio * 100)}% suprafață liberă`).join('')} · magazine: {a.boq.retailers.join(', ') || '—'}</p>
          {a.outcomes.filter(o => o.status === 'FAILED').map((o, k) => <div key={k} className="issue ERROR">{o.ref}: {o.reason}</div>)}
          {a.issues.map((i, k) => <div key={k} className={`issue ${i.severity}`}>{i.message}</div>)}
          {st === 'WARNING' && !done[a.index] && <label className="f" style={{ display: 'flex', gap: 8, alignItems: 'center' }}><input type="checkbox" checked={!!ack[a.index]} onChange={e => setAck({ ...ack, [a.index]: e.target.checked })} /> Am citit avertismentele și vreau să aplic varianta</label>}
          {!done[a.index] && <div className="vbtns">
            <button className="btn" disabled={!a.ok} onClick={() => onPreview(previewSnap(a), a.title)}>Previzualizează</button>
            <button className="btn primary" disabled={!a.ok || (st === 'WARNING' && !ack[a.index])} onClick={() => act(a, 'apply')}>Aplică</button>
            <button className="btn" onClick={() => act(a, 'reject')}>Respinge</button></div>}
        </div>; })}
    </>}
  </div>);
}
