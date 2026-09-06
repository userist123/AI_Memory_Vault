import type { FiscalReport } from './types';

/**
 * Escape CSV field - handles commas, quotes, newlines.
 * Uses RFC 4180 rules.
 */
function csvEscape(value: string | number | null | undefined): string {
  if (value === null || value === undefined) return '';
  const str = String(value);
  if (/[",\n\r;]/.test(str)) {
    return `"${str.replace(/"/g, '""')}"`;
  }
  return str;
}

/**
 * Export detailed CSV with all trades + fiscal calculations.
 * Uses semicolon separator (common in Romanian Excel).
 */
export function exportFiscalCsv(report: FiscalReport): string {
  const sep = ';';
  const lines: string[] = [];

  // Header section
  lines.push(`Raport Fiscal - An ${report.year}`);
  lines.push(`Generat: ${report.generatedAt.toISOString()}`);
  lines.push('');
  lines.push('REZUMAT');
  lines.push(`Venit brut total (câștiguri)${sep}${report.totalGainsRon.toFixed(2)} RON`);
  lines.push(`Pierderi totale${sep}${report.totalLossesRon.toFixed(2)} RON`);
  lines.push(`Venit declarabil (după reguli)${sep}${report.netDeclarableIncomeRon.toFixed(2)} RON`);
  lines.push('');
  lines.push('IMPOZITE');
  lines.push(`Impozit crypto (${(report.params.cryptoTaxRate * 100).toFixed(0)}%)${sep}${report.cryptoTaxDue.toFixed(2)} RON`);
  lines.push(`Impozit câștiguri de capital (10%)${sep}${report.capitalGainsTaxDue.toFixed(2)} RON`);
  lines.push(`Impozit total${sep}${report.totalIncomeTaxDue.toFixed(2)} RON`);
  lines.push(`CASS (prag ${report.cassThresholdReached} salarii minime)${sep}${report.cassDue.toFixed(2)} RON`);
  lines.push(`Total de plată${sep}${report.totalDueStandard.toFixed(2)} RON`);
  if (report.bonificationApplicable) {
    lines.push(`Total cu bonificație 3% (plată până la ${report.params.bonificationDeadline})${sep}${report.totalDueIfBonificationApplied.toFixed(2)} RON`);
  }
  lines.push('');

  // Per-category breakdown
  lines.push('DETALIU PE CATEGORII');
  lines.push(
    [
      'Categorie',
      'Nr tranzacții',
      'Câștiguri brute RON',
      'Pierderi brute RON',
      'Venit declarabil RON',
      'Rată impozit',
      'Impozit datorat RON',
    ].map(csvEscape).join(sep)
  );
  for (const [key, cat] of Object.entries(report.categories)) {
    if (cat.tradeCount === 0) continue;
    lines.push(
      [
        key,
        cat.tradeCount,
        cat.grossGainsRon.toFixed(2),
        cat.grossLossesRon.toFixed(2),
        cat.declarableIncomeRon.toFixed(2),
        `${(cat.taxRate * 100).toFixed(0)}%`,
        cat.taxDue.toFixed(2),
      ].map(csvEscape).join(sep)
    );
  }
  lines.push('');

  // Trade-by-trade detail
  lines.push('DETALIU TRANZACȚII');
  lines.push(
    [
      'ID tranzacție',
      'Simbol',
      'Categorie fiscală',
      'Direcție',
      'Dată intrare',
      'Dată ieșire',
      'Cantitate',
      'Preț intrare (local)',
      'Preț ieșire (local)',
      'Monedă',
      'P&L local',
      'Curs BNR intrare',
      'Curs BNR ieșire',
      'P&L RON',
      'Comision RON',
      'Swap RON',
      'P&L net RON',
      'Scutit',
    ].map(csvEscape).join(sep)
  );

  const allTrades = [
    ...report.categories.crypto.trades,
    ...report.categories.stocks_eu.trades,
    ...report.categories.stocks_ro.trades,
    ...report.categories.forex.trades,
    ...report.categories.other.trades,
  ].sort((a, b) => a.exitDate!.localeCompare(b.exitDate!));

  for (const t of allTrades) {
    lines.push(
      [
        t.tradeId,
        t.symbol,
        t.fiscalCategory,
        t.direction,
        t.entryDate,
        t.exitDate ?? '',
        t.quantity,
        t.entryPriceLocal.toFixed(5),
        t.exitPriceLocal?.toFixed(5) ?? '',
        t.currency,
        t.pnlLocal?.toFixed(2) ?? '',
        t.bnrRateEntry.toFixed(4),
        t.bnrRateExit?.toFixed(4) ?? '',
        t.pnlRon?.toFixed(2) ?? '',
        t.commissionRon.toFixed(2),
        t.swapRon.toFixed(2),
        t.netPnlRon?.toFixed(2) ?? '',
        t.isExempt ? 'DA' : 'NU',
      ].map(csvEscape).join(sep)
    );
  }

  lines.push('');
  lines.push('NOTE');
  for (const note of report.notes) {
    lines.push(csvEscape(note));
  }

  if (report.warnings.length > 0) {
    lines.push('');
    lines.push('AVERTIZĂRI');
    for (const w of report.warnings) {
      lines.push(csvEscape(w));
    }
  }

  lines.push('');
  lines.push('Disclaimer: Acest raport este orientativ. Consultă un expert contabil pentru depunerea Declarației Unice (Formular 212) la ANAF.');

  // Add UTF-8 BOM for Excel compatibility
  return '\uFEFF' + lines.join('\n');
}

/**
 * Export summary ready to paste into D212 sections.
 * Maps to the specific fields ANAF expects.
 */
export function exportD212Summary(report: FiscalReport): string {
  const sep = ';';
  const lines: string[] = [];

  lines.push('GHID COMPLETARE DECLARAȚIA UNICĂ (Formular 212)');
  lines.push(`An fiscal: ${report.year}`);
  lines.push('');

  lines.push('SECȚIUNEA II: VENITURI DIN TRANSFERUL DE MONEDĂ VIRTUALĂ (CRYPTO)');
  lines.push(`Câștig net anual${sep}${report.categories.crypto.declarableIncomeRon.toFixed(2)} RON`);
  lines.push(`Cotă impozit${sep}${(report.params.cryptoTaxRate * 100).toFixed(0)}%`);
  lines.push(`Impozit datorat${sep}${report.categories.crypto.taxDue.toFixed(2)} RON`);
  lines.push('');

  const nonCryptoGains =
    report.categories.stocks_eu.declarableIncomeRon +
    report.categories.forex.declarableIncomeRon +
    report.categories.other.declarableIncomeRon;
  if (nonCryptoGains > 0) {
    lines.push('SECȚIUNEA II: VENITURI DIN INVESTIȚII (acțiuni, ETF, forex nerezident)');
    lines.push(`Câștig net anual${sep}${nonCryptoGains.toFixed(2)} RON`);
    lines.push(`Cotă impozit${sep}${(report.params.capitalGainsTaxRate * 100).toFixed(0)}%`);
    lines.push(`Impozit datorat${sep}${report.capitalGainsTaxDue.toFixed(2)} RON`);
    lines.push('');
  }

  if (report.cassThresholdReached > 0) {
    lines.push('SECȚIUNEA III: CONTRIBUȚIA DE ASIGURĂRI SOCIALE DE SĂNĂTATE (CASS)');
    lines.push(`Bază de calcul: ${report.cassThresholdReached} salarii minime${sep}${(report.cassThresholdReached * report.params.minimumWageAvg).toFixed(2)} RON`);
    lines.push(`Cotă${sep}${(report.params.cassRate * 100).toFixed(0)}%`);
    lines.push(`CASS datorat${sep}${report.cassDue.toFixed(2)} RON`);
    lines.push('');
  }

  lines.push('TOTAL DE PLATĂ');
  lines.push(`Impozit pe venit${sep}${report.totalIncomeTaxDue.toFixed(2)} RON`);
  lines.push(`CASS${sep}${report.cassDue.toFixed(2)} RON`);
  lines.push(`Total standard (termen ${report.params.standardDeadline})${sep}${report.totalDueStandard.toFixed(2)} RON`);
  if (report.bonificationApplicable) {
    lines.push(`Total cu bonificație 3% (termen ${report.params.bonificationDeadline})${sep}${report.totalDueIfBonificationApplied.toFixed(2)} RON`);
    lines.push(`Economisire prin plată timpurie${sep}${report.bonificationAmount.toFixed(2)} RON`);
  }

  return '\uFEFF' + lines.join('\n');
}
