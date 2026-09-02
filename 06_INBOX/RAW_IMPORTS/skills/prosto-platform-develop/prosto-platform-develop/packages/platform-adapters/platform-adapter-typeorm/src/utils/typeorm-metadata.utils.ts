import type { IPersistenceDescriptor } from '@prosto/platform-sdk';
import type {
  ITypeOrmPersistenceDescriptorPayload,
  TypeOrmDialectType,
} from '@/interfaces/index.js';
import {
  type EntityTarget,
  getMetadataArgsStorage,
  type MigrationInterface,
} from 'typeorm';
import { TypeOrmDescriptorValidationError } from '@/errors/index.js';
import { getTypeOrmPersistenceDescriptorPayload } from './persistence-descriptor.utils.js';

interface IValidatedTypeOrmDescriptorMetadata {
  readonly ownerId: string;
  readonly entityNames: readonly string[];
  readonly tableNames: readonly string[];
  readonly migrationNames: readonly string[];
}

type EntityConstructorType = (new (...args: never[]) => object) & {
  readonly name: string;
};

interface ITypeOrmMetadataCollectionState {
  readonly entities: EntityTarget<unknown>[];
  readonly migrations: (new () => MigrationInterface)[];
  readonly ownership: IValidatedTypeOrmDescriptorMetadata[];
  readonly entityOwners: Map<EntityTarget<unknown>, string>;
  readonly entityNames: Set<string>;
  readonly tableNames: Set<string>;
  readonly migrationNames: Set<string>;
  readonly normalizedOwners: Map<string, string>;
}

interface IValidatedDescriptorPayload {
  readonly descriptor: IPersistenceDescriptor;
  readonly payload: ITypeOrmPersistenceDescriptorPayload;
  readonly prefix: string;
}

const PORTABLE_TABLE_NAME_PATTERN = /^[a-z][a-z0-9_]{0,62}$/;
const MIGRATION_TIMESTAMP_PATTERN = /\d{13}$/;

/**
 * @internal
 * Validates TypeORM-specific ownership declarations before connecting.
 */
export function collectValidatedTypeOrmMetadata(
  descriptors: readonly IPersistenceDescriptor[],
  dialect?: TypeOrmDialectType,
): {
  readonly entities: EntityTarget<unknown>[];
  readonly migrations: (new () => MigrationInterface)[];
  readonly ownership: readonly IValidatedTypeOrmDescriptorMetadata[];
} {
  const storage = getMetadataArgsStorage();
  const state = createCollectionState();
  const validatedDescriptors: IValidatedDescriptorPayload[] = [];

  for (const descriptor of descriptors) {
    const validatedDescriptor = validateDescriptorPayload(
      descriptor,
      dialect,
      state,
    );

    collectDescriptorMetadata(validatedDescriptor, storage, state);

    validatedDescriptors.push(validatedDescriptor);
  }

  for (const validatedDescriptor of validatedDescriptors) {
    validateRelationOwnership(validatedDescriptor, storage, state.entityOwners);
  }

  return state;
}

function createCollectionState(): ITypeOrmMetadataCollectionState {
  return {
    entities: [],
    migrations: [],
    ownership: [],
    entityOwners: new Map(),
    entityNames: new Set(),
    tableNames: new Set(),
    migrationNames: new Set(),
    normalizedOwners: new Map(),
  };
}

function validateDescriptorPayload(
  descriptor: IPersistenceDescriptor,
  dialect: TypeOrmDialectType | undefined,
  state: ITypeOrmMetadataCollectionState,
): IValidatedDescriptorPayload {
  validateDriverCapabilities(descriptor, dialect);

  const payload = getTypeOrmPersistenceDescriptorPayload(descriptor);

  if (!payload) {
    throw validationError(
      descriptor,
      'The descriptor payload is not a TypeORM entity and migration constructor payload.',
      'Create the descriptor with createTypeOrmPersistenceDescriptor().',
    );
  }

  return {
    descriptor,
    payload,
    prefix: getOwnerPrefix(descriptor, state.normalizedOwners),
  };
}

