'use client';
import dynamic from 'next/dynamic';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { Catalog, Snapshot, Severity, Issue, FurniturePlacement, MaterialsCatalog, BudgetSettings, RoomFinishes } from '@/core/types';
import BudgetPanel, { type Outbound } from './BudgetPanel';
import { relFor, freshness } from '@/core/outbound';
import DesignPanel from './DesignPanel';
import CatalogPanel from './CatalogPanel';
import TwinDesignPanel from './TwinDesignPanel';
import RevisionDiff from './RevisionDiff';
import { finishesOf, budgetOf, roomGeometry } from '@/core/boq';
import { History } from '@/core/history';
import { validatePlacement, validateFloor, severityOf } from '@/core/validate';
import { autoLayout, addPlacement } from '@/core/project';
import { resolve, groups, groupOf } from '@/core/catalog';
import { area, r3, wallLength } from '@/core/geometry';
import PlanView, { type Tool, type Sel } from './PlanView';
const Viewer3D = dynamic(() => import('./Viewer3D'), { ssr: false });

const lei = (v: number) => v.toLocaleString('ro-RO', { minimumFractionDigits: v % 1 ? 2 : 0, maximumFractionDigits: 2 }) + ' lei';
const uid = () => crypto.randomUUID();
const ROOM_TYPES = [['living', 'Living'], ['dormitor', 'Dormitor'], ['bucatarie', 'Bucătărie'], ['baie', 'Baie'], ['hol', 'Hol']];
const ICON: Record<Tool, string> = { select: 'M5 3l12 8-6 1 3 7-2 1-3-7-4 4z', wall: 'M3 12h18M3 9v6M21 9v6', room: 'M4 4h16v16H4z', door: 'M5 21V4h9v17M5 21h14M12 12h.01', window: 'M4 5h16v14H4zM12 5v14M4 12h16' };
const TOOL_LABEL: Record<Tool, string> = { select: 'Selectez', wall: 'Perete', room: 'Cameră', door: 'Ușă', window: 'Fereastră' };

