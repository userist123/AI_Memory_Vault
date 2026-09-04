import type { DataSource } from 'typeorm';
import type { IMigrationLock } from './migration-lock.interface.js';
import type { ITypeOrmPersistenceConfig } from './typeorm-persistence-config.interface.js';

/**
 * @alpha
 * Factory interface for creating migration lock instances.
 */
export interface IMigrationLockFactoryInterface {
  /**
   * Creates a dialect-specific lock without exposing its implementation publicly.
   */
  create(
    dataSource: DataSource,
    configuration: ITypeOrmPersistenceConfig,
  ): IMigrationLock;
}