function collectDescriptorMetadata(
  validated: IValidatedDescriptorPayload,
  storage: ReturnType<typeof getMetadataArgsStorage>,
  state: ITypeOrmMetadataCollectionState,
): void {
  const entityNames: string[] = [];
  const tableNames: string[] = [];
  const migrationNames: string[] = [];

  collectDescriptorEntities(validated, storage, state, entityNames, tableNames);
  collectDescriptorMigrations(validated, state, migrationNames);

  state.ownership.push({
    ownerId: validated.descriptor.ownerId,
    entityNames,
    tableNames,
    migrationNames,
  });
}

function collectDescriptorEntities(
  validated: IValidatedDescriptorPayload,
  storage: ReturnType<typeof getMetadataArgsStorage>,
  state: ITypeOrmMetadataCollectionState,
  entityNames: string[],
  tableNames: string[],
): void {
  for (const entity of validated.payload.entities) {
    const { entityConstructor, entityName, tableName } =
      validateEntityDeclaration(validated, entity, storage);

    assertUnique(
      validated.descriptor,
      state.entityNames,
      entityName,
      'entity name',
    );

    assertUnique(
      validated.descriptor,
      state.tableNames,
      tableName,
      'table name',
    );

    state.entityOwners.set(entityConstructor, validated.descriptor.ownerId);
    state.entities.push(entity);
    entityNames.push(entityName);
    tableNames.push(tableName);

    validateAuxiliaryTables(
      validated.descriptor,
      validated.prefix,
      entityConstructor,
      storage,
    );
  }
}

function validateEntityDeclaration(
  validated: IValidatedDescriptorPayload,
  entity: EntityTarget<unknown>,
  storage: ReturnType<typeof getMetadataArgsStorage>,
): {
  readonly entityConstructor: EntityConstructorType;
  readonly entityName: string;
  readonly tableName: string;
} {
  if (typeof entity !== 'function') {
    throw validationError(
      validated.descriptor,
      'Entity schemas and non-constructor entity targets are not supported.',
      'Register an @Entity class with an explicit literal table name.',
    );
  }

  const entityConstructor = entity as EntityConstructorType;
  const table = storage.tables.find(
    (candidate) => candidate.target === entity && candidate.type === 'regular',
  );
  const tableName = typeof table?.name === 'string' ? table.name : undefined;

  if (!tableName) {
    throw validationError(
      validated.descriptor,
      `Entity "${entityConstructor.name}" does not declare an explicit table name.`,
      'Use @Entity("<owner>_<table>") rather than TypeORM generated names.',
    );
  }

  validateTableName(validated.descriptor, validated.prefix, tableName);

  return { entityConstructor, entityName: entityConstructor.name, tableName };
}

function collectDescriptorMigrations(
  validated: IValidatedDescriptorPayload,
  state: ITypeOrmMetadataCollectionState,
  migrationNames: string[],
): void {
  for (const migration of validated.payload.migrations) {
    if (typeof migration !== 'function') {
      throw validationError(
        validated.descriptor,
        'Migration declarations must be constructors.',
        'Register a TypeORM migration class constructor.',
      );
    }

    const migrationName = migration.name;

    if (
      !migrationName.startsWith(validated.prefix) ||
      !MIGRATION_TIMESTAMP_PATTERN.test(migrationName)
    ) {
      throw validationError(
        validated.descriptor,
        `Migration "${migrationName}" must start with "${validated.prefix}" and end with a 13-digit timestamp.`,
        'Rename the migration to the owner-prefixed TypeORM timestamp convention.',
      );
    }

    assertUnique(
      validated.descriptor,
      state.migrationNames,
      migrationName,
      'migration name',
    );

    state.migrations.push(migration);
    migrationNames.push(migrationName);
  }
}

function validateRelationOwnership(
  validated: IValidatedDescriptorPayload,
  storage: ReturnType<typeof getMetadataArgsStorage>,
  entityOwners: ReadonlyMap<EntityTarget<unknown>, string>,
): void {
  for (const entity of validated.payload.entities) {
    if (typeof entity !== 'function') continue;

    const entityConstructor = entity as EntityConstructorType;

    for (const relation of storage.relations.filter(
      (item) => item.target === entityConstructor,
    )) {
      const target = resolveRelationTarget(relation.type);

      if (target && entityOwners.get(target) !== validated.descriptor.ownerId) {
        throw validationError(
          validated.descriptor,
          `Entity "${entityConstructor.name}" declares a relation to an entity owned by another descriptor.`,
          'Relations may only target entities declared by the same persistence owner.',
        );
      }
    }
  }
}

