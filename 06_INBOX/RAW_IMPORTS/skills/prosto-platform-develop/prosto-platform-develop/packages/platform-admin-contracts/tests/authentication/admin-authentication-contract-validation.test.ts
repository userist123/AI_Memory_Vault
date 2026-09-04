import { describe, expect, it } from 'vitest';
import {
  ADMIN_AUTHENTICATION_API_SCHEMA_VERSION,
  ADMIN_AUTHENTICATION_MAXIMUM_PASSWORD_LENGTH,
  AdminAuthenticationContractValidator,
  AdminAuthenticationValidationError,
} from '@/index.js';

const schemaVersion = ADMIN_AUTHENTICATION_API_SCHEMA_VERSION;

describe('admin authentication contract validation', () => {
  const validator = new AdminAuthenticationContractValidator();

  it('accepts valid local and OIDC session payloads without account data', () => {
    const localSession = validator.parseSessionResponse({
      schemaVersion,
      mode: 'local',
      state: 'password-change-required',
    });
    const oidcSession = validator.parseSessionResponse({
      schemaVersion,
      mode: 'oidc',
      state: 'anonymous',
      loginUrl: '/auth/login',
    });

    expect(localSession).toEqual({
      schemaVersion,
      mode: 'local',
      state: 'password-change-required',
    });
    expect(oidcSession).toEqual({
      schemaVersion,
      mode: 'oidc',
      state: 'anonymous',
      loginUrl: '/auth/login',
    });
  });

  it('accepts valid local mutation requests and responses', () => {
    const loginRequest = validator.parseLoginRequest({
      schemaVersion,
      username: 'admin',
      password: 'password-123',
    });
    const changePasswordRequest = validator.parseChangePasswordRequest({
      schemaVersion,
      currentPassword: 'password-123',
      newPassword: 'new-password-456',
    });
    const logoutResponse = validator.parseLogoutResponse({
      schemaVersion,
      mode: 'local',
      state: 'anonymous',
    });

    expect(loginRequest.username).toBe('admin');
    expect(changePasswordRequest.newPassword).toBe('new-password-456');
    expect(logoutResponse.state).toBe('anonymous');
  });

  it('rejects malformed JSON values and unknown properties', () => {
    expect(() => validator.parseLoginRequest('{not-json')).toThrow(
      AdminAuthenticationValidationError,
    );
    expect(() =>
      validator.parseLoginRequest({
        schemaVersion,
        username: 'admin',
        password: 'password-123',
        isAdministrator: true,
      }),
    ).toThrow(AdminAuthenticationValidationError);
  });

  it('rejects invalid password shapes before an implementation hashes them', () => {
    expect(() =>
      validator.parseLoginRequest({
        schemaVersion,
        username: 'admin',
        password: 'short',
      }),
    ).toThrow(AdminAuthenticationValidationError);
    expect(() =>
      validator.parseChangePasswordRequest({
        schemaVersion,
        currentPassword: 'password-123',
        newPassword: 'a'.repeat(
          ADMIN_AUTHENTICATION_MAXIMUM_PASSWORD_LENGTH + 1,
        ),
      }),
    ).toThrow(AdminAuthenticationValidationError);
    expect(() =>
      validator.parseLoginRequest({
        schemaVersion,
        username: 'admin',
        password: null,
      }),
    ).toThrow(AdminAuthenticationValidationError);
  });

  it('rejects forbidden provider and session-state transitions', () => {
    expect(() =>
      validator.parseSessionResponse({
        schemaVersion,
        mode: 'oidc',
        state: 'password-change-required',
      }),
    ).toThrow(AdminAuthenticationValidationError);
    expect(() =>
      validator.parseLoginResponse({
        schemaVersion,
        mode: 'local',
        state: 'anonymous',
      }),
    ).toThrow(AdminAuthenticationValidationError);
    expect(() =>
      validator.parseChangePasswordResponse({
        schemaVersion,
        mode: 'local',
        state: 'password-change-required',
      }),
    ).toThrow(AdminAuthenticationValidationError);
    expect(() =>
      validator.parseLogoutResponse({
        schemaVersion,
        mode: 'oidc',
        state: 'authenticated',
      }),
    ).toThrow(AdminAuthenticationValidationError);
  });
});
