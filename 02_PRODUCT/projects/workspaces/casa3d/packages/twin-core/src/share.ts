// Read-only sharing of one revision by an unguessable token. No owner cookie, no editing, revocable.
import { randomBytes, timingSafeEqual } from 'node:crypto';
import type { Project, Revision } from './approval.js';

export interface ShareRecord {
  token: string;          // base64url, 32 random bytes
  projectId: string;
  revisionNumber: number;
  createdAt: string;
  expiresAt?: string;
  revokedAt?: string;
}

export interface SharedView {
  projectId: string;
  revisionNumber: number;
  fingerprint: string;
  twin: Revision['twin'];
  readOnly: true;
}

export const TOKEN_BYTES = 32;

export function createShare(project: Project, revisionNumber: number, opts: { now?: string; expiresAt?: string; token?: string } = {}): ShareRecord {
  const rev = project.revisions.find(r => r.number === revisionNumber);
  if (!rev) throw new RangeError(`Revizia ${revisionNumber} nu exista.`);
  const token = opts.token ?? randomBytes(TOKEN_BYTES).toString('base64url');
  const rec: ShareRecord = { token, projectId: project.id, revisionNumber, createdAt: opts.now ?? new Date().toISOString() };
  if (opts.expiresAt) rec.expiresAt = opts.expiresAt;
  return rec;
}

export function revokeShare(rec: ShareRecord, now = new Date().toISOString()): ShareRecord {
  return rec.revokedAt ? rec : { ...rec, revokedAt: now };
}

function tokensEqual(a: string, b: string): boolean {
  const ba = Buffer.from(a), bb = Buffer.from(b);
  return ba.length === bb.length && timingSafeEqual(ba, bb);
}

/**
 * Resolves a token to a read-only snapshot of exactly the shared revision, never the live project.
 * Returns null for unknown, revoked or expired tokens; the caller answers 404 in every case (no oracle).
 */
export function resolveShare(records: readonly ShareRecord[], projects: readonly Project[], token: string, now = new Date().toISOString()): SharedView | null {
  if (typeof token !== 'string' || token.length < 16) return null;
  const rec = records.find(r => tokensEqual(r.token, token));
  if (!rec || rec.revokedAt || (rec.expiresAt && rec.expiresAt <= now)) return null;
  const project = projects.find(p => p.id === rec.projectId);
  const rev = project?.revisions.find(r => r.number === rec.revisionNumber);
  if (!project || !rev) return null;
  return { projectId: project.id, revisionNumber: rev.number, fingerprint: rev.fingerprint, twin: structuredClone(rev.twin), readOnly: true };
}
