'use client';
// Blocul „Chestionarul clientului” din panoul Design: linkul (generare, copiere, revocare, redeschidere, ștergere), starea,
// răspunsurile pe secțiuni, pozele clientului grupate după părere și „Preia în brief” cu previzualizare câmp cu câmp.
import { useEffect, useMemo, useRef, useState } from 'react';
import { usePrefs } from '@/lib/prefs';
import { intlLocale } from '@/lib/i18n';
import { formatMoney } from '@/core/format';
import { askConfirm } from '@/lib/dialog';
import type { DesignBrief } from '@/core/brief';
import { SECTIONS, briefFromClient, type ClientBrief, type BriefField } from '@/core/client-brief';
import { answerText, questionLabel, questionsOf } from '@/lib/client-brief-labels';

type Photo = { id: string; verdict: 'love' | 'ok' | 'dislike' | null; note: string; width: number; height: number };
type State = { active: boolean; token?: string; path?: string; url?: string | null; publicUrl: boolean; createdAt: string; updatedAt: string; submittedAt: string | null; expiresAt: string | null; revokedAt: string | null;
  importedAt: string | null; answered: boolean; data: Partial<ClientBrief>; photos: Photo[] };
const GROUPS = ['love', 'ok', 'dislike', 'none'] as const;
const FIELD_LABEL: Record<keyof DesignBrief, string> = { propertyType: 'design.property', occupants: 'design.occupants', children: 'design.children', pets: 'design.pets', accessibility: 'design.accessibility', style: 'design.style',
  colors: 'design.colors', priorities: 'cbrief.f.priorities', budget: 'design.totalBudget', suppliers: 'cbrief.f.suppliers', avoid: 'design.avoid', notes: 'design.notes' };