function validationError(
  descriptor: IPersistenceDescriptor,
  message: string,
  remediationHint: string,
): TypeOrmDescriptorValidationError {
  return new TypeOrmDescriptorValidationError(
    message,
    descriptor,
    remediationHint,
  );
}

function validateDriverCapabilities(
  descriptor: IPersistenceDescriptor,
  dialect?: TypeOrmDialectType,
): void {
  if (!descriptor.requiredDriverCapabilities?.length) return;

  if (!dialect || !descriptor.requiredDriverCapabilities.includes(dialect)) {
    throw validationError(
      descriptor,
      'The descriptor does not support the configured database dialect.',
      'Configure a supported dialect or register a descriptor with the required driver capability.',
    );
  }
}

function getOwnerPrefix(
  descriptor: IPersistenceDescriptor,
  normalizedOwners: Map<string, string>,
): string {
  if (descriptor.owner === 'platform') {
    if (descriptor.ownerId !== 'platform') {
      throw validationError(
        descriptor,
        'Platform ownership must use ownerId "platform".',
        'Use the platform descriptor only from application composition.',
      );
    }

    return 'platform_';
  }

  const normalized = descriptor.ownerId
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '_')
    .replace(/^_+|_+$/g, '');

  if (!normalized.length) {
    throw validationError(
      descriptor,
      'Module owner ID cannot be normalized to a table prefix.',
      'Use a module ID containing at least one letter or digit.',
    );
  }

  const existingOwner = normalizedOwners.get(normalized);

  if (!!existingOwner && existingOwner !== descriptor.ownerId) {
    throw validationError(
      descriptor,
      `Module owner ID collides with "${existingOwner}" after normalization.`,
      'Choose a module ID with a unique normalized storage prefix.',
    );
  }

  normalizedOwners.set(normalized, descriptor.ownerId);

  return `${normalized}_`;
}

function validateTableName(
  descriptor: IPersistenceDescriptor,
  prefix: string,
  tableName: string,
): void {
  if (
    !PORTABLE_TABLE_NAME_PATTERN.test(tableName) ||
    !tableName.startsWith(prefix)
  ) {
    throw validationError(
      descriptor,
      `Table "${tableName}" must be a portable identifier prefixed with "${prefix}".`,
      'Declare a lowercase explicit table name using the persistence owner prefix.',
    );
  }
}

function validateAuxiliaryTables(
  descriptor: IPersistenceDescriptor,
  prefix: string,
  entity: EntityConstructorType,
  storage: ReturnType<typeof getMetadataArgsStorage>,
): void {
  for (const joinTable of storage.joinTables.filter(
    (item) => item.target === entity,
  )) {
    if (typeof joinTable.name !== 'string') {
      throw validationError(
        descriptor,
        `Entity "${entity.name}" uses a generated join table name.`,
        'Declare an explicit @JoinTable({ name: "<owner>_<table>" }).',
      );
    }

    validateTableName(descriptor, prefix, joinTable.name);
  }

  if (storage.trees.some((item) => item.target === entity)) {
    throw validationError(
      descriptor,
      `Entity "${entity.name}" uses a TypeORM tree auxiliary table.`,
      'Tree entities are unsupported until all auxiliary table names can be declared explicitly.',
    );
  }
}

function resolveRelationTarget(
  value: unknown,
): EntityTarget<unknown> | undefined {
  if (typeof value !== 'function') return undefined;

  try {
    const target = value();

    return typeof target === 'function'
      ? (target as EntityConstructorType)
      : undefined;
  } catch {
    return undefined;
  }
}

function assertUnique(
  descriptor: IPersistenceDescriptor,
  identities: Set<string>,
  identity: string,
  label: string,
): void {
  if (identities.has(identity)) {
    throw validationError(
      descriptor,
      `Duplicate ${label} "${identity}" was declared.`,
      'Use a globally unique persistence declaration name.',
    );
  }

  identities.add(identity);
}
