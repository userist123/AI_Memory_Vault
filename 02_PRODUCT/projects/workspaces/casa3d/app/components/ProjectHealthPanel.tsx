'use client';
// Panoul de sănătate a proiectului (Project Health & Action Queue)
import { useMemo } from 'react';
import type { Catalog, MaterialsCatalog, Snapshot } from '@/core/types';
import { evaluateProjectHealth, type HealthAction } from '@/core/project-health';
import { usePrefs } from '@/lib/prefs';

export default function ProjectHealthPanel({
  snap,
  catalog,
  mc,
  onAction,
}: {
  snap: Snapshot;
  catalog: Catalog;
  mc: MaterialsCatalog;
  onAction(action: HealthAction): void;
}) {
  const { lang, t } = usePrefs();
  const report = useMemo(() => evaluateProjectHealth(snap, catalog, mc), [snap, catalog, mc]);

  const scoreColor =
    report.score >= 85 ? 'var(--ok)' : report.score >= 65 ? 'var(--warn)' : 'var(--err)';

  return (
    <div className="health-panel" style={{ display: 'grid', gap: 12 }}>
      {/* Header with Score */}
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: 12,
          padding: 12,
          backgroundColor: 'var(--bg)',
          borderRadius: 8,
          border: '1px solid var(--line)',
        }}
      >
        <div
          style={{
            width: 48,
            height: 48,
            borderRadius: '50%',
            backgroundColor: '#ffffff',
            border: `3px solid ${scoreColor}`,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            fontSize: 'var(--fs-sm)',
            fontWeight: 700,
            color: scoreColor,
            flexShrink: 0,
          }}
        >
          {report.score}
        </div>
        <div>
          <b style={{ fontSize: 'var(--fs-sm)' }}>
            {t('health.scoreTitle')}
          </b>
          <p className="prov" style={{ margin: 0 }}>
            {report.score >= 85
              ? t('health.statusGood')
              : report.score >= 65
              ? t('health.statusWarn')
              : t('health.statusBad')}
          </p>
        </div>
      </div>

      {/* Facts summary */}
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: 6, fontSize: 'var(--fs-xs)' }}>
        <div style={{ padding: '6px 8px', backgroundColor: '#fff', border: '1px solid var(--line)', borderRadius: 6, textAlign: 'center' }}>
          <span className="prov">{t('health.errors')}</span>
          <b style={{ display: 'block', color: report.facts.errorsCount > 0 ? 'var(--err)' : 'var(--ok)' }}>
            {report.facts.errorsCount}
          </b>
        </div>
        <div style={{ padding: '6px 8px', backgroundColor: '#fff', border: '1px solid var(--line)', borderRadius: 6, textAlign: 'center' }}>
          <span className="prov">{t('health.warnings')}</span>
          <b style={{ display: 'block', color: report.facts.warningsCount > 0 ? 'var(--warn)' : 'var(--ok)' }}>
            {report.facts.warningsCount}
          </b>
        </div>
        <div style={{ padding: '6px 8px', backgroundColor: '#fff', border: '1px solid var(--line)', borderRadius: 6, textAlign: 'center' }}>
          <span className="prov">{t('health.noPrice')}</span>
          <b style={{ display: 'block' }}>{report.facts.unknownPricesCount}</b>
        </div>
      </div>

      {/* Action Queue */}
      <div>
        <h4 style={{ margin: '0 0 6px', fontSize: 'var(--fs-xs)', textTransform: 'uppercase', letterSpacing: '0.05em', color: 'var(--ink-3)' }}>
          {t('health.queueTitle')}
        </h4>
        {report.actionsQueue.length === 0 ? (
          <div className="issue PASS">{t('health.noActions')}</div>
        ) : (
          <div style={{ display: 'grid', gap: 6 }}>
            {report.actionsQueue.map(act => (
              <div
                key={act.id}
                style={{
                  padding: 8,
                  backgroundColor: '#ffffff',
                  border: '1px solid var(--line)',
                  borderRadius: 6,
                  display: 'flex',
                  justifyContent: 'space-between',
                  alignItems: 'center',
                  gap: 8,
                }}
              >
                <div style={{ fontSize: 'var(--fs-xs)' }}>
                  <span
                    className={`tag ${act.severity === 'BLOCKER' ? 'ERROR' : act.severity === 'WARNING' ? 'WARNING' : 'PASS'}`}
                    style={{ fontSize: 9, padding: '1px 5px', marginRight: 6 }}
                  >
                    {act.severity}
                  </span>
                  <span style={{ color: 'var(--ink-2)' }}>{act.title[lang === 'ro' ? 'ro' : 'en']}</span>
                </div>
                <button
                  type="button"
                  className="btn sm"
                  style={{ fontSize: 11, height: 26, padding: '0 8px', flexShrink: 0 }}
                  onClick={() => onAction(act)}
                >
                  {act.actionLabel[lang === 'ro' ? 'ro' : 'en']}
                </button>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}
