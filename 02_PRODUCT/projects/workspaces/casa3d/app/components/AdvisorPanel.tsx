'use client';
import type { Advice, AdviceSeverity } from '@/core/advisor';
import type { Units } from '@/core/format';
import { adviceText, translator, type Lang } from '@/lib/i18n';

const SEV_KEY: Record<AdviceSeverity, string> = { BLOCKER: 'advisor.sev.BLOCKER', WARNING: 'advisor.sev.WARNING', TIP: 'advisor.sev.TIP' };
const COUNT_KEY: Record<AdviceSeverity, string> = { BLOCKER: 'advisor.count.BLOCKER', WARNING: 'advisor.count.WARNING', TIP: 'advisor.count.TIP' };

const COLOR: Record<AdviceSeverity, string> = { BLOCKER: '#B3261E', WARNING: '#B7791F', TIP: '#1F4E79' };
const ORDER: AdviceSeverity[] = ['BLOCKER', 'WARNING', 'TIP'];

export default function AdvisorPanel({ advice, rooms, onShow, lang = 'ro', units = 'metric', currency = 'RON' }: { advice: Advice[]; rooms: { id: string; name: string }[]; onShow(a: Advice): void; lang?: Lang; units?: Units; currency?: string }){
  const t = translator(lang);
  const count = (s: AdviceSeverity) => advice.filter(a => a.severity === s).length;
  const groups: { key: string; name: string; items: Advice[] }[] = [];
  for (const r of rooms){ const items = advice.filter(a => a.refs.roomId === r.id); if (items.length) groups.push({ key: r.id, name: r.name, items }); }
  const rest = advice.filter(a => !a.refs.roomId || !rooms.some(r => r.id === a.refs.roomId)); if (rest.length) groups.push({ key: '_', name: t('advisor.noRoom'), items: rest });
  return (
    <div className="advisor" data-testid="advisor-panel">
      <h3>{t('advisor.title')}</h3>
      <p className="muted">{t('advisor.intro')}</p>
      <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', margin: '8px 0' }}>
        {ORDER.map(s => <span key={s} style={{ color: COLOR[s], fontWeight: 600 }}>{count(s)} {t(COUNT_KEY[s])}</span>)}
      </div>
      {advice.length === 0 && <p className="muted">{t('advisor.empty')}</p>}
      {groups.map(g => (
        <section key={g.key} style={{ marginBottom: 12 }}>
          <h4 style={{ margin: '8px 0 4px' }}>{g.name}</h4>
          {g.items.map(a => { const x = adviceText(lang, a, units, currency); return (
            <article key={a.id} style={{ borderLeft: `3px solid ${COLOR[a.severity]}`, padding: '6px 8px', marginBottom: 6, background: 'rgba(0,0,0,.03)' }}>
              <div style={{ display: 'flex', gap: 8, alignItems: 'center', justifyContent: 'space-between' }}>
                <strong><span style={{ background: COLOR[a.severity], color: '#fff', borderRadius: 4, padding: '1px 6px', fontSize: 11, marginRight: 6 }}>{t(SEV_KEY[a.severity])}</span>{x.title}</strong>
                <button className="btn" style={{ minHeight: 26 }} onClick={() => onShow(a)}>{t('advisor.show')}</button>
              </div>
              <p style={{ margin: '4px 0' }}><b>{t('advisor.why')}:</b> {x.why}</p>
              <p style={{ margin: '4px 0' }}><b>{t('advisor.fix')}:</b> {x.fix}</p>
            </article>); })}
        </section>
      ))}
    </div>
  );
}
