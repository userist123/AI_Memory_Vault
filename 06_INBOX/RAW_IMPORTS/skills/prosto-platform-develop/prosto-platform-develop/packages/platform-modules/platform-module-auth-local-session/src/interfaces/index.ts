import type {
  IPlatformLocalAuthRuntime,
  IPlatformLocalAuthRuntimeConfig,
} from '@prosto/platform-adapter-auth-local';
import type {
  IPlatformAuthenticationProvider,
  IPlatformHttpRouteRegistration,
} from '@prosto/platform-sdk';

/** @alpha */
export interface IPlatformLocalAuthBootstrapOutput {
  readonly isInteractive: boolean;
  write(message: string): void;
}

/** @alpha */
export interface IPlatformLocalAuthSessionModuleConfig extends IPlatformLocalAuthRuntimeConfig {
  readonly bootstrapRoles?: readonly string[];
  readonly bootstrapPermissions?: readonly string[];
  readonly bootstrapOutput?: IPlatformLocalAuthBootstrapOutput;
}

/** @alpha */
export interface IPlatformLocalAuthBootstrapResult {
  readonly created: boolean;
  readonly username?: string;
  readonly password?: string;
}

/** @alpha */
export interface IPlatformLocalAuthSessionModuleFacade {
  readonly provider: IPlatformAuthenticationProvider;
  readonly api: IPlatformLocalAuthRuntime;
  readonly routes: readonly IPlatformHttpRouteRegistration[];
  readonly ready: boolean;
}
