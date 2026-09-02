import { createServiceToken } from '@prosto/platform-sdk';
import type { DataSource } from 'typeorm';

/**
 * @alpha
 * Typed service token for the ready shared TypeORM DataSource.
 */
export const TYPEORM_DATA_SOURCE_SERVICE_TOKEN = createServiceToken<DataSource>(
  'typeorm-data-source',
);
