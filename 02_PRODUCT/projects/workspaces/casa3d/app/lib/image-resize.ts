// Poza de pe telefon, micșorată înainte de trimitere (latura lungă ≤ 1600 px, JPEG): încape în limită și se încarcă repede pe date mobile.
export async function shrinkPhoto(file: File, max = 1600, quality = .82): Promise<Blob> {
  let bmp: ImageBitmap | null = null;
  try { bmp = await createImageBitmap(file, { imageOrientation: 'from-image' } as any); } catch { /* format necunoscut browserului: se trimite așa cum e, serverul îl verifică */ }
  if (!bmp) return file;
  const k = Math.min(1, max / Math.max(bmp.width, bmp.height)), w = Math.round(bmp.width * k), h = Math.round(bmp.height * k);
  const c = document.createElement('canvas'); c.width = w; c.height = h; c.getContext('2d')!.drawImage(bmp, 0, 0, w, h); bmp.close?.();
  return await new Promise<Blob>((res, rej) => c.toBlob(b => b ? res(b) : rej(new Error('jpeg')), 'image/jpeg', quality));
}
