'use client';
// „Cum se formează totalul”: același lanț, cu aceleași nume de rânduri, în buget și în panoul comercial
// (listă → adaos → preț de client → manoperă, transport → onorariu net + TVA → de facturat = proforma → rezervă → total).
import { chainRows, type ProjectChain } from '@/core/commercial';
import { usePrefs } from '@/lib/prefs';

/** Evenimentele dintre panouri (același browser): datele comerciale salvate, onorariul schimbat. detail: { id } */
export const TOTALS_EVENT = 'casa3d:totals', FEE_EVENT = 'casa3d:fee';

export function ChainTable({ chain, money, proformaGross }: { chain: ProjectChain; money(v: number): string; proformaGross?: number }){
  const { t } = usePrefs();
  return (<div className="chain">
    <h4>{t('chain.title')}</h4>
    <table className="com-table"><tbody>{chainRows(chain, proformaGross).map(r => <tr key={r.key} className={r.strong ? 'com-total' : ''}>
      <td>{r.strong ? <b>{t(r.key, r.vars)}</b> : t(r.key, r.vars)}</td><td className="n">{r.strong ? <b>{money(r.amount)}</b> : money(r.amount)}</td></tr>)}</tbody></table>
    {chain.unknownLines > 0 && <p className="muted small">{t('chain.unknown', { n: chain.unknownLines })}</p>}
    {chain.feeUnknown && <p className="muted small">{t('chain.feeUnknown')}</p>}
  </div>);
}
