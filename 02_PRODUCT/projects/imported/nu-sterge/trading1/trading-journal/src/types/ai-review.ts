import { z } from 'zod';

// ===== AI Trade Review =====

export const TradeReviewSchema = z.object({
  tradeId: z.string(),
  userId: z.string(),
  language: z.enum(['ro', 'en']),
  createdAt: z.date(),

  // Overall assessment
  grade: z.enum(['A', 'B', 'C', 'D', 'F']).describe('Trade quality grade'),
  score: z.number().min(0).max(100).describe('Overall score 0-100'),

  // Key findings
  strengths: z.array(z.string()).describe('What was done well'),
  weaknesses: z.array(z.string()).describe('What could be improved'),

  // Specific analysis
  entryQuality: z.enum(['excellent', 'good', 'neutral', 'poor', 'bad']),
  exitQuality: z.enum(['excellent', 'good', 'neutral', 'poor', 'bad']),
  riskManagement: z.enum(['excellent', 'good', 'neutral', 'poor', 'bad']),

  // Pattern flags
  flags: z.array(z.enum([
    'no_stop_loss',
    'oversized_position',
    'tight_stop',
    'wide_stop',
    'held_too_long',
    'cut_profits_early',
    'revenge_trade',
    'overtrading',
    'counter_trend',
    'chased_entry',
    'good_rr_ratio',
    'disciplined_exit',
    'strong_setup',
  ])),

  // Recommendations
  recommendations: z.array(z.string()).describe('Concrete actionable advice'),

  // Narrative
  summary: z.string().describe('2-3 sentence executive summary'),

  // Metadata
  provider: z.string(),
  model: z.string(),
});

export type TradeReview = z.infer<typeof TradeReviewSchema>;

// ===== Weekly Coach Report =====

export const CoachReportSchema = z.object({
  _id: z.string().optional(),
  userId: z.string(),
  language: z.enum(['ro', 'en']),
  createdAt: z.date(),

  // Period
  periodStart: z.date(),
  periodEnd: z.date(),
  periodType: z.enum(['day', 'week', 'month']).default('week'),

  // Stats snapshot
  stats: z.object({
    totalTrades: z.number(),
    closedTrades: z.number(),
    winRate: z.number(),
    totalPnL: z.number(),
    profitFactor: z.number(),
    avgWin: z.number(),
    avgLoss: z.number(),
    maxDrawdown: z.number(),
    avgRMultiple: z.number().nullable(),
    bestTrade: z.number().nullable(),
    worstTrade: z.number().nullable(),
  }),

  // Overall assessment
  grade: z.enum(['A', 'B', 'C', 'D', 'F']),
  momentum: z.enum(['improving', 'stable', 'declining']),

  // Behavioral patterns identified
  patterns: z.array(z.object({
    type: z.enum([
      'revenge_trading',
      'overtrading',
      'position_sizing_drift',
      'time_of_day_edge',
      'symbol_bias',
      'direction_bias',
      'tilt_detected',
      'risk_management_slip',
      'profitability_by_setup',
      'consistency',
    ]),
    severity: z.number().min(1).max(5),
    description: z.string(),
    evidence: z.string().describe('Specific data points'),
  })),

  // Key insights
  strengths: z.array(z.string()),
  weaknesses: z.array(z.string()),

  // Action plan for next period
  actionPlan: z.array(z.object({
    priority: z.enum(['critical', 'high', 'medium', 'low']),
    action: z.string(),
    rationale: z.string(),
  })),

  // Narrative
  summary: z.string(),
  headline: z.string().describe('One-line headline for the week'),

  // Metadata
  provider: z.string(),
  model: z.string(),
});

export type CoachReport = z.infer<typeof CoachReportSchema>;

// API schemas
export const ReviewRequestSchema = z.object({
  tradeId: z.string(),
  language: z.enum(['ro', 'en']).default('ro'),
});

export const CoachRequestSchema = z.object({
  language: z.enum(['ro', 'en']).default('ro'),
  periodDays: z.number().min(1).max(90).default(7),
});
