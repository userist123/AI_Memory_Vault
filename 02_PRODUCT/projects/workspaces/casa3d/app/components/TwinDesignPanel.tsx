'use client';
import { useEffect, useState } from 'react';
import type { Catalog, Snapshot, FurniturePlacement } from '@/core/types';
import { groups, defaultWants } from '@/core/catalog';
import { failText } from '@/core/messages';
import { usePrefs } from '@/lib/prefs';
import { issueText } from '@/lib/i18n';
import { formatMoney, catalogCurrency } from '@/core/format';
type Alt = { index: number; title: string; ok: boolean; outcomes: { op: string; ref: string; status: string; reason?: string }[]; measures: { items: number; rooms: { roomId: string; area: number; freeRatio: number; items: number }[]; warnings: number; errors: number };
  boq: { knownTotal: number; unknownCount: number; retailers: string[]; budget?: { target: number; delta: number; status: string } }; issues: { severity: string; code: string; message: string }[]; placements: FurniturePlacement[] };
const BUDGET: Record<string, string> = { UNDER: 'twin.budget.UNDER', OVER: 'twin.budget.OVER', UNKNOWN: 'twin.budget.UNKNOWN' };

export default function TwinDesignPanel({ id, snap, catalog, onPreview, onApplied, say }: { id: string; snap: Snapshot; catalog: Catalog; onPreview(s: Snapshot | null, label: string | null): void; onApplied(s: Snapshot, rev: number): void; say(t: string): void }){
  const { t, lang, units } = usePrefs(), iT = (i: any) => issueText(lang, i, units), cur = catalogCurrency(catalog), lei = (v: number) => formatMoney(Math.round(v), cur, lang);
  const G = groups(catalog), groupKeys = Object.keys(G).filter(k => !G[k].includedWith);
  const typeOf = (rid: string) => snap.floor.rooms.find(r => r.id === rid)?.type;
  const [roomId, setRoomId] = useState(snap.floor.rooms[0]?.id ?? ''), [wants, setWants] = useState<string[]>(() => defaultWants(typeOf(snap.floor.rooms[0]?.id ?? ''), groupKeys));
  // La schimbarea camerei, bifele pornesc de la mobilierul tipic al acelui tip de cameră (utilizatorul le poate modifica).
  const pickRoom = (rid: string) => { setRoomId(rid); setWants(defaultWants(typeOf(rid), groupKeys)); };
  const [budget, setBudget] = useState<string>(''), [retailers, setRetailers] = useState<('IKEA' | 'Dedeman')[]>(['IKEA', 'Dedeman']), [accessibility, setAccessibility] = useState(false), [replace, setReplace] = useState(true);
  const [busy, setBusy] = useState(false), [acting, setActing] = useState(false), [res, setRes] = useState<{ id: string; baseFingerprint: string; notes: string[]; brief: { roomId: string; replace: boolean }; alternatives: Alt[] } | null>(null), [ack, setAck] = useState<Record<number, boolean>>({}), [done, setDone] = useState<Record<number, string>>({});
  useEffect(() => { if (!snap.floor.rooms.some(r => r.id === roomId)) pickRoom(snap.floor.rooms[0]?.id ?? ''); }, [snap, roomId]);
  const toggle = <T,>(arr: T[], v: T) => arr.includes(v) ? arr.filter(x => x !== v) : [...arr, v];
  async function generate(){ setBusy(true); onPreview(null, null); setRes(null); setDone({}); setAck({});
    const r = await fetch(`/api/projects/${id}/design`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ brief: { roomId, wants, budget: budget === '' ? null : Number(budget), retailers, accessibility, replace } }) });
    const j = await r.json(); setBusy(false); if (!r.ok){ say(j.error || t('design.generateFailed')); return; } setRes(j); }
  async function act(a: Alt, action: 'apply' | 'reject'){ if (!res || acting) return; setActing(true);
    const r = await fetch(`/api/projects/${id}/design/${res.id}`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ index: a.index, action, confirmWarnings: !!ack[a.index] }) }); const j = await r.json(); setActing(false);
    if (!r.ok){ say(`${j.error || t('design.actionFailed')} ${(j.details || []).slice(0, 2).map((d: any) => iT(d)).join(' ')}`); return; }
    setDone({ ...done, [a.index]: action }); onPreview(null, null);
    if (action === 'apply'){ onApplied(j.snapshot, j.revision); say(t('twin.applied', { title: a.title, n: j.revision })); } else say(t('design.rejectedNote')); }
  // Alternativa conține deja toate piesele proiectului după aplicare (generate pe brief-ul salvat), deci previzualizarea e exact ce se aplică.
  const previewSnap = (a: Alt): Snapshot => ({ ...snap, placements: a.placements });
  return (<div className="design">
    <h3>{t('twin.title')}</h3>
    <p className="prov" style={{ margin: 0 }}>{t('twin.intro')}</p>
    <div className="grid2">
      <label className="f"><span>{t('twin.room')}</span><select value={roomId} onChange={e => pickRoom(e.target.value)}>{snap.floor.rooms.map(r => <option key={r.id} value={r.id}>{r.name}</option>)}</select></label>
      <label className="f"><span>{t('twin.budget', { cur })}</span><input type="number" min={0} value={budget} placeholder={t('design.noLimit')} onChange={e => setBudget(e.target.value)} /></label>
    </div>
    <div className="chips">{groupKeys.map(k => <label key={k} className="chip"><input type="checkbox" checked={wants.includes(k)} onChange={() => setWants(toggle(wants, k))} />{G[k].label}</label>)}</div>
    <div className="chips">{(['IKEA', 'Dedeman'] as const).map(s => <label key={s} className="chip"><input type="checkbox" checked={retailers.includes(s)} onChange={() => setRetailers(toggle(retailers, s))} />{s}</label>)}
      <label className="chip"><input type="checkbox" checked={accessibility} onChange={e => setAccessibility(e.target.checked)} />{t('twin.accessibility')}</label>
      <label className="chip"><input type="checkbox" checked={replace} onChange={e => setReplace(e.target.checked)} />{t('twin.replace')}</label></div>
    <button className="btn primary" disabled={busy || !wants.length || !retailers.length || !roomId} onClick={generate}>{busy ? t('design.generating') : t('twin.generate')}</button>
    {res && <>
      <div className="issue WARNING">{res.notes.join(' ')}</div>
      {res.alternatives.map(a => { const st = !a.ok ? 'ERROR' : a.issues.some(i => i.severity === 'WARNING') ? 'WARNING' : 'PASS';
        return <div key={a.index} className={`vcard ${st}`}>
          <div className="vhead"><b>{a.title}</b><span className={`tag ${st}`}>{done[a.index] === 'apply' ? t('design.done.apply') : done[a.index] === 'reject' ? t('design.done.reject') : t(`design.status.${st}`)}</span></div>
          {a.ok ? <><div className="vtotal"><b className="mono">{lei(a.boq.knownTotal)}</b><span className="mono">{a.boq.unknownCount ? t('twin.unknownPrices', { n: a.boq.unknownCount }) : t('twin.allKnown')}{a.boq.budget ? ` · ${t(BUDGET[a.boq.budget.status] ?? 'twin.budget.UNKNOWN')}` : ''}</span></div>
          <p className="prov" style={{ margin: 0 }}>{t('twin.measures', { items: a.measures.items, free: a.measures.rooms.filter(r => r.roomId === res.brief.roomId).map(r => t('twin.freeArea', { pct: Math.round(r.freeRatio * 100) })).join(''), shops: a.boq.retailers.join(', ') || '—' })}</p></>
          : <p className="prov" style={{ margin: 0 }}>{t('twin.notPlaced')}</p>}
          {a.outcomes.filter(o => o.status === 'FAILED').map((o, k) => <div key={k} className="issue ERROR">{failText(o.reason, lang)}</div>)}
          {a.issues.map((i, k) => <div key={k} className={`issue ${i.severity}`}>{iT(i)}</div>)}
          {st === 'WARNING' && !done[a.index] && <label className="f" style={{ display: 'flex', gap: 8, alignItems: 'center' }}><input type="checkbox" checked={!!ack[a.index]} onChange={e => setAck({ ...ack, [a.index]: e.target.checked })} /> {t('design.ack')}</label>}
          {!done[a.index] && <div className="vbtns">
            <button className="btn" disabled={!a.ok} onClick={() => onPreview(previewSnap(a), a.title)}>{t('design.preview')}</button>
            <button className="btn primary" disabled={acting || !a.ok || (st === 'WARNING' && !ack[a.index])} onClick={() => act(a, 'apply')}>{acting ? t('twin.applying') : t('design.apply')}</button>
            <button className="btn" disabled={acting} onClick={() => act(a, 'reject')}>{t('design.reject')}</button></div>}
        </div>; })}
    </>}
  </div>);
}
