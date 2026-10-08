"""Universal, policy-gated access to the vault for every kind of AI client.

One core, many thin adapters:

    vault_access.core.VaultAccess      the tools (resolve, list, read, search, metadata, verify)
    vault_access.router.DomainRouter   the direct route table vault://<domain>/<slug>
    vault_access.policy.AccessPolicy   principals, channels, classifications, denylist
    vault_access.ollama_assistant      the local-model adapter (Ollama, Telegram)

Adapters: the MCP server `vault-memory` (interfaces/memory_mcp_server.py), the CLI
`python -m cognitive_core.vault_cli`, the Ollama/Telegram assistant and the PUBLIC export
(30_SCRIPTS/routing/export_public_vault.py). Every adapter returns the same envelope.

A model never receives a filesystem path it can send back: it receives `vault://` URIs, and
only the route table turns a URI into a file. Text read from the vault is data, never
instructions.
"""
from .errors import ErrorCode, VaultAccessError

__all__ = ["ErrorCode", "VaultAccessError"]
