/**
 * Configuration provider interface.
 */
export interface IConfigurationProvider {
  load(): Record<string, unknown>;
}
