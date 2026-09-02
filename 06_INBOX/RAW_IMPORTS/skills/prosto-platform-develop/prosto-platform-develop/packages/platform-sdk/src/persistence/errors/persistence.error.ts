import { PlatformSdkError } from '@/errors/index.js';

/** @alpha Stable error codes emitted by persistence contracts and providers. */
export type PersistenceErrorCodeType =
  | 'PersistenceRegistryNotCollecting'
  | 'PersistenceDescriptorOwnerMismatch'
  | 'PersistenceDuplicateDescriptor'
  | 'PersistenceProviderNotReady'
  | 'PersistenceDescriptorValidationFailed'
  | 'PersistenceMigrationLockTimeout'
  | 'PersistenceDriverUnavailable'
  | 'PersistenceInitializationFailed'
  | 'PersistenceMigrationFailed';

/** @alpha Structured, non-secret data attached to a persistence error. */
export interface IPersistenceErrorDetails extends Record<string, unknown> {
  readonly ownerId?: string;
  readonly moduleId?: string;
  readonly phase?: string;
  readonly remediationHint?: string;
  readonly dialect?: string;
  readonly identity?: string;
}

/** @alpha Base error for persistence contract and adapter failures. */
export class PersistenceError extends PlatformSdkError {
  constructor(
    code: PersistenceErrorCodeType,
    message: string,
    details?: IPersistenceErrorDetails,
  ) {
    super(code, message, details);
    this.name = 'PersistenceError';
  }
}
