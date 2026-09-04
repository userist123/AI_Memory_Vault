import { describe, expect, it, vi } from 'vitest';
import {
  isAuthenticationFailure,
  logoutAndNavigate,
  resolveAdminBffBaseUrl,
  resolveInAppDestination,
} from '@/app/config/admin-shell-browser-config.js';

describe('admin shell browser configuration', () => {
  it('should use the browser origin by default', () => {
    expect(resolveAdminBffBaseUrl(undefined, 'https://admin.example')).toBe(
      'https://admin.example',
    );
  });

  it('should accept and normalize a same-origin configured URL', () => {
    expect(
      resolveAdminBffBaseUrl(
        'https://admin.example/shell/',
        'https://admin.example',
      ),
    ).toBe('https://admin.example');
  });

  it.each([
    'http://admin.example',
    'https://other.example',
    'https://admin.example:8443',
    'not-a-url',
    'https://user:password@admin.example',
  ])('should reject unsafe configured URL %s', (configuredUrl) => {
    expect(() =>
      resolveAdminBffBaseUrl(configuredUrl, 'https://admin.example'),
    ).toThrow('Configured admin BFF URL must be an absolute same-origin URL.');
  });

  it('should detect only the fixed authentication failure state', () => {
    expect(isAuthenticationFailure('?auth=failed')).toBe(true);
    expect(isAuthenticationFailure('?auth=success')).toBe(false);
    expect(isAuthenticationFailure('')).toBe(false);
  });

  it.each([
    ['/', '/'],
    ['/diagnostics?tab=plugins', '/diagnostics?tab=plugins'],
    ['https://attacker.example', '/'],
    ['//attacker.example', '/'],
    ['\\\\attacker.example', '/'],
    [undefined, '/'],
  ])(
    'should keep navigation destinations in the shell: %s',
    (value, expected) => {
      expect(resolveInAppDestination(value)).toBe(expected);
    },
  );

  it('should navigate to login after successful logout', async () => {
    const logout = vi.fn().mockResolvedValue(undefined);
    const navigate = vi.fn();

    await logoutAndNavigate(logout, navigate);

    expect(logout).toHaveBeenCalledTimes(1);
    expect(navigate).toHaveBeenCalledOnce();
    expect(navigate).toHaveBeenCalledWith('/auth/login');
  });

  it('should navigate to login after failed logout', async () => {
    const logout = vi.fn().mockRejectedValue(new Error('Network unavailable'));
    const navigate = vi.fn();

    await expect(logoutAndNavigate(logout, navigate)).rejects.toThrow(
      'Network unavailable',
    );
    expect(navigate).toHaveBeenCalledOnce();
    expect(navigate).toHaveBeenCalledWith('/auth/login');
  });
});
