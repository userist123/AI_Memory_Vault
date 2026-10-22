'use client';
// Releveu pe șantier (de pe telefon): pe camera aleasă, laturile N/E/S/V și cele două diagonale, în cm. Rezultatul se vede
// imediat (laturi opuse, echer, față de plan) și se salvează cu data măsurării.
import { useEffect, useMemo, useState } from 'react';
import type { Room } from '@/core/types';
import { analyzeSurvey, type RoomSurvey } from '@/core/survey';
import { usePrefs } from '@/lib/prefs';
import { intlLocale } from '@/lib/i18n';

type F = { n: string; e: string; s: string; w: string; d1: string; d2: string; note: string };
const EMPTY: F = { n: '', e: '', s: '', w: '', d1: '', d2: '', note: '' };
const num = (v: string) => v.trim() === '' ? undefined : Number(v.replace(',', '.'));
export default function SurveyPanel({ id, rooms, say }: { id: string; rooms: Room[]; say(s: string): void }){
  const { t, lang } = usePrefs(), [list, setList] = useState<RoomSurvey[]>([]), [roomId, setRoomId] = useState(rooms[0]?.id ?? ''), [f, setF] = useState<F>(EMPTY), [busy, setBusy] = useState(false);
  const load = () => fetch(`/api/projects/${id}/survey`).then(r => r.ok ? r.json() : []).then(setList).catch(() => {});
  useEffect(() => { load(); }, [id]); // eslint-disable-line
  const saved = list.find(s => s.roomId === roomId), room = rooms.find(r => r.id === roomId);
  useEffect(() => { setF(saved ? { n: String(saved.n), e: String(saved.e), s: String(saved.s), w: String(saved.w), d1: saved.d1 != null ? String(saved.d1) : '', d2: saved.d2 != null ? String(saved.d2) : '', note: saved.note ?? '' } : EMPTY); }, [roomId, saved?.measuredAt]); // eslint-disable-line
  const m = { n: num(f.n), e: num(f.e), s: num(f.s), w: num(f.w), d1: num(f.d1), d2: num(f.d2) }, complete = [m.n, m.e, m.s, m.w].every(v => v != null && Number.isFinite(v) && v! >= 10);
  const a = useMemo(() => room && complete ? analyzeSurvey(room.rect, { n: m.n!, e: m.e!, s: m.s!, w: m.w!, ...(m.d1 && m.d2 ? { d1: m.d1, d2: m.d2 } : {}) }) : null, [room, f]); // eslint-disable-line
  async function save(){ setBusy(true);
    try { const r = await fetch(`/api/projects/${id}/survey`, { method: 'PUT', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ roomId, ...m, note: f.note }) }), j = await r.json().catch(() => ({}));
      if (!r.ok){ say(j.error || t('sv.error')); return; } say(t('sv.saved', { room: room?.name ?? '' })); load(); } finally { setBusy(false); } }
  const field = (k: keyof F, label: string) => <label className="f"><span>{label}</span><input inputMode="decimal" value={f[k]} onChange={e => setF(x => ({ ...x, [k]: e.target.value }))} placeholder="cm" /></label>;
  const absTxt = (v: number) => `${String(v).replace('.', lang === 'ro' ? ',' : '.')} cm`;
  const cmTxt = (v: number) => `${v > 0 ? '+' : ''}${String(v).replace('.', lang === 'ro' ? ',' : '.')} cm`;
  if (!rooms.length) return null;
  return (<section className="survey"><h3>{t('sv.title')}<span className="pill">{t('sv.done', { n: list.length, total: rooms.length })}</span></h3>
    <p className="muted">{t('sv.sub')}</p>
    <label className="f"><span>{t('sv.room')}</span><select value={roomId} onChange={e => setRoomId(e.target.value)}>{rooms.map(r => <option key={r.id} value={r.id}>{r.name}{list.some(s => s.roomId === r.id) ? ' ✓' : ''}</option>)}</select></label>
    <div className="grid2">{field('n', t('sv.n'))}{field('s', t('sv.s'))}{field('w', t('sv.w'))}{field('e', t('sv.e'))}{field('d1', t('sv.d1'))}{field('d2', t('sv.d2'))}</div>
    {a && <div className={`sv-res sv-${a.level}`} role="status">
      <p><b>{t(`sv.level.${a.level}`)}</b></p>
      <ul><li>{t('sv.opposite', { ns: absTxt(a.opposite.ns), ew: absTxt(a.opposite.ew) })}</li>
        {a.diagDiff != null ? <li>{t('sv.diag', { d: absTxt(a.diagDiff), deg: String(a.angleOffDeg).replace('.', lang === 'ro' ? ',' : '.') })}</li> : <li className="muted">{t('sv.noDiag')}</li>}
        <li>{t('sv.vsPlan', { w: cmTxt(a.vsPlan.width), d: cmTxt(a.vsPlan.depth), pw: a.planW, pd: a.planD })}</li></ul></div>}
    <label className="f"><span>{t('sv.note')}</span><input value={f.note} maxLength={500} onChange={e => setF(x => ({ ...x, note: e.target.value }))} /></label>
    <div className="oedit-act">{saved && <small className="muted">{t('sv.measuredAt', { d: new Date(saved.measuredAt).toLocaleDateString(intlLocale(lang)) })}</small>}<button className="btn primary" disabled={busy || !complete} onClick={save}>{t('sv.save')}</button></div>
  </section>);
}
