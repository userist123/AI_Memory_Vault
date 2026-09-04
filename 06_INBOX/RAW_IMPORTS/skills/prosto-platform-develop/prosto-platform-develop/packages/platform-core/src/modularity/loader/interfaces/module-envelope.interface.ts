import type {
  IPlatformModule,
  IPlatformModuleManifest,
} from '@prosto/platform-sdk';
import type { ModuleState } from '../constants/index.js';

/**
 * @alpha
 * Module envelope interface
 */
export interface IModuleEnvelope {
  manifest: IPlatformModuleManifest;
  module: IPlatformModule;
  state: ModuleState;
  fullPhysicalPath: string;
}
