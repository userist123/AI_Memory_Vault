import { z } from 'zod';
import {
  ADMIN_AUTHENTICATION_API_SCHEMA_VERSION,
  ADMIN_AUTHENTICATION_FAILURE_CODES,
  ADMIN_AUTHENTICATION_MAXIMUM_PASSWORD_LENGTH,
  ADMIN_AUTHENTICATION_MAXIMUM_USERNAME_LENGTH,
  ADMIN_AUTHENTICATION_MINIMUM_PASSWORD_LENGTH,
  ADMIN_AUTHENTICATION_MODES,
  ADMIN_AUTHENTICATION_SESSION_STATES,
} from './admin-authentication.constants.js';

/**
 * @alpha
 * Zod schema for the authentication API schema-version discriminator.
 */
export const AdminAuthenticationApiSchemaVersionSchema = z.literal(
  ADMIN_AUTHENTICATION_API_SCHEMA_VERSION,
);

/**
 * @alpha
 * Zod schema for an account name before the local provider normalizes it.
 */
export const AdminAuthenticationUsernameSchema = z
  .string()
  .min(1)
  .max(ADMIN_AUTHENTICATION_MAXIMUM_USERNAME_LENGTH)
  .regex(/\S/u, {
    message: 'Username must contain a non-whitespace character.',
  });

/**
 * @alpha
 * Zod schema for password values submitted over the JSON authentication API.
 */
export const AdminAuthenticationPasswordSchema = z
  .string()
  .min(ADMIN_AUTHENTICATION_MINIMUM_PASSWORD_LENGTH)
  .max(ADMIN_AUTHENTICATION_MAXIMUM_PASSWORD_LENGTH);

const AdminAuthenticationPayloadSchema = z
  .object({
    schemaVersion: AdminAuthenticationApiSchemaVersionSchema,
  })
  .strict();

const AdminLocalAuthenticationSessionResponseSchema =
  AdminAuthenticationPayloadSchema.extend({
    mode: z.literal('local'),
    state: z.enum(ADMIN_AUTHENTICATION_SESSION_STATES),
  }).strict();

const AdminOidcAnonymousAuthenticationSessionResponseSchema =
  AdminAuthenticationPayloadSchema.extend({
    mode: z.literal('oidc'),
    state: z.literal('anonymous'),
    loginUrl: z.string().regex(/^\/[^\s]*$/u, {
      message: 'Login URL must be a same-origin path without whitespace.',
    }),
  }).strict();

const AdminOidcAuthenticatedAuthenticationSessionResponseSchema =
  AdminAuthenticationPayloadSchema.extend({
    mode: z.literal('oidc'),
    state: z.literal('authenticated'),
  }).strict();

/**
 * @alpha
 * Zod schema for `GET /admin/api/v1/auth/session` responses.
 */
export const AdminAuthenticationSessionResponseSchema = z.union([
  AdminLocalAuthenticationSessionResponseSchema,
  AdminOidcAnonymousAuthenticationSessionResponseSchema,
  AdminOidcAuthenticatedAuthenticationSessionResponseSchema,
]);

/**
 * @alpha
 * Zod schema for `POST /admin/api/v1/auth/login` JSON bodies.
 */
export const AdminAuthenticationLoginRequestSchema =
  AdminAuthenticationPayloadSchema.extend({
    username: AdminAuthenticationUsernameSchema,
    password: AdminAuthenticationPasswordSchema,
  }).strict();

/**
 * @alpha
 * Zod schema for successful local-login responses.
 */
export const AdminAuthenticationLoginResponseSchema =
  AdminAuthenticationPayloadSchema.extend({
    mode: z.literal('local'),
    state: z.enum(['authenticated', 'password-change-required']),
  }).strict();

/**
 * @alpha
 * Zod schema for `POST /admin/api/v1/auth/change-password` JSON bodies.
 */
export const AdminAuthenticationChangePasswordRequestSchema =
  AdminAuthenticationPayloadSchema.extend({
    currentPassword: AdminAuthenticationPasswordSchema,
    newPassword: AdminAuthenticationPasswordSchema,
  }).strict();

/**
 * @alpha
 * Zod schema for a completed forced-password-change transition.
 */
export const AdminAuthenticationChangePasswordResponseSchema =
  AdminAuthenticationPayloadSchema.extend({
    mode: z.literal('local'),
    state: z.literal('authenticated'),
  }).strict();

/**
 * @alpha
 * Zod schema for `POST /admin/api/v1/auth/logout` JSON bodies.
 */
export const AdminAuthenticationLogoutRequestSchema =
  AdminAuthenticationPayloadSchema;

/**
 * @alpha
 * Zod schema for logout responses, which always leave the shell anonymous.
 */
export const AdminAuthenticationLogoutResponseSchema =
  AdminAuthenticationPayloadSchema.extend({
    mode: z.enum(ADMIN_AUTHENTICATION_MODES),
    state: z.literal('anonymous'),
  }).strict();

/**
 * @alpha
 * Zod schema for generic authentication API failures without account details.
 */
export const AdminAuthenticationFailureResponseSchema =
  AdminAuthenticationPayloadSchema.extend({
    code: z.enum(ADMIN_AUTHENTICATION_FAILURE_CODES),
  }).strict();
