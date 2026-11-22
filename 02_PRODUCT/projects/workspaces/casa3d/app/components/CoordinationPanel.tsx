'use client';
// Revizia coordonării (docs/bim/GHID_BIM.md §3.7, §6 „Clash Review”): rulează verificarea pe toată casa, cu toleranța și
// distanțele minime alese aici, arată domeniul verificat, compară cu rularea anterioară, filtrează pe disciplină și tip,
// salvează statusul fiecărei constatări pe id-ul ei stabil și exportă raportul. Nimic nu dispare din listă din cauza statusului.
import { useMemo, useState } from 'react';
import type { Catalog, IssueRecord, IssueStatus, MepSystem, Snapshot } from '@/core/types';
import { runCoordination, type CoordinationRun, type CoordinationFinding } from '@/core/coordination-review';
import { withStatus, compareRuns, coordinationReport, ISSUE_STATUSES } from '@/core/issues';
import { elementIndex, DISCIPLINES, type Discipline } from '@/core/elements';
import { MEP_SYSTEMS } from '@/core/mep';
import { floors } from '@/core/levels';
import { roomsAlongWall } from '@/core/plan-describe';
import { TECH_KINDS } from '@/core/technical';
import { resolve } from '@/core/catalog';
import { DEFAULT_TOLERANCE_M, type ClearanceRule } from '@/core/clash';
import { usePrefs } from '@/lib/prefs';

const SYSTEMS = Object.keys(MEP_SYSTEMS) as MepSystem[];
interface Props { house: Snapshot; catalog: Catalog; say(t: string): void; onIssue(id: string, rec: IssueRecord | null): void }

