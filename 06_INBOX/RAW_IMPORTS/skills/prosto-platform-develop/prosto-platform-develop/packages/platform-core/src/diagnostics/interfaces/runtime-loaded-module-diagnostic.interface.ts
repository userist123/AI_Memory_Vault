/**
 * @alpha
 * Interface representing diagnostic information for a loaded module during runtime startup.
 */
export interface IRuntimeLoadedModuleDiagnostic {
  readonly moduleId: string;
  readonly version: string;
}
