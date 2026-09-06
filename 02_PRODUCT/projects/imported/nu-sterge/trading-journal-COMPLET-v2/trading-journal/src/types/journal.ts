import { z } from 'zod';

// ===== Voice Journal Entry =====

export const EmotionSchema = z.enum([
  'confident',
  'fearful',
  'greedy',
  'calm',
  'frustrated',
  'excited',
  'patient',
  'impulsive',
  'disciplined',
  'tilted',
]);

export type Emotion = z.infer<typeof EmotionSchema>;

export const MistakeSchema = z.enum([
  'revengeTrade',
  'oversized',
  'noStopLoss',
  'movedStopLoss',
  'fomo',
  'overTrading',
  'againstTrend',
  'ignoredPlan',
  'tookProfitsEarly',
  'heldTooLong',
]);

export type Mistake = z.infer<typeof MistakeSchema>;

export const DirectionSchema = z.enum(['long', 'short']);
export type Direction = z.infer<typeof DirectionSchema>;

// AI-extracted structured data from voice journal
export const JournalExtractionSchema = z.object({
  instrument: z.string().nullable().describe('Trading instrument/symbol mentioned'),
  direction: DirectionSchema.nullable().describe('Trade direction if mentioned'),
  setup: z.string().nullable().describe('Trading setup/pattern described'),
  emotions: z.array(EmotionSchema).describe('Emotions felt before/during trade'),
  mistakes: z.array(MistakeSchema).describe('Mistakes identified in the trade'),
  lesson: z.string().nullable().describe('Main lesson learned, in original language'),
  rMultipleEstimate: z.number().nullable().describe('R-multiple if mentioned (e.g. 2R win, -1R loss)'),
  confidence: z.number().min(0).max(1).describe('AI confidence in extraction 0-1'),
  summary: z.string().describe('One-sentence summary of the journal entry'),
});

export type JournalExtraction = z.infer<typeof JournalExtractionSchema>;

// Full journal entry stored in DB
export const JournalEntrySchema = z.object({
  _id: z.string().optional(),
  userId: z.string(),
  tradeId: z.string().nullable().optional(),
  createdAt: z.date(),
  updatedAt: z.date(),

  // Voice recording metadata
  audioUrl: z.string().nullable().optional(),
  audioDurationSec: z.number().nullable().optional(),
  language: z.enum(['ro', 'en']),

  // Transcription
  transcript: z.string(),
  transcriptConfidence: z.number().nullable().optional(),

  // AI extraction
  extraction: JournalExtractionSchema.nullable().optional(),

  // Manual fields
  tags: z.array(z.string()).default([]),
  notes: z.string().nullable().optional(),
});

export type JournalEntry = z.infer<typeof JournalEntrySchema>;

// API request/response types
export const TranscribeRequestSchema = z.object({
  language: z.enum(['ro', 'en']).default('ro'),
});

export const AnalyzeRequestSchema = z.object({
  transcript: z.string().min(5).max(10000),
  language: z.enum(['ro', 'en']).default('ro'),
});
