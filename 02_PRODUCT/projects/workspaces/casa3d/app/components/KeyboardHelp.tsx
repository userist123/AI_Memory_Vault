'use client';
import { usePrefs } from '@/lib/prefs';
import { formatLength } from '@/core/format';
import { NUDGE_CM, NUDGE_BIG_CM } from '@/core/edit-ops';
// Tastele sunt aceleași în orice limbă; descrierile vin din dicționar (cheile help.*).
const ROWS = [['help.undoKeys', 'help.undo'], ['help.redoKeys', 'help.redo'], ['help.deleteKeys', 'help.delete'], ['R', 'help.rotate'], ['help.duplicateKeys', 'help.duplicate'],
  ['help.arrowKeys', 'help.nudge'], ['help.shiftArrowKeys', 'help.nudgeBig'], ['Esc', 'help.escape'], ['help.wheelKeys', 'help.zoom'], ['help.altDragKeys', 'help.pan'], ['?', 'help.toggle']] as const;
export default function KeyboardHelp({ onClose }: { onClose(): void }){
  const { t, lang, units } = usePrefs(), step = (cm: number) => formatLength(cm / 100, units, lang);
  return (<div className="pending" role="dialog" aria-label={t('help.title')} style={{ maxWidth: 420 }}>
    <strong>{t('help.title')}</strong>
    <table style={{ borderCollapse: 'collapse', fontSize: 13 }}><tbody>{ROWS.map(([k, d]) => <tr key={d}><td style={{ padding: '3px 10px 3px 0', whiteSpace: 'nowrap' }}><kbd className="mono">{k.startsWith('help.') ? t(k) : k}</kbd></td><td>{t(d, { small: step(NUDGE_CM), big: step(NUDGE_BIG_CM) })}</td></tr>)}</tbody></table>
    <div><button className="btn primary" onClick={onClose}>{t('common.close')}</button></div>
  </div>);
}
