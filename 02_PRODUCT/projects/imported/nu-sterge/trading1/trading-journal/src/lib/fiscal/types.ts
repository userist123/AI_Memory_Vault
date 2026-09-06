import { z } from 'zod';

/**
 * Romania fiscal parameters per year.
 * Source: Codul Fiscal, Legea 227/2015 cu modificările ulterioare.
 * Updated 2026 per Legea 239/2025 + OUG 8/2026.
 */
export interface FiscalYearParams {
  year: number;
  // Tax rates
  cryptoTaxRate: number; // 10% for 2025, 16% from 2026
  capitalGainsTaxRate: number; // 10% for non-resident broker
  cassRate: number; // 10% social health contribution

  // Minimum wage (used for CASS thresholds)
  minimumWageH1: number; // Jan-Jun
  minimumWageH2: number; // Jul-Dec
  minimumWageAvg: number; // average used for annual calculations

  // CASS thresholds (multiples of minimum wage)
  cassThreshold6: number; // 6 × min wage
  cassThreshold12: number; // 12 × min wage
  cassThreshold24: number; // 24 × min wage

  // Non-taxable thresholds (crypto)
  cryptoPerTradeExemption: number; // 200 lei per trade
  cryptoAnnualExemption: number; // 600 lei per year

  // Bonification
  bonificationRate: number; // 3% if paid early
  bonificationDeadline: string; // ISO date
  standardDeadline: string; // ISO date
}

export const FISCAL_PARAMS: Record<number, FiscalYearParams> = {
  2025: {
    year: 2025,
    cryptoTaxRate: 0.10,
    capitalGainsTaxRate: 0.10,
    cassRate: 0.10,
    minimumWageH1: 4050,
    minimumWageH2: 4050,
    minimumWageAvg: 4050,
    cassThreshold6: 24300, // 6 × 4050
    cassThreshold12: 48600,
    cassThreshold24: 97200,
    cryptoPerTradeExemption: 200,
    cryptoAnnualExemption: 600,
    bonificationRate: 0.03,
    bonificationDeadline: '2026-04-15',
    standardDeadline: '2026-05-25',
  },
  2026: {
    year: 2026,
    cryptoTaxRate: 0.16, // INCREASED from 10% to 16% per Legea 239/2025
    capitalGainsTaxRate: 0.10,
    cassRate: 0.10,
    minimumWageH1: 4050, // Jan-Jun 2026
    minimumWageH2: 4325, // Jul-Dec 2026
    minimumWageAvg: 4187.5, // ((4050×6) + (4325×6)) / 12
    cassThreshold6: 25125, // 6 × 4187.5
    cassThreshold12: 50250,
    cassThreshold24: 100500,
    cryptoPerTradeExemption: 200,
    cryptoAnnualExemption: 600,
    bonificationRate: 0.03,
    bonificationDeadline: '2027-04-15',
    standardDeadline: '2027-05-25',
  },
};

export function getFiscalParams(year: number): FiscalYearParams {
  return FISCAL_PARAMS[year] || FISCAL_PARAMS[2026];
}

// ===== Trade categorization =====

export const AssetFiscalCategorySchema = z.enum([
  'crypto', // 16% din 2026, CASS pe plusvaloare
  'stocks_eu', // 10% broker nerezident
  'stocks_ro', // reținut la sursă de broker rezident (informativ)
  'forex', // 10% broker nerezident (considerat capital gains)
  'commodities',
  'indices',
  'other',
]);

export type AssetFiscalCategory = z.infer<typeof AssetFiscalCategorySchema>;

/**
 * Map our trade asset classes to Romanian fiscal categories.
 * Critical for correct tax calculation.
 */
export function mapAssetClassToFiscal(
  assetClass: string,
  broker?: string
): AssetFiscalCategory {
  // Romanian brokers = withholding at source
  const romanianBrokers = ['tradeville', 'bt_capital'];
  if (broker && romanianBrokers.includes(broker)) {
    return 'stocks_ro';
  }

  switch (assetClass) {
    case 'crypto':
      return 'crypto';
    case 'stocks':
    case 'etf':
      return 'stocks_eu';
    case 'forex':
      return 'forex';
    case 'commodities':
      return 'commodities';
    case 'indices':
      return 'indices';
    default:
      return 'other';
  }
}

// ===== BNR Exchange rate =====

export interface BnrRate {
  date: string; // YYYY-MM-DD
  currency: 'EUR' | 'USD' | 'GBP' | 'CHF';
  rate: number; // RON per unit
}

// ===== Tax calculation result =====

export interface FiscalReport {
  year: number;
  userId: string;
  generatedAt: Date;
  params: FiscalYearParams;

  // Income by category
  categories: {
    crypto: CategoryResult;
    stocks_eu: CategoryResult;
    stocks_ro: CategoryResult;
    forex: CategoryResult;
    other: CategoryResult;
  };

  // Totals
  totalGainsRon: number;
  totalLossesRon: number;
  netDeclarableIncomeRon: number; // Sum of category net incomes (losses NOT deductible for crypto)

  // Tax breakdown
  cryptoTaxDue: number;
  capitalGainsTaxDue: number;
  totalIncomeTaxDue: number;

  // CASS
  cassThresholdReached: 0 | 6 | 12 | 24; // 0 = under threshold
  cassDue: number;

  // Bonification
  bonificationApplicable: boolean;
  bonificationAmount: number;
  totalDueIfBonificationApplied: number;
  totalDueStandard: number;

  // Warnings
  warnings: string[];
  notes: string[];
}

export interface CategoryResult {
  category: AssetFiscalCategory;
  tradeCount: number;
  grossGainsRon: number;
  grossLossesRon: number;
  netGainRon: number;
  exemptTradeCount: number; // under 200 lei threshold
  declarableIncomeRon: number; // after exemptions
  taxRate: number;
  taxDue: number;
  trades: FiscalTradeLine[];
}

export interface FiscalTradeLine {
  tradeId: string;
  symbol: string;
  direction: 'long' | 'short';
  entryDate: string;
  exitDate: string | null;
  quantity: number;
  entryPriceLocal: number;
  exitPriceLocal: number | null;
  currency: string;
  pnlLocal: number | null;
  bnrRateEntry: number;
  bnrRateExit: number | null;
  pnlRon: number | null;
  commissionRon: number;
  swapRon: number;
  netPnlRon: number | null;
  isExempt: boolean;
  fiscalCategory: AssetFiscalCategory;
}
