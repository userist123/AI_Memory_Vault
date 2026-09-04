import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  ADMIN_BFF_MODULE_ID,
  AdminBffPhase,
  ConsoleAdminBffLogger,
} from '@/observability/index.js';

describe('ConsoleAdminBffLogger', () => {
  let debugSpy: ReturnType<typeof vi.spyOn>;
  let infoSpy: ReturnType<typeof vi.spyOn>;
  let warnSpy: ReturnType<typeof vi.spyOn>;
  let errorSpy: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    debugSpy = vi.spyOn(console, 'debug').mockImplementation(() => {
      /* empty */
    });
    infoSpy = vi.spyOn(console, 'info').mockImplementation(() => {
      /* empty */
    });
    warnSpy = vi.spyOn(console, 'warn').mockImplementation(() => {
      /* empty */
    });
    errorSpy = vi.spyOn(console, 'error').mockImplementation(() => {
      /* empty */
    });
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('should log info messages with structured context', () => {
    const logger = new ConsoleAdminBffLogger();

    logger.info('test message', {
      correlationId: 'corr-123',
      phase: AdminBffPhase.REQUEST,
    });

    expect(infoSpy).toHaveBeenCalledTimes(1);

    const [, entry] = infoSpy.mock.calls[0];

    expect(entry).toMatchObject({
      level: 'info',
      message: 'test message',
      moduleId: ADMIN_BFF_MODULE_ID,
      context: {
        correlationId: 'corr-123',
        phase: 'request',
      },
    });
    expect(entry.timestamp).toBeDefined();
  });

  it('should log debug messages with structured context', () => {
    const logger = new ConsoleAdminBffLogger();

    logger.debug('debug message', { key: 'value' });

    expect(debugSpy).toHaveBeenCalledTimes(1);

    const [, entry] = debugSpy.mock.calls[0];

    expect(entry).toMatchObject({
      level: 'debug',
      message: 'debug message',
      moduleId: ADMIN_BFF_MODULE_ID,
      context: { key: 'value' },
    });
  });

  it('should log warn messages with structured context', () => {
    const logger = new ConsoleAdminBffLogger();

    logger.warn('warn message', { detail: 'info' });

    expect(warnSpy).toHaveBeenCalledTimes(1);

    const [, entry] = warnSpy.mock.calls[0];

    expect(entry).toMatchObject({
      level: 'warn',
      message: 'warn message',
      moduleId: ADMIN_BFF_MODULE_ID,
    });
  });

  it('should log error messages with structured context', () => {
    const logger = new ConsoleAdminBffLogger();

    logger.error('error message', {
      errorCode: 'TEST_ERROR',
      duration: 100,
    });

    expect(errorSpy).toHaveBeenCalledTimes(1);

    const [, entry] = errorSpy.mock.calls[0];

    expect(entry).toMatchObject({
      level: 'error',
      message: 'error message',
      moduleId: ADMIN_BFF_MODULE_ID,
      context: {
        errorCode: 'TEST_ERROR',
        duration: 100,
      },
    });
  });

  it('should use custom moduleId from config', () => {
    const logger = new ConsoleAdminBffLogger({
      moduleId: 'custom-module',
    });

    logger.info('test');

    const [, entry] = infoSpy.mock.calls[0];

    expect(entry.moduleId).toBe('custom-module');
  });

  it('should use default level "debug" when not specified', () => {
    const logger = new ConsoleAdminBffLogger();

    logger.debug('debug msg');
    logger.info('info msg');

    expect(debugSpy).toHaveBeenCalledTimes(1);
    expect(infoSpy).toHaveBeenCalledTimes(1);
  });

  it('should respect configured log level', () => {
    const logger = new ConsoleAdminBffLogger({ level: 'warn' });

    logger.debug('should not appear');
    logger.info('should not appear');
    logger.warn('should appear');
    logger.error('should appear');

    expect(debugSpy).not.toHaveBeenCalled();
    expect(infoSpy).not.toHaveBeenCalled();
    expect(warnSpy).toHaveBeenCalledTimes(1);
    expect(errorSpy).toHaveBeenCalledTimes(1);
  });

  it('should redact sensitive keys in context', () => {
    const logger = new ConsoleAdminBffLogger();

    logger.info('test', {
      password: 'my-secret-password',
      token: 'bearer-token-123',
      apiKey: 'api-key-456',
      secret: 'my-secret',
      authorization: 'Bearer xyz',
      safeKey: 'visible',
    });

    const [, entry] = infoSpy.mock.calls[0];

    expect(entry.context.password).toBe('[REDACTED]');
    expect(entry.context.token).toBe('[REDACTED]');
    expect(entry.context.apiKey).toBe('[REDACTED]');
    expect(entry.context.secret).toBe('[REDACTED]');
    expect(entry.context.authorization).toBe('[REDACTED]');
    expect(entry.context.safeKey).toBe('visible');
  });

  it('should recursively redact identity PII keys in objects and arrays', () => {
    const logger = new ConsoleAdminBffLogger();

    logger.info('test', {
      identity: {
        subjectId: 'operator-1',
        roles: ['admin'],
        permissions: ['catalog.read'],
      },
      entries: [{ subject: 'operator-2', safeKey: 'visible' }],
    });

    const [, entry] = infoSpy.mock.calls[0];
    const context = entry.context as Record<string, unknown>;
    const identity = context.identity as Record<string, unknown>;
    const entries = context.entries as Record<string, unknown>[];

    expect(identity.subjectId).toBe('[REDACTED]');
    expect(identity.roles).toBe('[REDACTED]');
    expect(identity.permissions).toBe('[REDACTED]');
    expect(entries[0]?.subject).toBe('[REDACTED]');
    expect(entries[0]?.safeKey).toBe('visible');
  });

  it('should handle undefined context gracefully', () => {
    const logger = new ConsoleAdminBffLogger();

    logger.info('no context');

    const [, entry] = infoSpy.mock.calls[0];

    expect(entry.context).toBeUndefined();
  });

  it('should log without context key when context is empty', () => {
    const logger = new ConsoleAdminBffLogger();

    logger.info('empty context', {});

    const [, entry] = infoSpy.mock.calls[0];

    expect(entry.context).toBeUndefined();
  });
});