export default function Editor({ id }: { id: string }){
  const [snap, setSnap] = useState<Snapshot | null>(null), [catalog, setCatalog] = useState<Catalog | null>(null), [mc, setMc] = useState<MaterialsCatalog | null>(null), [out, setOut] = useState<Outbound | null>(null);
  const [sideOpen, setSideOpen] = useState(true), [sel, setSel] = useState<Sel>(null), [tool, setTool] = useState<Tool>('select'), [view, setView] = useState<'2d' | '3d' | 'split'>('split');
  const [save, setSave] = useState<'saved' | 'dirty' | 'saving' | 'error'>('saved'), [toast, setToast] = useState(''), [revs, setRevs] = useState<any[]>([]), [rev, setRev] = useState(0);
  const [pending, setPending] = useState<{ before: Snapshot; issues: Issue[] } | null>(null), [persistent, setPersistent] = useState(true), [panel, setPanel] = useState<'props' | 'catalog' | 'budget' | 'design' | 'twin' | 'revs'>('props'), [preview, setPreview] = useState<{ v: any; pid: string } | null>(null), [shares, setShares] = useState<any[]>([]);
  const hist = useRef(new History<Snapshot>()), dragStart = useRef<Snapshot | null>(null), timer = useRef<any>(null), latest = useRef<Snapshot | null>(null), [, force] = useState(0);
  const say = (t: string) => { setToast(t); clearTimeout((say as any).t); (say as any).t = setTimeout(() => setToast(''), 3500); };

  useEffect(() => { Promise.all([fetch(`/api/projects/${id}`).then(r => r.ok ? r.json() : Promise.reject(r)), fetch('/api/catalog').then(r => r.json()), fetch('/api/health').then(r => r.json()), fetch('/api/materials').then(r => r.json())])
    .then(([p, c, h, m]) => { setSnap(p.draft); latest.current = p.draft; setCatalog(c); setMc(m); setRev(p.currentRevision); setPersistent(h.persistent); }).catch(() => say('Proiectul nu există sau nu ai acces la el.'));
    fetch('/api/outbound').then(r => r.ok ? r.json() : null).then(setOut).catch(() => {});
    loadRevs(); }, [id]); // eslint-disable-line
  const loadRevs = () => { fetch(`/api/projects/${id}/revisions`).then(r => r.ok ? r.json() : []).then(setRevs); fetch(`/api/projects/${id}/shares`).then(r => r.ok ? r.json() : []).then(setShares); };
  async function shareRevision(n: number){ const r = await fetch(`/api/projects/${id}/shares`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ revision: n }) }); const j = await r.json();
    if (!r.ok){ say(j.error || 'Partajarea a eșuat.'); return; } const url = `${location.origin}${j.path}`; try { await navigator.clipboard.writeText(url); say('Link de vizualizare copiat; arată doar această revizie.'); } catch { say(`Link de vizualizare: ${url}`); } loadRevs(); }
  async function revokeShare(token: string){ const r = await fetch(`/api/projects/${id}/shares`, { method: 'DELETE', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ token }) }); if (!r.ok){ say('Revocarea a eșuat.'); return; } say('Linkul a fost revocat.'); loadRevs(); }
  const persist = useCallback((s: Snapshot) => { latest.current = s; setSave('dirty'); clearTimeout(timer.current);
    timer.current = setTimeout(async () => { setSave('saving'); const r = await fetch(`/api/projects/${id}`, { method: 'PUT', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ snapshot: latest.current }) });
      if (r.ok) setSave('saved'); else { setSave('error'); say((await r.json()).error || 'Salvarea a eșuat.'); } }, 700); }, [id]);
  const commit = (next: Snapshot, record = true) => { if (record && snap) hist.current.push(snap); setSnap(next); persist(next); force(x => x + 1); };
  const mutate = (fn: (s: Snapshot) => void) => { if (!snap) return; const n = structuredClone(snap); fn(n); commit(n); };

  const issues = useMemo(() => { const m: Record<string, Issue[]> = {}; if (snap && catalog) for (const p of snap.placements) m[p.id] = validatePlacement(snap, catalog, p); return m; }, [snap, catalog]);
  const sev = useMemo(() => Object.fromEntries(Object.entries(issues).map(([k, v]) => [k, severityOf(v)])) as Record<string, Severity>, [issues]);
  const floorIssues = useMemo(() => snap ? validateFloor(snap.floor) : [], [snap]);

  // editare din plan: mutările se validează la final (ERROR → revine, WARNING → cere confirmare)
  const onEdit = (fn: (s: Snapshot) => void, phase: 'start' | 'move' | 'end') => { if (!snap || !catalog) return;
    if (phase === 'start'){ dragStart.current = snap; hist.current.push(snap); }
    if (phase === 'end'){ const before = dragStart.current!; dragStart.current = null; const cur = latest.current!;
      if (sel?.kind === 'placement'){ const p = cur.placements.find(x => x.id === sel.id); const iss = p ? validatePlacement(cur, catalog, p) : [];
        if (severityOf(iss) === 'ERROR'){ hist.current.discardLast(); setSnap(before); persist(before); say(`Poziție refuzată: ${iss.find(i => i.severity === 'ERROR')!.message}`); return; }
        if (severityOf(iss) === 'WARNING'){ setPending({ before, issues: iss }); } }
      return; }
    const n = structuredClone(latest.current || snap); fn(n); latest.current = n; setSnap(n); persist(n); };
  const undo = () => { if (!snap) return; const s = hist.current.undo(snap); if (s){ setPending(null); setSnap(s); persist(s); force(x => x + 1); } };
  const redo = () => { if (!snap) return; const s = hist.current.redo(snap); if (s){ setSnap(s); persist(s); force(x => x + 1); } };
  const del = () => { if (!sel) return; mutate(s => {
    if (sel.kind === 'placement') s.placements = s.placements.filter(p => p.id !== sel.id);
    if (sel.kind === 'wall') s.floor.walls = s.floor.walls.filter(w => w.id !== sel.id);
    if (sel.kind === 'opening'){ const w = s.floor.walls.find(w => w.id === sel.wallId); if (w) w.openings = w.openings.filter(o => o.id !== sel.id); }
    if (sel.kind === 'room'){ s.floor.rooms = s.floor.rooms.filter(r => r.id !== sel.id); s.placements = s.placements.filter(p => p.roomId !== sel.id); } }); setSel(null); };
  const rotate = (pid: string) => { if (!snap || !catalog) return; const n = structuredClone(snap), p = n.placements.find(x => x.id === pid)!; p.rotation = r3(((p.rotation + Math.PI / 2) % (Math.PI * 2))); p.source = 'manual';
    const iss = validatePlacement(n, catalog, p); if (severityOf(iss) === 'ERROR'){ say(`Rotită nu încape: ${iss.find(i => i.severity === 'ERROR')!.message}`); return; } commit(n); if (iss.length) setPending({ before: snap, issues: iss }); };
  useEffect(() => { const k = (e: KeyboardEvent) => { if (preview || (e.target as HTMLElement).closest('input,select,textarea')) return;
    if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'z'){ e.preventDefault(); e.shiftKey ? redo() : undo(); }
    else if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'y'){ e.preventDefault(); redo(); }
    else if (e.key === 'Delete' || e.key === 'Backspace'){ del(); }
    else if (e.key.toLowerCase() === 'r' && sel?.kind === 'placement') rotate(sel.id);
    else if (e.key === 'Escape'){ setTool('select'); } };
    addEventListener('keydown', k); return () => removeEventListener('keydown', k); });

  async function saveRevision(){ if (!snap) return; clearTimeout(timer.current); await fetch(`/api/projects/${id}`, { method: 'PUT', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ snapshot: snap }) });
    const note = prompt('Notă pentru revizie (opțional):') ?? ''; const r = await fetch(`/api/projects/${id}/revisions`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ note }) }); const j = await r.json();
    if (!r.ok){ say(`${j.error} ${(j.details || []).slice(0, 2).map((d: any) => d.message).join(' ')}`); return; } setRev(j.number); setSave('saved'); say(`Revizia ${j.number} a fost salvată.`); loadRevs(); }
  async function restore(n: number){ if (!confirm(`Revii la revizia ${n}? Modificările nesalvate ca revizie se pierd (le poți recupera cu Undo până închizi pagina).`)) return;
    const r = await fetch(`/api/projects/${id}/revisions/${n}`, { method: 'POST' }); if (!r.ok){ say('Restaurarea a eșuat.'); return; } const s = await r.json(); hist.current.push(snap!); setSnap(s); latest.current = s; setSel(null); say(`Ai revenit la revizia ${n}.`); }

  if (!snap || !catalog || !mc) return <div className="home"><p className="muted">{toast || 'Se încarcă proiectul…'}</p></div>;
  const G = groups(catalog);
  const onAddWall = (a: [number, number], b: [number, number]) => mutate(s => { s.floor.walls.push({ id: `wall-${uid().slice(0, 8)}`, a, b, thickness: .15, exterior: false, openings: [] }); });
  const onAddRoom = (rect: any) => { const rid = `camera-${uid().slice(0, 8)}`; mutate(s => { s.floor.rooms.push({ id: rid, name: `Cameră ${s.floor.rooms.length + 1}`, type: 'living', rect }); }); setSel({ kind: 'room', id: rid }); setTool('select'); };
  const onAddOpening = (wallId: string, offset: number, kind: 'door' | 'window') => { const w = snap.floor.walls.find(x => x.id === wallId)!, L = wallLength(w.a, w.b), width = kind === 'door' ? .8 : 1.2;
    if (L < width + .1){ say('Peretele e prea scurt pentru acest gol.'); return; } const oid = `op-${uid().slice(0, 8)}`;
    mutate(s => { s.floor.walls.find(x => x.id === wallId)!.openings.push({ id: oid, kind, offset: r3(Math.max(0, Math.min(L - width, offset - width / 2))), width }); }); setSel({ kind: 'opening', id: oid, wallId }); };
  const total = snap.placements.reduce((a, p) => a + (resolve(catalog, p.variantId)?.offer?.price || 0), 0);
  const selPl = sel?.kind === 'placement' ? snap.placements.find(p => p.id === sel.id) : undefined, selRoom = sel?.kind === 'room' ? snap.floor.rooms.find(r => r.id === sel.id) : undefined;
  const selWall = sel?.kind === 'wall' ? snap.floor.walls.find(w => w.id === sel.id) : undefined, selOp = sel?.kind === 'opening' ? snap.floor.walls.find(w => w.id === sel.wallId)?.openings.find(o => o.id === sel.id) : undefined;
  const num = (v: string, f: (x: number) => void, min = 0) => { const x = Number(v.replace(',', '.')); if (Number.isFinite(x) && x >= min) f(x); };

  return (<div className="ws">
    {!persistent && <div className="banner">Mod demonstrativ: baza de date nu e conectată, iar proiectele se pierd la repornirea serverului.</div>}
    <header className="topbar">
      <a className="btn" href="/">← Proiecte</a>
      <input className="name" value={snap.name} aria-label="Numele proiectului" maxLength={120} onChange={e => { const n = { ...snap, name: e.target.value }; setSnap(n); persist(n); }} />
      <span className={`status ${save === 'error' ? 'err' : ''}`}>{save === 'saved' ? 'Salvat' : save === 'saving' ? 'Se salvează…' : save === 'dirty' ? 'Modificări nesalvate' : 'Eroare la salvare'} · {rev ? `revizia ${rev}` : 'fără revizii'}</span>
      <button className="btn" onClick={undo} disabled={!hist.current.canUndo} title="Ctrl+Z">↶ Anulează</button>
      <button className="btn" onClick={redo} disabled={!hist.current.canRedo} title="Ctrl+Y">↷ Refă</button>
      <div className="btn" role="group" aria-label="Vizualizare" style={{ padding: 2, gap: 2 }}>
        {(['2d', 'split', '3d'] as const).map(vv => <button key={vv} className="btn" style={{ minHeight: 30, border: 0, background: view === vv ? 'var(--graphite)' : 'transparent', color: view === vv ? '#fff' : undefined }} onClick={() => { setView(vv); if (vv === '3d' && typeof matchMedia === 'function' && matchMedia('(max-width: 900px)').matches) setSideOpen(false); }}>{vv === '2d' ? 'Plan' : vv === '3d' ? '3D' : 'Plan + 3D'}</button>)}
      </div>
      <a className="btn" href={`/p/${id}/print`} target="_blank" rel="noopener">Tipărește / PDF</a>
      <button className="btn primary" onClick={saveRevision}>Salvează revizia</button>
    </header>
    <div className="body">
      <nav className="tools" aria-label="Unelte">
        {(Object.keys(ICON) as Tool[]).map(t => <button key={t} aria-pressed={tool === t} onClick={() => setTool(t)} title={TOOL_LABEL[t]}><svg viewBox="0 0 24 24"><path d={ICON[t]} /></svg>{TOOL_LABEL[t]}</button>)}
      </nav>
      <div className={`canvas ${view === 'split' ? 'split' : ''}`}>
        {view !== '3d' && <div style={{ position: 'relative', minHeight: 0 }}><PlanView key={preview ? 'p' + preview.v.tier : 'live'} snap={preview ? preview.v.candidate : snap} catalog={catalog} sel={preview ? null : sel} tool={preview ? 'select' : tool} severities={preview ? {} : sev} onSelect={preview ? () => {} : setSel} onEdit={preview ? () => {} : onEdit} onAddWall={preview ? () => {} : onAddWall} onAddRoom={preview ? () => {} : onAddRoom} onAddOpening={preview ? () => {} : onAddOpening} />
          <div className="hintbar">{tool === 'wall' ? 'Click pentru început, click pentru fiecare colț · Esc sau click dreapta termină' : tool === 'room' ? 'Trage un dreptunghi' : tool === 'door' || tool === 'window' ? 'Apasă lângă un perete' : 'Rotița = zoom · Alt+trage = deplasare · R = rotește · Delete = șterge'}</div></div>}
        {view !== '2d' && <Viewer3D snap={preview ? preview.v.candidate : snap} catalog={catalog} onPick={pid => !preview && pid && setSel({ kind: 'placement', id: pid })} />}
        {preview && <div className="previewbar" role="status">Previzualizare: {preview.v.title} (nesalvată) <button className="btn" onClick={() => setPreview(null)}>Închide</button></div>}
        {pending && <div className="pending" role="alertdialog" aria-label="Avertismente">
          <strong>Poziția are avertismente</strong>{pending.issues.map((i, k) => <div key={k} className={`issue ${i.severity}`}>{i.message}</div>)}
          <div style={{ display: 'flex', gap: 8 }}><button className="btn primary" onClick={() => setPending(null)}>Păstrez poziția</button><button className="btn" onClick={() => { setSnap(pending.before); persist(pending.before); setPending(null); }}>Revin</button></div></div>}
      </div>
      <aside className={`side ${sideOpen ? '' : 'closed'}`}>
        <div className="sidetabs" style={{ display: 'flex', gap: 6, alignItems: 'center', flexWrap: 'wrap' }}>
        <div className="btn" role="tablist" style={{ padding: 2, gap: 2, justifySelf: 'start', overflowX: 'auto', maxWidth: '100%' }}>
          {([['props', 'Proprietăți'], ['catalog', 'Catalog'], ['budget', 'Buget'], ['design', 'Design'], ['twin', 'Twin'], ['revs', 'Revizii']] as const).map(([k, l]) => <button key={k} role="tab" aria-selected={panel === k} className="btn" style={{ minHeight: 30, border: 0, background: panel === k ? 'var(--graphite)' : 'transparent', color: panel === k ? '#fff' : undefined }} onClick={() => { setPanel(k); setSideOpen(true); }}>{l}</button>)}
        </div>
          <button className="btn sidetoggle" aria-expanded={sideOpen} onClick={() => setSideOpen(o => !o)}>{sideOpen ? 'Ascunde ▾' : 'Panou ▴'}</button>
        </div>
        {panel === 'props' && <>
          {floorIssues.map((i, k) => <div key={k} className={`issue ${i.severity}`}>{i.message}</div>)}
          {!sel && <>
            <h3>{snap.name}</h3>
            <div className="prov">{snap.floor.rooms.length} încăperi · {snap.floor.rooms.reduce((a, r) => a + area(r.rect), 0).toFixed(1)} m² · {snap.placements.length} piese · mobilier {lei(total)} · bugetul complet e în tabul Buget</div>
            <label className="f"><span>Înălțime tavan (cm)</span><input type="number" value={Math.round(snap.floor.ceilingHeight * 100)} onChange={e => num(e.target.value, x => mutate(s => { s.floor.ceilingHeight = r3(x / 100); }), 200)} /></label>
            <button className="btn" onClick={() => { if (!confirm('Amenajarea automată înlocuiește toată mobila din plan. Continui?')) return; const r = autoLayout(snap, catalog); commit(r.snapshot); say(r.notFit.length ? `Nu au încăput: ${r.notFit.map(n => n.key + ' în ' + n.room).join(', ')}` : 'Amenajare automată aplicată.'); }}>Amenajare automată (toată casa)</button>
            <p className="muted" style={{ margin: 0, fontSize: 12.5 }}>Selectează o cameră, un perete sau o piesă de mobilier ca să le modifici.</p>
          </>}
          {selRoom && <RoomPanel room={selRoom} snap={snap} G={G} mc={mc} onFinish={(patch: Partial<RoomFinishes>) => mutate(s => { s.finishes = { ...(s.finishes || {}), [selRoom.id]: { ...finishesOf(s, selRoom), ...patch } }; })} onChange={(fn: (r: any) => void) => mutate(s => fn(s.floor.rooms.find(r => r.id === selRoom.id)!))}
            onAuto={() => { const r = autoLayout(snap, catalog, { roomId: selRoom.id }); commit(r.snapshot); say(r.notFit.length ? 'Unele piese nu au încăput.' : `${selRoom.name}: amenajare automată aplicată.`); }}
            onAdd={(vid: string) => { const r = addPlacement(snap, catalog, selRoom.id, vid); if (!r){ say('Nu am găsit loc liber pentru piesa asta în cameră.'); return; } commit(r); setSel({ kind: 'placement', id: r.placements.at(-1)!.id }); }} num={num} />}
          {selWall && <>
            <h3>Perete</h3>
            <div className="grid2">
              <label className="f"><span>Lungime (cm)</span><input type="number" value={Math.round(wallLength(selWall.a, selWall.b) * 100)} onChange={e => num(e.target.value, x => mutate(s => { const w = s.floor.walls.find(w => w.id === selWall.id)!, L = wallLength(w.a, w.b), ux = (w.b[0] - w.a[0]) / L, uz = (w.b[1] - w.a[1]) / L; w.b = [r3(w.a[0] + ux * x / 100), r3(w.a[1] + uz * x / 100)]; }), 20)} /></label>
              <label className="f"><span>Grosime (cm)</span><input type="number" value={Math.round(selWall.thickness * 100)} onChange={e => num(e.target.value, x => mutate(s => { s.floor.walls.find(w => w.id === selWall.id)!.thickness = r3(x / 100); }), 5)} /></label>
            </div>
            <label className="f" style={{ display: 'flex', gap: 8, alignItems: 'center' }}><input type="checkbox" checked={selWall.exterior} onChange={e => mutate(s => { s.floor.walls.find(w => w.id === selWall.id)!.exterior = e.target.checked; })} /> Perete exterior</label>
            <div className="prov">{selWall.openings.length} goluri · trage capetele albastre ca să-l modifici</div>
            <button className="btn danger" onClick={del}>Șterge peretele</button>
          </>}
          {selOp && <>
            <h3>{selOp.kind === 'door' ? 'Ușă' : 'Fereastră'}</h3>
            <div className="grid2">
              <label className="f"><span>Lățime (cm)</span><input type="number" value={Math.round(selOp.width * 100)} onChange={e => num(e.target.value, x => mutate(s => { s.floor.walls.find(w => w.id === (sel as any).wallId)!.openings.find(o => o.id === selOp.id)!.width = r3(x / 100); }), 30)} /></label>
              <label className="f"><span>Poziție pe perete (cm)</span><input type="number" value={Math.round(selOp.offset * 100)} onChange={e => num(e.target.value, x => mutate(s => { s.floor.walls.find(w => w.id === (sel as any).wallId)!.openings.find(o => o.id === selOp.id)!.offset = r3(x / 100); }))} /></label>
            </div>
            <button className="btn danger" onClick={del}>Șterge golul</button>
          </>}
          {selPl && <PlacementPanel out={out} p={selPl} snap={snap} catalog={catalog} issues={issues[selPl.id] || []} G={G} num={num}
            onVariant={vid => { const n = structuredClone(snap), q = n.placements.find(x => x.id === selPl.id)!; q.variantId = vid; const iss = validatePlacement(n, catalog, q);
              if (severityOf(iss) === 'ERROR'){ say(`Varianta nu încape aici: ${iss.find(i => i.severity === 'ERROR')!.message}`); return; } commit(n); if (iss.length) setPending({ before: snap, issues: iss }); }}
            onMove={(x, z) => { const n = structuredClone(snap), q = n.placements.find(p => p.id === selPl.id)!; q.x = r3(x); q.z = r3(z); q.source = 'manual'; const iss = validatePlacement(n, catalog, q);
              if (severityOf(iss) === 'ERROR'){ say(`Poziție refuzată: ${iss.find(i => i.severity === 'ERROR')!.message}`); return; } commit(n); }}
            onRotate={() => rotate(selPl.id)} onDelete={del} />}
        </>}
        {panel === 'design' && <DesignPanel id={id} snap={snap} say={say} onPreview={(v: any, pid: string | null) => setPreview(v && pid ? { v, pid } : null)} onApplied={(s: Snapshot, n: number) => { hist.current.push(snap); setSnap(s); latest.current = s; setRev(n); setSave('saved'); setSel(null); }} />}
        {panel === 'twin' && <TwinDesignPanel id={id} snap={snap} catalog={catalog} say={say} onPreview={(s: Snapshot | null, label: string | null) => setPreview(s && label ? { v: { candidate: s, title: label, tier: 'twin-' + label }, pid: 'twin' } : null)} onApplied={(s: Snapshot, n: number) => { hist.current.push(snap); setSnap(s); latest.current = s; setRev(n); setSave('saved'); setSel(null); loadRevs(); }} />}
        {panel === 'catalog' && <CatalogPanel catalog={catalog} snap={snap} onUse={(g, vid) => mutate(s => { s.selections[g] = vid; if (!s.picked.includes(g)) s.picked.push(g); })} onAdd={selRoom ? (vid: string) => { const r = addPlacement(snap, catalog, selRoom.id, vid); if (!r){ say('Nu am găsit loc liber pentru piesa asta în cameră.'); return; } commit(r); setSel({ kind: 'placement', id: r.placements.at(-1)!.id }); } : undefined} />}
        {panel === 'budget' && <BudgetPanel snap={snap} catalog={catalog} mc={mc} out={out} onBudget={(patch: Partial<BudgetSettings>) => mutate(s => { s.budget = { ...budgetOf(s), ...patch }; })} />}
        {panel === 'revs' && <div className="revs"><h3>Revizii</h3>{revs.length === 0 && <p className="muted">Nicio revizie încă. Folosește „Salvează revizia”.</p>}
          {revs.map(r => <div key={r.number} className="r"><span>Revizia {r.number}{r.note ? ` · ${r.note}` : ''}</span><button className="btn" onClick={() => restore(r.number)}>Revin</button><button className="btn" onClick={() => shareRevision(r.number)} title="Link doar pentru vizualizare, către această revizie">Partajează</button><small>{new Date(r.created_at).toLocaleString('ro-RO')}</small></div>)}
          <RevisionDiff id={id} revs={revs} />
          {shares.filter(s => !s.revokedAt).length > 0 && <><h4>Linkuri de vizualizare active</h4>{shares.filter(s => !s.revokedAt).map(s => <div key={s.token} className="r"><span>Revizia {s.revisionNumber}</span><a className="btn" href={s.path} target="_blank" rel="noopener">Deschide</a><button className="btn danger" onClick={() => revokeShare(s.token)}>Revocă</button><small>{new Date(s.createdAt).toLocaleString('ro-RO')}</small></div>)}</>}</div>}
      </aside>
    </div>
    {toast && <div className="toast" role="status">{toast}</div>}
  </div>);
}

