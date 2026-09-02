import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ConsoleAdminShellLogger } from '@/shared/observability/console-admin-shell.logger.js';

describe('ConsoleAdminShellLogger', () => {
  let consoleDebugSpy: ReturnType<typeof vi.spyOn>;
  let consoleInfoSpy: ReturnType<typeof vi.spyOn>;
  let consoleWarnSpy: ReturnType<typeof vi.spyOn>;
  let consoleErrorSpy: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    consoleDebugSpy = vi.spyOn(console, 'debug').mockImplementation(() => {
      /* empty */
    });
    consoleInfoSpy = vi.spyOn(console, 'info').mockImplementation(() => {
      /* empty */
    });
    consoleWarnSpy = vi.spyOn(console, 'warn').mockImplementation(() => {
      /* empty */
    });
    consoleErrorSpy = vi.spyOn(console, 'error').mockImplementation(() => {
      /* empty */
    });
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('should log at debug level', () => {
    const logger = new ConsoleAdminShellLogger();
    logger.debug('test message', { key: 'value' });

    expect(consoleDebugSpy).toHaveBeenCalledOnce();
    const call = consoleDebugSpy.mock.calls[0];
    expect(call?.[0]).toBe('[platform-admin-shell]');

    const entry = call?.[1] as Record<string, unknown>;
    expect(entry.level).toBe('debug');
    expect(entry.message).toBe('test message');
    expect(entry.context).toEqual({ key: 'value' });
  });

  it('should log at info level', () => {
    const logger = new ConsoleAdminShellLogger();
    logger.info('info message');

    expect(consoleInfoSpy).toHaveBeenCalledOnce();
    const entry = consoleInfoSpy.mock.calls[0]?.[1] as Record<string, unknown>;
    expect(entry.level).toBe('info');
    expect(entry.message).toBe('info message');
  });

  it('should log at warn level', () => {
    const logger = new ConsoleAdminShellLogger();
    logger.warn('warn message');

    expect(consoleWarnSpy).toHaveBeenCalledOnce();
    const entry = consoleWarnSpy.mock.calls[0]?.[1] as Record<string, unknown>;
    expect(entry.level).toBe('warn');
  });

  it('should log at error level', () => {
    const logger = new ConsoleAdminShellLogger();
    logger.error('error message');

    expect(consoleErrorSpy).toHaveBeenCalledOnce();
    const entry = consoleErrorSpy.mock.calls[0]?.[1] as Record<string, unknown>;
    expect(entry.level).toBe('error');
  });

  it('should include timestamp in log entries', () => {
    const logger = new ConsoleAdminShellLogger();
    logger.info('test');

    const entry = consoleInfoSpy.mock.calls[0]?.[1] as Record<string, unknown>;
    expect(entry.timestamp).toBeDefined();
    expect(typeof entry.timestamp).toBe('string');
  });

  it('should use custom moduleId', () => {
    const logger = new ConsoleAdminShellLogger({ moduleId: 'custom-id' });
    logger.info('test');

    expect(consoleInfoSpy.mock.calls[0]?.[0]).toBe('[custom-id]');
  });

  it('should filter logs below configured level', () => {
    const logger = new ConsoleAdminShellLogger({ level: 'warn' });
    logger.debug('should not appear');
    logger.info('should not appear');
    logger.warn('should appear');
    logger.error('should appear');

    expect(consoleDebugSpy).not.toHaveBeenCalled();
    expect(consoleInfoSpy).not.toHaveBeenCalled();
    expect(consoleWarnSpy).toHaveBeenCalledOnce();
    expect(consoleErrorSpy).toHaveBeenCalledOnce();
  });

  it('should redact sensitive keys in context', () => {
    const logger = new ConsoleAdminShellLogger();
    logger.info('test', {
      password: 'secret123',
      token: 'abc',
      apiKey: 'key123',
      normalField: 'visible',
    });

    const entry = consoleInfoSpy.mock.calls[0]?.[1] as Record<string, unknown>;
    const context = entry.context as Record<string, unknown>;

    expect(context.password).toBe('[REDACTED]');
    expect(context.token).toBe('[REDACTED]');
    expect(context.apiKey).toBe('[REDACTED]');
    expect(context.normalField).toBe('visible');
  });

  it('should handle undefined context', () => {
    const logger = new ConsoleAdminShellLogger();
    logger.info('test');

    const entry = consoleInfoSpy.mock.calls[0]?.[1] as Record<string, unknown>;
    expect(entry.context).toBeUndefined();
  });

  it('should handle empty context', () => {
    const logger = new ConsoleAdminShellLogger();
    logger.info('test', {});

    const entry = consoleInfoSpy.mock.calls[0]?.[1] as Record<string, unknown>;
    expect(entry.context).toBeUndefined();
  });
});
