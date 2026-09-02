import type { ZodIssue, ZodType } from 'zod';
import { AdminDiscoveryPayloadValidationError } from './admin-discovery.error.js';
import type {
  AdminDiscoveryPayloadValidationResultType,
  IAdminDiscoveryPayload,
  IAdminDiscoveryPayloadValidationIssue,
  IAdminDiscoveryPayloadValidator,
  IAdminExtensionDescriptorMetadata,
} from './admin-discovery.interfaces.js';
import { AdminDiscoveryPayloadSchema } from './admin-discovery.schema.js';

/**
 * @alpha
 * The default implementation of admin discovery payload validation.
 */
export class AdminDiscoveryPayloadValidator implements IAdminDiscoveryPayloadValidator {
  constructor(
    protected readonly payloadSchema: ZodType<IAdminDiscoveryPayload> = AdminDiscoveryPayloadSchema,
  ) {}

  validate(payload: unknown): AdminDiscoveryPayloadValidationResultType {
    const schemaResult = this._validatePayloadSchema(
      this.payloadSchema,
      payload,
    );

    if (!schemaResult.success) {
      return schemaResult;
    }

    const semanticIssues = this._validatePayloadSemantics(schemaResult.payload);

    if (semanticIssues.length) {
      return {
        success: false,
        error: new AdminDiscoveryPayloadValidationError(semanticIssues),
      };
    }

    return schemaResult;
  }

  parse(payload: unknown): IAdminDiscoveryPayload {
    const result = this.validate(payload);

    if (!result.success) {
      throw result.error;
    }

    return result.payload;
  }

  protected _toPayloadValidationIssue(
    issue: ZodIssue,
  ): IAdminDiscoveryPayloadValidationIssue {
    return {
      code: issue.code,
      message: issue.message,
      path: !issue.path.length ? '$' : issue.path.join('.'),
    };
  }

  protected _collectDuplicates(values: readonly string[]): string[] {
    const seen = new Set<string>();
    const duplicates = new Set<string>();

    for (const value of values) {
      if (seen.has(value)) {
        duplicates.add(value);
        continue;
      }

      seen.add(value);
    }

    return [...duplicates];
  }

  protected _validatePayloadSchema(
    payloadSchema: ZodType<IAdminDiscoveryPayload>,
    payload: unknown,
  ): AdminDiscoveryPayloadValidationResultType {
    const parsed = payloadSchema.safeParse(payload);

    if (!parsed.success) {
      const issues = parsed.error.issues.map((issue) =>
        this._toPayloadValidationIssue(issue),
      );

      return {
        success: false,
        error: new AdminDiscoveryPayloadValidationError(issues),
      };
    }

    return {
      success: true,
      payload: parsed.data,
    };
  }

  protected _validatePayloadSemantics(
    payload: IAdminDiscoveryPayload,
  ): IAdminDiscoveryPayloadValidationIssue[] {
    const issues: IAdminDiscoveryPayloadValidationIssue[] = [];
    const pluginIds = payload.plugins.map((plugin) => plugin.id);

    this._appendDuplicateIssues(
      issues,
      'plugins',
      'duplicate_plugin',
      pluginIds,
    );

    for (const [pluginIndex, plugin] of payload.plugins.entries()) {
      this._validatePluginDescriptors(
        issues,
        `plugins.${pluginIndex.toString()}`,
        plugin.id,
        [
          ...plugin.extensions.navigation,
          ...plugin.extensions.pages,
          ...plugin.extensions.widgets,
          ...plugin.extensions.actions,
        ],
      );
    }

    return issues;
  }

  protected _validatePluginDescriptors(
    issues: IAdminDiscoveryPayloadValidationIssue[],
    pluginPath: string,
    pluginId: string,
    descriptors: readonly IAdminExtensionDescriptorMetadata[],
  ): void {
    const descriptorIds = descriptors.map((descriptor) => descriptor.id);

    this._appendDuplicateIssues(
      issues,
      `${pluginPath}.extensions`,
      'duplicate_extension_descriptor',
      descriptorIds,
    );

    for (const descriptor of descriptors) {
      if (descriptor.pluginId !== pluginId) {
        issues.push({
          code: 'descriptor_plugin_mismatch',
          message: `Descriptor "${descriptor.id}" references plugin "${descriptor.pluginId}" but belongs to plugin "${pluginId}".`,
          path: `${pluginPath}.extensions`,
        });
      }
    }
  }

  protected _appendDuplicateIssues(
    issues: IAdminDiscoveryPayloadValidationIssue[],
    path: string,
    code: string,
    values: readonly string[],
  ): void {
    const duplicateValues = this._collectDuplicates(values);

    for (const value of duplicateValues) {
      issues.push({
        code,
        message: `Value "${value}" is declared more than once.`,
        path,
      });
    }
  }
}
