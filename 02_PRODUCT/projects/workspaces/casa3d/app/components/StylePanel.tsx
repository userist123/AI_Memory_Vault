'use client';
// Pachetele de stil pentru toată casa: ce conține fiecare, cât costă pe casa asta, aplicare dintr-un clic (cu confirmare).
import { useMemo } from 'react';
import type { Catalog, MaterialsCatalog, Snapshot } from '@/core/types';
import { STYLE_PACKAGES, styleCosts, applyStyle } from '@/core/styles';
import { formatMoney } from '@/core/format';
import { usePrefs } from '@/lib/prefs';

export default function StylePanel({ house, cat, mc, cur, onApply }: { house: Snapshot; cat: Catalog; mc: MaterialsCatalog; cur: string; onApply(s: Snapshot, id: string): void }){
  const { t, lang } = usePrefs();
  const costs = useMemo(() => styleCosts(house, cat, mc), [house, cat, mc]);
  return (<div className="finishes"><fieldset className="fin-group"><legend>{t('style.title')}</legend>
    <div className="prov">{t('style.intro')}</div>
    {STYLE_PACKAGES.map(pkg => { const c = costs.find(x => x.id === pkg.id)!;
      return (<div key={pkg.id} className="style-card">
        <div className="style-swatches" aria-hidden>{[pkg.walls, pkg.kitchen.frontColor, pkg.kitchen.countertopColor, pkg.fixtureColor].map((h, i) => <span key={i} style={{ background: h }} />)}</div>
        <div><b>{t(`style.${pkg.id}`)}</b><div className="prov">{t(`style.${pkg.id}.desc`)}</div>
          <div className="prov">{t('style.cost', { mat: formatMoney(c.materials, cur, lang), lab: formatMoney(c.labor, cur, lang), n: c.unknown })}</div></div>
        <button className="btn" onClick={() => { if (confirm(t('style.confirm', { name: t(`style.${pkg.id}`) }))) onApply(applyStyle(house, cat, mc, pkg), pkg.id); }}>{t('style.apply')}</button>
      </div>); })}
  </fieldset></div>);
}
