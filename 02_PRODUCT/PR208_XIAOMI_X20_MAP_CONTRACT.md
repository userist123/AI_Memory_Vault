# PR #208 — Xiaomi X20+ Map Research Contract

## Scope

PR #208 establishes the Memory Vault contract for researching and editing Xiaomi X20+ maps.

The initial implementation is strictly read-only. It may acquire, decode, validate,
render, compare, and export map data, but it MUST NOT send a mutation command to the
robot.

## Device

Target model: Xiaomi X20+ / MIoT model identifier `xiaomi.vacuum.c102gl`.

## Read-only data contract

The acquisition layer may work with these documented map-related properties:

- `map-data`
- `frame-info`
- `map-extend-data`
- `object-name`
- `map-req`

The presence of a property is not proof that arbitrary edited map data can be written
back to the device.

## Canonical pipeline

```
device -> read-only acquisition -> immutable raw artifact
       -> decoder -> normalized geometry -> validator
       -> editor/importer -> candidate map -> validator
       -> diff/backup/export
```

## Mutation boundary

The following are outside the initial PR-208 trust boundary:

- `update-map`
- robot movement/control commands
- firmware modification
- root access
- arbitrary cloud/device configuration changes

Any future mutation-capable integration MUST use the repository owner-authority
mechanism established by the security work and require explicit owner approval.

## Artifact requirements

For every acquired map, retain enough provenance to reproduce the transformation:

- original raw payload
- acquisition metadata
- decoder version
- normalized representation
- validation report
- content hashes

Edited maps must remain separate from the original artifact.

## Safety invariants

1. Raw acquired data is immutable.
2. Decoder failures fail closed; they do not produce a write candidate.
3. Validation failures cannot enter a mutation path.
4. No network write is performed by read-only components.
5. Unsupported/unknown fields are preserved or rejected, never silently discarded.
6. A future write path must be opt-in and owner-authorized.

## Initial acceptance criteria

- [ ] Contract is versioned in the repository.
- [ ] Read-only acquisition has no write primitive.
- [ ] Raw artifacts can be hashed and traced.
- [ ] Decoder output has a versioned schema.
- [ ] Invalid geometry is rejected.
- [ ] Tests prove that mutation commands are unavailable from the read-only API.
