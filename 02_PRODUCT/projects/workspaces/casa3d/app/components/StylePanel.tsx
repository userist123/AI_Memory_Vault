'use client';
// Pachetele de stil pentru toată casa: ce conține fiecare, cât costă pe casa asta, aplicare dintr-un clic (cu confirmare și alegerea scopului).
import { useMemo, useState } from 'react';
import type { Catalog, MaterialsCatalog, Snapshot } from '@/core/types';
import { STYLE_PACKAGES, styleCosts, applyStyleScoped, appliedStyleId, previewStyle, type StyleScope } from '@/core/styles';
import { floors } from '@/core/levels';
import { formatMoney } from '@/core/format';
import { usePrefs } from '@/lib/prefs';
import { askConfirm } from '@/lib/dialog';

export default function StylePanel({ house, cat, mc, cur, onApply }: { house: Snapshot; cat: Catalog; mc: MaterialsCatalog; cur: string; onApply(s: Snapshot, id: string): void }){
  const { t, lang } = usePrefs();
  const allRooms = useMemo(() => floors(house).flatMap(f => f.rooms), [house]);
  const [scopeRoomId, setScopeRoomId] = useState<string>('ALL');
  const [preserveOverrides, setPreserveOverrides] = useState<boolean>(false);
  const scope: StyleScope = scopeRoomId === 'ALL' ? { kind: 'project' } : { kind: 'room', roomId: scopeRoomId };
  const costs = useMemo(() => styleCosts(house, cat, mc), [house, cat, mc]), applied = useMemo(() => appliedStyleId(house, mc), [house, mc]);
  return (<div className="finishes"><fieldset className="fin-group"><legend>{t('style.title')}</legend>
    <div className="prov">{t('style.intro')}</div>
    <div className="grid2" style={{ marginBottom: 8 }}>
      <label className="f"><span>{t('style.scope')}</span>
        <select value={scopeRoomId} onChange={e => setScopeRoomId(e.target.value)}>
          <option value="ALL">{t('style.scopeAll')}</option>
          {allRooms.map(r => <option key={r.id} value={r.id}>{r.name}</option>)}
        </select>
      </label>
      <label className="f" style={{ display: 'flex', alignItems: 'center', gap: 6, paddingTop: 18 }}>
        <input type="checkbox" checked={preserveOverrides} onChange={e => setPreserveOverrides(e.target.checked)} />
        <span>{t('style.keepOverrides')}</span>
      </label>
    </div>
    {STYLE_PACKAGES.map(pkg => { const c = costs.find(x => x.id === pkg.id)!;
      const on = applied === pkg.id, prev = previewStyle(house, pkg, scope);
      return (<div key={pkg.id} className={`style-card${on ? ' is-applied' : ''}`} aria-current={on ? 'true' : undefined}>
        <div className="style-swatches" aria-hidden>{[pkg.walls, pkg.kitchen.frontColor, pkg.kitchen.countertopColor, pkg.fixtureColor].map((h, i) => <span key={i} style={{ background: h }} />)}</div>
        <div><b>{t(`style.${pkg.id}`)}</b>{on && <span className="pill"> {t('style.isApplied')}</span>}<div className="prov">{t(`style.${pkg.id}.desc`)}</div>
          <div className="prov">{t('style.cost', { mat: formatMoney(c.materials, cur, lang), lab: formatMoney(c.labor, cur, lang), n: c.unknown })}</div></div>
        <button className="btn" onClick={async () => {
          const extra = prev.overriddenRoomNames.length ? `\n(${t('style.overriddenRooms')}: ${prev.overriddenRoomNames.join(', ')})` : '';
          if (await askConfirm({ title: t('dlg.styleTitle', { name: t(`style.${pkg.id}`) }), body: `${t('style.confirm')}${extra}`, confirmLabel: t('dlg.styleOk') })) {
            onApply(applyStyleScoped(house, cat, mc, pkg, { scope, preserveOverrides }), pkg.id);
          }
        }}>{on ? t('style.reapply') : t('style.apply')}</button>
      </div>); })}
  </fieldset></div>);
}

