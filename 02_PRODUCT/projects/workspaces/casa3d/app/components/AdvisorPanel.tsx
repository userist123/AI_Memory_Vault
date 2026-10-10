'use client';
import type { Advice, AdviceSeverity } from '@/core/advisor';

const T = {
  title: 'Consilier', intro: 'Ce nu e bine în proiect, de ce și cum se repară. Sunt reguli de bun-simț, nu norme legale.',
  empty: 'Nu am găsit nimic de semnalat. Proiectul arată bine.', noRoom: 'Proiect în ansamblu', show: 'Arată', why: 'De ce', fix: 'Cum repari',
  sev: { BLOCKER: 'Blocant', WARNING: 'Atenție', TIP: 'Sfat' } as Record<AdviceSeverity, string>,
  counts: { BLOCKER: 'blocante', WARNING: 'atenționări', TIP: 'sfaturi' } as Record<AdviceSeverity, string>,
};
const COLOR: Record<AdviceSeverity, string> = { BLOCKER: '#B3261E', WARNING: '#B7791F', TIP: '#1F4E79' };
const ORDER: AdviceSeverity[] = ['BLOCKER', 'WARNING', 'TIP'];

export default function AdvisorPanel({ advice, rooms, onShow }: { advice: Advice[]; rooms: { id: string; name: string }[]; onShow(a: Advice): void }){
  const count = (s: AdviceSeverity) => advice.filter(a => a.severity === s).length;
  const groups: { key: string; name: string; items: Advice[] }[] = [];
  for (const r of rooms){ const items = advice.filter(a => a.refs.roomId === r.id); if (items.length) groups.push({ key: r.id, name: r.name, items }); }
  const rest = advice.filter(a => !a.refs.roomId || !rooms.some(r => r.id === a.refs.roomId)); if (rest.length) groups.push({ key: '_', name: T.noRoom, items: rest });
  return (
    <div className="advisor" data-testid="advisor-panel">
      <h3>{T.title}</h3>
      <p className="muted">{T.intro}</p>
      <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', margin: '8px 0' }}>
        {ORDER.map(s => <span key={s} style={{ color: COLOR[s], fontWeight: 600 }}>{count(s)} {T.counts[s]}</span>)}
      </div>
      {advice.length === 0 && <p className="muted">{T.empty}</p>}
      {groups.map(g => (
        <section key={g.key} style={{ marginBottom: 12 }}>
          <h4 style={{ margin: '8px 0 4px' }}>{g.name}</h4>
          {g.items.map(a => (
            <article key={a.id} style={{ borderLeft: `3px solid ${COLOR[a.severity]}`, padding: '6px 8px', marginBottom: 6, background: 'rgba(0,0,0,.03)' }}>
              <div style={{ display: 'flex', gap: 8, alignItems: 'center', justifyContent: 'space-between' }}>
                <strong><span style={{ background: COLOR[a.severity], color: '#fff', borderRadius: 4, padding: '1px 6px', fontSize: 11, marginRight: 6 }}>{T.sev[a.severity]}</span>{a.title}</strong>
                <button className="btn" style={{ minHeight: 26 }} onClick={() => onShow(a)}>{T.show}</button>
              </div>
              <p style={{ margin: '4px 0' }}><b>{T.why}:</b> {a.why}</p>
              <p style={{ margin: '4px 0' }}><b>{T.fix}:</b> {a.fix}</p>
            </article>
          ))}
        </section>
      ))}
    </div>
  );
}
