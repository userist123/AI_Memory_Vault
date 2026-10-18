'use client';

import { useRef, useState } from 'react';
import { usePrefs } from '@/lib/prefs';
import { newProjectRequest } from '@/core/templates';
import { imageFileToUnderlayDataUrl, parseImportedSnapshot, ProjectImportError } from '@/core/project-import';

const baseName = (name: string) => name.replace(/\.[^.]+$/, '').replace(/[<>:"/\\|?*\u0000-\u001f]/g, ' ').trim().slice(0, 100);

export default function ImportExistingProject({ projectName }: { projectName: string }) {
  const { lang } = usePrefs();
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
        throw new ProjectImportError('Acum sunt acceptate planuri PNG/JPEG și proiecte Casa3D în format JSON. Pentru PDF/DWG/DXF/IFC este necesar un adaptor dedicat; nu le vom trata fals ca geometrie editabilă.');
      }
      const name = (projectName.trim() || baseName(file.name) || 'Proiect importat').slice(0, 120);
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
      if (!create.ok || typeof created.id !== 'string') throw new Error(created.error || 'Nu am putut crea proiectul de import.');
      createdId = created.id;

      const initialResponse = await fetch(`/api/projects/${createdId}`);
      const initial = await initialResponse.json().catch(() => ({}));
      if (!initialResponse.ok || !initial.draft || !Number.isInteger(initial.draftRev)) {
        throw new Error(initial.error || 'Nu am putut încărca proiectul nou pentru import.');
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
      if (!saved.ok) throw new Error(savedBody.error || 'Importul a fost respins de validarea proiectului.');
      setMessage(isImage
        ? 'Planul a fost atașat ca releveu de referință. În editor, calibrează scara și trasează/confirmă geometria; camerele nu sunt inventate automat.'
        : 'Proiectul Casa3D a fost importat. Se deschide editorul.');
      location.href = `/p/${createdId}`;
    } catch (e) {
      if (createdId) await fetch(`/api/projects/${createdId}`, { method: 'DELETE' }).catch(() => {});
      setError(e instanceof Error ? e.message : 'Importul a eșuat.');
    } finally {
      setBusy(false);
      if (input.current) input.current.value = '';
    }
  }

  return (
    <section className="import-existing" aria-labelledby="import-existing-title">
      <div className="import-existing-mark" aria-hidden="true"><span>↗</span><i>PLAN / MODEL</i></div>
      <div className="import-existing-copy">
        <p className="import-eyebrow">CONTINUĂ UN PROIECT EXISTENT</p>
        <h2 id="import-existing-title">Nu porni de la zero. Adu casa ta în studio.</h2>
        <p>Încarcă <strong>proiect.json</strong> exportat din Casa3D sau un releveu PNG/JPEG. Importul păstrează proiectul nativ; imaginile devin plan de referință calibrabil, nu geometrie inventată.</p>
        {error && <p className="issue ERROR" role="alert">{error}</p>}
        {message && <p className="issue WARNING" role="status">{message}</p>}
        <div className="import-existing-actions">
          <button type="button" className="btn primary lg" disabled={busy} onClick={() => input.current?.click()}>{busy ? 'Se importă…' : 'Alege proiectul sau planul'}</button>
          <span>JSON Casa3D · PNG · JPEG · max. 15 MB pentru imagini</span>
          <input ref={input} className="sr-only" type="file" accept=".json,application/json,image/png,image/jpeg" disabled={busy} onChange={e => importFile(e.target.files?.[0])} aria-label="Alege proiectul Casa3D sau planul casei" />
        </div>
      </div>
      <div className="import-existing-note"><span>01</span><p>Import</p><span>02</span><p>Calibrare</p><span>03</span><p>Editare</p></div>
    </section>
  );
}
