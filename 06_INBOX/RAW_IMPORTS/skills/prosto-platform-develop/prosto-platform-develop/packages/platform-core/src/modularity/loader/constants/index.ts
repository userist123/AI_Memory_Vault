/**
 * @alpha
 * Enum representing the different states of a module.
 */
export enum ModuleState {
  /**
   * The assembly that holds the Module is present.
   * This means the module can be instantiated and initialized.
   */
  ReadyForInitialization = 'READY_FOR_INITIALIZATION',

  /**
   * The module is currently Initializing.
   * This means the module is being initialized and is not ready for starting.
   */
  Initializing = 'INITIALIZING',

  /**
   * The module is initialized and ready to start.
   */
  Initialized = 'INITIALIZED',

  /**
   * The module is not initialized.
   */
  NotInitialized = 'NOT_INITIALIZED',

  /**
   * The module is currently starting.
   */
  Starting = 'STARTING',

  /**
   * The module is started and ready to be used.
   */
  Started = 'STARTED',

  /**
   * The module is not started.
   */
  NotStarted = 'NOT_STARTED',
}

/**
 * @alpha
 * Enum representing the different sources for module artifacts.
 */
export enum ModuleArtifactSource {
  Memory = 'memory',
  Path = 'path',
  Url = 'url',
  Registry = 'registry',
}

/**
 * @alpha
 * Enum representing the different packaging formats for module artifacts.
 */
export enum ModuleArtifactPackaging {
  Zip = 'zip',
  Tgz = 'tgz',
  Esm = 'esm',
}
