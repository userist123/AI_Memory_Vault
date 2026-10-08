---
type: index
category: navigation
status: active
title: Jarvis Web Command Center Specification Stub
---

# Jarvis Web Command Center Specification

Canonical documentation has moved to [[Jarvis_Web_Command_Center_Specification]].

## API token

The memory gateway (`03_IMPLEMENTATION/packages/interfaces/api_server.py`) answers `401` on every
`/api/v1/*` route except `/status` unless the request carries
`Authorization: Bearer <AI_MEMORY_VAULT_API_TOKEN>`, and refuses everything while that variable is
unset on the server. The token is never part of this page's source:

- start the gateway with `AI_MEMORY_VAULT_API_TOKEN` set to a secret of your choosing;
- the page asks for it once (a prompt, after the first `401`) and keeps it in `sessionStorage` for
  that browser tab only; `new VaultClient({token})`, `new VaultClient({getToken})` and
  `client.setToken(token)` do the same for the `VaultClient` class (`js/vault_client.js`);
- the token is sent to the gateway and to no other origin, and not at all to `/status`.

`test/test_vault_client_auth.js` covers the client; `test/api_smoke_test.py` covers the gateway side.
