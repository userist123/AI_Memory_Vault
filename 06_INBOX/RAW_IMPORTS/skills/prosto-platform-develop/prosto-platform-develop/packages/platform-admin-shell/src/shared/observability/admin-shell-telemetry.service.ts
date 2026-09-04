import type { IAdminShellLogger } from './admin-shell-logger.interface.js';
import {
  AdminShellErrorCodes,
  AdminShellLogEvents,
  AdminShellPhase,
  AdminShellTelemetryMetrics,
} from './admin-shell-observability.constants.js';

/**
 * @alpha
 * Outcome of a plugin load attempt.
 */
export type PluginLoadOutcomeType = 'loaded' | 'rejected' | 'failed';

/**
 * @alpha
 * A single telemetry metric entry.
 */
export interface ITelemetryMetric {
  readonly name: string;
  readonly value: number;
  readonly timestamp: string;
  readonly tags: Readonly<Record<string, string>>;
}

/**
 * @alpha
 * A single telemetry event entry.
 */
export interface ITelemetryEvent {
  readonly event: string;
  readonly timestamp: string;
  readonly properties: Readonly<Record<string, unknown>>;
}

/**
 * @alpha
 * Snapshot of telemetry state at a point in time.
 */
export interface ITelemetrySnapshot {
  readonly recordedAt: string;
  readonly metrics: readonly ITelemetryMetric[];
  readonly events: readonly ITelemetryEvent[];
  readonly pluginLoadSummary: IPluginLoadSummary;
  readonly extensionUsageSummary: IExtensionUsageSummary;
}

/**
 * @alpha
 * Summary of plugin load outcomes.
 */
export interface IPluginLoadSummary {
  readonly totalDiscovered: number;
  readonly loaded: number;
  readonly rejected: number;
  readonly failed: number;
  readonly compatibilityRejections: number;
  readonly permissionRejections: number;
  readonly loadFailures: number;
}

/**
 * @alpha
 * Summary of UI extension usage.
 */
export interface IExtensionUsageSummary {
  readonly totalRegistered: number;
  readonly byKind: Readonly<Record<string, number>>;
  readonly conflictsDetected: number;
}

/**
 * @alpha
 * Telemetry service for collecting plugin load outcomes and UI extension usage.
 *
 * Accumulates metrics and events during the shell lifecycle and provides
 * snapshots for observability pipelines. Does not perform network I/O —
 * consumers are responsible for flushing snapshots to external backends.
 *
 * @example
 * ```typescript
 * const telemetry = new AdminShellTelemetryService(logger);
 *
 * telemetry.recordPluginLoadStarted('my-plugin');
 * telemetry.recordPluginLoadCompleted('my-plugin', 150);
 *
 * const snapshot = telemetry.takeSnapshot();
 * ```
 */
export class AdminShellTelemetryService {
  private readonly _logger: IAdminShellLogger;
  private readonly _metrics: ITelemetryMetric[] = [];
  private readonly _events: ITelemetryEvent[] = [];
  private readonly _pluginOutcomes = new Map<string, PluginLoadOutcomeType>();
  private readonly _pluginLoadDurations = new Map<string, number>();
  private readonly _extensionCountsByKind = new Map<string, number>();
  private _extensionConflictsDetected = 0;
  private _totalDiscovered = 0;
  private _compatibilityRejections = 0;
  private _permissionRejections = 0;
  private _loadFailures = 0;

  constructor(logger: IAdminShellLogger) {
    this._logger = logger;
  }

  /**
   * Record that the shell startup sequence has begun.
   */
  recordStartupStarted(): void {
    this._recordEvent(AdminShellLogEvents.SHELL_STARTUP_STARTED, {});

    this._logger.info('Shell startup started', {
      phase: AdminShellPhase.SHELL_STARTUP,
      event: AdminShellLogEvents.SHELL_STARTUP_STARTED,
    });
  }

  /**
   * Record successful shell startup with timing.
   */
  recordStartupCompleted(durationMs: number): void {
    this._recordMetric(
      AdminShellTelemetryMetrics.SHELL_STARTUP_DURATION_MS,
      durationMs,
      { outcome: 'success' },
    );

    this._recordEvent(AdminShellLogEvents.SHELL_STARTUP_COMPLETED, {
      durationMs,
      outcome: 'success',
      pluginCount: this._pluginOutcomes.size,
    });

    this._logger.info('Shell startup completed', {
      phase: AdminShellPhase.SHELL_STARTUP,
      event: AdminShellLogEvents.SHELL_STARTUP_COMPLETED,
      durationMs,
      pluginCount: this._pluginOutcomes.size,
    });
  }

