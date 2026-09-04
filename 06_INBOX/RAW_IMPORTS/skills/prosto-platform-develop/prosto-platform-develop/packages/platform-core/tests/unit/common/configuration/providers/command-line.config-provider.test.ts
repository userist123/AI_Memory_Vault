import { describe, expect, it } from 'vitest';
import { CommandLineConfigurationProvider } from '@/common/index.js';

describe('CommandLineConfigProvider', () => {
  it('parses --key=value format', () => {
    const provider = new CommandLineConfigurationProvider([
      '--logging:level=debug',
    ]);
    const config = provider.load();

    expect(config).toEqual({ logging: { level: 'debug' } });
  });

  it('parses --key value format', () => {
    const provider = new CommandLineConfigurationProvider([
      '--logging:level',
      'debug',
    ]);
    const config = provider.load();

    expect(config).toEqual({ logging: { level: 'debug' } });
  });

  it('parses --key as boolean true', () => {
    const provider = new CommandLineConfigurationProvider(['--verbose']);
    const config = provider.load();

    expect(config).toEqual({ verbose: true });
  });

  it('parses -key=value format', () => {
    const provider = new CommandLineConfigurationProvider(['-port=8080']);
    const config = provider.load();

    expect(config).toEqual({ port: 8080 });
  });

  it('parses nested keys with colon separator', () => {
    const provider = new CommandLineConfigurationProvider([
      '--modules:artifactCache:enabled=true',
      '--modules:artifactCache:path=./cache',
    ]);
    const config = provider.load();

    expect(config).toEqual({
      modules: {
        artifactCache: {
          enabled: true,
          path: './cache',
        },
      },
    });
  });

  it('coerces numeric values', () => {
    const provider = new CommandLineConfigurationProvider([
      '--runtime:shutdownTimeoutMs=60000',
    ]);
    const config = provider.load();

    expect(config).toEqual({ runtime: { shutdownTimeoutMs: 60000 } });
  });

  it('coerces boolean values', () => {
    const provider = new CommandLineConfigurationProvider([
      '--feature:enabled=true',
      '--feature:active=false',
    ]);
    const config = provider.load();

    expect(config).toEqual({
      feature: { enabled: true, active: false },
    });
  });

  it('handles multiple arguments of different types', () => {
    const provider = new CommandLineConfigurationProvider([
      '--environment=production',
      '--logging:level=error',
      '--platform:startupPolicy=best-effort',
    ]);
    const config = provider.load();

    expect(config).toEqual({
      environment: 'production',
      logging: { level: 'error' },
      platform: { startupPolicy: 'best-effort' },
    });
  });

  it('skips non-flag arguments', () => {
    const provider = new CommandLineConfigurationProvider([
      'some-command',
      '--key=value',
      'another-arg',
    ]);
    const config = provider.load();

    expect(config).toEqual({ key: 'value' });
  });
});
