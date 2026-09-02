import { describe, expect, it, vi } from 'vitest';
import {
  JsonFileConfigurationProvider,
  loadJsonFileSync,
} from '@/common/index.js';

vi.mock('@/common/utils/index.js', () => ({
  loadJsonFileSync: vi.fn(),
}));

describe('JsonFileConfigurationProvider', () => {
  it('returns parsed JSON content', () => {
    vi.mocked(loadJsonFileSync).mockReturnValue({ key: 'value', port: 8080 });

    const provider = new JsonFileConfigurationProvider('/path/to/config.json');
    const config = provider.load();

    expect(config).toEqual({ key: 'value', port: 8080 });
    expect(loadJsonFileSync).toHaveBeenCalledWith(
      '/path/to/config.json',
      false,
    );
  });

  it('passes optional flag when set to true', () => {
    vi.mocked(loadJsonFileSync).mockReturnValue({});

    const provider = new JsonFileConfigurationProvider(
      '/path/to/optional.json',
      { optional: true },
    );
    const config = provider.load();

    expect(config).toEqual({});
    expect(loadJsonFileSync).toHaveBeenCalledWith(
      '/path/to/optional.json',
      true,
    );
  });

  it('passes optional flag as false by default', () => {
    vi.mocked(loadJsonFileSync).mockReturnValue({});

    const provider = new JsonFileConfigurationProvider('/path/to/config.json');
    provider.load();

    expect(loadJsonFileSync).toHaveBeenCalledWith(
      '/path/to/config.json',
      false,
    );
  });

  it('forwards errors from loadJsonFileSync', () => {
    vi.mocked(loadJsonFileSync).mockImplementation(() => {
      throw new Error('File not found: /path/to/missing.json');
    });

    const provider = new JsonFileConfigurationProvider('/path/to/missing.json');

    expect(() => provider.load()).toThrow(
      'File not found: /path/to/missing.json',
    );
  });

  it('forwards empty object from optional missing file', () => {
    vi.mocked(loadJsonFileSync).mockReturnValue({});

    const provider = new JsonFileConfigurationProvider(
      '/path/to/missing.json',
      { optional: true },
    );
    const config = provider.load();

    expect(config).toEqual({});
  });
});
