export type TypeOrmDialectType =
  | 'postgres'
  | 'mysql'
  | 'mariadb'
  | 'sqlite'
  | 'mssql';

export type MigrationTransactionModeType = 'all' | 'each' | 'none';

/**
 * @alpha
 * TypeORM persistence configuration contract.
 */
export interface ITypeOrmPersistenceConfig extends Record<string, unknown> {
  readonly enabled?: boolean;
  readonly type?: TypeOrmDialectType;
  readonly host?: string;
  readonly port?: number;
  readonly database?: string;
  readonly username?: string;
  readonly password?: string;
  readonly url?: string;
  readonly schema?: string;
  readonly poolSize?: number;
  readonly connectTimeoutMs?: number;
  readonly migrationsRun?: boolean;
  readonly migrationLockTimeoutMs?: number;
  readonly migrationTransactionMode?: MigrationTransactionModeType;
  /**
   * Driver-specific options forwarded to the underlying TypeORM driver
   * (e.g., `encrypt`, `trustServerCertificate` for MSSQL/`tedious`).
   */
  readonly options?: Readonly<Record<string, unknown>>;
}
