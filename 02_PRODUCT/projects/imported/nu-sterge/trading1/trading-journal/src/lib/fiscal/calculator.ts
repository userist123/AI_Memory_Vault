import type { Trade } from '@/types/trade';
import type {
  FiscalReport,
  CategoryResult,
  FiscalTradeLine,
  AssetFiscalCategory,
} from './types';
import { getFiscalParams, mapAssetClassToFiscal } from './types';
import { getBnrRatesForDates } from './bnr';

/**
 * Compute fiscal report for a given year.
 *
 * CRITICAL RULES (Codul Fiscal Romania):
 * 1. Crypto: 16% from 2026 (was 10% for 2025), applied on NET GAIN per trade
 *    - Price sold - price bought - direct costs (fees, swap)
 *    - Losses CANNOT offset gains (simple sum of gains only)
 *    - Trades with gain < 200 RON are exempt IF annual total gains < 600 RON
 * 2. Stocks via NON-resident broker: 10% on net capital gain
 *    - Losses can offset gains within the category
 * 3. Stocks via RO resident broker (Tradeville, BT Capital): withheld at source
 *    - Still must be declared but informative only
 * 4. All calculations in RON using BNR rate from TRADE DATE (not declaration date)
 * 5. CASS 10% on thresholds: 6/12/24 minimum wages (incremental, based on total income)
 */
