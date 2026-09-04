import type { ZodIssue, ZodType } from 'zod';
import { AdminAuthenticationValidationError } from './admin-authentication.error.js';
import type {
  AdminAuthenticationSessionResponseType,
  IAdminAuthenticationChangePasswordRequest,
  IAdminAuthenticationChangePasswordResponse,
  IAdminAuthenticationFailureResponse,
  IAdminAuthenticationLoginRequest,
  IAdminAuthenticationLoginResponse,
  IAdminAuthenticationLogoutRequest,
  IAdminAuthenticationLogoutResponse,
  IAdminAuthenticationValidationIssue,
} from './admin-authentication.interfaces.js';
import {
  AdminAuthenticationChangePasswordRequestSchema,
  AdminAuthenticationChangePasswordResponseSchema,
  AdminAuthenticationFailureResponseSchema,
  AdminAuthenticationLoginRequestSchema,
  AdminAuthenticationLoginResponseSchema,
  AdminAuthenticationLogoutRequestSchema,
  AdminAuthenticationLogoutResponseSchema,
  AdminAuthenticationSessionResponseSchema,
} from './admin-authentication.schema.js';

/**
 * @alpha
 * Parser for versioned admin authentication API bodies and responses.
 */
export class AdminAuthenticationContractValidator {
  parseSessionResponse(
    payload: unknown,
  ): AdminAuthenticationSessionResponseType {
    return this._parse(AdminAuthenticationSessionResponseSchema, payload);
  }

  parseLoginRequest(payload: unknown): IAdminAuthenticationLoginRequest {
    return this._parse(AdminAuthenticationLoginRequestSchema, payload);
  }

  parseLoginResponse(payload: unknown): IAdminAuthenticationLoginResponse {
    return this._parse(AdminAuthenticationLoginResponseSchema, payload);
  }

  parseChangePasswordRequest(
    payload: unknown,
  ): IAdminAuthenticationChangePasswordRequest {
    return this._parse(AdminAuthenticationChangePasswordRequestSchema, payload);
  }

  parseChangePasswordResponse(
    payload: unknown,
  ): IAdminAuthenticationChangePasswordResponse {
    return this._parse(
      AdminAuthenticationChangePasswordResponseSchema,
      payload,
    );
  }

  parseLogoutRequest(payload: unknown): IAdminAuthenticationLogoutRequest {
    return this._parse(AdminAuthenticationLogoutRequestSchema, payload);
  }

  parseLogoutResponse(payload: unknown): IAdminAuthenticationLogoutResponse {
    return this._parse(AdminAuthenticationLogoutResponseSchema, payload);
  }

  parseFailureResponse(payload: unknown): IAdminAuthenticationFailureResponse {
    return this._parse(AdminAuthenticationFailureResponseSchema, payload);
  }

  private _parse<TPayload>(
    schema: ZodType<TPayload>,
    payload: unknown,
  ): TPayload {
    const parsed = schema.safeParse(payload);

    if (parsed.success) {
      return parsed.data;
    }

    throw new AdminAuthenticationValidationError(
      parsed.error.issues.map((issue) => this._toValidationIssue(issue)),
    );
  }

  private _toValidationIssue(
    issue: ZodIssue,
  ): IAdminAuthenticationValidationIssue {
    return {
      code: issue.code,
      message: issue.message,
      path: issue.path.length ? issue.path.join('.') : '$',
    };
  }
}
