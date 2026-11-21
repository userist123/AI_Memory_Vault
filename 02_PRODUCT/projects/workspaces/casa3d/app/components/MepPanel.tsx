'use client';
// Structură și instalații (experimental, docs/bim/GHID_BIM.md Faza 4): trasee între punctele tehnice, perete structural, fundații.
// Reprezentare, nu proiectare: diametrul, materialul și panta sunt cele introduse; lipsa lor rămâne „nedeclarat”.
// Lungimile se afișează în unitățile utilizatorului (formatLength); punctele, sistemele și avertismentele în limba interfeței.
import { useMemo, useState } from 'react';
import type { Floor, MepRun, MepSystem, Snapshot } from '@/core/types';
import { MEP_SYSTEMS, proposeRun, runLength, mepIssues, MEP_LIMITS } from '@/core/mep';
import { TECH_KINDS } from '@/core/technical';
import { formatLength } from '@/core/format';
import { usePrefs } from '@/lib/prefs';

const SYSTEMS = Object.keys(MEP_SYSTEMS) as MepSystem[];
interface Props { snap: Snapshot; takenIds: string[]; say(t: string): void; onChange(fn: (f: Floor) => void): void;
  /** fundațiile se adaugă doar la parter: sub un etaj ar ajunge în tavanul nivelului de dedesubt */
  isGround: boolean }