function RoomPanel({ room, snap, G, mc, onFinish, onChange, onAuto, onAdd, num }: any){
  const [grp, setGrp] = useState(Object.keys(G).find(k => !G[k].includedWith)!), [vi, setVi] = useState(0);
  const w = room.rect.x1 - room.rect.x0, d = room.rect.z1 - room.rect.z0;
  return (<>
    <h3>{room.name}</h3><div className="prov">{area(room.rect).toFixed(2)} m² · {snap.placements.filter((p: any) => p.roomId === room.id).length} piese</div>
    <div className="grid2">
      <label className="f"><span>Nume</span><input type="text" value={room.name} maxLength={60} onChange={e => onChange((r: any) => { r.name = e.target.value; })} /></label>
      <label className="f"><span>Tip</span><select value={room.type} onChange={e => onChange((r: any) => { r.type = e.target.value; })}>{ROOM_TYPES.map(([v, l]) => <option key={v} value={v}>{l}</option>)}</select></label>
      <label className="f"><span>Lățime (cm)</span><input type="number" value={Math.round(w * 100)} onChange={e => num(e.target.value, (x: number) => onChange((r: any) => { r.rect.x1 = r3(r.rect.x0 + x / 100); }), 60)} /></label>
      <label className="f"><span>Lungime (cm)</span><input type="number" value={Math.round(d * 100)} onChange={e => num(e.target.value, (x: number) => onChange((r: any) => { r.rect.z1 = r3(r.rect.z0 + x / 100); }), 60)} /></label>
      <label className="f"><span>Poziție x (cm)</span><input type="number" value={Math.round(room.rect.x0 * 100)} onChange={e => { const x = Number(e.target.value) / 100; if (Number.isFinite(x)) onChange((r: any) => { r.rect.x1 = r3(x + (r.rect.x1 - r.rect.x0)); r.rect.x0 = r3(x); }); }} /></label>
      <label className="f"><span>Poziție z (cm)</span><input type="number" value={Math.round(room.rect.z0 * 100)} onChange={e => { const z = Number(e.target.value) / 100; if (Number.isFinite(z)) onChange((r: any) => { r.rect.z1 = r3(z + (r.rect.z1 - r.rect.z0)); r.rect.z0 = r3(z); }); }} /></label>
    </div>
    <p className="prov" style={{ margin: 0 }}>Camera și pereții se editează separat: după ce schimbi dimensiunile, ajustează și pereții (sau invers).</p>
    <FinishesPanel room={room} snap={snap} mc={mc} onFinish={onFinish} />
    <button className="btn" onClick={onAuto}>Amenajare automată în cameră</button>
    <h4>Adaugă mobilier</h4>
    <select value={grp} onChange={e => { setGrp(e.target.value); setVi(0); }}>{Object.entries<any>(G).filter(([, g]) => !g.includedWith).map(([k, g]) => <option key={k} value={k}>{g.label}</option>)}</select>
    <select value={vi} onChange={e => setVi(Number(e.target.value))}>{G[grp].variants.map((v: any, i: number) => <option key={v.id} value={i}>{v.name}</option>)}</select>
    <button className="btn primary" onClick={() => onAdd(G[grp].variants[vi].id)}>Adaugă în cameră</button>
  </>);
}
function PlacementPanel({ out, p, snap, catalog, issues, G, num, onVariant, onMove, onRotate, onDelete }: { out: Outbound | null; p: FurniturePlacement; snap: Snapshot; catalog: Catalog; issues: Issue[]; G: any; num: any; onVariant(v: string): void; onMove(x: number, z: number): void; onRotate(): void; onDelete(): void }){
  const rv = resolve(catalog, p.variantId)!, room = snap.floor.rooms.find(r => r.id === p.roomId), g = G[groupOf(p.variantId)];
  return (<>
    <h3>{rv.variant.name}</h3>
    <div className="prov">{g.label} · {room?.name} · {rv.offer ? lei(rv.offer.price) : 'preț necunoscut'}</div>
    {rv.offer && <div className="prov">Sursa: {rv.offer.provenance.source} · verificat {rv.offer.provenance.verifiedAt} · încredere preț {rv.offer.provenance.confidence} · dimensiuni {rv.variant.dimensionsConfidence} · stoc {rv.offer.availability === 'UNKNOWN' ? 'necunoscut' : rv.offer.availability}{freshness(rv.offer.provenance.verifiedAt).stale ? ' · preț de reverificat' : ''}</div>}
    {issues.length ? issues.map((i, k) => <div key={k} className={`issue ${i.severity}`}>{i.message}</div>) : <div className="issue PASS">Poziție validă: încape, nu blochează uși, ferestre sau circulația.</div>}
    <div className="grid2">
      <label className="f"><span>x (cm)</span><input type="number" value={Math.round(p.x * 100)} onChange={e => num(e.target.value, (x: number) => onMove(x / 100, p.z), -1e9)} /></label>
      <label className="f"><span>z (cm)</span><input type="number" value={Math.round(p.z * 100)} onChange={e => num(e.target.value, (z: number) => onMove(p.x, z / 100), -1e9)} /></label>
    </div>
    <div style={{ display: 'flex', gap: 8 }}><button className="btn" onClick={onRotate}>Rotește 90° (R)</button>{rv.offer && out?.targets[`o:${rv.offer.id}`] && <a className="btn" href={`/go/o/${rv.offer.id}`} target="_blank" rel={relFor(out.targets[`o:${rv.offer.id}`])}>Vezi produsul{out.targets[`o:${rv.offer.id}`] === 'affiliate' ? ' (afiliere)' : ''}</a>}<button className="btn danger" onClick={onDelete}>Șterge</button></div>
    <h4>Variante ({g.variants.length}) — doar pentru această piesă</h4>
    {g.variants.map((v: any) => { const o = catalog.offers.find(x => x.variantId === v.id), dm = v.dimensionsCm;
      return <button key={v.id} className="var" aria-pressed={v.id === p.variantId} onClick={() => onVariant(v.id)}><span>{v.name}</span><span className="mono">{o ? lei(o.price) : '—'}</span><small>{dm ? `${dm.w}×${dm.d}×${dm.h} cm` : 'dimensiuni necunoscute'}{v.dimensionsConfidence === 'MEDIUM' ? ' (aprox.)' : ''}</small></button>; })}
  </>);
}

