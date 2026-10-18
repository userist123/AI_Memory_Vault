import type { Snapshot } from './types';

export class ProjectImportError extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'ProjectImportError';
  }
}

/**
 * Read a native Casa3D project JSON (the "proiect.json" member of an export)
 * or a standalone Snapshot. The server remains authoritative and validates the
 * resulting Snapshot again before persistence.
 */
export function parseImportedSnapshot(raw: string, projectName: string): Snapshot {
  if (raw.length > 3_500_000) throw new ProjectImportError('Fișierul JSON depășește limita de 3,5 MB.');
  let value: unknown;
  try { value = JSON.parse(raw); }
  catch { throw new ProjectImportError('Fișier JSON invalid sau incomplet.'); }

  if (!value || typeof value !== 'object' || Array.isArray(value)) {
    throw new ProjectImportError('Fișierul nu conține un proiect Casa3D valid.');
  }
  const envelope = value as Record<string, unknown>;
  const candidate = envelope.format === 'casa3d-project-export' && envelope.version === 1
    ? envelope.draft
    : envelope;
  if (!candidate || typeof candidate !== 'object' || Array.isArray(candidate)) {
    throw new ProjectImportError('Exportul nu conține câmpul draft așteptat.');
  }
  const snapshot = candidate as Record<string, unknown>;
  const floor = snapshot.floor as Record<string, unknown> | undefined;
  if (!floor || !Array.isArray(floor.rooms) || !Array.isArray(floor.walls) || !Array.isArray(snapshot.placements)) {
    throw new ProjectImportError('Nu am găsit geometria Casa3D (floor.rooms, floor.walls și placements). Încarcă fișierul proiect.json dintr-un export Casa3D.');
  }
  const name = projectName.trim().slice(0, 120);
  if (!name) throw new ProjectImportError('Introdu un nume pentru proiect.');
  const result = { ...snapshot, name } as unknown as Snapshot;
  const geometryAndData = { ...result };
  delete geometryAndData.underlay;
  if (JSON.stringify(geometryAndData).length > 950_000) {
    throw new ProjectImportError('Proiectul JSON este prea mare pentru importul direct (limita datelor proiectului este aproximativ 950 KB, separat de imaginea de calc).');
  }
  return result;
}

/** Convert a raster plan into a compact, browser-local underlay image. */
export async function imageFileToUnderlayDataUrl(file: File): Promise<string> {
  if (!['image/png', 'image/jpeg'].includes(file.type)) {
    throw new ProjectImportError('Planul imagine trebuie să fie PNG sau JPEG.');
  }
  if (file.size > 15 * 1024 * 1024) {
    throw new ProjectImportError('Imaginea originală depășește limita de 15 MB.');
  }
  const objectUrl = URL.createObjectURL(file);
  try {
    const image = await new Promise<HTMLImageElement>((resolve, reject) => {
      const img = new Image();
      img.onload = () => resolve(img);
      img.onerror = () => reject(new ProjectImportError('Imaginea nu poate fi citită.'));
      img.src = objectUrl;
    });
    let maxSide = 2000, quality = 0.85;
    for (let attempt = 0; attempt < 6; attempt++) {
      const scale = Math.min(1, maxSide / Math.max(image.naturalWidth, image.naturalHeight));
      const canvas = document.createElement('canvas');
      canvas.width = Math.max(1, Math.round(image.naturalWidth * scale));
      canvas.height = Math.max(1, Math.round(image.naturalHeight * scale));
      const ctx = canvas.getContext('2d');
      if (!ctx) throw new ProjectImportError('Browserul nu a putut pregăti imaginea.');
      ctx.fillStyle = '#fff';
      ctx.fillRect(0, 0, canvas.width, canvas.height);
      ctx.drawImage(image, 0, 0, canvas.width, canvas.height);
      const dataUrl = canvas.toDataURL('image/jpeg', quality);
      const approxBytes = Math.ceil((dataUrl.length - dataUrl.indexOf(',') - 1) * 3 / 4);
      if (approxBytes <= 1_400_000) return dataUrl;
      maxSide = Math.round(maxSide * 0.78);
      quality = Math.max(0.55, quality - 0.06);
    }
    throw new ProjectImportError('Imaginea nu poate fi comprimată suficient. Încearcă un plan cu rezoluție mai mică.');
  } finally {
    URL.revokeObjectURL(objectUrl);
  }
}
