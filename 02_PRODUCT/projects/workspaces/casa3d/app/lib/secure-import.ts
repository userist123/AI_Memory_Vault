// Securitate la importul de fișiere (Master Prompt Secțiunea 17.1):
// Validare MIME reală, limită de dimensiune, protecție împotriva atacurilor de tip decompression bomb și path traversal.

export interface ImportValidationOptions {
  maxSizeBytes?: number;
  allowedMimes?: string[];
  maxDecompressedBytes?: number;
}

export const DEFAULT_IMPORT_LIMITS = {
  planMaxBytes: 15 * 1024 * 1024,         // 15 MB pentru planuri / relevee (PDF/PNG/JPEG/DXF)
  modelMaxBytes: 25 * 1024 * 1024,        // 25 MB pentru modele 3D (GLTF/GLB/OBJ)
  archiveMaxBytes: 30 * 1024 * 1024,      // 30 MB pentru arhive ZIP
  maxDecompressedRatio: 10,               // rată maximă de expansiune compresie (protecție ZIP bomb)
  maxFilesPerArchive: 500,
};

export class SecureImportError extends Error {
  constructor(message: string, public readonly code: 'SIZE_EXCEEDED' | 'INVALID_MIME' | 'PATH_TRAVERSAL' | 'DECOMPRESSION_BOMB' | 'MALFORMED') {
    super(message);
    this.name = 'SecureImportError';
  }
}

/** Sanitizează numele de fișier și verifică împotriva path traversal (../, absolut, caractere periculoase). */
export function sanitizeImportFilename(rawName: string): string {
  if (!rawName || typeof rawName !== 'string') {
    throw new SecureImportError('Nume de fișier lipsă sau invalid.', 'MALFORMED');
  }
  // Îndepărtează căile absolute sau secvențele relative periculoase
  const normalized = rawName.replace(/\\/g, '/');
  if (normalized.includes('../') || normalized.includes('/..') || normalized.startsWith('/')) {
    throw new SecureImportError(`Secvență de traversare cale nepermisă în numele de fișier: "${rawName}".`, 'PATH_TRAVERSAL');
  }
  const basename = normalized.split('/').pop() || '';
  const safe = basename
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .replace(/[^a-zA-Z0-9._-]/g, '_')
    .replace(/_+/g, '_')
    .replace(/^_+|_+$/g, '');
  const dotIdx = safe.lastIndexOf('.');
  if (dotIdx > 0) {
    const namePart = safe.slice(0, dotIdx).replace(/_+$/, '');
    const extPart = safe.slice(dotIdx + 1);
    return `${namePart}.${extPart}`.slice(0, 80);
  }
  return safe.slice(0, 80);
}

/** Verifică dimensiunea octeților primiți împotriva plafonului configurat. */
export function validateImportSize(byteLength: number, maxBytes: number = DEFAULT_IMPORT_LIMITS.planMaxBytes): void {
  if (byteLength > maxBytes) {
    throw new SecureImportError(
      `Fișierul depășește limita admisă de ${(maxBytes / (1024 * 1024)).toFixed(1)} MB (are ${(byteLength / (1024 * 1024)).toFixed(1)} MB).`,
      'SIZE_EXCEEDED'
    );
  }
}

/** Detectează tipul real al fișierului pe baza semnăturilor binare (magic bytes). */
export function detectMagicMime(buffer: Uint8Array | Buffer): string | null {
  if (buffer.length < 4) return null;
  // PNG: 89 50 4E 47
  if (buffer[0] === 0x89 && buffer[1] === 0x50 && buffer[2] === 0x4E && buffer[3] === 0x47) return 'image/png';
  // JPEG: FF D8 FF
  if (buffer[0] === 0xFF && buffer[1] === 0xD8 && buffer[2] === 0xFF) return 'image/jpeg';
  // PDF: 25 50 44 46 (%PDF)
  if (buffer[0] === 0x25 && buffer[1] === 0x50 && buffer[2] === 0x44 && buffer[3] === 0x46) return 'application/pdf';
  // ZIP / DOCX / GLTF-Zip: 50 4B 03 04 (PK..)
  if (buffer[0] === 0x50 && buffer[1] === 0x4B && buffer[2] === 0x03 && buffer[3] === 0x04) return 'application/zip';
  // GLB (Binary GLTF): 67 6C 54 46 (glTF)
  if (buffer[0] === 0x67 && buffer[1] === 0x6C && buffer[2] === 0x54 && buffer[3] === 0x46) return 'model/gltf-binary';
  return null;
}

/** Verifică dacă bufferul respectă tipurile MIME admise (magic bytes + header declared). */
export function validateImportMime(buffer: Uint8Array | Buffer, declaredMime: string, allowedMimes: string[]): string {
  const magic = detectMagicMime(buffer);
  const effective = magic || declaredMime.toLowerCase().trim();
  const matched = allowedMimes.some(allowed => {
    if (allowed.endsWith('/*')) {
      const prefix = allowed.slice(0, -1);
      return effective.startsWith(prefix);
    }
    return effective === allowed;
  });
  if (!matched) {
    throw new SecureImportError(`Tipul de fișier "${effective}" nu este permis. Tipuri acceptate: ${allowedMimes.join(', ')}.`, 'INVALID_MIME');
  }
  return effective;
}

/** Protecție împotriva arhivelor malițioase de tip ZIP bomb. */
export function assertSafeDecompressionRatio(compressedSize: number, uncompressedSize: number, maxRatio = DEFAULT_IMPORT_LIMITS.maxDecompressedRatio): void {
  if (compressedSize <= 0) return;
  const ratio = uncompressedSize / compressedSize;
  if (ratio > maxRatio && uncompressedSize > 10 * 1024 * 1024) {
    throw new SecureImportError(
      `Raport de compresie suspect (${ratio.toFixed(1)}:1 > ${maxRatio}:1). Fișierul a fost respins pentru siguranță.`,
      'DECOMPRESSION_BOMB'
    );
  }
}
