'use client';
// Laborator de comparare opțiuni de design (Design Options Lab)
import { useMemo, useState } from 'react';
import type { Catalog, MaterialsCatalog, Snapshot } from '@/core/types';
import { compareDesignOptions, type DesignOptionEvaluation } from '@/core/design-lab';
import { usePrefs } from '@/lib/prefs';
import { formatMoney } from '@/core/format';
import Icon from './Icon';

export default function DesignOptionsLab({
  baseSnap,
  alternatives,
  catalog,
  mc,
  onApply,
  onClose,
}: {
  baseSnap: Snapshot;
  alternatives: { id: string; title: string; snapshot: Snapshot }[];
  catalog: Catalog;
  mc: MaterialsCatalog;
  onApply(s: Snapshot, title: string): void;
  onClose(): void;
}) {
  const { lang, t } = usePrefs();
  const comparison = useMemo(
    () => compareDesignOptions(baseSnap, alternatives, catalog, mc),
    [baseSnap, alternatives, catalog, mc]
  );
  const [selectedId, setSelectedId] = useState<string>(
    alternatives[0]?.id ?? 'base'
  );

  const lei = (v: number) => formatMoney(v, 'RON', lang);

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
          maxWidth: 1040,
          width: '100%',
          maxHeight: '90vh',
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
          <div>
            <h3 style={{ margin: 0, fontSize: 'var(--fs-md)', fontWeight: 600 }}>
              {t('lab.title')}
            </h3>
            <p className="prov" style={{ margin: 0 }}>
              {t('lab.subtitle')}
            </p>
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

        {/* Content grid */}
        <div
          style={{
            padding: 20,
            overflowY: 'auto',
            display: 'grid',
            gridTemplateColumns: `repeat(${Math.min(3, comparison.options.length)}, 1fr)`,
            gap: 16,
          }}
        >
          {comparison.options.map(opt => {
            const isBase = opt.id === 'base';
            const m = opt.metrics;
            const isSel = selectedId === opt.id;

            return (
              <div
                key={opt.id}
                style={{
                  border: isSel ? '2px solid var(--ink)' : '1px solid var(--line)',
                  borderRadius: 10,
                  padding: 16,
                  display: 'flex',
                  flexDirection: 'column',
                  gap: 12,
                  backgroundColor: isBase ? '#faf9f6' : '#ffffff',
                }}
              >
                <div>
                  <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                    <b style={{ fontSize: 'var(--fs-sm)' }}>{opt.title}</b>
                    {isBase && (
                      <span className="tag PASS" style={{ fontSize: 10, padding: '2px 6px' }}>
                        {t('lab.current')}
                      </span>
                    )}
                  </div>
                  {m.budgetDeltaKnown != null && m.budgetDeltaKnown !== 0 && (
                    <small className="mono" style={{ color: m.budgetDeltaKnown > 0 ? 'var(--warn)' : 'var(--ok)' }}>
                      {m.budgetDeltaKnown > 0 ? '+' : ''}{lei(m.budgetDeltaKnown)}
                    </small>
                  )}
                </div>

                {/* Spatial metrics */}
                <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 8, fontSize: 'var(--fs-xs)' }}>
                  <div style={{ padding: '6px 8px', backgroundColor: 'var(--bg)', borderRadius: 6 }}>
                    <span className="prov">{t('lab.storage')}</span>
                    <b style={{ display: 'block', fontSize: 'var(--fs-sm)' }}>{m.storageMeters} ml</b>
                  </div>
                  <div style={{ padding: '6px 8px', backgroundColor: 'var(--bg)', borderRadius: 6 }}>
                    <span className="prov">{t('lab.freeSpace')}</span>
                    <b style={{ display: 'block', fontSize: 'var(--fs-sm)' }}>{m.freeSpacePct}%</b>
                  </div>
                  <div style={{ padding: '6px 8px', backgroundColor: 'var(--bg)', borderRadius: 6 }}>
                    <span className="prov">{t('lab.seating')}</span>
                    <b style={{ display: 'block', fontSize: 'var(--fs-sm)' }}>{t('lab.seatingSeats', { n: m.seatingCapacity })}</b>
                  </div>
                  <div style={{ padding: '6px 8px', backgroundColor: 'var(--bg)', borderRadius: 6 }}>
                    <span className="prov">{t('lab.budget')}</span>
                    <b style={{ display: 'block', fontSize: 'var(--fs-sm)' }}>{lei(m.budgetKnown)}</b>
                  </div>
                </div>

                {/* Trade-offs: Pros & Cons */}
                <div style={{ display: 'grid', gap: 6, fontSize: 'var(--fs-xs)' }}>
                  <b style={{ color: 'var(--ink-2)' }}>{t('lab.pros')}</b>
                  {opt.pros.map((p, idx) => (
                    <div key={idx} style={{ color: 'var(--ok)', display: 'flex', gap: 6 }}>
                      <span>✓</span>
                      <span>{p[lang === 'ro' ? 'ro' : 'en']}</span>
                    </div>
                  ))}
                  {opt.cons.length > 0 && (
                    <>
                      <b style={{ color: 'var(--ink-2)', marginTop: 4 }}>
                        {t('lab.cons')}
                      </b>
                      {opt.cons.map((c, idx) => (
                        <div key={idx} style={{ color: 'var(--warn)', display: 'flex', gap: 6 }}>
                          <span>⚠</span>
                          <span>{c[lang === 'ro' ? 'ro' : 'en']}</span>
                        </div>
                      ))}
                    </>
                  )}
                </div>

                {/* Action button */}
                <div style={{ marginTop: 'auto', paddingTop: 8 }}>
                  {!isBase && (
                    <button
                      type="button"
                      className="btn primary block sm"
                      onClick={() => {
                        onApply(opt.snapshot, opt.title);
                        onClose();
                      }}
                    >
                      {t('lab.apply')}
                    </button>
                  )}
                </div>
              </div>
            );
          })}
        </div>
      </div>
    </div>
  );
}
