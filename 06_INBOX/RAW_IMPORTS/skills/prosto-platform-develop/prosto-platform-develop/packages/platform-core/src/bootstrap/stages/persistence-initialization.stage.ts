import type { PersistenceError } from '@prosto/platform-sdk';
import type { IBootstrapStageContext } from '../interfaces/index.js';
import { RuntimeErrorCodes } from '@/common/index.js';
import { BootstrapStage } from '../constants/index.js';
import { BootstrapBaseStage } from './bootstrap.base-stage.js';

/**
 * @alpha
 * Initializes the shared persistence provider between init and start.
 */
export class PersistenceInitializationStage extends BootstrapBaseStage {
  readonly stageType = BootstrapStage.Persistence;

  override async execute(
    context: IBootstrapStageContext,
  ): Promise<IBootstrapStageContext> {
    if (!this.isPersistenceEnabled(context)) {
      this.addOutcome(context, {
        ok: true,
        details: 'Persistence is disabled.',
      });
      return context;
    }

    if (!context.persistenceProvider) {
      this.fail(context, 'Persistence provider is unavailable.');
      return context;
    }

    try {
      const descriptors = context.persistenceProvider.descriptors.seal();
      await context.persistenceProvider.initialize({
        descriptors,
        configuration: context.persistenceConfiguration,
        services: context.services,
      });
    } catch (error) {
      this.fail(context, 'Persistence initialization failed.', error);
      return context;
    }

    this.addOutcome(context, {
      ok: true,
      details: 'Persistence provider is ready.',
    });

    return context;
  }

  private fail(
    context: IBootstrapStageContext,
    message: string,
    error?: unknown,
  ): void {
    const persistenceError = error as Partial<PersistenceError> | undefined;
    const details = persistenceError?.details;
    const remediationHint =
      typeof details?.remediationHint === 'string'
        ? details.remediationHint
        : 'Inspect persistence configuration and provider diagnostics.';

    this.addFailure(context, {
      moduleId: 'platform',
      errorCode: RuntimeErrorCodes.PersistenceFailed,
      message,
      remediationHint,
    });
    this.addOutcome(context, { ok: false, details: message });
    this.stopPipeline(context);
  }
}
