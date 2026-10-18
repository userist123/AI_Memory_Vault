'use client';
// Modal pentru harta de impact live (Live Design Impact Map)
import type { DesignImpact } from '@/core/impact-map';
import { usePrefs } from '@/lib/prefs';
import { formatMoney } from '@/core/format';
import Icon from './Icon';

export default function ImpactModal({
  impact,
  onClose,
}: {
  impact: DesignImpact;
  onClose(): void;
}) {
  const { lang, t } = usePrefs();
  const lei = (v: number) => formatMoney(v, 'RON', lang);

  const riskClass: Record<string, string> = {
    LOW: 'PASS',
    MEDIUM: 'WARNING',
    HIGH: 'ERROR',
  };

  return (
    <div
      role="dialog"
      aria-modal="true"
      style={{
        position: 'fixed',
        inset: 0,
        zIndex: 100,
        backgroundColor: 'rgba(25, 24, 22, 0.72)',
        backdropFilter: 'blur(4px)',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        padding: 20,
      }}
    >
      <div
        style={{
          backgroundColor: '#FFFFFF',
          borderRadius: 12,
          maxWidth: 640,
          width: '100%',
          display: 'flex',
          flexDirection: 'column',
          boxShadow: '0 20px 50px rgba(0,0,0,0.3)',
          overflow: 'hidden',
        }}
      >
        {/* Header */}
        <div
          style={{
            padding: '16px 20px',
            borderBottom: '1px solid var(--line)',
            display: 'flex',
            justifyContent: 'space-between',
            alignItems: 'center',
            backgroundColor: 'var(--bg)',
          }}
        >
          <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
            <h3 style={{ margin: 0, fontSize: 'var(--fs-md)', fontWeight: 600 }}>
              {t('impact.title')}
            </h3>
            <span className={`tag ${riskClass[impact.riskLevel]}`} style={{ fontSize: 10, padding: '2px 6px' }}>
              {t('impact.risk', { risk: impact.riskLevel })}
            </span>
          </div>
          <button
            type="button"
            className="btn ghost sm"
            onClick={onClose}
            aria-label={t('common.close')}
          >
            <Icon name="close" size={18} />
          </button>
        </div>

        {/* Content */}
        <div style={{ padding: 20, display: 'grid', gap: 14, maxHeight: '75vh', overflowY: 'auto' }}>
          <p style={{ margin: 0, fontSize: 'var(--fs-sm)', color: 'var(--ink)' }}>
            {impact.summary[lang === 'ro' ? 'ro' : 'en']}
          </p>

          {/* Resized rooms */}
          {impact.geometry.roomsResized.length > 0 && (
            <div style={{ display: 'grid', gap: 6 }}>
              <b style={{ fontSize: 'var(--fs-xs)', color: 'var(--ink-2)' }}>
                {t('impact.roomsTitle')}
              </b>
              {impact.geometry.roomsResized.map(r => (
                <div
                  key={r.id}
                  style={{
                    padding: '6px 10px',
                    backgroundColor: 'var(--bg)',
                    borderRadius: 6,
                    fontSize: 'var(--fs-xs)',
                    display: 'flex',
                    justifyContent: 'space-between',
                  }}
                >
                  <span>{r.name}</span>
                  <span className="mono">
                    {r.beforeArea} m² → <b>{r.afterArea} m²</b> ({r.deltaArea >= 0 ? '+' : ''}{r.deltaArea} m²)
                  </span>
                </div>
              ))}
            </div>
          )}

          {/* Furniture impacts */}
          {(impact.furniture.added.length > 0 || impact.furniture.removed.length > 0 || impact.furniture.moved.length > 0) && (
            <div style={{ display: 'grid', gap: 6 }}>
              <b style={{ fontSize: 'var(--fs-xs)', color: 'var(--ink-2)' }}>
                {t('impact.itemsTitle')}
              </b>
              <div style={{ fontSize: 'var(--fs-xs)', color: 'var(--ink-2)' }}>
                {impact.furniture.added.length > 0 && <div>{t('impact.itemsAdded', { n: impact.furniture.added.length })}</div>}
                {impact.furniture.removed.length > 0 && <div>{t('impact.itemsRemoved', { n: impact.furniture.removed.length })}</div>}
                {impact.furniture.moved.length > 0 && <div>{t('impact.itemsMoved', { n: impact.furniture.moved.length })}</div>}
              </div>
            </div>
          )}

          {/* Stale drawings */}
          {impact.drawingsStale.length > 0 && (
            <div style={{ display: 'grid', gap: 6 }}>
              <b style={{ fontSize: 'var(--fs-xs)', color: 'var(--ink-2)' }}>
                {t('impact.sheetsTitle')}
              </b>
              <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
                {impact.drawingsStale.map(d => (
                  <span key={d} className="tag WARNING" style={{ fontSize: 11, padding: '3px 8px' }}>
                    {d === 'floor_plan'
                      ? t('impact.sheetFloorPlan')
                      : d === 'finishes'
                      ? t('impact.sheetFinishes')
                      : d === 'ceiling_lighting'
                      ? t('impact.sheetCeiling')
                      : t('impact.sheetElevations')}
                  </span>
                ))}
              </div>
            </div>
          )}

          {/* Budget impact */}
          {impact.budget.deltaKnown !== 0 && (
            <div style={{ padding: '8px 12px', backgroundColor: 'var(--bg)', borderRadius: 6, display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
              <span style={{ fontSize: 'var(--fs-xs)' }}>{t('impact.budgetDelta')}</span>
              <b className="mono" style={{ color: impact.budget.deltaKnown > 0 ? 'var(--warn)' : 'var(--ok)' }}>
                {impact.budget.deltaKnown > 0 ? '+' : ''}{lei(impact.budget.deltaKnown)}
              </b>
            </div>
          )}
        </div>

        <div style={{ padding: '12px 20px', borderTop: '1px solid var(--line)', textAlign: 'right' }}>
          <button type="button" className="btn primary sm" onClick={onClose}>
            {t('impact.ack')}
          </button>
        </div>
      </div>
    </div>
  );
}
