# @prosto/platform-admin-contracts

Contract authority for Prosto platform admin shell and UI plugin integration.

## Status
- Phase 07 baseline completed
- Phase 09 integration baseline completed
- Local authentication Phase 1 contracts completed
- All exported contracts are marked `@alpha`

## Public API

### Constants
- `ADMIN_CONTRACT_VERSION`
- `ADMIN_UI_PLUGIN_MANIFEST_SCHEMA_VERSION`
- `ADMIN_UI_PLUGIN_TRUST_CLASSES`
- `ADMIN_UI_PLUGIN_REVIEW_STATUSES`
- `ADMIN_UI_PLUGIN_EXTENSION_POINTS`
- `ADMIN_UI_PLUGIN_CAPABILITIES`
- `ADMIN_DISCOVERY_PAYLOAD_SCHEMA_VERSION`
- `ADMIN_DISCOVERY_REJECTION_REASON_CODES`
- `ADMIN_DISCOVERY_EXTENSION_KINDS`
- `ADMIN_DISCOVERY_COMPONENT_KEYS`
- `ADMIN_DISCOVERY_WIDGET_SLOTS`
- `ADMIN_DISCOVERY_ACTION_TARGETS`
- `ADMIN_PERMISSION_POLICY_SCHEMA_VERSION`
- `ADMIN_PERMISSION_TOKENS`
- `ADMIN_ROLE_IDENTIFIERS`
- `ADMIN_ACTION_IDENTIFIERS`
- `ADMIN_PERMISSION_MATCH_STRATEGIES`
- `ADMIN_ACTION_GATING_EFFECTS`
- `ADMIN_COMPATIBILITY_CONTRACT_VERSION`
- `ADMIN_COMPATIBILITY_REASON_CODES`
- `ADMIN_AUTHENTICATION_API_SCHEMA_VERSION`
- `ADMIN_AUTHENTICATION_MODES`
- `ADMIN_AUTHENTICATION_SESSION_STATES`
- `ADMIN_AUTHENTICATION_API_ROUTES`
- `ADMIN_AUTHENTICATION_FAILURE_CODES`
- `ADMIN_AUTHENTICATION_MINIMUM_PASSWORD_LENGTH`
- `ADMIN_AUTHENTICATION_MAXIMUM_PASSWORD_LENGTH`
- `ADMIN_AUTHENTICATION_MAXIMUM_USERNAME_LENGTH`
- `CAPABILITY_METADATA_KEY`
- `PERMISSION_METADATA_KEY`

### Types
- `AdminUIPluginIdentifierType`
- `AdminUIPluginVersionType`
- `AdminUIPluginManifestSchemaVersionType`
- `AdminUIPluginTrustClassType`
- `AdminUIPluginReviewStatusType`
- `AdminUIPluginExtensionPointType`
- `AdminUIPluginCapabilityType`
- `AdminUIPluginPermissionType`
- `AdminShellCompatibilityRangeType`
- `AdminDiscoveryPayloadSchemaVersionType`
- `AdminDiscoveryDescriptorIdentifierType`
- `AdminDiscoveryRejectionReasonCodeType`
- `AdminDiscoveryExtensionKindType`
- `AdminDiscoveryComponentKeyType`
- `AdminDiscoveryWidgetSlotType`
- `AdminDiscoveryActionTargetType`
- `AdminDiscoveryRouteType`
- `AdminPermissionPolicySchemaVersionType`
- `AdminPermissionTokenType`
- `AdminRoleIdentifierType`
- `AdminActionIdentifierType`
- `AdminPermissionMatchStrategyType`
- `AdminActionGatingEffectType`
- `AdminPluginCompatibilityReasonCodeType`
- `AdminCompatibilityContractVersionType`
- `AdminUIPluginManifestValidationResultType`
- `AdminDiscoveryPayloadValidationResultType`
- `AdminPermissionPolicyValidationResultType`
- `AdminPluginCompatibilityResultType`
- `AdminAuthenticationApiSchemaVersionType`
- `AdminAuthenticationModeType`
- `AdminAuthenticationSessionStateType`
- `AdminAuthenticationFailureCodeType`
- `AdminAuthenticationSessionResponseType`

