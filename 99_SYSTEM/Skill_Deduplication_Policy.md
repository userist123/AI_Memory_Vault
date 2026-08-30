# Skill Deduplication Policy

## Purpose
Keep skill bodies focused on domain capability while moving repeated runtime infrastructure into canonical system contracts.

## Remove from skill bodies when duplicated
- Council routing rules
- Global memory-loading rules
- Obsidian navigation instructions
- Master catalog references
- Council map references
- Generic validation rules already defined by the runtime contract
- Generic output formatting rules that are not domain-specific

## Preserve
- Domain-specific procedures
- Domain-specific constraints
- Tool usage specific to the capability
- Examples that materially improve execution
- Safety/security constraints specific to the skill
- Failure modes and verification steps specific to the skill

## Canonical runtime references
The following are authoritative and should not be copied into every skill:
- `99_SYSTEM/Council_Runtime_Profile.yaml`
- `99_SYSTEM/Council_Context_Budget.md`
- `99_SYSTEM/Council_Context_Protocol.md`
- `99_SYSTEM/Skill_Runtime_Manifest.md`
- `99_SYSTEM/Agent_Capability_Registry.md`

## Important
This policy is a normalization rule, not permission to delete useful knowledge. Before compression, preserve the original skill content in Git history and verify that the resulting skill still contains its complete domain capability.
