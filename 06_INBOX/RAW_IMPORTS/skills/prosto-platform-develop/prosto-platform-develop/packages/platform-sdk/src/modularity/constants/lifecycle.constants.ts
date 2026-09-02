/**
 * @alpha
 * Ordered lifecycle stages executed by the runtime kernel.
 */
export const MODULE_LIFECYCLE_STAGES = ['init', 'start', 'stop'] as const;

/**
 * @alpha
 * Runtime startup policy names.
 */
export const STARTUP_POLICIES = ['strict', 'best-effort'] as const;
