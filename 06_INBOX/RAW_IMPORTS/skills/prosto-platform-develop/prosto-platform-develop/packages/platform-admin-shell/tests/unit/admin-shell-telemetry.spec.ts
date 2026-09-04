import type { IAdminShellLogger } from '@/shared/observability/index.js';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AdminShellTelemetryService } from '@/shared/observability/index.js';

function createMockLogger(): IAdminShellLogger & {
  calls: {
    level: string;
    message: string;
    context?: Record<string, unknown>;
  }[];
} {
  const calls: {
    level: string;
    message: string;
    context?: Record<string, unknown>;
  }[] = [];

  return {
    calls,
    debug: vi.fn((message: string, context?: Record<string, unknown>) => {
      calls.push({ level: 'debug', message, context });
    }),
    info: vi.fn((message: string, context?: Record<string, unknown>) => {
      calls.push({ level: 'info', message, context });
    }),
    warn: vi.fn((message: string, context?: Record<string, unknown>) => {
      calls.push({ level: 'warn', message, context });
    }),
    error: vi.fn((message: string, context?: Record<string, unknown>) => {
      calls.push({ level: 'error', message, context });
    }),
  };
}

describe('AdminShellTelemetryService', () => {
  let logger: ReturnType<typeof createMockLogger>;
  let telemetry: AdminShellTelemetryService;

  beforeEach(() => {
    logger = createMockLogger();
    telemetry = new AdminShellTelemetryService(logger);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  describe('startup telemetry', () => {
    it('should record startup started event', () => {
      telemetry.recordStartupStarted();

      const snapshot = telemetry.takeSnapshot();

      expect(snapshot.events).toHaveLength(1);
      expect(snapshot.events[0]?.event).toBe('shell_startup_started');
    });

    it('should record startup completed with duration metric', () => {
      telemetry.recordStartupCompleted(250);

      const snapshot = telemetry.takeSnapshot();
      const durationMetric = snapshot.metrics.find(
        (m) => m.name === 'admin_shell_startup_duration_ms',
      );

      expect(durationMetric).toBeDefined();
      expect(durationMetric?.value).toBe(250);
      expect(durationMetric?.tags.outcome).toBe('success');
    });

    it('should record startup degraded', () => {
      telemetry.recordStartupDegraded(300);

      const snapshot = telemetry.takeSnapshot();
      const durationMetric = snapshot.metrics.find(
        (m) => m.name === 'admin_shell_startup_duration_ms',
      );

      expect(durationMetric?.tags.outcome).toBe('degraded');
      expect(snapshot.pluginLoadSummary.rejected).toBe(0);
    });

    it('should record startup failed', () => {
      telemetry.recordStartupFailed('DISCOVERY_NETWORK_ERROR');

      const snapshot = telemetry.takeSnapshot();
      const durationMetric = snapshot.metrics.find(
        (m) => m.name === 'admin_shell_startup_duration_ms',
      );

      expect(durationMetric?.tags.outcome).toBe('failed');
      expect(logger.error).toHaveBeenCalled();
    });
  });

  describe('discovery telemetry', () => {
    it('should record discovery started', () => {
      telemetry.recordDiscoveryStarted();

      const snapshot = telemetry.takeSnapshot();

      expect(snapshot.events).toHaveLength(1);
      expect(snapshot.events[0]?.event).toBe('discovery_started');
    });

    it('should record discovery completed with duration', () => {
      telemetry.recordDiscoveryCompleted(120, 3, 'corr-123');

      const snapshot = telemetry.takeSnapshot();
      const durationMetric = snapshot.metrics.find(
        (m) => m.name === 'admin_shell_discovery_duration_ms',
      );

      expect(durationMetric?.value).toBe(120);
      expect(durationMetric?.tags.outcome).toBe('success');
      expect(snapshot.pluginLoadSummary.totalDiscovered).toBe(3);
    });

    it('should record discovery failed', () => {
      telemetry.recordDiscoveryFailed(
        'TIMEOUT',
        10000,
        'ADMIN_SHELL_DISCOVERY_TIMEOUT',
      );

      const snapshot = telemetry.takeSnapshot();
      const durationMetric = snapshot.metrics.find(
        (m) => m.name === 'admin_shell_discovery_duration_ms',
      );

      expect(durationMetric?.tags.outcome).toBe('failure');
      expect(logger.error).toHaveBeenCalled();
    });
  });

  describe('plugin load telemetry', () => {
    it('should record plugin load started', () => {
      telemetry.recordPluginLoadStarted('plugin-a');

      const snapshot = telemetry.takeSnapshot();

      expect(snapshot.events).toHaveLength(1);
      expect(snapshot.events[0]?.properties.pluginId).toBe('plugin-a');
    });

    it('should record plugin load completed with duration', () => {
      telemetry.recordPluginLoadCompleted('plugin-a', 150);

      const snapshot = telemetry.takeSnapshot();
      const durationMetric = snapshot.metrics.find(
        (m) =>
          m.name === 'admin_shell_plugin_load_duration_ms' &&
          m.tags.pluginId === 'plugin-a',
      );

      expect(durationMetric?.value).toBe(150);
      expect(durationMetric?.tags.outcome).toBe('loaded');
      expect(snapshot.pluginLoadSummary.loaded).toBe(1);
    });

    it('should record plugin load failed', () => {
      telemetry.recordPluginLoadFailed('plugin-a', 'entry point not found');

      const snapshot = telemetry.takeSnapshot();
      const outcomeMetric = snapshot.metrics.find(
        (m) =>
          m.name === 'admin_shell_plugin_load_outcome' &&
          m.tags.pluginId === 'plugin-a',
      );

      expect(outcomeMetric?.tags.outcome).toBe('failed');
      expect(snapshot.pluginLoadSummary.failed).toBe(1);
      expect(snapshot.pluginLoadSummary.loadFailures).toBe(1);
    });

    it('should record plugin rejected', () => {
      telemetry.recordPluginRejected(
        'plugin-a',
        'SHELL_VERSION_MISMATCH',
        'Incompatible version',
      );

      const snapshot = telemetry.takeSnapshot();
      const outcomeMetric = snapshot.metrics.find(
        (m) =>
          m.name === 'admin_shell_plugin_load_outcome' &&
          m.tags.pluginId === 'plugin-a',
      );

      expect(outcomeMetric?.tags.outcome).toBe('rejected');
      expect(outcomeMetric?.tags.reasonCode).toBe('SHELL_VERSION_MISMATCH');
      expect(snapshot.pluginLoadSummary.rejected).toBe(1);
    });
  });

  describe('plugin compatibility telemetry', () => {
    it('should record compatibility check passed', () => {
      telemetry.recordPluginCompatibilityChecked('plugin-a', true);

      const snapshot = telemetry.takeSnapshot();

      expect(snapshot.events).toHaveLength(1);
      expect(snapshot.events[0]?.properties.allowed).toBe(true);
      expect(snapshot.pluginLoadSummary.compatibilityRejections).toBe(0);
    });

    it('should record compatibility check failed', () => {
      telemetry.recordPluginCompatibilityChecked(
        'plugin-a',
        false,
        'SHELL_VERSION_MISMATCH',
      );

      const snapshot = telemetry.takeSnapshot();

      expect(snapshot.events[0]?.properties.allowed).toBe(false);
      expect(snapshot.pluginLoadSummary.compatibilityRejections).toBe(1);
    });
  });

  describe('permission telemetry', () => {
    it('should record permission granted', () => {
      telemetry.recordPluginPermissionChecked('plugin-a', true);

      const snapshot = telemetry.takeSnapshot();

      expect(snapshot.events[0]?.event).toBe('plugin_permission_granted');
      expect(snapshot.pluginLoadSummary.permissionRejections).toBe(0);
    });

    it('should record permission denied', () => {
      telemetry.recordPluginPermissionChecked('plugin-a', false, [
        'admin',
        'plugins.write',
      ]);

      const snapshot = telemetry.takeSnapshot();

      expect(snapshot.events[0]?.event).toBe('plugin_permission_denied');
      expect(snapshot.pluginLoadSummary.permissionRejections).toBe(1);
    });
  });

  describe('extension telemetry', () => {
    it('should record extension registered', () => {
      telemetry.recordExtensionRegistered(
        'plugin-a',
        'navigation',
        'plugin-a-nav',
      );

      const snapshot = telemetry.takeSnapshot();

      expect(snapshot.extensionUsageSummary.totalRegistered).toBe(1);
      expect(snapshot.extensionUsageSummary.byKind.navigation).toBe(1);
    });

    it('should record extension conflict', () => {
      telemetry.recordExtensionConflict(
        'plugin-b',
        'navigation',
        'plugin-a-nav',
        'DUPLICATE_ID',
      );

      const snapshot = telemetry.takeSnapshot();

      expect(snapshot.extensionUsageSummary.conflictsDetected).toBe(1);
    });

    it('should count multiple extension kinds', () => {
      telemetry.recordExtensionRegistered('plugin-a', 'navigation', 'nav-1');
      telemetry.recordExtensionRegistered('plugin-a', 'page', 'page-1');
      telemetry.recordExtensionRegistered('plugin-a', 'widget', 'widget-1');
      telemetry.recordExtensionRegistered('plugin-a', 'action', 'action-1');

      const snapshot = telemetry.takeSnapshot();

      expect(snapshot.extensionUsageSummary.totalRegistered).toBe(4);
      expect(snapshot.extensionUsageSummary.byKind.navigation).toBe(1);
      expect(snapshot.extensionUsageSummary.byKind.page).toBe(1);
      expect(snapshot.extensionUsageSummary.byKind.widget).toBe(1);
      expect(snapshot.extensionUsageSummary.byKind.action).toBe(1);
    });
  });

  describe('UI extension usage telemetry', () => {
    it('should record extension used', () => {
      telemetry.recordExtensionUsed(
        'plugin-a',
        'page',
        'plugin-a-page',
        'render',
      );

      const snapshot = telemetry.takeSnapshot();
      const usageMetric = snapshot.metrics.find(
        (m) => m.name === 'admin_shell_ui_extension_used',
      );

      expect(usageMetric).toBeDefined();
      expect(usageMetric?.tags.pluginId).toBe('plugin-a');
      expect(usageMetric?.tags.action).toBe('render');
    });
  });

  describe('degraded mode telemetry', () => {
    it('should record degraded mode entered', () => {
      telemetry.recordDegradedModeEntered(
        'PLUGIN_LOAD_FAILURE',
        'Some plugins failed',
      );

      const snapshot = telemetry.takeSnapshot();
      const degradedMetric = snapshot.metrics.find(
        (m) => m.name === 'admin_shell_degraded_mode_active',
      );

      expect(degradedMetric).toBeDefined();
      expect(snapshot.events[0]?.event).toBe('degraded_mode_entered');
    });

    it('should record degraded mode exited', () => {
      telemetry.recordDegradedModeEntered('PLUGIN_LOAD_FAILURE', 'Some failed');
      telemetry.recordDegradedModeExited();

      const snapshot = telemetry.takeSnapshot();
      const events = snapshot.events.filter(
        (e) => e.event === 'degraded_mode_exited',
      );
      expect(events).toHaveLength(1);
    });
  });

  describe('plugin load summary', () => {
    it('should aggregate plugin load outcomes', () => {
      telemetry.recordPluginLoadCompleted('plugin-a', 100);
      telemetry.recordPluginLoadCompleted('plugin-b', 200);
      telemetry.recordPluginRejected('plugin-c', 'SHELL_VERSION_MISMATCH');
      telemetry.recordPluginLoadFailed('plugin-d', 'load error');

      const summary = telemetry.getPluginLoadSummary();

      expect(summary.loaded).toBe(2);
      expect(summary.rejected).toBe(1);
      expect(summary.failed).toBe(1);
    });

    it('should track total discovered count', () => {
      telemetry.recordDiscoveryCompleted(50, 5);

      const summary = telemetry.getPluginLoadSummary();

      expect(summary.totalDiscovered).toBe(5);
    });
  });

  describe('telemetry snapshot', () => {
    it('should include all metrics and events', () => {
      telemetry.recordStartupStarted();
      telemetry.recordDiscoveryCompleted(100, 2);
      telemetry.recordPluginLoadCompleted('plugin-a', 50);

      const snapshot = telemetry.takeSnapshot();

      expect(snapshot.metrics.length).toBeGreaterThan(0);
      expect(snapshot.events.length).toBeGreaterThan(0);
      expect(snapshot.recordedAt).toBeDefined();
    });

    it('should record snapshot event when taking snapshot', () => {
      telemetry.takeSnapshot();

      const calls = logger.calls.filter(
        (c) => c.message === 'Telemetry snapshot recorded',
      );
      expect(calls).toHaveLength(1);
    });
  });

  describe('clear', () => {
    it('should reset all telemetry data', () => {
      telemetry.recordPluginLoadCompleted('plugin-a', 100);
      telemetry.recordExtensionRegistered('plugin-a', 'navigation', 'nav-1');
      telemetry.clear();

      const snapshot = telemetry.takeSnapshot();

      expect(snapshot.metrics).toHaveLength(0);
      expect(snapshot.events).toHaveLength(0);
      expect(snapshot.pluginLoadSummary.loaded).toBe(0);
      expect(snapshot.extensionUsageSummary.totalRegistered).toBe(0);
    });
  });
});
