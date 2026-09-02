/**
 * @alpha
 * Standardized failure code taxonomy for CI consumers.
 * CT – Contract Test.
 */
export enum ContractFailureCodes {
  ManifestSchemaInvalid = 'CT_MANIFEST_SCHEMA_INVALID',
  ManifestSemanticInvalid = 'CT_MANIFEST_SEMANTIC_INVALID',
  LifecycleMethodMissing = 'CT_LIFECYCLE_METHOD_MISSING',
  LifecycleMethodFailed = 'CT_LIFECYCLE_METHOD_FAILED',
}
