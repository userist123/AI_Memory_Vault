/**
 * @alpha
 * Enum representing the different stages of the runtime startup process.
 */
export enum RuntimeStage {
  Discover = 'discover',
  Validate = 'validate',
  Resolve = 'resolve',
  Lifecycle = 'lifecycle',
  Persistence = 'persistence',
  Shutdown = 'shutdown',
}

/**
 * @alpha
 * Canonical error codes for runtime diagnostic reporting.
 */
export enum RuntimeErrorCodes {
  SourceDescriptorInvalid = 'SOURCE_DESCRIPTOR_INVALID',
  SourceUrlInvalid = 'SOURCE_URL_INVALID',
  SourceFetchFailed = 'SOURCE_FETCH_FAILED',
  SourceIntegrityMismatch = 'SOURCE_INTEGRITY_MISMATCH',
  SourceExtractionFailed = 'SOURCE_EXTRACTION_FAILED',
  SourceEntryResolveFailed = 'SOURCE_ENTRY_RESOLVE_FAILED',
  ManifestInvalid = 'MANIFEST_INVALID',
  IntegrityCheckFailed = 'INTEGRITY_CHECK_FAILED',
  CompatibilityMismatch = 'COMPATIBILITY_MISMATCH',
  ConfigAccessDenied = 'CONFIG_ACCESS_DENIED',
  ConfigCapabilityInvalid = 'CONFIG_CAPABILITY_INVALID',
  ConfigSectionNotAllowlisted = 'CONFIG_SECTION_NOT_ALLOWLISTED',
  ConfigWildcardForbidden = 'CONFIG_WILDCARD_FORBIDDEN',
  DependencyCycleDetected = 'DEPENDENCY_CYCLE_DETECTED',
  DependencyMissing = 'DEPENDENCY_MISSING',
  DependencyFailed = 'DEPENDENCY_FAILED',
  LifecycleInitFailed = 'LIFECYCLE_INIT_FAILED',
  LifecycleStartFailed = 'LIFECYCLE_START_FAILED',
  PersistenceFailed = 'PERSISTENCE_FAILED',
  ShutdownTimeout = 'SHUTDOWN_TIMEOUT',
  ShutdownFailed = 'SHUTDOWN_FAILED',
}
