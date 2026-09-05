# Verify and Fix JARVIS Web Project Integration with Ollama

## User Review Required
> [!IMPORTANT]
> The current `js/app.js` does **not** import or use the newly added `js/ollama_client.js`. This means the UI never reaches the Ollama streaming path, leading to a broken or missing LLM response.
>
> We will modify `js/app.js` to integrate the Ollama client, adjust the chat flow to use streaming responses, and update UI helpers for streaming bubbles. These changes affect core interaction logic, so please review the plan before we apply them.

## Open Questions
- Do you want to keep the existing fallback to the Memory Vault summary when Ollama is offline, or always require Ollama?
- Any preference for the model selection order beyond the default `MODEL_PREFERENCE` list?
- Should the UI display a loading spinner while waiting for Ollama detection on startup?

## Proposed Changes
---
### Frontend Integration
#### [MODIFY] [app.js](workspaces/jarvis_web/js/app.js)
- Add `import OllamaClient from './ollama_client.js';`
- Instantiate `this.ollama = new OllamaClient();` in initialization code.
- In `init()` (or after page load) call `this.ollama.detectAndConnect()` and update UI status.
- Replace `sendChat` logic with a new method `processQuery(message)` that:
  1. Retrieves memory via Vault as before.
  2. If `this.ollama.isOnline` use `this.ollama.chat` with streaming callbacks to update chat UI token‑by‑token.
  3. Else fallback to existing `request(SUPERVISOR_API, ...)` path.
- Add helper methods `_appendStreamingMessage`, `_updateStreamingMessage`, `_finalizeStreamingMessage` to manage streaming bubbles (similar to earlier summary).
- Adjust voice output to use streamed reply text.

#### [NEW] [ollama_client.js](workspaces/jarvis_web/js/ollama_client.js)
- Ensure file exists (it was created earlier). Verify its API: `detectAndConnect()`, `chat(userMessage, vaultContext, callbacks)`.
- If missing, we will add a minimal implementation based on previous summary.

### UI Styling
#### [MODIFY] [style.css](workspaces/jarvis_web/style.css)
- Add/adjust CSS rules for `.chat-message.streaming` and cursor animation if not present.

### Verification Plan
- Run the server (`node server.cjs`).
- Open `http://127.0.0.1:3000` in a browser.
- Confirm Ollama detection banner appears.
- Submit a chat query and verify streaming text appears token‑by‑token.
- Check console for any errors, network tab for `/api/chat` streaming response.
- Verify fallback works when Ollama is stopped.

## Automated Tests
- No unit tests exist for frontend; we will perform manual verification steps.
- Optionally add a simple Cypress test that types a message and ensures a streaming bubble appears.

---
