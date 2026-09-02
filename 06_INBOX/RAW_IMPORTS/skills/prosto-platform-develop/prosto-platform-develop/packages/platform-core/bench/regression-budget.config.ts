/**
 * Default regression budget configuration.
 */
export const DEFAULT_REGRESSION_BUDGET = {
  /**
   * Number of warmup iterations before measurement.
   */
  warmupIterations: 100,
  /**
   * Number of measured iterations.
   */
  measuredIterations: 1000,
  /**
   * Time needed for running a benchmark task (milliseconds)
   */
  time: 5000,
} as const;
