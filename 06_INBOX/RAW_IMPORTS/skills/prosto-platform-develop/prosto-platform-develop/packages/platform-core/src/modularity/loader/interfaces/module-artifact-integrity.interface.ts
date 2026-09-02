/**
 * @alpha
 * Interface for module artifact integrity information.
 */
export interface IModuleArtifactIntegrity {
  readonly checksum?: string;
  readonly signature?: string;
}
