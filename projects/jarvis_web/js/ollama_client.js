/**
 * JARVIS — Ollama Local LLM Brain
 * File: projects/jarvis_web/js/ollama_client.js
 *
 * Connects JARVIS to a local Ollama instance (http://localhost:11434).
 * Zero API keys. 100% offline. Streams responses via ReadableStream.
 *
 * Models preference order (auto-detected):
 *   1. gemma4:26b (best reasoning + thinking)
 *   2. gemma4:26b-64k
 *   3. qwen2.5-coder:7b (fast, good general)
 *   4. qwen2.5-coder:3b (fastest, light)
 *   any other model found
 */

const OLLAMA_BASE = 'http://localhost:11434';

// Model preference for conversational JARVIS
const MODEL_PREFERENCE = [
  'gemma4:26b',
  'gemma4:26b-64k',
  'glm-4.7-flash:latest',
  'qwen3-coder:30b',
  'qwen2.5-coder:7b',
  'qwen2.5-coder:3b',
];

const JARVIS_SYSTEM_PROMPT = `You are JARVIS — a highly intelligent AI assistant, exactly like the one from Iron Man. 
You assist your user (referred to as "sir" or by name) with intelligence, wit, and precision.
You have access to an AI Memory Vault containing curated knowledge notes.

Rules:
- Be concise and direct. No fluff.
- Respond in the same language the user speaks (Romanian or English).
- If the user speaks Romanian, respond entirely in Romanian.
- If context notes are provided, cite them naturally in your answer.
- If you don't know something, say so honestly — don't hallucinate.
- Keep responses under 3 sentences for simple questions, more for complex ones.
- Occasionally add dry wit, like the real JARVIS would.
- Never break character.`;

export class OllamaClient {
  constructor(options = {}) {
    this.baseUrl = options.baseUrl || OLLAMA_BASE;
    this.model = options.model || null; // auto-detect
    this.availableModels = [];
    this.isOnline = false;
    this.conversationHistory = [];
    this.maxHistory = options.maxHistory || 10;
    this._detectPromise = null;
  }

  /**
   * Check if Ollama is running and detect best available model.
   * @returns {Promise<boolean>}
   */
  async detectAndConnect() {
    if (this._detectPromise) return this._detectPromise;
    this._detectPromise = this._doDetect();
    return this._detectPromise;
  }

  async _doDetect() {
    try {
      const res = await fetch(`${this.baseUrl}/api/tags`, {
        signal: AbortSignal.timeout(2000)
      });
      if (!res.ok) { this.isOnline = false; return false; }

      const data = await res.json();
      this.availableModels = (data.models || []).map(m => m.name);

      // Pick best model
      if (!this.model) {
        for (const preferred of MODEL_PREFERENCE) {
          if (this.availableModels.includes(preferred)) {
            this.model = preferred;
            break;
          }
        }
        // fallback: any model
        if (!this.model && this.availableModels.length > 0) {
          this.model = this.availableModels[0];
        }
      }

      this.isOnline = !!this.model;
      console.info(`[OllamaClient] Connected. Model: ${this.model || 'none'}`);
      return this.isOnline;
    } catch {
      this.isOnline = false;
      return false;
    }
  }

  /**
   * Generate a streaming AI response.
   * @param {string} userMessage
   * @param {Array<object>} vaultContext - Notes from Memory Vault
   * @param {object} options
   * @param {function(string):void} options.onToken - Called with each text token
   * @param {function(string):void} options.onDone - Called with full response
   * @param {function(Error):void} options.onError - Called on failure
   */
  async chat(userMessage, vaultContext = [], { onToken, onDone, onError } = {}) {
    if (!this.isOnline) {
      const err = new Error('Ollama offline');
      if (onError) onError(err);
      return;
    }

    // Build context snippet from vault results
    let contextBlock = '';
    if (vaultContext.length > 0) {
      const snippets = vaultContext.slice(0, 3).map((n, i) => {
        const title = n.title || n.id || `Note ${i + 1}`;
        const summary = (n.summary || n.content || '').substring(0, 200);
        return `[${i + 1}] ${title}: ${summary}`;
      });
      contextBlock = `\n\nRelevant Memory Vault context:\n${snippets.join('\n')}`;
    }

    const fullUserMessage = userMessage + contextBlock;

    // Maintain rolling conversation history
    this.conversationHistory.push({ role: 'user', content: fullUserMessage });
    if (this.conversationHistory.length > this.maxHistory * 2) {
      // Keep system + last N turns
      this.conversationHistory = this.conversationHistory.slice(-this.maxHistory * 2);
    }

    const messages = [
      { role: 'system', content: JARVIS_SYSTEM_PROMPT },
      ...this.conversationHistory
    ];

    try {
      const res = await fetch(`${this.baseUrl}/api/chat`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          model: this.model,
          messages,
          stream: true,
          options: {
            temperature: 0.7,
            num_predict: 300,    // max tokens in response
            top_p: 0.9,
          }
        }),
        signal: AbortSignal.timeout(30000)
      });

      if (!res.ok) {
        throw new Error(`Ollama HTTP ${res.status}`);
      }

      const reader = res.body.getReader();
      const decoder = new TextDecoder();
      let fullResponse = '';

      while (true) {
        const { done, value } = await reader.read();
        if (done) break;

        const chunk = decoder.decode(value, { stream: true });
        const lines = chunk.split('\n').filter(l => l.trim());

        for (const line of lines) {
          try {
            const parsed = JSON.parse(line);
            if (parsed.message?.content) {
              const token = parsed.message.content;
              fullResponse += token;
              if (onToken) onToken(token);
            }
            if (parsed.done) {
              // Remove context block from history (keep only user intent)
              this.conversationHistory[this.conversationHistory.length - 1].content = userMessage;
              // Add assistant response to history
              this.conversationHistory.push({ role: 'assistant', content: fullResponse });
              if (onDone) onDone(fullResponse);
              return;
            }
          } catch { /* skip malformed JSON line */ }
        }
      }

      if (onDone) onDone(fullResponse);

    } catch (err) {
      console.error('[OllamaClient] Chat error:', err);
      // Remove the failed message from history
      this.conversationHistory.pop();
      if (onError) onError(err);
    }
  }

  /**
   * Clear conversation memory.
   */
  clearHistory() {
    this.conversationHistory = [];
  }

  /**
   * Get status info for diagnostics.
   */
  getStatus() {
    return {
      online: this.isOnline,
      model: this.model,
      availableModels: this.availableModels,
      historyLength: this.conversationHistory.length,
    };
  }
}
