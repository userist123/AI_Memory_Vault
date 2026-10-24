'use client';
// Panou de trasabilitate cerințe brief -> model 3D -> produse -> deviz
import { useMemo } from 'react';
import type { Catalog, MaterialsCatalog, Snapshot } from '@/core/types';
import { buildTraceabilityGraph, type RequirementNode } from '@/core/traceability';
import { usePrefs } from '@/lib/prefs';

export default function TraceabilityPanel({
  snap,
  catalog,
  mc,
  onFocus,
}: {
  snap: Snapshot;
  catalog: Catalog;
  mc: MaterialsCatalog;
  onFocus(target: { kind: 'room' | 'placement'; id: string }): void;
}) {
  const { lang, t } = usePrefs();
  const graph = useMemo(() => buildTraceabilityGraph(snap, catalog, mc), [snap, catalog, mc]);

  const statusLabel: Record<string, string> = {
    satisfied: t('trace.satisfied'),
    partial: t('trace.partial'),
    unresolved: t('trace.unresolved'),
    conflict: t('trace.conflict'),
  };

  const statusClass: Record<string, string> = {
    satisfied: 'PASS',
    partial: 'WARNING',
    unresolved: 'ERROR',
    conflict: 'ERROR',
  };

  return (
    <div className="trace-panel" style={{ display: 'grid', gap: 12 }}>
      <div>
        <h4 style={{ margin: '0 0 4px', fontSize: 'var(--fs-sm)' }}>
          {t('trace.title')}
        </h4>
        <p className="prov" style={{ margin: 0 }}>
          {t('trace.summary', { satisfied: graph.summary.satisfied, total: graph.summary.total })}
        </p>
      </div>

      <div style={{ display: 'grid', gap: 8 }}>
        {graph.requirements.map(req => {
          const st = req.status;
          return (
            <div
              key={req.id}
              className={`vcard ${statusClass[st]}`}
              style={{ padding: 10, borderRadius: 8, display: 'grid', gap: 6 }}
            >
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                <b style={{ fontSize: 'var(--fs-xs)' }}>{req.label[lang === 'ro' ? 'ro' : 'en']}</b>
                <span className={`tag ${statusClass[st]}`} style={{ fontSize: 10, padding: '2px 6px' }}>
                  {statusLabel[st]}
                </span>
              </div>

              <p style={{ margin: 0, fontSize: 'var(--fs-xs)', color: 'var(--ink-2)', lineHeight: 1.4 }}>
                {req.explanation[lang === 'ro' ? 'ro' : 'en']}
              </p>

              {req.matchedPlacementIds.length > 0 && (
                <div style={{ display: 'flex', gap: 4, flexWrap: 'wrap', marginTop: 2 }}>
                  {req.matchedPlacementIds.map((pid, idx) => (
                    <button
                      key={pid}
                      type="button"
                      className="btn sm"
                      style={{ fontSize: 11, height: 24, padding: '0 6px' }}
                      onClick={() => onFocus({ kind: 'placement', id: pid })}
                    >
                      {t('trace.itemN', { idx: idx + 1 })}
                    </button>
                  ))}
                </div>
              )}
            </div>
          );
        })}
      </div>
    </div>
  );
}
