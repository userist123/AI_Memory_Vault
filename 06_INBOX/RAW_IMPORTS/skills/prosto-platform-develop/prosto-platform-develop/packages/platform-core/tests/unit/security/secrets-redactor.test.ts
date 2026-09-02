import { describe, expect, it } from 'vitest';
import { SecretsRedactor } from '@/security/index.js';

describe('SecretsRedactor', () => {
  const redactor = new SecretsRedactor({
    enabled: true,
    patterns: ['password', 'token', 'secret', 'key', 'apiKey', 'passphrase'],
  });

  it('redacts password=value', () => {
    expect(redactor.redact('password=pwd')).toBe('password=[REDACTED]');
  });

  it('redacts token=value', () => {
    expect(redactor.redact('token=abc123')).toBe('token=[REDACTED]');
  });

  it('redacts secret=value', () => {
    expect(redactor.redact('secret=shh')).toBe('secret=[REDACTED]');
  });

  it('redacts key=value', () => {
    expect(redactor.redact('key=k')).toBe('key=[REDACTED]');
  });

  it('redacts apikey=value', () => {
    expect(redactor.redact('apikey=ak')).toBe('apikey=[REDACTED]');
  });

  it('redacts apiKey=value case-insensitively', () => {
    expect(redactor.redact('apikey=ak')).toBe('apikey=[REDACTED]');
    expect(redactor.redact('APIKEY=ak')).toBe('APIKEY=[REDACTED]');
    expect(redactor.redact('ApiKey=ak')).toBe('ApiKey=[REDACTED]');
  });

  it('redacts passphrase=value', () => {
    expect(redactor.redact('passphrase=myphrase')).toBe(
      'passphrase=[REDACTED]',
    );
  });

  it('handles multiple matches in single string', () => {
    expect(redactor.redact('token=t1 password=p2 key=k3')).toBe(
      'token=[REDACTED] password=[REDACTED] key=[REDACTED]',
    );
  });

  it('redacts bearer token', () => {
    expect(redactor.redact('bearer xyz')).toBe('bearer [REDACTED]');
  });

  it('redacts authorization basic', () => {
    expect(redactor.redact('authorization: basic dXNlcjpwYXNz')).toBe(
      'authorization: basic [REDACTED]',
    );
  });

  it('redacts generic URL connection values', () => {
    expect(
      redactor.redact('url=postgres://prosto:password@localhost:5432/prosto'),
    ).toBe('url=[REDACTED]');
    expect(
      redactor.redactObject({
        url: 'postgres://prosto:password@localhost:5432/prosto',
        connectionString: 'Server=localhost;Password=password',
      }),
    ).toEqual({ url: '[REDACTED]', connectionString: '[REDACTED]' });
  });

  it('preserves non-secret text', () => {
    expect(redactor.redact('hello world')).toBe('hello world');
  });

  it('returns original value when disabled', () => {
    const disabled = new SecretsRedactor({
      enabled: false,
      patterns: ['password'],
    });

    expect(disabled.redact('password=secret')).toBe('password=secret');
  });

  it('returns original value for empty string', () => {
    expect(redactor.redact('')).toBe('');
  });

  it('uses custom patterns', () => {
    const custom = new SecretsRedactor({
      enabled: true,
      patterns: ['customKey'],
    });

    expect(custom.redact('customKey=secretValue')).toBe('customKey=[REDACTED]');
    expect(custom.redact('password=value')).toBe('password=value');
  });
});
