import { groq, GROQ_MODELS, isGroqAvailable } from './groq';
import { GoogleGenerativeAI } from '@google/generative-ai';

const geminiApiKey = process.env.GEMINI_API_KEY;
const gemini = geminiApiKey ? new GoogleGenerativeAI(geminiApiKey) : null;

export interface LLMOptions {
  systemPrompt: string;
  userPrompt: string;
  jsonMode?: boolean;
  maxTokens?: number;
  temperature?: number;
}

export interface LLMResponse {
  content: string;
  provider: 'groq' | 'gemini' | 'fallback';
  model: string;
}

/**
 * Call LLM with automatic fallback chain:
 * 1. Groq Llama 3.3 70B (fastest, highest quality free)
 * 2. Google Gemini 2.0 Flash (1M context, good for long analysis)
 * 3. Minimal fallback response (never crash the UI)
 */
export async function callLLM(opts: LLMOptions): Promise<LLMResponse> {
  const {
    systemPrompt,
    userPrompt,
    jsonMode = false,
    maxTokens = 1024,
    temperature = 0.3,
  } = opts;

  // Try Groq first
  if (isGroqAvailable() && groq) {
    try {
      const response = await groq.chat.completions.create({
        model: GROQ_MODELS.LLM,
        messages: [
          { role: 'system', content: systemPrompt },
          { role: 'user', content: userPrompt },
        ],
        temperature,
        max_tokens: maxTokens,
        response_format: jsonMode ? { type: 'json_object' } : undefined,
      });

      const content = response.choices[0]?.message?.content || '';
      return {
        content,
        provider: 'groq',
        model: GROQ_MODELS.LLM,
      };
    } catch (error: unknown) {
      const err = error as { status?: number; message?: string };
      console.warn('[LLM] Groq failed, falling back to Gemini:', err.message);
      // Fall through to Gemini
    }
  }

  // Try Gemini
  if (gemini) {
    try {
      const model = gemini.getGenerativeModel({
        model: 'gemini-2.0-flash',
        generationConfig: {
          temperature,
          maxOutputTokens: maxTokens,
          responseMimeType: jsonMode ? 'application/json' : 'text/plain',
        },
      });

      const result = await model.generateContent(
        `${systemPrompt}\n\n${userPrompt}`
      );
      const content = result.response.text();

      return {
        content,
        provider: 'gemini',
        model: 'gemini-2.0-flash',
      };
    } catch (error: unknown) {
      const err = error as { message?: string };
      console.warn('[LLM] Gemini failed:', err.message);
    }
  }

  // Minimal fallback - return empty JSON or explanation
  return {
    content: jsonMode
      ? JSON.stringify({
          error: 'AI service unavailable',
          instrument: null,
          direction: null,
          setup: null,
          emotions: [],
          mistakes: [],
          lesson: null,
          rMultipleEstimate: null,
          confidence: 0,
          summary: 'AI analysis not available right now.',
        })
      : 'AI service temporarily unavailable. Please try again.',
    provider: 'fallback',
    model: 'none',
  };
}