export default function MepPanel({ snap, takenIds, say, onChange, isGround }: Props){
  const { t, lang, units } = usePrefs(), L = lang === 'en' ? 'en' : 'ro', f = snap.floor, len = (m: number) => formatLength(m, units, lang);
  const rooms = new Map(f.rooms.map(r => [r.id, r.name])), pts = (snap.tech ?? []).filter(p => rooms.has(p.roomId));
  // eticheta punctului: tipul și camera; punctele de același fel din aceeași cameră primesc număr de ordine, ca să se poată deosebi
  const ptLabel = (p: (typeof pts)[number]) => { const base = `${TECH_KINDS[p.kind]?.label[L] ?? p.kind} · ${rooms.get(p.roomId)}`, same = pts.filter(q => q.kind === p.kind && q.roomId === p.roomId);
    return same.length > 1 ? `${base} ${same.indexOf(p) + 1}` : base; };
  const [sys, setSys] = useState<MepSystem>('water_cold'), [a, setA] = useState(''), [b, setB] = useState(''), [hCm, setHCm] = useState('');
  const [dia, setDia] = useState(''), [mat, setMat] = useState(''), [slope, setSlope] = useState(''), [err, setErr] = useState('');
  const [wall, setWall] = useState(''), [fd, setFd] = useState({ x: '', z: '', w: '', d: '', depth: '' });   // fără valori precompletate: dimensiunile fundației le dă utilizatorul (art. 4)
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
    onChange(fl => { fl.mepRuns = [...(fl.mepRuns ?? []), run]; }); say(t('mep.added', { m: len(runLength(run)) }));
  }
  function addFooting(){
    setErr(''); const v = { x: Number(fd.x), z: Number(fd.z), w: Number(fd.w) / 100, d: Number(fd.d) / 100, depth: Number(fd.depth) / 100 };
    if (Object.values(fd).some(x => x.trim() === '') || !Object.values(v).every(Number.isFinite) || v.w < .1 || v.d < .1 || v.depth < .1) { setErr(t('mep.footingBad')); return; }
    onChange(fl => { fl.footings = [...(fl.footings ?? []), { id: newId('fd'), ...v }]; }); setFd({ x: '', z: '', w: '', d: '', depth: '' }); say(t('mep.footingAdded'));
  }
  const wallLabel = (id: string) => { const w = f.walls.find(x => x.id === id); return w ? `${id} · ${len(Math.hypot(w.b[0] - w.a[0], w.b[1] - w.a[1]))}` : id; };
  return (<div className="mep-panel bim-panel">
    <h4>{t('mep.title')} <small className="muted">({t('dxf.experimental')})</small></h4>
    <p className="prov">{t('mep.hint')}</p>
    {err && <p role="alert" className="issue ERROR">{err}</p>}
    <fieldset><legend>{t('mep.newRun')}</legend>
      <label className="f"><span>{t('mep.system')}</span><select value={sys} onChange={e => setSys(e.target.value as MepSystem)}>{SYSTEMS.map(s => <option key={s} value={s}>{MEP_SYSTEMS[s].label[L]}</option>)}</select></label>
      {pts.length < 2 ? <p className="muted">{t('mep.needPoints')}</p> : <>
        <label className="f"><span>{t('mep.from')}</span><select value={a} onChange={e => setA(e.target.value)}><option value="">—</option>{pts.map(p => <option key={p.id} value={p.id}>{ptLabel(p)}</option>)}</select></label>
        <label className="f"><span>{t('mep.to')}</span><select value={b} onChange={e => setB(e.target.value)}><option value="">—</option>{pts.filter(p => p.id !== a).map(p => <option key={p.id} value={p.id}>{ptLabel(p)}</option>)}</select></label>
        <label className="f"><span>{t('mep.routeHeight')} *</span><input type="number" min={0} max={Math.round(f.ceilingHeight * 100)} value={hCm} onChange={e => setHCm(e.target.value)} /></label>
        <div className="bim-grid2">
          <label className="f"><span>{t('mep.diameter')}</span><input type="number" min={MEP_LIMITS.minDiameterMm} max={MEP_LIMITS.maxDiameterMm} value={dia} placeholder={t('mep.undeclared')} onChange={e => setDia(e.target.value)} /></label>
          <label className="f"><span>{t('mep.material')}</span><input value={mat} maxLength={60} placeholder={t('mep.undeclared')} onChange={e => setMat(e.target.value)} /></label>
        </div>
        {sys === 'drain' && <label className="f"><span>{t('mep.slope')}</span><input type="number" min={0} max={MEP_LIMITS.maxSlopePct} step={.5} value={slope} placeholder={t('mep.undeclared')} onChange={e => setSlope(e.target.value)} /></label>}
        {preview && <p className="prov">{t('mep.preview', { n: preview.length, m: len(runLength({ points: preview })) })}</p>}
        <button className="btn" disabled={!preview} onClick={addRun}>{t('mep.addRun')}</button>
      </>}
    </fieldset>
    {(f.mepRuns ?? []).length > 0 && <ul className="bim-list">{f.mepRuns!.map(r => <li key={r.id}>
      <span className="bim-swatch" style={{ background: MEP_SYSTEMS[r.system].color }} aria-hidden />
      <span className="bim-li-text">{MEP_SYSTEMS[r.system].label[L]} · {len(runLength(r))} · {r.diameterMm ? `Ø${r.diameterMm} mm` : t('bim.diameterUnknown')}</span>
      <button className="btn ghost sm" aria-label={t('mep.remove', { id: r.id })} title={t('mep.remove', { id: r.id })} onClick={() => onChange(fl => { fl.mepRuns = (fl.mepRuns ?? []).filter(x => x.id !== r.id); if (!fl.mepRuns.length) delete fl.mepRuns; })}>×</button></li>)}</ul>}
    {issues.length > 0 && <ul className="bim-issues">{issues.map(i => <li key={i.code + i.refs[0]} className={`issue ${i.severity === 'WARNING' ? 'WARNING' : ''}`}>
      {t(i.key, { ...i.vars, system: i.vars.system ? MEP_SYSTEMS[i.vars.system as MepSystem]?.label[L] ?? i.vars.system : '', from: typeof i.vars.from === 'number' ? len(i.vars.from) : '', to: typeof i.vars.to === 'number' ? len(i.vars.to) : '' })}</li>)}</ul>}
    <fieldset><legend>{t('mep.structure')}</legend>
      <label className="f"><span>{t('mep.wall')}</span><select value={wall} onChange={e => setWall(e.target.value)}><option value="">—</option>{f.walls.map(w => <option key={w.id} value={w.id}>{wallLabel(w.id)}{w.structural ? ` (${t('mep.isStructural')})` : ''}</option>)}</select></label>
      <button className="btn" disabled={!wall} onClick={() => onChange(fl => { const w = fl.walls.find(x => x.id === wall); if (w) { if (w.structural) delete w.structural; else w.structural = true; } })}>{t('mep.toggleStructural')}</button>
      {isGround ? <>
        <div className="bim-grid2">
          {(['x', 'z', 'w', 'd', 'depth'] as const).map(k => <label key={k} className="f"><span>{t(`mep.fd.${k}`)}</span><input type="number" value={fd[k]} onChange={e => setFd({ ...fd, [k]: e.target.value })} /></label>)}
        </div>
        <button className="btn" onClick={addFooting}>{t('mep.addFooting')}</button>
        {(f.footings ?? []).length > 0 && <ul className="bim-list">{f.footings!.map(x => <li key={x.id}>
          <span className="bim-li-text">{t('bim.footing')} {len(x.w)} × {len(x.d)} · {t('mep.fd.depth')}: {len(x.depth)}</span>
          <button className="btn ghost sm" aria-label={t('mep.removeFooting', { id: x.id })} title={t('mep.removeFooting', { id: x.id })} onClick={() => onChange(fl => { fl.footings = (fl.footings ?? []).filter(y => y.id !== x.id); if (!fl.footings.length) delete fl.footings; })}>×</button></li>)}</ul>}
      </> : <p className="muted">{t('mep.footingGroundOnly')}</p>}
    </fieldset>
  </div>);
}