  /**
   * Record degraded shell startup with timing.
   */
  recordStartupDegraded(durationMs: number): void {
    this._recordMetric(
      AdminShellTelemetryMetrics.SHELL_STARTUP_DURATION_MS,
      durationMs,
      { outcome: 'degraded' },
    );

    this._recordEvent(AdminShellLogEvents.SHELL_STARTUP_COMPLETED, {
      durationMs,
      outcome: 'degraded',
      pluginCount: this._pluginOutcomes.size,
      rejectedCount: this._pluginOutcomes.size,
    });

    this._logger.warn('Shell startup completed in degraded mode', {
      phase: AdminShellPhase.SHELL_STARTUP,
      event: AdminShellLogEvents.SHELL_STARTUP_COMPLETED,
      durationMs,
      outcome: 'degraded',
    });
  }

  /**
   * Record failed shell startup.
   */
  recordStartupFailed(reason: string): void {
    this._recordMetric(
      AdminShellTelemetryMetrics.SHELL_STARTUP_DURATION_MS,
      0,
      { outcome: 'failed' },
    );

    this._recordEvent(AdminShellLogEvents.SHELL_STARTUP_FAILED, {
      reason,
    });

    this._logger.error('Shell startup failed', {
      phase: AdminShellPhase.SHELL_STARTUP,
      event: AdminShellLogEvents.SHELL_STARTUP_FAILED,
      errorCode: AdminShellErrorCodes.SHELL_STARTUP_FAILED,
      reason,
    });
  }

  /**
   * Record discovery request started.
   */
  recordDiscoveryStarted(): void {
    this._recordEvent(AdminShellLogEvents.DISCOVERY_STARTED, {});

    this._logger.info('Discovery request started', {
      phase: AdminShellPhase.DISCOVERY,
      event: AdminShellLogEvents.DISCOVERY_STARTED,
    });
  }

  /**
   * Record discovery request completed successfully.
   */
  recordDiscoveryCompleted(
    durationMs: number,
    pluginCount: number,
    correlationId?: string,
  ): void {
    this._totalDiscovered = pluginCount;

    this._recordMetric(
      AdminShellTelemetryMetrics.DISCOVERY_DURATION_MS,
      durationMs,
      { outcome: 'success' },
    );

    this._recordEvent(AdminShellLogEvents.DISCOVERY_COMPLETED, {
      durationMs,
      pluginCount,
      correlationId,
    });

    this._logger.info('Discovery request completed', {
      phase: AdminShellPhase.DISCOVERY,
      event: AdminShellLogEvents.DISCOVERY_COMPLETED,
      durationMs,
      pluginCount,
      correlationId,
    });
  }

  /**
   * Record discovery request failure.
   */
  recordDiscoveryFailed(
    reason: string,
    durationMs: number,
    errorCode?: string,
  ): void {
    this._recordMetric(
      AdminShellTelemetryMetrics.DISCOVERY_DURATION_MS,
      durationMs,
      { outcome: 'failure' },
    );

    this._recordEvent(AdminShellLogEvents.DISCOVERY_FAILED, {
      reason,
      durationMs,
      errorCode,
    });

    this._logger.error('Discovery request failed', {
      phase: AdminShellPhase.DISCOVERY,
      event: AdminShellLogEvents.DISCOVERY_FAILED,
      reason,
      durationMs,
      errorCode,
    });
  }

  /**
   * Record a plugin compatibility check result.
   */
  recordPluginCompatibilityChecked(
    pluginId: string,
    allowed: boolean,
    reasonCode?: string,
  ): void {
    if (!allowed) {
      this._compatibilityRejections++;
    }

    this._recordEvent(AdminShellLogEvents.PLUGIN_COMPATIBILITY_CHECKED, {
      pluginId,
      allowed,
      reasonCode,
    });

    this._logger.debug('Plugin compatibility checked', {
      phase: AdminShellPhase.PLUGIN_COMPATIBILITY,
      event: AdminShellLogEvents.PLUGIN_COMPATIBILITY_CHECKED,
      pluginId,
      allowed,
      reasonCode,
    });
  }

  /**
   * Record a plugin permission check result.
   */
  recordPluginPermissionChecked(
    pluginId: string,
    allowed: boolean,
    missingPermissions?: readonly string[],
  ): void {
    if (!allowed) {
      this._permissionRejections++;
    }

    this._recordEvent(
      allowed
        ? AdminShellLogEvents.PLUGIN_PERMISSION_GRANTED
        : AdminShellLogEvents.PLUGIN_PERMISSION_DENIED,
      {
        pluginId,
        allowed,
        missingPermissions,
      },
    );

    this._logger.debug(
      allowed ? 'Plugin permission granted' : 'Plugin permission denied',
      {
        phase: AdminShellPhase.PLUGIN_PERMISSIONS,
        event: allowed
          ? AdminShellLogEvents.PLUGIN_PERMISSION_GRANTED
          : AdminShellLogEvents.PLUGIN_PERMISSION_DENIED,
        pluginId,
        allowed,
        missingPermissions,
      },
    );
  }

