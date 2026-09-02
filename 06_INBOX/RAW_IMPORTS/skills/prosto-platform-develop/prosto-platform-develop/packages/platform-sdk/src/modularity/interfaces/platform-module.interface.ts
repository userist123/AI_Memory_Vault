import type { MODULE_LIFECYCLE_STAGES } from '../constants/index.js';
import type { IPlatformModuleContext } from './platform-module-context.interface.js';

/**
 * @alpha
 * A single lifecycle stage identifier.
 */
export type PlatformModuleLifecycleStageType =
  (typeof MODULE_LIFECYCLE_STAGES)[number];

/**
 * @alpha
 * Common lifecycle handler return contract.
 */
export type PlatformModuleLifecycleResultType = void | Promise<void>;

/**
 * @alpha
 * Defines the contract for the modules deployed in the platform.
 */
export interface IPlatformModule {
  /**
   * Notifies the module that it has been initialized.
   */
  init(ctx: IPlatformModuleContext): PlatformModuleLifecycleResultType;

  /**
   * This method is called after all modules have been initialized with init().
   */
  start(ctx: IPlatformModuleContext): PlatformModuleLifecycleResultType;

  /**
   * This method is called before stopping the module.
   */
  stop(ctx: IPlatformModuleContext): PlatformModuleLifecycleResultType;
}