export default function ClientBriefPanel({ id, brief, cur, say, flush, onImported }: { id: string; brief: DesignBrief; cur: string; say(t: string): void; flush?(): Promise<boolean>;
  onImported(serverBrief: DesignBrief, applied: (keyof DesignBrief)[], draftRev: number): void }){
  const { t, tp, lang } = usePrefs(), api = `/api/projects/${id}/client-brief`;
  const [st, setSt] = useState<State | null | undefined>(undefined), [busy, setBusy] = useState(false), [view, setView] = useState(false), [pick, setPick] = useState<Record<string, boolean> | null>(null);
  // brief-ul văzut ca „actual” în previzualizare: „Preia” îl trimite, iar serverul refuză (409) dacă brief-ul salvat diferă,
  // ca o schimbare făcută între timp pe alt dispozitiv să nu fie suprascrisă
  const previewBase = useRef<DesignBrief | null>(null);
  const when = (d: string) => new Date(d).toLocaleString(intlLocale(lang), { dateStyle: 'medium', timeStyle: 'short' });
  useEffect(() => { let on = true; fetch(api).then(r => r.ok ? r.json() : null).then(j => { if (on) setSt(j ? j.brief : null); }).catch(() => { if (on) setSt(null); }); return () => { on = false; }; }, [api]);
  async function call(method: string, body?: unknown){ if (busy) return null; setBusy(true);
    try { const r = await fetch(api, { method, ...(body ? { headers: { 'content-type': 'application/json' }, body: JSON.stringify(body) } : {}) }), j = await r.json().catch(() => ({}));
      if (!r.ok){ say(j.error || t('cbrief.d.failed')); return null; } setSt(j.brief); return j.brief as State | null; }
    catch { say(t('cbrief.d.failed')); return null; } finally { setBusy(false); } }
  const linkOf = (s: State) => s.url || `${location.origin}${s.path}`;
  const copy = async (s: State) => { const url = linkOf(s); try { await navigator.clipboard.writeText(url); say(t('cbrief.d.copied')); } catch { say(t('cbrief.d.linkShown', { url })); } };
  const create = async () => { const s = await call('POST', {}); if (s?.active) void copy(s); };
  const revoke = async () => { if (await askConfirm({ title: t('cbrief.d.confirmRevoke'), body: t('cbrief.d.confirmRevokeBody'), confirmLabel: t('cbrief.d.revoke'), danger: true })) void call('PATCH', { action: 'revoke' }); };
  const remove = async () => { if (await askConfirm({ title: t('cbrief.d.confirmDelete'), body: t('cbrief.d.confirmDeleteBody'), confirmLabel: t('cbrief.d.delete'), danger: true })){ setPick(null); setView(false); void call('DELETE'); } };

  // previzualizarea se calculează față de brief-ul pe care îl vede designerul acum; serverul recalculează la preluare
  const fields: BriefField[] = useMemo(() => st ? briefFromClient(st.data, brief, cur).fields : [], [st, brief, cur]);
  const show = (k: keyof DesignBrief, v: unknown): string => {
    if (k === 'style') return t(`design.styles.${v}`); if (k === 'propertyType') return t(`design.property.${v}`);
    if (typeof v === 'boolean') return t(v ? 'cbrief.yes' : 'cbrief.no');
    if (k === 'budget') return v == null ? t('design.noLimit') : formatMoney(v as number, cur, lang);
    if (k === 'priorities') return (v as string[]).length ? (v as string[]).map(p => t(`design.priorities.${p}`)).join(', ') : t('cbrief.d.preview.empty');
    if (Array.isArray(v)) return v.join(', ') || t('cbrief.d.preview.empty');
    return v === '' || v == null ? t('cbrief.d.preview.empty') : String(v); };
  async function importPicked(){ const keys = fields.filter(f => pick?.[f.key]).map(f => f.key); if (!keys.length || busy) return; setBusy(true);
    try { if (flush && !(await flush())) return;   // ciorna din editor se salvează întâi; apoi se citește versiunea proiectului
      if (!previewBase.current){ say(t('cbrief.d.failed')); return; }
      const r = await fetch(`${api}/import`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ fields: keys, base: previewBase.current }) }), j = await r.json().catch(() => ({}));
      if (!r.ok){ say(j.error || t('cbrief.d.failed')); return; }
      onImported(j.brief, j.applied, j.draftRev); setPick(null); say(tp('cbrief.d.preview.done', j.applied.length));
      setSt(s => s ? { ...s, importedAt: new Date().toISOString() } : s); }
    catch { say(t('cbrief.d.failed')); } finally { setBusy(false); } }

  async function openPreview(){ if (busy) return; setBusy(true);
    try { if (flush && !(await flush())) return;   // ciorna din editor se salvează întâi, ca „actual” să fie brief-ul salvat
      const s = await fetch(api).then(r => r.ok ? r.json() : null); if (!s){ say(t('cbrief.d.failed')); return; }
      previewBase.current = brief; setSt(s.brief); setPick(Object.fromEntries(fields.map(f => [f.key, f.preselect]))); }
    catch { say(t('cbrief.d.failed')); } finally { setBusy(false); } }
  if (st === undefined) return null;
  const status = !st ? '' : st.submittedAt ? t('cbrief.d.status.submitted', { when: when(st.submittedAt) }) : st.answered || st.photos.length ? t('cbrief.d.status.draft', { when: when(st.updatedAt) }) : t('cbrief.d.status.empty');
  const linkState = !st || st.active ? (st?.expiresAt ? t('cbrief.d.status.expires', { when: when(st.expiresAt) }) : '') : t(st.revokedAt ? 'cbrief.d.status.revoked' : 'cbrief.d.status.expired');
  return (<section className="cbp" aria-labelledby="cbp-h">
    <h4 id="cbp-h">{t('cbrief.d.title')}</h4>
    {!st ? <><p className="prov">{t('cbrief.d.intro')}</p><button className="btn" disabled={busy} onClick={create}>{t('cbrief.d.create')}</button></> : <>
      <p className="cbp-status"><b className={st.submittedAt ? 'ok' : ''}>{status}</b>{[st.photos.length ? tp('cbrief.d.photos', st.photos.length) : '', linkState, st.importedAt ? t('cbrief.d.imported', { when: when(st.importedAt) }) : ''].filter(Boolean).map(x => <span key={x}> · {x}</span>)}</p>
      {st.active && !st.publicUrl && <p className="prov">{t('cbrief.d.localOnly')}</p>}
      <div className="cbp-btns">
        {st.active ? <><button className="btn sm" onClick={() => copy(st)}>{t('cbrief.d.copy')}</button><button className="btn sm" disabled={busy} onClick={revoke}>{t('cbrief.d.revoke')}</button></>
          : <button className="btn sm" disabled={busy} onClick={create}>{t('cbrief.d.createNew')}</button>}
        {st.submittedAt && <button className="btn sm" disabled={busy} onClick={() => call('PATCH', { action: 'reopen' })}>{t('cbrief.d.reopen')}</button>}
        {(st.answered || st.photos.length > 0) && <button className="btn sm" aria-expanded={view} onClick={() => setView(!view)}>{t(view ? 'cbrief.d.hide' : 'cbrief.d.view')}</button>}
        {st.answered && <button className="btn sm primary" aria-expanded={!!pick} onClick={() => { if (pick) setPick(null); else void openPreview(); }}>{t('cbrief.d.import')}</button>}
        <button className="btn sm danger" disabled={busy} onClick={remove}>{t('cbrief.d.delete')}</button>
      </div>
      {pick && <div className="cbp-preview"><p className="prov">{t(fields.length ? 'cbrief.d.preview.intro' : 'cbrief.d.preview.none')}</p>
        {fields.map(f => <label key={f.key} className="cbp-row"><input type="checkbox" checked={!!pick[f.key]} onChange={e => setPick({ ...pick, [f.key]: e.target.checked })} />
          <span><b>{t(FIELD_LABEL[f.key], { cur })}</b><small>{t('cbrief.d.preview.arrow', { from: show(f.key, f.current), to: show(f.key, f.proposed) })}</small></span></label>)}
        <div className="cbp-btns"><button className="btn sm primary" disabled={busy || !fields.some(f => pick[f.key])} onClick={importPicked}>{t('cbrief.d.preview.apply')}</button><button className="btn sm" onClick={() => setPick(null)}>{t('cbrief.d.preview.cancel')}</button></div></div>}
      {view && <div className="cbp-answers">
        {SECTIONS.map(s => { const rows = questionsOf(s).map(q => [q.key, answerText(t, lang, q, st.data, cur)] as const).filter(r => r[1] != null); if (!rows.length) return null;
          return <div key={s} className="cbp-sec"><h5>{t(`cbrief.s.${s}`)}</h5><dl>{rows.map(([k, v]) => <div key={k}><dt>{questionLabel(t, k)}</dt><dd>{v}</dd></div>)}</dl></div>; })}
        {st.photos.length > 0 && <div className="cbp-sec"><h5>{t('cbrief.d.moodboard')}</h5>
          {GROUPS.map(g => { const list = st.photos.filter(p => (p.verdict ?? 'none') === g); if (!list.length) return null;
            return <div key={g} className={`cbp-group v-${g}`}><h6>{t(`cbrief.photos.${g}`)} ({list.length})</h6>
              <div className="cbp-grid">{list.map(p => <figure key={p.id}><a href={`${api}/photos/${p.id}`} target="_blank" rel="noreferrer"><img src={`${api}/photos/${p.id}`} alt={p.note || t(`cbrief.photos.${g}`)} width={p.width} height={p.height} loading="lazy" /></a>{p.note && <figcaption>{p.note}</figcaption>}</figure>)}</div></div>; })}</div>}
      </div>}
    </>}
  </section>);
}