### Interfaces
- `IAdminUIPluginIdentity`
- `IAdminUIPluginShellCompatibility`
- `IAdminUIPluginAccessRequirements`
- `IAdminUIPluginReviewMetadata`
- `IAdminUIPluginManifest`
- `IAdminUIPluginManifestValidationIssue`
- `IAdminUIPluginManifestValidationSuccess`
- `IAdminUIPluginManifestValidationFailure`
- `IAdminUIPluginManifestValidator`
- `IAdminExtensionDescriptorMetadata`
- `IAdminNavigationExtensionDescriptor`
- `IAdminPageExtensionDescriptor`
- `IAdminWidgetExtensionDescriptor`
- `IAdminActionExtensionDescriptor`
- `IAdminPluginDiscoveryExtensions`
- `IAdminDiscoveredPluginDescriptor`
- `IAdminRejectedPluginDiagnostic`
- `IAdminDiscoveryPayload`
- `IAdminDiscoveryPayloadValidationIssue`
- `IAdminDiscoveryPayloadValidationSuccess`
- `IAdminDiscoveryPayloadValidationFailure`
- `IAdminDiscoveryPayloadValidator`
- `IAdminRolePermissionMapping`
- `IAdminActionGatePolicy`
- `IAdminPermissionPolicy`
- `IAdminActionGateEvaluationContext`
- `IAdminActionGateDecision`
- `IAdminPermissionPolicyValidationIssue`
- `IAdminPermissionPolicyValidationSuccess`
- `IAdminPermissionPolicyValidationFailure`
- `IAdminPermissionPolicyValidator`
- `IAdminActionGateEvaluator`
- `IAdminPluginCompatibilityInput`
- `IAdminPluginCompatibilityAllowedResult`
- `IAdminPluginCompatibilityRejectedResult`
- `IAdminPluginCompatibilityEvaluator`
- `IAdminAuthenticationPayload`
- `IAdminLocalAuthenticationSessionResponse`
- `IAdminOidcAnonymousAuthenticationSessionResponse`
- `IAdminOidcAuthenticatedAuthenticationSessionResponse`
- `IAdminAuthenticationLoginRequest`
- `IAdminAuthenticationLoginResponse`
- `IAdminAuthenticationChangePasswordRequest`
- `IAdminAuthenticationChangePasswordResponse`
- `IAdminAuthenticationLogoutRequest`
- `IAdminAuthenticationLogoutResponse`
- `IAdminAuthenticationFailureResponse`
- `IAdminAuthenticationValidationIssue`

### Schemas
- `SemverVersionSchema`
- `SemverRangeSchema`
- `CapabilitySchema`
- `ModuleDependencySchema`
- `PlatformModuleManifestSchema`
- `AdminUIPluginManifestSchema`
- `AdminDiscoveryPayloadSchema`
- `AdminPermissionPolicySchema`
- `AdminAuthenticationApiSchemaVersionSchema`
- `AdminAuthenticationUsernameSchema`
- `AdminAuthenticationPasswordSchema`
- `AdminAuthenticationSessionResponseSchema`
- `AdminAuthenticationLoginRequestSchema`
- `AdminAuthenticationLoginResponseSchema`
- `AdminAuthenticationChangePasswordRequestSchema`
- `AdminAuthenticationChangePasswordResponseSchema`
- `AdminAuthenticationLogoutRequestSchema`
- `AdminAuthenticationLogoutResponseSchema`
- `AdminAuthenticationFailureResponseSchema`

### Validators
- `PlatformModuleManifestValidator`
- `PlatformModuleCompatibilityValidator`
- `AdminUIPluginManifestValidator`
- `AdminDiscoveryPayloadValidator`
- `AdminPermissionPolicyValidator`
- `AdminPluginCompatibilityEvaluator`
- `AdminActionGateEvaluator`
- `AdminAuthenticationContractValidator`

### Utilities
- `convertDescriptorToManifest`

### Errors
- `AdminAuthenticationValidationError`

## Commands
- `npm run --workspace @prosto/platform-admin-contracts build`
- `npm run --workspace @prosto/platform-admin-contracts typecheck`
- `npm run --workspace @prosto/platform-admin-contracts test`

## Notes
- This package is contract-only and does not implement runtime module loading or UI rendering.
- Admin BFF adapter and admin shell runtime depend on this package for type-safe contracts.