export async function computeFiscalReport(
  userId: string,
  trades: Trade[],
  year: number
): Promise<FiscalReport> {
  const params = getFiscalParams(year);

  // Filter trades closed in the fiscal year
  const yearStart = new Date(`${year}-01-01T00:00:00Z`);
  const yearEnd = new Date(`${year + 1}-01-01T00:00:00Z`);

  const closedInYear = trades.filter((t) => {
    if (t.status !== 'closed') return false;
    const exit = t.exitTime instanceof Date ? t.exitTime : (t.exitTime ? new Date(t.exitTime) : null);
    return exit !== null && exit >= yearStart && exit < yearEnd;
  });

  // Collect all unique dates + currencies for BNR batch fetch
  const datesByCurrency: Record<string, Set<string>> = {};
  for (const t of closedInYear) {
    const entry = t.entryTime instanceof Date ? t.entryTime : new Date(t.entryTime);
    const exit = t.exitTime instanceof Date ? t.exitTime : new Date(t.exitTime!);
    const currency = t.currency || 'USD';
    if (!datesByCurrency[currency]) datesByCurrency[currency] = new Set();
    datesByCurrency[currency].add(entry.toISOString().slice(0, 10));
    datesByCurrency[currency].add(exit.toISOString().slice(0, 10));
  }

  // Fetch BNR rates (parallel per currency)
  const ratesByCurrency: Record<string, Map<string, number>> = {};
  await Promise.all(
    Object.entries(datesByCurrency).map(async ([currency, dateSet]) => {
      const dates = Array.from(dateSet).map((s) => new Date(s + 'T12:00:00Z'));
      ratesByCurrency[currency] = await getBnrRatesForDates(dates, currency);
    })
  );

  // Build fiscal lines per trade
  const lines: FiscalTradeLine[] = [];
  const warnings: string[] = [];

  for (const t of closedInYear) {
    const entry = t.entryTime instanceof Date ? t.entryTime : new Date(t.entryTime);
    const exit = t.exitTime instanceof Date ? t.exitTime : new Date(t.exitTime!);
    const currency = t.currency || 'USD';
    const fiscalCategory = mapAssetClassToFiscal(t.assetClass, t.broker);

    const entryDateKey = entry.toISOString().slice(0, 10);
    const exitDateKey = exit.toISOString().slice(0, 10);
    const rateEntry = ratesByCurrency[currency]?.get(entryDateKey) || 0;
    const rateExit = ratesByCurrency[currency]?.get(exitDateKey) || 0;

    if (rateEntry === 0 && currency !== 'RON') {
      warnings.push(
        `Trade ${t.symbol} ${entryDateKey}: curs BNR ${currency} indisponibil pentru ${entryDateKey}`
      );
    }

    // Convert P&L to RON using exit date rate (when the gain was realized)
    const pnlRon = t.pnl !== null && t.pnl !== undefined ? t.pnl * rateExit : null;
    const commissionRon = (t.commission || 0) * rateExit;
    const swapRon = (t.swap || 0) * rateExit;
    // Commission already subtracted in broker P&L usually, but we show explicitly
    const netPnlRon = pnlRon;

    const isExempt = netPnlRon !== null && netPnlRon > 0 && netPnlRon < params.cryptoPerTradeExemption;

    lines.push({
      tradeId: t._id || '',
      symbol: t.symbol,
      direction: t.direction,
      entryDate: entryDateKey,
      exitDate: exitDateKey,
      quantity: t.quantity,
      entryPriceLocal: t.entryPrice,
      exitPriceLocal: t.exitPrice ?? null,
      currency,
      pnlLocal: t.pnl ?? null,
      bnrRateEntry: rateEntry,
      bnrRateExit: rateExit,
      pnlRon,
      commissionRon,
      swapRon,
      netPnlRon,
      isExempt,
      fiscalCategory,
    });
  }

  // Group by fiscal category
  const categories: FiscalReport['categories'] = {
    crypto: buildCategoryResult('crypto', lines, params.cryptoTaxRate, params, true),
    stocks_eu: buildCategoryResult('stocks_eu', lines, params.capitalGainsTaxRate, params, false),
    stocks_ro: buildCategoryResult('stocks_ro', lines, 0, params, false), // withheld at source
    forex: buildCategoryResult('forex', lines, params.capitalGainsTaxRate, params, false),
    other: buildCategoryResult('other', lines, params.capitalGainsTaxRate, params, false),
  };

  // Stocks RO are informational only
  categories.stocks_ro.taxDue = 0;

  // Check annual crypto exemption rule (600 RON threshold)
  const cryptoGainsTotal = categories.crypto.grossGainsRon;
  if (cryptoGainsTotal < params.cryptoAnnualExemption && cryptoGainsTotal > 0) {
    // ALL crypto gains are exempt (under 600 RON annual)
    categories.crypto.declarableIncomeRon = 0;
    categories.crypto.taxDue = 0;
    categories.crypto.trades = categories.crypto.trades.map((l) => ({ ...l, isExempt: true }));
  } else if (cryptoGainsTotal >= params.cryptoAnnualExemption) {
    // Over 600 RON annual -> ALL gains taxable, even those previously marked exempt
    const allGains = categories.crypto.trades
      .filter((l) => (l.netPnlRon ?? 0) > 0)
      .reduce((s, l) => s + (l.netPnlRon ?? 0), 0);
    categories.crypto.declarableIncomeRon = allGains;
    categories.crypto.taxDue = allGains * params.cryptoTaxRate;
    categories.crypto.trades = categories.crypto.trades.map((l) => ({ ...l, isExempt: false }));
  }

  // Totals
  const totalGainsRon =
    categories.crypto.grossGainsRon +
    categories.stocks_eu.grossGainsRon +
    categories.forex.grossGainsRon +
    categories.other.grossGainsRon;
  const totalLossesRon =
    categories.crypto.grossLossesRon +
    categories.stocks_eu.grossLossesRon +
    categories.forex.grossLossesRon +
    categories.other.grossLossesRon;
  const netDeclarableIncomeRon =
    categories.crypto.declarableIncomeRon +
    categories.stocks_eu.declarableIncomeRon +
    categories.forex.declarableIncomeRon +
    categories.other.declarableIncomeRon;

  const cryptoTaxDue = categories.crypto.taxDue;
  const capitalGainsTaxDue =
    categories.stocks_eu.taxDue + categories.forex.taxDue + categories.other.taxDue;
  const totalIncomeTaxDue = cryptoTaxDue + capitalGainsTaxDue;

  // CASS calculation
  // CASS thresholds are based on TOTAL declarable income (all categories combined)
  let cassThresholdReached: 0 | 6 | 12 | 24 = 0;
  let cassDue = 0;
  if (netDeclarableIncomeRon >= params.cassThreshold24) {
    cassThresholdReached = 24;
    cassDue = params.cassThreshold24 * params.cassRate;
  } else if (netDeclarableIncomeRon >= params.cassThreshold12) {
    cassThresholdReached = 12;
    cassDue = params.cassThreshold12 * params.cassRate;
  } else if (netDeclarableIncomeRon >= params.cassThreshold6) {
    cassThresholdReached = 6;
    cassDue = params.cassThreshold6 * params.cassRate;
  }

  const bonificationAmount = totalIncomeTaxDue * params.bonificationRate;
  const totalDueStandard = totalIncomeTaxDue + cassDue;
  const totalDueIfBonificationApplied = totalIncomeTaxDue - bonificationAmount + cassDue;

  // Warnings
  const notes: string[] = [];

  if (categories.stocks_ro.tradeCount > 0) {
    notes.push(
      `Ai ${categories.stocks_ro.tradeCount} tranzacții prin broker rezident român. Impozitul e reținut la sursă — informație inclusă doar pentru completitudine.`
    );
  }

  if (year >= 2026) {
    notes.push(
      'Din 2026, impozitul pe crypto este 16% (nu 10%). Pierderile la crypto NU sunt deductibile din câștiguri.'
    );
  }

  if (cryptoGainsTotal < params.cryptoAnnualExemption && cryptoGainsTotal > 0) {
    notes.push(
      `Câștiguri crypto ${cryptoGainsTotal.toFixed(2)} RON < ${params.cryptoAnnualExemption} RON — NEIMPOZABIL (regula 200/600 lei).`
    );
  }

  if (cassThresholdReached > 0) {
    notes.push(
      `CASS 10% datorat pe plafonul de ${cassThresholdReached} salarii minime = ${cassDue.toFixed(2)} RON. Se cumulează cu alte venituri non-salariale (chirii, dividende, PFA).`
    );
  } else if (netDeclarableIncomeRon > 0 && netDeclarableIncomeRon < params.cassThreshold6) {
    notes.push(
      `Venit declarabil ${netDeclarableIncomeRon.toFixed(2)} RON < ${params.cassThreshold6} RON (pragul CASS 6 salarii minime). Nu datorezi CASS din aceste venituri.`
    );
  }

  if (bonificationAmount > 0) {
    notes.push(
      `Bonificație 3% dacă depui + plătești până la ${params.bonificationDeadline}: economisești ${bonificationAmount.toFixed(2)} RON.`
    );
  }

  notes.push(
    'Declarația Unică (Formular 212) se depune online prin SPV ANAF. Acest raport e orientativ — consultă un expert contabil pentru situații complexe.'
  );

  return {
    year,
    userId,
    generatedAt: new Date(),
    params,
    categories,
    totalGainsRon,
    totalLossesRon,
    netDeclarableIncomeRon,
    cryptoTaxDue,
    capitalGainsTaxDue,
    totalIncomeTaxDue,
    cassThresholdReached,
    cassDue,
    bonificationApplicable: totalIncomeTaxDue > 0,
    bonificationAmount,
    totalDueIfBonificationApplied,
    totalDueStandard,
    warnings,
    notes,
  };
}

