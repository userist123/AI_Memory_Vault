import type {
  IPersistenceDescriptor,
  PersistenceDescriptorPayloadType,
} from '@prosto/platform-sdk';
import type { EntityTarget, MigrationInterface } from 'typeorm';
import type { ITypeOrmPersistenceDescriptorPayload } from '@/interfaces/index.js';

/**
 * @alpha
 * Creates adapter-owned TypeORM metadata for a generic persistence descriptor.
 */
export function createTypeOrmPersistenceDescriptor(input: {
  readonly entities: readonly EntityTarget<unknown>[];
  readonly migrations: readonly (new () => MigrationInterface)[];
}): PersistenceDescriptorPayloadType {
  return {
    entities: input.entities,
    migrations: input.migrations,
  } satisfies ITypeOrmPersistenceDescriptorPayload;
}

/**
 * @alpha
 * Extracts TypeORM metadata from a persistence descriptor.
 */
export function getTypeOrmPersistenceDescriptorPayload(
  descriptor: IPersistenceDescriptor,
): ITypeOrmPersistenceDescriptorPayload | undefined {
  const { payload } = descriptor;

  if (typeof payload !== 'object' || payload === null) {
    return undefined;
  }

  const candidate = payload as Partial<ITypeOrmPersistenceDescriptorPayload>;

  if (
    !Array.isArray(candidate.entities) ||
    !Array.isArray(candidate.migrations)
  ) {
    return undefined;
  }

  return candidate as ITypeOrmPersistenceDescriptorPayload;
}
