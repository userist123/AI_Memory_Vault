'use client';

import { useRef, useState } from 'react';
import { usePrefs } from '@/lib/prefs';
import { newProjectRequest } from '@/core/templates';
import { imageFileToUnderlayDataUrl, parseImportedSnapshot, ProjectImportError } from '@/core/project-import';

const baseName = (name: string) => name.replace(/\.[^.]+$/, '').replace(/[<>:"/\\|?*\u0000-\u001f]/g, ' ').trim().slice(0, 100);

export default function ImportExistingProject({ projectName }: { projectName: string }) {
  const { lang, t } = usePrefs();
  const input = useRef<HTMLInputElement>(null);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState('');
  const [error, setError] = useState('');

  async function importFile(file?: File) {
    if (!file) return;
    setBusy(true); setError(''); setMessage('');
    let createdId: string | null = null;
    try {
      const isImage = ['image/png', 'image/jpeg'].includes(file.type);
      const isJson = file.type === 'application/json' || /\.json$/i.test(file.name);
      if (!isImage && !isJson) {
        throw new ProjectImportError(t('import.err.types'));
      }
      const name = (projectName.trim() || baseName(file.name) || t('import.defaultName')).slice(0, 120);
      let importedSnapshot: any = null;
      let underlay: string | null = null;
      if (isImage) underlay = await imageFileToUnderlayDataUrl(file);
      else importedSnapshot = parseImportedSnapshot(await file.text(), name);

      const create = await fetch('/api/projects', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify(newProjectRequest(name, 'blank', lang === 'en' ? 'en' : 'ro')),
      });
      const created = await create.json().catch(() => ({}));
      if (!create.ok || typeof created.id !== 'string') throw new Error(created.error || t('import.err.create'));
      createdId = created.id;

      const initialResponse = await fetch(`/api/projects/${createdId}`);
      const initial = await initialResponse.json().catch(() => ({}));
      if (!initialResponse.ok || !initial.draft || !Number.isInteger(initial.draftRev)) {
        throw new Error(initial.error || t('import.err.load'));
      }
      const snapshot = importedSnapshot
        ? { ...importedSnapshot, name }
        : {
            ...initial.draft,
            name,
            underlay: { dataUrl: underlay, x: 0, z: 0, widthM: 10, opacity: 0.62, locked: true, confirmed: false },
          };
      const saved = await fetch(`/api/projects/${createdId}`, {
        method: 'PUT',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ snapshot, base: initial.draftRev }),
      });
      const savedBody = await saved.json().catch(() => ({}));
      if (!saved.ok) throw new Error(savedBody.error || t('import.err.rejected'));
      setMessage(isImage ? t('import.ok.underlay') : t('import.ok.native'));
      location.href = `/p/${createdId}`;
    } catch (e) {
      if (createdId) await fetch(`/api/projects/${createdId}`, { method: 'DELETE' }).catch(() => {});
      setError(e instanceof Error ? e.message : t('import.err.failed'));
    } finally {
      setBusy(false);
      if (input.current) input.current.value = '';
    }
  }

  return (
    <section className="import-existing" aria-labelledby="import-existing-title">
      <div className="import-existing-mark" aria-hidden="true"><span>↗</span><i>{t('import.mark')}</i></div>
      <div className="import-existing-copy">
        <p className="import-eyebrow">{t('import.eyebrow')}</p>
        <h2 id="import-existing-title">{t('import.title')}</h2>
        <p>{t('import.desc')}</p>
        {error && <p className="issue ERROR" role="alert">{error}</p>}
        {message && <p className="issue WARNING" role="status">{message}</p>}
        <div className="import-existing-actions">
          <button type="button" className="btn primary lg" disabled={busy} onClick={() => input.current?.click()}>{busy ? t('import.btnBusy') : t('import.btn')}</button>
          <span>{t('import.hint')}</span>
          <input ref={input} className="sr-only" type="file" accept=".json,application/json,image/png,image/jpeg" disabled={busy} onChange={e => importFile(e.target.files?.[0])} aria-label={t('import.aria')} />
        </div>
      </div>
      <div className="import-existing-note"><span>01</span><p>{t('import.step1')}</p><span>02</span><p>{t('import.step2')}</p><span>03</span><p>{t('import.step3')}</p></div>
    </section>
  );
}