function FinishesPanel({ room, snap, mc, onFinish }: { room: any; snap: Snapshot; mc: MaterialsCatalog; onFinish(p: Partial<RoomFinishes>): void }){
  const f = finishesOf(snap, room), g = roomGeometry(snap, room), by = (...c: string[]) => mc.materials.filter(m => c.includes(m.category));
  const opt = (m: any) => <option key={m.id} value={m.id}>{m.name} · {m.unitPrice.toLocaleString('ro-RO')} lei/{m.unit === 'm2' ? 'm²' : m.unit}</option>;
  return (<>
    <h4>Finisaje</h4>
    <div className="prov">Pardoseală {g.floorArea} m² · pereți {g.wallNet} m² (fără goluri) · tavan {g.ceiling} m² · perimetru {g.perimeter} m</div>
    <label className="f"><span>Pardoseală</span><select value={f.floor} onChange={e => onFinish({ floor: e.target.value })}>{by('parquet', 'floor_tile').map(opt)}</select></label>
    <label className="f"><span>Vopsea pereți și tavan</span><select value={f.wallPaint} onChange={e => onFinish({ wallPaint: e.target.value })}>{by('paint').map(opt)}</select></label>
    {(room.type === 'baie' || room.type === 'bucatarie') && <label className="f"><span>Faianță</span><select value={f.wallTile || ''} onChange={e => onFinish({ wallTile: e.target.value || null })}><option value="">Fără faianță</option>{by('wall_tile').map(opt)}</select></label>}
    {room.type !== 'baie' && room.type !== 'bucatarie' && <label className="f"><span>Plintă</span><select value={f.baseboard || ''} onChange={e => onFinish({ baseboard: e.target.value || null })}><option value="">Fără plintă</option>{by('baseboard').map(opt)}</select></label>}
    <div className="grid2">
      <label className="f"><span>Corp de iluminat</span><select value={f.light} onChange={e => onFinish({ light: e.target.value })}>{by('lighting').map(opt)}</select></label>
      <label className="f"><span>Număr corpuri</span><input type="number" min={0} max={20} value={f.lights ?? ''} placeholder="automat" onChange={e => onFinish({ lights: e.target.value === '' ? undefined : Math.max(0, Math.min(20, Number(e.target.value) || 0)) })} /></label>
    </div>
  </>);
}
