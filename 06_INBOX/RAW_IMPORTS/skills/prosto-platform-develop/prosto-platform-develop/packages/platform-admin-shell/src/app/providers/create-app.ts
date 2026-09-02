import {
  ADMIN_COMPATIBILITY_CONTRACT_VERSION,
  type AdminAuthenticationSessionResponseType,
} from '@prosto/platform-admin-contracts';
import { type App as AppInstance, createApp } from 'vue';
import { createPinia } from 'pinia';
import { AdminAuthClient, AdminDiscoveryClient } from '@/shared/api';
import {
  AdminShellTelemetryService,
  ConsoleAdminShellLogger,
} from '@/shared/observability';
import { PluginRuntimeService } from '@/features/plugin-runtime';
import { ADMIN_AUTHENTICATION_CONTEXT } from '@/features/authentication';
import { useDiagnosticsStore } from '@/entities/diagnostics';
import { usePluginStore } from '@/entities/plugin';
import { shellBootstrap } from '@/processes/admin-shell-bootstrap';
import { APP_VERSION } from '@/shared/version';
import { createVuetify } from './create-vuetify.js';
import { createI18n } from './create-i18n.js';
import {
  isAuthenticationFailure,
  logoutAndNavigate,
  resolveAdminBffBaseUrl,
  resolveInAppDestination,
} from '../config/admin-shell-browser-config.js';
import App from '../app.vue';
import router from '../routes/index.js';

export async function bootstrapAdminShellApp(): Promise<AppInstance<Element>> {
  const baseUrl = resolveAdminBffBaseUrl(
    import.meta.env.VITE_ADMIN_BFF_BASE_URL,
    window.location.origin,
  );
  const authenticationFailed = isAuthenticationFailure(window.location.search);
  const authClient = new AdminAuthClient({ baseUrl });
  const navigate = (path: string): void => window.location.assign(path);
  let authenticationMode: 'local' | 'oidc' = 'local';
  let reloadAuthenticatedShell: (
    destination: string,
  ) => Promise<void> = async (): Promise<void> => {
    throw new Error('Admin shell authentication is not initialized.');
  };
  let clearShellState: () => void = () => undefined;

  const app = createApp(App, {
    authenticationFailed,
    logout: async () => {
      if (authenticationMode === 'oidc') {
        clearShellState();
        navigate('/auth/logout');
        return;
      }

      try {
        await logoutAndNavigate(
          () => authClient.logout().then(() => undefined),
          navigate,
          '/login',
        );
      } finally {
        clearShellState();
      }
    },
  });

  app.use(createPinia());
  app.use(createVuetify());
  app.use(createI18n());
  app.use(router);

  app.provide(ADMIN_AUTHENTICATION_CONTEXT, {
    authClient,
    completeAuthentication: (destination: string) =>
      reloadAuthenticatedShell(resolveInAppDestination(destination)),
  });

  app.mount('#app');

  if (authenticationFailed) {
    return app;
  }

  const pluginStore = usePluginStore();
  const diagnosticsStore = useDiagnosticsStore();

  const logger = new ConsoleAdminShellLogger();
  const telemetry = new AdminShellTelemetryService(logger);
  const discoveryClient = new AdminDiscoveryClient({
    baseUrl,
  });

  const pluginRuntime = new PluginRuntimeService(
    { pluginStore, diagnosticsStore, telemetry, logger },
    {
      shellVersion: APP_VERSION,
      supportedContractVersion: ADMIN_COMPATIBILITY_CONTRACT_VERSION,
    },
  );

  clearShellState = (): void => {
    pluginStore.clear();
    diagnosticsStore.clear();
  };

  const handleUnauthenticated = async (): Promise<void> => {
    try {
      const status = await authClient.getSessionStatus();

      authenticationMode = status.mode;

      if (status.mode === 'oidc' && status.state === 'anonymous') {
        navigate(status.loginUrl);
        return;
      }
    } catch {
      // Keep an expired local session on a shell-owned route, not an OIDC path.
    }

    await router.replace({
      name: 'login',
      query: {
        redirect: resolveInAppDestination(router.currentRoute.value.fullPath),
      },
    });
  };
  const loadDiscovery = async (): Promise<void> => {
    await shellBootstrap({
      discoveryClient,
      pluginRuntime,
      pluginStore,
      diagnosticsStore,
      telemetry,
      logger,
      navigateToLogin: handleUnauthenticated,
    });
  };

  reloadAuthenticatedShell = async (destination) => {
    clearShellState();
    await router.replace(destination);
    await loadDiscovery();
  };

  await router.isReady();

  let status: AdminAuthenticationSessionResponseType;

  try {
    status = await authClient.getSessionStatus();
  } catch {
    diagnosticsStore.enterDegradedMode(
      'DISCOVERY_NETWORK_ERROR',
      'Cannot reach admin BFF. Shell running with no plugins.',
    );

    return app;
  }

  authenticationMode = status.mode;

  const destination = resolveInAppDestination(
    router.currentRoute.value.fullPath,
  );

  if (status.state === 'anonymous') {
    if (status.mode === 'oidc') {
      navigate(status.loginUrl);
    } else {
      await router.replace({ name: 'login', query: { redirect: destination } });
    }

    return app;
  }

  if (status.state === 'password-change-required') {
    await router.replace({
      name: 'change-password',
      query: { redirect: destination },
    });
    return app;
  }

  if (router.currentRoute.value.meta.public === true) {
    await router.replace(
      resolveInAppDestination(router.currentRoute.value.query.redirect),
    );
  }

  await loadDiscovery();

  return app;
}