  /**
   * Record that a plugin load attempt has started.
   */
  recordPluginLoadStarted(pluginId: string): void {
    this._recordEvent(AdminShellLogEvents.PLUGIN_LOAD_STARTED, {
      pluginId,
    });

    this._logger.debug('Plugin load started', {
      phase: AdminShellPhase.PLUGIN_LOAD,
      event: AdminShellLogEvents.PLUGIN_LOAD_STARTED,
      pluginId,
    });
  }

  /**
   * Record that a plugin loaded successfully with timing.
   */
  recordPluginLoadCompleted(pluginId: string, durationMs: number): void {
    this._pluginOutcomes.set(pluginId, 'loaded');
    this._pluginLoadDurations.set(pluginId, durationMs);

    this._recordMetric(
      AdminShellTelemetryMetrics.PLUGIN_LOAD_DURATION_MS,
      durationMs,
      { pluginId, outcome: 'loaded' },
    );

    this._recordMetric(AdminShellTelemetryMetrics.PLUGIN_LOAD_OUTCOME, 1, {
      pluginId,
      outcome: 'loaded',
    });

    this._recordEvent(AdminShellLogEvents.PLUGIN_LOAD_COMPLETED, {
      pluginId,
      durationMs,
    });

    this._logger.info('Plugin load completed', {
      phase: AdminShellPhase.PLUGIN_LOAD,
      event: AdminShellLogEvents.PLUGIN_LOAD_COMPLETED,
      pluginId,
      durationMs,
    });
  }

  /**
   * Record that a plugin failed to load.
   */
  recordPluginLoadFailed(pluginId: string, reason: string): void {
    this._pluginOutcomes.set(pluginId, 'failed');
    this._loadFailures++;

    this._recordMetric(AdminShellTelemetryMetrics.PLUGIN_LOAD_OUTCOME, 1, {
      pluginId,
      outcome: 'failed',
    });

    this._recordEvent(AdminShellLogEvents.PLUGIN_LOAD_FAILED, {
      pluginId,
      reason,
    });

    this._logger.error('Plugin load failed', {
      phase: AdminShellPhase.PLUGIN_LOAD,
      event: AdminShellLogEvents.PLUGIN_LOAD_FAILED,
      pluginId,
      reason,
      errorCode: AdminShellErrorCodes.PLUGIN_LOAD_FAILED,
    });
  }

  /**
   * Record that a plugin was rejected (compatibility, permission, or conflict).
   */
  recordPluginRejected(
    pluginId: string,
    reasonCode: string,
    message?: string,
  ): void {
    this._pluginOutcomes.set(pluginId, 'rejected');

    this._recordMetric(AdminShellTelemetryMetrics.PLUGIN_LOAD_OUTCOME, 1, {
      pluginId,
      outcome: 'rejected',
      reasonCode,
    });

    this._recordEvent(AdminShellLogEvents.PLUGIN_REJECTED, {
      pluginId,
      reasonCode,
      message,
    });

    this._logger.warn('Plugin rejected', {
      phase: AdminShellPhase.PLUGIN_REGISTRATION,
      event: AdminShellLogEvents.PLUGIN_REJECTED,
      pluginId,
      reasonCode,
      message,
    });
  }

  /**
   * Record extension registration for a plugin.
   */
  recordExtensionRegistered(
    pluginId: string,
    kind: string,
    descriptorId: string,
  ): void {
    const current = this._extensionCountsByKind.get(kind) ?? 0;
    this._extensionCountsByKind.set(kind, current + 1);

    this._recordEvent(AdminShellLogEvents.EXTENSION_REGISTERED, {
      pluginId,
      kind,
      descriptorId,
    });

    this._logger.debug('Extension registered', {
      phase: AdminShellPhase.EXTENSION_REGISTRATION,
      event: AdminShellLogEvents.EXTENSION_REGISTERED,
      pluginId,
      kind,
      descriptorId,
    });
  }

  /**
   * Record an extension registration conflict.
   */
  recordExtensionConflict(
    pluginId: string,
    kind: string,
    conflictingDescriptorId: string,
    reason: string,
  ): void {
    this._extensionConflictsDetected++;

    this._recordEvent(AdminShellLogEvents.EXTENSION_REGISTRATION_CONFLICT, {
      pluginId,
      kind,
      conflictingDescriptorId,
      reason,
    });

    this._logger.warn('Extension registration conflict', {
      phase: AdminShellPhase.EXTENSION_REGISTRATION,
      event: AdminShellLogEvents.EXTENSION_REGISTRATION_CONFLICT,
      pluginId,
      kind,
      conflictingDescriptorId,
      reason,
    });
  }

