import type {
  AdminAuthenticationApiSchemaVersionType,
  AdminAuthenticationFailureCodeType,
  AdminAuthenticationModeType,
  AdminAuthenticationSessionStateType,
} from './admin-authentication.types.js';

/**
 * @alpha
 * Common fields of every versioned admin authentication API payload.
 */
export interface IAdminAuthenticationPayload {
  readonly schemaVersion: AdminAuthenticationApiSchemaVersionType;
}

/**
 * @alpha
 * Local-provider session status. It contains no account or credential data.
 */
export interface IAdminLocalAuthenticationSessionResponse extends IAdminAuthenticationPayload {
  readonly mode: 'local';
  readonly state: AdminAuthenticationSessionStateType;
}

/**
 * @alpha
 * Anonymous OIDC status with the same-origin BFF login route.
 */
export interface IAdminOidcAnonymousAuthenticationSessionResponse extends IAdminAuthenticationPayload {
  readonly mode: 'oidc';
  readonly state: 'anonymous';
  readonly loginUrl: string;
}

/**
 * @alpha
 * Authenticated OIDC status. Password change is intentionally not an OIDC state.
 */
export interface IAdminOidcAuthenticatedAuthenticationSessionResponse extends IAdminAuthenticationPayload {
  readonly mode: 'oidc';
  readonly state: 'authenticated';
}

/**
 * @alpha
 * Session response consumed by the admin shell before discovery is requested.
 */
export type AdminAuthenticationSessionResponseType =
  | IAdminLocalAuthenticationSessionResponse
  | IAdminOidcAnonymousAuthenticationSessionResponse
  | IAdminOidcAuthenticatedAuthenticationSessionResponse;

/**
 * @alpha
 * JSON credentials submitted only to the local login endpoint.
 */
export interface IAdminAuthenticationLoginRequest extends IAdminAuthenticationPayload {
  readonly username: string;
  readonly password: string;
}

/**
 * @alpha
 * Successful local login response. Anonymous transitions are invalid.
 */
export interface IAdminAuthenticationLoginResponse extends IAdminAuthenticationPayload {
  readonly mode: 'local';
  readonly state: 'authenticated' | 'password-change-required';
}

/**
 * @alpha
 * JSON password change request for a local account session.
 */
export interface IAdminAuthenticationChangePasswordRequest extends IAdminAuthenticationPayload {
  readonly currentPassword: string;
  readonly newPassword: string;
}

/**
 * @alpha
 * Successful password change response. It completes the forced-change transition.
 */
export interface IAdminAuthenticationChangePasswordResponse extends IAdminAuthenticationPayload {
  readonly mode: 'local';
  readonly state: 'authenticated';
}

/**
 * @alpha
 * JSON logout request, required so the mutation cannot be submitted as a form.
 */
export interface IAdminAuthenticationLogoutRequest {
  readonly schemaVersion: AdminAuthenticationApiSchemaVersionType;
}

/**
 * @alpha
 * Logout response after the server has invalidated the current session.
 */
export interface IAdminAuthenticationLogoutResponse extends IAdminAuthenticationPayload {
  readonly mode: AdminAuthenticationModeType;
  readonly state: 'anonymous';
}

/**
 * @alpha
 * Generic, non-enumerating failure payload for authentication API mutations.
 */
export interface IAdminAuthenticationFailureResponse extends IAdminAuthenticationPayload {
  readonly code: AdminAuthenticationFailureCodeType;
}

/**
 * @alpha
 * One normalized issue produced while parsing an authentication API payload.
 */
export interface IAdminAuthenticationValidationIssue {
  readonly code: string;
  readonly message: string;
  readonly path: string;
}
