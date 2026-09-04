import type { InjectionKey } from 'vue';
import type { AdminAuthClient } from '@/shared/api/admin-auth';

/** @internal */
export interface IAdminAuthenticationContext {
  readonly authClient: AdminAuthClient;
  completeAuthentication(destination: string): Promise<void>;
}

/** @internal */
export const ADMIN_AUTHENTICATION_CONTEXT: InjectionKey<IAdminAuthenticationContext> =
  Symbol('admin-authentication-context');
