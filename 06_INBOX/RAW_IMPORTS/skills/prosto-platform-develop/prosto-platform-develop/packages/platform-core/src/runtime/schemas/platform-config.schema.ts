import pkg from '../../../package.json' with { type: 'json' };
import type { ZodType } from 'zod';
import type { IPlatformConfig } from '../interfaces/index.js';
import { z } from 'zod';

const TYPEORM_DIALECTS = [
  'postgres',
  'mysql',
  'mariadb',
  'sqlite',
  'mssql',
] as const;

const typeOrmPersistenceSchema = z
  .object({
    enabled: z.boolean().default(false),
    type: z.enum(TYPEORM_DIALECTS).optional(),
    host: z.string().min(1).optional(),
    port: z.number().int().min(1).max(65535).optional(),
    database: z.string().min(1).optional(),
    username: z.string().min(1).optional(),
    password: z.string().min(1).optional(),
    url: z.string().url().optional(),
    schema: z.string().min(1).optional(),
    poolSize: z.number().int().positive().finite().optional(),
    connectTimeoutMs: z.number().int().positive().finite().optional(),
    migrationLockTimeoutMs: z
      .number()
      .int()
      .positive()
      .max(600000)
      .default(60000),
    migrationTransactionMode: z.enum(['all', 'each', 'none']).default('each'),
    synchronize: z.literal(false).default(false),
    migrationsRun: z.boolean().default(true),
  })
  .superRefine((config, context) => {
    if (!config.enabled) {
      return;
    }

    if (!config.type) {
      context.addIssue({
        code: 'custom',
        path: ['type'],
        message:
          'A supported TypeORM dialect is required when persistence is enabled.',
      });
      return;
    }

    const structuredFields = [
      config.host,
      config.port,
      config.database,
      config.username,
      config.password,
      config.schema,
      config.poolSize,
    ];

    if (config.url && structuredFields.some((value) => value !== undefined)) {
      context.addIssue({
        code: 'custom',
        path: ['url'],
        message:
          'TypeORM url cannot be combined with structured connection fields.',
      });
    }

    if (!config.url && !config.database) {
      context.addIssue({
        code: 'custom',
        path: ['database'],
        message: 'TypeORM requires either url or database connection settings.',
      });
    }

    if (config.type === 'sqlite') {
      for (const field of [
        'host',
        'port',
        'username',
        'password',
        'schema',
        'poolSize',
      ] as const) {
        if (config[field] !== undefined) {
          context.addIssue({
            code: 'custom',
            path: [field],
            message: `TypeORM sqlite does not support ${field}.`,
          });
        }
      }
    } else if (!config.url && (!config.host || !config.username)) {
      context.addIssue({
        code: 'custom',
        path: ['host'],
        message:
          'TypeORM server dialects require host and username when url is absent.',
      });
    }

    if (config.schema && config.type !== 'postgres') {
      context.addIssue({
        code: 'custom',
        path: ['schema'],
        message: 'TypeORM schema is supported only for the postgres dialect.',
      });
    }
  });

const typeOrmLocalOverrideSchema = z
  .object({
    persistence: z
      .object({
        // Validation of cross-field requirements happens after this narrow
        // secret override is merged with the package/deployment configuration.
        typeorm: z
          .object({
            enabled: z.boolean().optional(),
            type: z.enum(TYPEORM_DIALECTS).optional(),
            host: z.string().min(1).optional(),
            port: z.number().int().min(1).max(65535).optional(),
            database: z.string().min(1).optional(),
            username: z.string().min(1).optional(),
            password: z.string().min(1).optional(),
            url: z.string().url().optional(),
            schema: z.string().min(1).optional(),
            poolSize: z.number().int().positive().finite().optional(),
            connectTimeoutMs: z.number().int().positive().finite().optional(),
            migrationLockTimeoutMs: z
              .number()
              .int()
              .positive()
              .max(600000)
              .optional(),
            migrationTransactionMode: z
              .enum(['all', 'each', 'none'])
              .optional(),
            synchronize: z.literal(false).optional(),
            migrationsRun: z.boolean().optional(),
          })
          .strict()
          .optional(),
      })
      .strict()
      .optional(),
  })
  .strict();

/**
 * Validates the deployment-local secret override without allowing it to alter
 * unrelated runtime settings.
 */
export const platformLocalPersistenceConfigSchema = typeOrmLocalOverrideSchema;

export const platformConfigSchema: ZodType<IPlatformConfig> = z.object({
  platform: z
    .object({
      name: z.string().default('Prosto Platform'),
      version: z.string().default(pkg.version),
      basePath: z.string().default(process.cwd()),
      startupPolicy: z.literal(['strict', 'best-effort']).default('strict'),
    })
    .default({
      name: 'Prosto Platform',
      version: pkg.version,
      basePath: process.cwd(),
      startupPolicy: 'strict',
    }),
  runtime: z
    .object({
      shutdownTimeoutMs: z.number().positive().default(30000),
      correlationId: z.string().optional(),
    })
    .default({
      shutdownTimeoutMs: 30000,
    }),
  persistence: z
    .object({
      typeorm: typeOrmPersistenceSchema.default({
        enabled: false,
        migrationLockTimeoutMs: 60000,
        migrationTransactionMode: 'each',
        synchronize: false,
        migrationsRun: true,
      }),
    })
    .default({
      typeorm: {
        enabled: false,
        migrationLockTimeoutMs: 60000,
        migrationTransactionMode: 'each',
        synchronize: false,
        migrationsRun: true,
      },
    }),
  modules: z
    .object({
      configAccessPolicy: z
        .object({
          productionStrictMode: z.boolean().default(true),
        })
        .default({
          productionStrictMode: true,
        }),
      artifactCache: z
        .object({
          enabled: z.boolean().default(false),
          path: z.string().optional(),
          maxAgeMs: z.number().positive().optional(),
          maxSizeBytes: z.number().positive().optional(),
        })
        .default({
          enabled: false,
        }),
    })
    .default({
      configAccessPolicy: {
        productionStrictMode: true,
      },
      artifactCache: {
        enabled: false,
      },
    }),
  security: z
    .object({
      secretRedaction: z
        .object({
          enabled: z.boolean().default(true),
          patterns: z
            .array(z.string())
            .default([
              'key',
              'token',
              'secret',
              'password',
              'passphrase',
              'url',
              'connectionString',
            ]),
        })
        .default({
          enabled: true,
          patterns: [
            'key',
            'token',
            'secret',
            'password',
            'passphrase',
            'url',
            'connectionString',
          ],
        }),
    })
    .default({
      secretRedaction: {
        enabled: true,
        patterns: [
          'key',
          'token',
          'secret',
          'password',
          'passphrase',
          'url',
          'connectionString',
        ],
      },
    }),
  logging: z
    .object({
      level: z.string().default('info'),
      format: z.string().default('text'),
    })
    .default({
      level: 'info',
      format: 'text',
    }),
  custom: z.record(z.string(), z.unknown()).default({}),
});
