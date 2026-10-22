'use client';
import { usePrefs } from '@/lib/prefs';
import { formatLength } from '@/core/format';
import { NUDGE_CM, NUDGE_BIG_CM } from '@/core/edit-ops';
import { helpRows } from '@/lib/commands';
// Rândurile vin din registrul de comenzi (lib/commands.ts); tastele sunt aceleași în orice limbă, descrierile vin din dicționar.
export default function KeyboardHelp({ onClose }: { onClose(): void }){
  const { t, lang, units } = usePrefs(), step = (cm: number) => formatLength(cm / 100, units, lang), mac = typeof navigator !== 'undefined' && /Mac|iPhone|iPad/.test(navigator.platform || navigator.userAgent);
  return (<div className="pending" role="dialog" aria-label={t('help.title')} style={{ maxWidth: 420 }}>
    <strong>{t('help.title')}</strong>
    <table style={{ borderCollapse: 'collapse', fontSize: 13 }}><tbody>{helpRows({ mac }).map(r => <tr key={r.label}><td style={{ padding: '3px 10px 3px 0', whiteSpace: 'nowrap' }}><kbd className="mono">{r.keysKey ? t(r.keysKey) : r.keys}</kbd></td><td>{t(r.label, { small: step(NUDGE_CM), big: step(NUDGE_BIG_CM) })}</td></tr>)}</tbody></table>
    <div><button className="btn primary" onClick={onClose}>{t('common.close')}</button></div>
  </div>);
}
