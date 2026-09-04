export interface IDiagnosticEntry {
  pluginId: string;
  reasonCode: string;
  timestamp: Date;
  message?: string;
  remediationHint?: string;
}

export type DegradedModeReasonType =
  | 'DISCOVERY_NETWORK_ERROR'
  | 'DISCOVERY_TIMEOUT'
  | 'DISCOVERY_HTTP_ERROR'
  | 'DISCOVERY_VALIDATION_FAILED'
  | 'PLUGIN_LOAD_FAILURE'
  | 'UNKNOWN';

export interface IDegradedModeContext {
  active: boolean;
  reason: DegradedModeReasonType;
  message: string;
  timestamp: Date;
}
