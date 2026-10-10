'use client';
import type { Catalog, Snapshot } from '@/core/types';
import { intlLocale } from '@/lib/i18n';
import { usePrefs } from '@/lib/prefs';
import PrefsSwitcher from './PrefsSwitcher';
import SharedPlanView from './SharedPlanView';

/** Pagina publică, doar pentru vizualizare, a unei revizii partajate. */
export default function SharedView({ view, catalog }: { view: { projectName: string; revisionNumber: number; note?: string | null; createdAt: string; snapshot: Snapshot }; catalog: Catalog }){
  const { t, lang } = usePrefs();
  return (<main className="home" style={{ maxWidth: 1100 }}>
    <div style={{ display: 'flex', justifyContent: 'space-between', gap: 12, alignItems: 'flex-start' }}><h1 style={{ marginBottom: 4 }}>{view.projectName}</h1><PrefsSwitcher /></div>
    <p className="muted">{t('editor.revisionN', { n: view.revisionNumber })}{view.note ? ` · ${view.note}` : ''} · {new Date(view.createdAt).toLocaleString(intlLocale(lang))} · {t('share.viewOnly')}</p>
    <SharedPlanView snap={view.snapshot} catalog={catalog} />
    <p className="prov">{t('share.frozenNote')}</p>
  </main>);
}