  /**
   * Record UI extension usage by the operator.
   */
  recordExtensionUsed(
    pluginId: string,
    kind: string,
    descriptorId: string,
    action: 'render' | 'navigate' | 'invoke',
  ): void {
    this._recordMetric(AdminShellTelemetryMetrics.UI_EXTENSION_USED, 1, {
      pluginId,
      kind,
      descriptorId,
      action,
    });

    this._recordEvent(AdminShellLogEvents.EXTENSION_REGISTERED, {
      pluginId,
      kind,
      descriptorId,
      action,
    });

    this._logger.debug('UI extension used', {
      phase: AdminShellPhase.EXTENSION_REGISTRATION,
      pluginId,
      kind,
      descriptorId,
      action,
    });
  }

  /**
   * Record entry into degraded mode.
   */
  recordDegradedModeEntered(reason: string, message: string): void {
    this._recordMetric(AdminShellTelemetryMetrics.DEGRADED_MODE_ACTIVE, 1, {
      reason,
    });

    this._recordEvent(AdminShellLogEvents.DEGRADED_MODE_ENTERED, {
      reason,
      message,
    });

    this._logger.warn('Degraded mode entered', {
      phase: AdminShellPhase.DEGRADED_MODE,
      event: AdminShellLogEvents.DEGRADED_MODE_ENTERED,
      reason,
      message,
    });
  }

  /**
   * Record exit from degraded mode.
   */
  recordDegradedModeExited(): void {
    this._recordEvent(AdminShellLogEvents.DEGRADED_MODE_EXITED, {});

    this._logger.info('Degraded mode exited', {
      phase: AdminShellPhase.DEGRADED_MODE,
      event: AdminShellLogEvents.DEGRADED_MODE_EXITED,
    });
  }

  /**
   * Take a snapshot of all telemetry data collected so far.
   */
  takeSnapshot(): ITelemetrySnapshot {
    const snapshot: ITelemetrySnapshot = {
      recordedAt: new Date().toISOString(),
      metrics: [...this._metrics],
      events: [...this._events],
      pluginLoadSummary: this._buildPluginLoadSummary(),
      extensionUsageSummary: this._buildExtensionUsageSummary(),
    };

    this._logger.info('Telemetry snapshot recorded', {
      phase: AdminShellPhase.TELEMETRY_FLUSH,
      event: AdminShellLogEvents.TELEMETRY_SNAPSHOT_RECORDED,
      metricsCount: snapshot.metrics.length,
      eventsCount: snapshot.events.length,
    });

    return snapshot;
  }

  /**
   * Get the current plugin load summary.
   */
  getPluginLoadSummary(): IPluginLoadSummary {
    return this._buildPluginLoadSummary();
  }

  /**
   * Get the current extension usage summary.
   */
  getExtensionUsageSummary(): IExtensionUsageSummary {
    return this._buildExtensionUsageSummary();
  }

  /**
   * Clear all accumulated telemetry data.
   */
  clear(): void {
    this._metrics.length = 0;
    this._events.length = 0;
    this._pluginOutcomes.clear();
    this._pluginLoadDurations.clear();
    this._extensionCountsByKind.clear();
    this._extensionConflictsDetected = 0;
    this._totalDiscovered = 0;
    this._compatibilityRejections = 0;
    this._permissionRejections = 0;
    this._loadFailures = 0;
  }

  private _recordMetric(
    name: string,
    value: number,
    tags: Record<string, string>,
  ): void {
    this._metrics.push({
      name,
      value,
      timestamp: new Date().toISOString(),
      tags,
    });
  }

  private _recordEvent(
    event: string,
    properties: Record<string, unknown>,
  ): void {
    this._events.push({
      event,
      timestamp: new Date().toISOString(),
      properties,
    });
  }

  private _buildPluginLoadSummary(): IPluginLoadSummary {
    let loaded = 0;
    let rejected = 0;
    let failed = 0;

    for (const outcome of this._pluginOutcomes.values()) {
      switch (outcome) {
        case 'loaded':
          loaded++;
          break;
        case 'rejected':
          rejected++;
          break;
        case 'failed':
          failed++;
          break;
      }
    }

    return {
      totalDiscovered: this._totalDiscovered,
      loaded,
      rejected,
      failed,
      compatibilityRejections: this._compatibilityRejections,
      permissionRejections: this._permissionRejections,
      loadFailures: this._loadFailures,
    };
  }

  private _buildExtensionUsageSummary(): IExtensionUsageSummary {
    const byKind: Record<string, number> = {};

    for (const [kind, count] of this._extensionCountsByKind) {
      byKind[kind] = count;
    }

    let totalRegistered = 0;

    for (const count of this._extensionCountsByKind.values()) {
      totalRegistered += count;
    }

    return {
      totalRegistered,
      byKind,
      conflictsDetected: this._extensionConflictsDetected,
    };
  }
}
