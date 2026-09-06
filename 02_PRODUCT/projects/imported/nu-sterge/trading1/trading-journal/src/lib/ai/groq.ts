import Groq from 'groq-sdk';

const groqApiKey = process.env.GROQ_API_KEY;

if (!groqApiKey && process.env.NODE_ENV === 'production') {
  console.error('[AI] GROQ_API_KEY not set in production');
}

export const groq = groqApiKey
  ? new Groq({ apiKey: groqApiKey })
  : null;

export const GROQ_MODELS = {
  // Whisper Large v3 - best accuracy, supports Romanian natively
  // Free tier: 20 RPM, 2000 RPD, 7200 seconds audio/hour
  WHISPER: 'whisper-large-v3',

  // Whisper Turbo - 216x real-time, slightly lower accuracy
  WHISPER_TURBO: 'whisper-large-v3-turbo',

  // Llama 3.3 70B - best free LLM for structured output
  // Free tier: 30 RPM, 1000 RPD, 12000 TPM, 500 max output tokens
  LLM: 'llama-3.3-70b-versatile',

  // Fallback: smaller but faster
  LLM_FAST: 'llama-3.1-8b-instant',
} as const;

export function isGroqAvailable(): boolean {
  return groq !== null;
}
