/** Adapter-internal database migration lock. */
export interface IMigrationLock {
  acquire(timeoutMs: number): Promise<void>;
  release(): Promise<void>;
}
