/**
 * @alpha
 * Options for loading environment variables.
 */
export interface IEnvOptions {
  prefix?: string;
  /**
   * The separator used to split the environment variable name into parts.
   * @default "__"
   */
  separator?: string;
}
