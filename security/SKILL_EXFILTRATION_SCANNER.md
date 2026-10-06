# Skill & Prompt Exfiltration Scanner

Read-only static analysis for the AI Memory Vault.

## Purpose

Identifies evidence of browser/session credential access, secret/environment access,
file collection, network requests/uploads/webhooks, prompt override, concealment,
and external destinations.

Educational examples are retained as evidence but are not treated as active
exfiltration instructions solely because they contain phrases such as
"Ignore all previous instructions".

## Provenance

When scanning a skill directory, PROVENANCE.json is read and copied into the result.
The scanner does not execute, import or install the scanned skill.

## Verdicts

SAFE = no active indicator.
REVIEW = active indicator exists without a complete data-access-to-network chain.
BLOCK = active data-access evidence plus active network/external-destination evidence.

A static verdict is not proof that exfiltration actually occurred.

## Usage

python -m security.skill_exfiltration_scanner .agents/skills --json

## Limitations

This version does not execute prompts, inspect live traffic, decrypt browser stores,
or establish that a request actually occurred. Runtime evidence remains separate.