export default function CoordinationPanel({ house, catalog, say, onIssue }: Props){
  const { t, lang } = usePrefs(), L = lang === 'en' ? 'en' : 'ro';
  const [tolMm, setTolMm] = useState(String(DEFAULT_TOLERANCE_M * 1000)), [rules, setRules] = useState<ClearanceRule[]>([]);
  const [ra, setRa] = useState<MepSystem>('water_hot'), [rb, setRb] = useState<MepSystem>('electric'), [rcm, setRcm] = useState('');
  const [run, setRun] = useState<CoordinationRun | null>(null), [prev, setPrev] = useState<CoordinationFinding[] | null>(null);
  const [disc, setDisc] = useState<Set<Discipline>>(new Set(DISCIPLINES)), [onlyClash, setOnlyClash] = useState(false);
  const [draft, setDraft] = useState<Record<string, { status: IssueStatus; note: string; by: string }>>({}), [err, setErr] = useState('');
  const index = useMemo(() => elementIndex(house), [house]), discOf = useMemo(() => new Map(index.map(e => [e.elementId, e.discipline])), [index]);
  // numele lizibil al unui element: camera, produsul, altfel tipul tradus (id-ul rămâne doar în indiciu)
  const nameOf = useMemo(() => { const rooms = new Map(floors(house).flatMap(fl => fl.rooms).map(r => [r.id, r.name])), type = new Map(index.map(e => [e.elementId, e.elementType]));
    const tech = new Map((house.tech ?? []).map(p => [p.id, p])), runs = new Map(floors(house).flatMap(fl => fl.mepRuns ?? []).map(r => [r.id, r]));
    const wallOf = (id: string) => { for (const fl of floors(house)) { const w = fl.walls.find(x => x.id === id); if (w) return { w, fl }; } return null; };
    return (id: string): string => { const room = rooms.get(id); if (room) return room;
      const p = house.placements.find(x => x.id === id); if (p) return `${resolve(catalog, p.variantId)?.product.name ?? p.group} · ${rooms.get(p.roomId) ?? ''}`;
      const tp = tech.get(id); if (tp) return `${TECH_KINDS[tp.kind]?.label[L] ?? tp.kind} · ${rooms.get(tp.roomId) ?? ''}`;
      const run = runs.get(id); if (run) return `${MEP_SYSTEMS[run.system]?.label[L] ?? run.system} (${t('el.run')})`;
      const wf = wallOf(id); if (wf) { const along = roomsAlongWall({ floor: wf.fl }, wf.w); return `${t(wf.w.structural ? 'el.structural_wall' : 'el.wall')}${along.length ? ` ${along.join(' / ')}` : ''}`; }
      return t(`el.${type.get(id) ?? 'element'}`); }; }, [house, catalog, index, t, L]);
  const textOf = (f: CoordinationFinding) => { if (!f.key) return f.message; const v: Record<string, string | number> = { ...(f.vars ?? {}) };
    for (const k of ['a', 'b', 'el', 'room']) if (typeof v[k] === 'string') v[k] = nameOf(v[k] as string);
    if (typeof v.system === 'string') v.system = MEP_SYSTEMS[v.system as MepSystem]?.label[L] ?? v.system;
    const s = t(f.key, v); return s === f.key ? f.message : s; };
  function check(){
    setErr(''); const tol = Number(tolMm) / 1000; if (!(tol >= 0 && tol <= .05)) { setErr(t('coord.tolRange')); return; }
    const r = runCoordination(house, catalog, { clash: { toleranceM: tol, clearances: rules } });
    setPrev(run ? run.findings : null); setRun(r); say(t('coord.done', { n: r.findings.length }));
  }
  const rows = run ? withStatus(run.findings, house.issues) : [];
  const shown = rows.filter(f => (!onlyClash || /^(CLASH_|WALL_PENETRATION)/.test(f.code)) && (f.refs.length === 0 || f.refs.some(id => disc.has(discOf.get(id) ?? 'architecture'))));
  const cmp = run && prev ? compareRuns(prev, run.findings) : null;
  function save(id: string){
    const d = draft[id]; if (!d) return; setErr('');
    if ((d.status === 'accepted' || d.status === 'false_positive') && !d.note.trim()) { setErr(t('coord.needNote')); return; }
    onIssue(id, d.status === 'new' ? null : { status: d.status, ...(d.note.trim() ? { note: d.note.trim().slice(0, 500) } : {}), ...(d.by.trim() ? { by: d.by.trim().slice(0, 60) } : {}), at: new Date().toISOString() });
    say(t('coord.saved'));
  }
  function exportReport(){
    if (!run) return; const md = coordinationReport(run, house.issues, { project: house.name, generatedAt: new Date().toISOString() });
    const a = document.createElement('a'); a.href = URL.createObjectURL(new Blob([md], { type: 'text/markdown' })); a.download = `${house.name.replace(/[^\p{L}\p{N}_-]+/gu, '_')}-coordonare.md`; a.click(); URL.revokeObjectURL(a.href);
  }
  return (<div className="coord-panel bim-panel">
    <h4>{t('coord.title')} <small className="muted">({t('dxf.experimental')})</small></h4>
    <p className="prov">{t('coord.hint')}</p>
    {err && <p role="alert" className="issue ERROR">{err}</p>}
    <label className="f"><span>{t('coord.tolerance')}</span><input type="number" min={0} max={50} value={tolMm} onChange={e => setTolMm(e.target.value)} /></label>
    <fieldset><legend>{t('coord.clearances')}</legend>
      {rules.length === 0 ? <p className="muted">{t('coord.noClearances')}</p> : <ul>{rules.map((r, i) => <li key={i}>{MEP_SYSTEMS[r.a].label[L]} ↔ {MEP_SYSTEMS[r.b].label[L]}: {Math.round(r.minM * 100)} cm <button className="btn ghost sm" aria-label={t('coord.removeRule')} onClick={() => setRules(rules.filter((_, k) => k !== i))}>×</button></li>)}</ul>}
      <div className="coord-rule">
        <select aria-label={t('coord.sysA')} value={ra} onChange={e => setRa(e.target.value as MepSystem)}>{SYSTEMS.map(s => <option key={s} value={s}>{MEP_SYSTEMS[s].label[L]}</option>)}</select>
        <select aria-label={t('coord.sysB')} value={rb} onChange={e => setRb(e.target.value as MepSystem)}>{SYSTEMS.map(s => <option key={s} value={s}>{MEP_SYSTEMS[s].label[L]}</option>)}</select>
        <input aria-label={t('coord.minCm')} type="number" min={1} max={200} placeholder="cm" value={rcm} onChange={e => setRcm(e.target.value)} />
        <button className="btn" disabled={!(Number(rcm) > 0)} onClick={() => { setRules([...rules, { a: ra, b: rb, minM: Number(rcm) / 100 }]); setRcm(''); }}>+</button>
      </div>
    </fieldset>
    <button className="btn" onClick={check}>{t('coord.run')}</button>
    {run && <>
      <p className="prov" data-testid="coord-statement">{t('coord.statement', { e: run.counts.ERROR, w: run.counts.WARNING, i: run.counts.INFO, u: run.counts.UNKNOWN, n: run.rulesChecked.length })}{run.rulesNotChecked.length > 0 && <> {t('coord.notRun', { list: run.rulesNotChecked.join(', ') })}</>} {t('coord.assistive')}</p>
      {cmp && <p className="prov">{t('coord.compare', { added: cmp.added.length, gone: cmp.gone.length, kept: cmp.kept.length })}</p>}
      <fieldset><legend>{t('coord.filter')}</legend>
        {DISCIPLINES.map(d => <label key={d} style={{ marginRight: 8 }}><input type="checkbox" checked={disc.has(d)} onChange={e => { const n = new Set(disc); if (e.target.checked) n.add(d); else n.delete(d); setDisc(n); }} /> {t(`coord.disc.${d}`)}</label>)}
        <label style={{ display: 'block' }}><input type="checkbox" checked={onlyClash} onChange={e => setOnlyClash(e.target.checked)} /> {t('coord.onlyClash')}</label>
      </fieldset>
      <ul className="coord-list bim-issues">{shown.map(f => { const d = draft[f.id] ?? { status: f.status, note: f.note ?? '', by: f.by ?? '' };
        return <li key={f.id} className={`issue ${f.severity === 'ERROR' ? 'ERROR' : f.severity === 'WARNING' ? 'WARNING' : ''}`}>
          <div className="coord-item-head" title={`${f.id} · ${f.code} · ${f.rule}`}><b>{t(`coord.sev.${f.severity}`)}</b>{f.stillPresent && <> · <b>{t('coord.stillPresent')}</b></>}</div>
          <div>{textOf(f)}</div>
          <div className="coord-item-edit">
            <select aria-label={t('coord.status')} value={d.status} onChange={e => setDraft({ ...draft, [f.id]: { ...d, status: e.target.value as IssueStatus } })}>{ISSUE_STATUSES.map(s => <option key={s} value={s}>{t(`coord.st.${s}`)}</option>)}</select>
            <input aria-label={t('coord.note')} placeholder={t('coord.note')} value={d.note} maxLength={500} onChange={e => setDraft({ ...draft, [f.id]: { ...d, note: e.target.value } })} />
            <input aria-label={t('coord.by')} placeholder={t('coord.by')} value={d.by} maxLength={60} onChange={e => setDraft({ ...draft, [f.id]: { ...d, by: e.target.value } })} />
            <button className="btn" disabled={!draft[f.id]} onClick={() => save(f.id)}>{t('coord.save')}</button>
          </div></li>; })}</ul>
      <button className="btn" onClick={exportReport}>{t('coord.export')}</button>
    </>}
  </div>);
}
