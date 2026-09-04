import type { IPersistenceDescriptor } from '@prosto/platform-sdk';

/**
 * @internal
 * Signals an invalid TypeORM descriptor before DataSource creation.
 */
export class TypeOrmDescriptorValidationError extends Error {
  constructor(
    message: string,
    readonly descriptor: IPersistenceDescriptor,
    readonly remediationHint: string,
  ) {
    super(message);
    this.name = 'TypeOrmDescriptorValidationError';
  }
}