function buildCategoryResult(
  category: AssetFiscalCategory,
  allLines: FiscalTradeLine[],
  taxRate: number,
  params: ReturnType<typeof getFiscalParams>,
  lossesNotDeductible: boolean
): CategoryResult {
  const trades = allLines.filter((l) => l.fiscalCategory === category);

  const grossGains = trades
    .filter((l) => (l.netPnlRon ?? 0) > 0)
    .reduce((s, l) => s + (l.netPnlRon ?? 0), 0);
  const grossLosses = Math.abs(
    trades
      .filter((l) => (l.netPnlRon ?? 0) < 0)
      .reduce((s, l) => s + (l.netPnlRon ?? 0), 0)
  );

  let declarableIncomeRon: number;
  if (lossesNotDeductible) {
    // Crypto: only sum of positive gains, losses NOT deductible
    declarableIncomeRon = grossGains;
  } else {
    // Stocks/forex: net (gains minus losses, but not below 0)
    declarableIncomeRon = Math.max(0, grossGains - grossLosses);
  }

  const exemptTradeCount = trades.filter((l) => l.isExempt).length;

  return {
    category,
    tradeCount: trades.length,
    grossGainsRon: grossGains,
    grossLossesRon: grossLosses,
    netGainRon: grossGains - grossLosses,
    exemptTradeCount,
    declarableIncomeRon,
    taxRate,
    taxDue: declarableIncomeRon * taxRate,
    trades,
  };
}
