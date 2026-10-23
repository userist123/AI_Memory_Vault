'use client';
import { usePrefs } from '@/lib/prefs';
import { formatLength } from '@/core/format';
import { NUDGE_CM, NUDGE_BIG_CM } from '@/core/edit-ops';
import { helpRows } from '@/lib/commands';
// Rândurile vin din registrul de comenzi (lib/commands.ts); tastele sunt aceleași în orice limbă, descrierile vin din dicționar.
const H = { fontSize: 12, fontWeight: 600, margin: '12px 0 4px', opacity: .8 } as const, K = { padding: '3px 12px 3px 0', whiteSpace: 'nowrap', verticalAlign: 'top' } as const;
export default function KeyboardHelp({ onClose }: { onClose(): void }){
  const { t, lang, units } = usePrefs(), step = (cm: number) => formatLength(cm / 100, units, lang), mac = typeof navigator !== 'undefined' && /Mac|iPhone|iPad/.test(navigator.platform || navigator.userAgent);
  const rows = helpRows({ mac }), vars = { small: step(NUDGE_CM), big: step(NUDGE_BIG_CM), ex: t(units === 'imperial' ? 'help.exImperial' : 'help.exMetric'), rule: t(units === 'imperial' ? 'help.ruleImperial' : 'help.ruleMetric') };
  const body = (sec: 'edit' | 'draw', title: string) => <tbody><tr><th colSpan={2} scope="colgroup" style={{ ...H, textAlign: 'left', padding: '12px 0 4px' }}>{title}</th></tr>
    {rows.filter(r => r.section === sec).map(r => <tr key={r.label}><td style={K}><kbd className="mono">{r.keysKey ? t(r.keysKey) : r.keys}</kbd></td><td>{t(r.label, vars)}</td></tr>)}</tbody>;
  // trei secțiuni (registrul le dă în ordine): editarea și desenarea într-un singur tabel, ca tastele să stea pe aceeași coloană;
  // uneltele, scurte, pe două coloane, ca lista să încapă și pe un laptop de 768 px. Lățimea e fixată: .pending e centrat
  // cu left:50%, iar fără lățime dialogul s-ar strânge la jumătatea pânzei și descrierile s-ar rupe cuvânt cu cuvânt
  return (<div className="pending" role="dialog" aria-label={t('help.title')} style={{ width: 'min(560px, calc(100% - 24px))', maxWidth: 560, maxHeight: 'calc(100% - 24px)', overflowY: 'auto' }}>
    <strong>{t('help.title')}</strong>
    <table style={{ borderCollapse: 'collapse', fontSize: 13 }}>{body('edit', t('help.section.edit'))}{body('draw', t('help.section.draw'))}</table>
    <h4 style={H}>{t('cmdk.group.tools')}</h4>
    <ul style={{ display: 'grid', gridTemplateColumns: 'repeat(2, minmax(0, 1fr))', gap: '4px 20px', listStyle: 'none', margin: 0, padding: 0, fontSize: 13 }}>
      {rows.filter(r => r.section === 'tools').map(r => <li key={r.label} style={{ display: 'flex', gap: 10, alignItems: 'baseline' }}><kbd className="mono" style={{ minWidth: '1.6em', textAlign: 'center' }}>{r.keys}</kbd><span>{t(r.label)}</span></li>)}</ul>
    <div style={{ marginTop: 12 }}><button className="btn primary" onClick={onClose}>{t('common.close')}</button></div>
  </div>);
}
