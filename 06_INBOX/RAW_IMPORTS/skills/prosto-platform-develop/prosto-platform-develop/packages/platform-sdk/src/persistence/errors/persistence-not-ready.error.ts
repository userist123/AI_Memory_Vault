import { PersistenceError } from './persistence.error.js';

/** @alpha Indicates use of persistence before the provider is ready. */
export class PersistenceNotReadyError extends PersistenceError {
  constructor() {
    super(
      'PersistenceProviderNotReady',
      'Persistence is not ready. Resolve native persistence services only after module start().',
      {
        phase: 'not-ready',
        remediationHint:
          'Register descriptors in Module.init() and defer database access until Module.start().',
      },
    );
    this.name = 'PersistenceNotReadyError';
  }
}
