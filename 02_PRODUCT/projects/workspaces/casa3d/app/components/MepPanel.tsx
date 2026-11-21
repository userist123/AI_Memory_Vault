'use client';
// Structură și instalații (experimental, docs/bim/GHID_BIM.md Faza 4): trasee între punctele tehnice, perete structural, fundații.
// Reprezentare, nu proiectare: diametrul, materialul și panta sunt cele introduse; lipsa lor rămâne „nedeclarat”.
import { useMemo, useState } from 'react';
import type { Floor, MepRun, MepSystem, Snapshot } from '@/core/types';
import { MEP_SYSTEMS, proposeRun, runLength, mepIssues, MEP_LIMITS } from '@/core/mep';
import { usePrefs } from '@/lib/prefs';

const SYSTEMS = Object.keys(MEP_SYSTEMS) as MepSystem[];
interface Props { snap: Snapshot; takenIds: string[]; say(t: string): void; onChange(fn: (f: Floor) => void): void }

export default function MepPanel({ snap, takenIds, say, onChange }: Props){
  const { t, lang } = usePrefs(), L = lang === 'en' ? 'en' : 'ro', f = snap.floor;
  const rooms = new Set(f.rooms.map(r => r.id)), pts = (snap.tech ?? []).filter(p => rooms.has(p.roomId));
  const [sys, setSys] = useState<MepSystem>('water_cold'), [a, setA] = useState(''), [b, setB] = useState(''), [hCm, setHCm] = useState('');
  const [dia, setDia] = useState(''), [mat, setMat] = useState(''), [slope, setSlope] = useState(''), [err, setErr] = useState('');
  const [wall, setWall] = useState(''), [fd, setFd] = useState({ x: '', z: '', w: '80', d: '80', depth: '60' });
  const pa = pts.find(p => p.id === a), pb = pts.find(p => p.id === b), routeY = Number(hCm) / 100;
  const preview = useMemo(() => pa && pb && hCm !== '' && Number.isFinite(routeY) ? proposeRun(pa, pb, routeY) : null, [pa, pb, hCm, routeY]);
  const issues = mepIssues(f);
  const newId = (p: string) => { const taken = new Set(takenIds); let n = 1; while (taken.has(`${p}-${n}`)) n++; return `${p}-${n}`; };
  const optNum = (v: string) => v.trim() === '' ? undefined : Number(v);
  function addRun(){
    setErr(''); if (!preview || !pa || !pb) return;
    if (!(routeY >= 0 && routeY <= f.ceilingHeight)) { setErr(t('mep.heightRange', { h: Math.round(f.ceilingHeight * 100) })); return; }
    const d = optNum(dia), s = optNum(slope);
    if (d != null && !(d >= MEP_LIMITS.minDiameterMm && d <= MEP_LIMITS.maxDiameterMm)) { setErr(t('mep.diaRange')); return; }
    if (s != null && !(s >= 0 && s <= MEP_LIMITS.maxSlopePct)) { setErr(t('mep.slopeRange')); return; }
    const run: MepRun = { id: newId('mep'), system: sys, points: preview, fromId: pa.id, toId: pb.id, ...(d != null ? { diameterMm: d } : {}), ...(mat.trim() ? { material: mat.trim().slice(0, 60) } : {}), ...(s != null ? { slopePct: s } : {}) };
    onChange(fl => { fl.mepRuns = [...(fl.mepRuns ?? []), run]; }); say(t('mep.added', { m: runLength(run).toFixed(2) }));
  }
  function addFooting(){
    setErr(''); const v = { x: Number(fd.x), z: Number(fd.z), w: Number(fd.w) / 100, d: Number(fd.d) / 100, depth: Number(fd.depth) / 100 };
    if (fd.x === '' || fd.z === '' || !Object.values(v).every(Number.isFinite) || v.w < .1 || v.d < .1 || v.depth < .1) { setErr(t('mep.footingBad')); return; }
    onChange(fl => { fl.footings = [...(fl.footings ?? []), { id: newId('fd'), ...v }]; }); say(t('mep.footingAdded'));
  }
  return (<div className="mep-panel">
    <h4>{t('mep.title')} <small className="muted">({t('dxf.experimental')})</small></h4>
    <p className="prov" style={{ margin: 0 }}>{t('mep.hint')}</p>
    {err && <p role="alert" className="issue ERROR">{err}</p>}
    <fieldset><legend>{t('mep.newRun')}</legend>
      <label className="f"><span>{t('mep.system')}</span><select value={sys} onChange={e => setSys(e.target.value as MepSystem)}>{SYSTEMS.map(s => <option key={s} value={s}>{MEP_SYSTEMS[s].label[L]}</option>)}</select></label>
      {pts.length < 2 ? <p className="muted">{t('mep.needPoints')}</p> : <>
        <label className="f"><span>{t('mep.from')}</span><select value={a} onChange={e => setA(e.target.value)}><option value="">—</option>{pts.map(p => <option key={p.id} value={p.id}>{p.id} · {p.kind}</option>)}</select></label>
        <label className="f"><span>{t('mep.to')}</span><select value={b} onChange={e => setB(e.target.value)}><option value="">—</option>{pts.filter(p => p.id !== a).map(p => <option key={p.id} value={p.id}>{p.id} · {p.kind}</option>)}</select></label>
        <label className="f"><span>{t('mep.routeHeight')} *</span><input type="number" min={0} max={Math.round(f.ceilingHeight * 100)} value={hCm} onChange={e => setHCm(e.target.value)} /></label>
        <label className="f"><span>{t('mep.diameter')}</span><input type="number" min={MEP_LIMITS.minDiameterMm} max={MEP_LIMITS.maxDiameterMm} value={dia} placeholder={t('mep.undeclared')} onChange={e => setDia(e.target.value)} /></label>
        <label className="f"><span>{t('mep.material')}</span><input value={mat} maxLength={60} placeholder={t('mep.undeclared')} onChange={e => setMat(e.target.value)} /></label>
        {sys === 'drain' && <label className="f"><span>{t('mep.slope')}</span><input type="number" min={0} max={MEP_LIMITS.maxSlopePct} step={.5} value={slope} placeholder={t('mep.undeclared')} onChange={e => setSlope(e.target.value)} /></label>}
        {preview && <p className="prov">{t('mep.preview', { n: preview.length, m: runLength({ points: preview }).toFixed(2) })}</p>}
        <button className="btn" disabled={!preview} onClick={addRun}>{t('mep.addRun')}</button>
      </>}
    </fieldset>
    {(f.mepRuns ?? []).length > 0 && <ul className="mep-list">{f.mepRuns!.map(r => <li key={r.id}>
      <span style={{ display: 'inline-block', width: 10, height: 10, background: MEP_SYSTEMS[r.system].color, marginRight: 6 }} aria-hidden />
      {r.id} · {MEP_SYSTEMS[r.system].label[L]} · {runLength(r).toFixed(2)} m · {r.diameterMm ? `Ø${r.diameterMm}` : t('bim.diameterUnknown')}
      {' '}<button className="btn danger" aria-label={t('mep.remove', { id: r.id })} onClick={() => onChange(fl => { fl.mepRuns = (fl.mepRuns ?? []).filter(x => x.id !== r.id); })}>×</button></li>)}</ul>}
    {issues.length > 0 && <ul>{issues.map(i => <li key={i.code + i.refs[0]} className={`issue ${i.severity === 'WARNING' ? 'WARNING' : ''}`}>{i.message}</li>)}</ul>}
    <fieldset><legend>{t('mep.structure')}</legend>
      <label className="f"><span>{t('mep.wall')}</span><select value={wall} onChange={e => setWall(e.target.value)}><option value="">—</option>{f.walls.map(w => <option key={w.id} value={w.id}>{w.id}{w.structural ? ` (${t('mep.isStructural')})` : ''}</option>)}</select></label>
      <button className="btn" disabled={!wall} onClick={() => onChange(fl => { const w = fl.walls.find(x => x.id === wall); if (w) { if (w.structural) delete w.structural; else w.structural = true; } })}>{t('mep.toggleStructural')}</button>
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(5, 1fr)', gap: 4, marginTop: 6 }}>
        {(['x', 'z', 'w', 'd', 'depth'] as const).map(k => <label key={k} className="f"><span>{t(`mep.fd.${k}`)}</span><input type="number" value={fd[k]} onChange={e => setFd({ ...fd, [k]: e.target.value })} /></label>)}
      </div>
      <button className="btn" onClick={addFooting}>{t('mep.addFooting')}</button>
    </fieldset>
  </div>);
}
