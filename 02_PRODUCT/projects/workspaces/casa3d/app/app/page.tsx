'use client';
import { useEffect, useState } from 'react';
import { templateSummary } from '../core/templates';
import { formatArea } from '../core/format';
type P = { id: string; name: string; updated_at: string; current_revision: number };
export default function Home(){
  const [list, setList] = useState<P[] | null>(null), [name, setName] = useState('Apartamentul meu'), [err, setErr] = useState(''), [busy, setBusy] = useState(false);
  useEffect(() => { fetch('/api/projects').then(r => r.json()).then(setList).catch(() => setErr('Nu pot încărca proiectele.')); }, []);
  const templates = templateSummary();
  async function create(template: string){ setBusy(true); setErr('');
    const r = await fetch('/api/projects', { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ name, template }) }); const j = await r.json();
    if (!r.ok){ setErr(j.error || 'Eroare.'); setBusy(false); return; } location.href = `/p/${j.id}`; }
  return (<main className="home">
    <h1>Casa mea 3D</h1>
    <p className="lead">Desenezi planul, mobilierul se așază automat cu produse reale, iar fiecare salvare devine o revizie la care poți reveni.</p>
    <div className="new">
      <input type="text" value={name} onChange={e => setName(e.target.value)} aria-label="Numele proiectului" maxLength={120} />
      <button className="btn" disabled={busy} onClick={() => create('blank')}>Plan gol</button>
    </div>
    <div className="tgrid" role="list" aria-label="Șabloane de pornire">
      {templates.map(t => <article key={t.id} className="tcard" role="listitem">
        <h2>{t.name.ro}</h2>
        <p className="tmeta mono">{t.rooms} camere · {formatArea(t.area, 'metric', 'ro')}</p>
        <p className="muted">{t.description.ro}</p>
        <button className="btn primary" disabled={busy} onClick={() => create(t.id)}>Pornesc de aici</button>
      </article>)}
    </div>
    {err && <p className="issue ERROR">{err}</p>}
    <div className="plist">
      {list === null ? <p className="muted">Se încarcă…</p> : list.length === 0 ? <p className="muted">Niciun proiect încă.</p> :
        list.map(p => <a key={p.id} href={`/p/${p.id}`}><span>{p.name}</span><small>{p.current_revision ? `revizia ${p.current_revision}` : 'fără revizii'}</small><small>modificat {new Date(p.updated_at).toLocaleString('ro-RO')}</small></a>)}
    </div>
  </main>);
}
