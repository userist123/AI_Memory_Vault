/**
 * Capture thumbnails (S8): sha256 -> blob: URL, fetched once over the authenticated REST route (api.fetchCapture
 * checks id, type, size, PNG magic and the content hash) and kept in a small LRU; evicted URLs are revoked.
 */
import { fetchCapture } from "./api";
import type { Session } from "./session";

export const CAPTURE_CACHE = 32;

export function createCaptureLoader(session: Session, fetcher: typeof fetchCapture = fetchCapture) {
  const cache = new Map<string, Promise<string>>();
  const load = (sha256: string): Promise<string> => {
    const hit = cache.get(sha256);
    if (hit) {
      cache.delete(sha256);
      cache.set(sha256, hit); // most recently used last
      return hit;
    }
    const p = fetcher(session, sha256).then((blob) => URL.createObjectURL(blob));
    p.catch(() => cache.delete(sha256)); // a failed load may be retried later
    cache.set(sha256, p);
    while (cache.size > CAPTURE_CACHE) {
      const [oldest, op] = cache.entries().next().value as [string, Promise<string>];
      cache.delete(oldest);
      void op.then((u) => URL.revokeObjectURL(u)).catch(() => undefined);
    }
    return p;
  };
  const dispose = () => {
    for (const p of cache.values()) void p.then((u) => URL.revokeObjectURL(u)).catch(() => undefined);
    cache.clear();
  };
  return { load, dispose };
}
