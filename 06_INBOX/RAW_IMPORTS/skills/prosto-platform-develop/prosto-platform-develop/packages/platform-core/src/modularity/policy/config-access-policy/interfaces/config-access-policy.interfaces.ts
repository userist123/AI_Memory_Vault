/**
 * @alpha
 * Configuration access policy definition.
 * Defines rules for module access to configuration sections.
 */
export interface IConfigAccessPolicy {
  /**
   * Whether to enforce strict mode in production.
   * When true, policy violations block module startup in production.
   */
  readonly productionStrictMode: boolean;
}
