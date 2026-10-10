'use client';
const T = {
  title: 'Comenzi rapide', close: 'Închide',
  rows: [
    ['Ctrl/Cmd + Z', 'Anulează'], ['Ctrl/Cmd + Y sau Ctrl/Cmd + Shift + Z', 'Refă'],
    ['Delete / Backspace', 'Șterge selecția'], ['R', 'Rotește piesa cu 90°'],
    ['Ctrl/Cmd + D', 'Duplică piesa selectată'], ['Săgeți', 'Mută piesa cu 5 cm'], ['Shift + săgeți', 'Mută piesa cu 50 cm'],
    ['Esc', 'Revine la „Selectez”, oprește măsurarea'], ['Rotița mouse-ului', 'Zoom în plan'], ['Alt + trage', 'Deplasează planul'],
    ['?', 'Afișează / ascunde această listă'],
  ] as const,
};
export default function KeyboardHelp({ onClose }: { onClose(): void }){
  return (<div className="pending" role="dialog" aria-label={T.title} style={{ maxWidth: 420 }}>
    <strong>{T.title}</strong>
    <table style={{ borderCollapse: 'collapse', fontSize: 13 }}><tbody>{T.rows.map(([k, d]) => <tr key={k}><td style={{ padding: '3px 10px 3px 0', whiteSpace: 'nowrap' }}><kbd className="mono">{k}</kbd></td><td>{d}</td></tr>)}</tbody></table>
    <div><button className="btn primary" onClick={onClose}>{T.close}</button></div>
  </div>);
}
