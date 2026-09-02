import type { PlatformStartupPolicyType } from '@prosto/platform-sdk';
import type { IConfigAccessPolicy } from '@/modularity/index.js';

export type TypeOrmDialectType =
  | 'postgres'
  | 'mysql'
  | 'mariadb'
  | 'sqlite'
  | 'mssql';

export type TypeOrmMigrationTransactionModeType = 'all' | 'each' | 'none';

/**
 * @alpha
 * Platform persistence configuration interface.
 */
export interface IPersistencePlatformConfig {
  typeorm: ITypeOrmPersistencePlatformConfig;
}

/**
 * @alpha
 * Driver-neutral TypeORM persistence settings. This contract deliberately does
 * not expose TypeORM option types so the core remains ORM independent.
 */
export interface ITypeOrmPersistencePlatformConfig extends Record<
  string,
  unknown
> {
  readonly enabled: boolean;
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
  readonly migrationLockTimeoutMs?: number;
  readonly migrationTransactionMode?: TypeOrmMigrationTransactionModeType;
  readonly synchronize?: false;
  readonly migrationsRun?: boolean;
}

/**
 * @alpha
 * Platform configuration interface.
 */
export interface IPlatformConfig extends Record<string, unknown> {
  platform: {
    name: string;
    version: string;
    /** @default process.cwd() */
    basePath: string;
    /** @default 'strict' */
    startupPolicy: PlatformStartupPolicyType;
  };
  runtime: {
    /** @default 60 seconds for production, 30 seconds for development */
    shutdownTimeoutMs: number;
    correlationId?: string;
  };
  persistence: IPersistencePlatformConfig;
  modules: {
    [key: string]: unknown;
    configAccessPolicy: IConfigAccessPolicy;
    artifactCache: {
      /** @default true – for production, false – for development */
      enabled: boolean;
      /** @default .cache/module-artifacts */
      path?: string;
      /** @default 14 days */
      maxAgeMs?: number;
      /** @default 500MB */
      maxSizeBytes?: number;
    };
  };
  security: {
    secretRedaction: {
      /**
       * Whether redaction is active.
       * @default true
       */
      enabled: boolean;
      /**
       * Key names to redact in `key=value` patterns.
       * @default ['password', 'token', 'secret', 'key', 'apiKey', 'passphrase', 'url', 'connectionString']
       */
      patterns: string[];
    };
  };
  logging: {
    /** @default 'info' */
    level: string;
    /** @default 'text' */
    format: string;
  };
  custom: Record<string, unknown>;
}
