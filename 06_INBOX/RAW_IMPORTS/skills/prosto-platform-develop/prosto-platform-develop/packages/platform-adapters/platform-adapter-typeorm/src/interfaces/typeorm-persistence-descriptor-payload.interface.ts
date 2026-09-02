import type { EntityTarget, MigrationInterface } from 'typeorm';

/**
 * @alpha
 * The payload of the TypeOrmPersistenceDescriptor.
 */
export interface ITypeOrmPersistenceDescriptorPayload {
  readonly entities: readonly EntityTarget<unknown>[];
  readonly migrations: readonly (new () => MigrationInterface)[];
}
