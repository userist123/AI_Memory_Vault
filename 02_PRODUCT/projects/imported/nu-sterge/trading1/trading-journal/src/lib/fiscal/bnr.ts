import type { BnrRate } from './types';

/**
 * BNR (Banca Națională a României) publishes official exchange rates daily.
 * For fiscal calculations, ANAF requires the BNR rate from the trade date.
 *
 * Public feeds:
 * - Daily XML: https://www.bnr.ro/nbrfxrates.xml
 * - Year archive: https://www.bnr.ro/files/xml/years/nbrfxrates{YEAR}.xml
 *
 * We cache aggressively - historical rates never change.
 */

const BNR_YEAR_URL = (year: number) =>
  `https://www.bnr.ro/files/xml/years/nbrfxrates${year}.xml`;

// In-memory cache (rates don't change for historical dates)
const rateCache = new Map<string, BnrRate>();

/**
 * Simple XML parser for BNR format.
 * Format is <Cube date="YYYY-MM-DD"><Rate currency="USD">4.5678</Rate>...</Cube>
 */
function parseBnrXml(xml: string, currency: string): Map<string, number> {
  const ratesByDate = new Map<string, number>();
  // Regex extraction is sufficient for BNR's simple format
  const cubeRegex = /<Cube\s+date="([^"]+)">([\s\S]*?)<\/Cube>/g;
  const rateRegex = new RegExp(
    `<Rate\\s+currency="${currency}"(?:\\s+multiplier="(\\d+)")?>([^<]+)</Rate>`,
    'i'
  );

  let match;
  while ((match = cubeRegex.exec(xml)) !== null) {
    const date = match[1];
    const cubeBody = match[2];
    const rateMatch = rateRegex.exec(cubeBody);
    if (rateMatch) {
      const multiplier = rateMatch[1] ? parseInt(rateMatch[1]) : 1;
      const rate = parseFloat(rateMatch[2]) / multiplier;
      if (isFinite(rate)) {
        ratesByDate.set(date, rate);
      }
    }
    rateRegex.lastIndex = 0;
  }
  return ratesByDate;
}

/**
 * Pre-fetched rates for a year (batch operation).
 * Stores all dates/rates in cache.
 */
async function fetchYearRates(year: number, currency: string): Promise<void> {
  const cacheKey = `${year}:${currency}`;
  if (rateCache.has(cacheKey + ':loaded')) return;

  try {
    const res = await fetch(BNR_YEAR_URL(year), {
      // Cache for 1 day - BNR publishes once per day around 13:00 Bucharest time
      next: { revalidate: 86400 },
    });
    if (!res.ok) {
      console.warn(`[BNR] Failed to fetch ${year}: ${res.status}`);
      return;
    }
    const xml = await res.text();
    const rates = parseBnrXml(xml, currency);
    for (const [date, rate] of rates) {
      rateCache.set(`${date}:${currency}`, { date, currency: currency as BnrRate['currency'], rate });
    }
    rateCache.set(cacheKey + ':loaded', { date: '', currency: currency as BnrRate['currency'], rate: 0 });
    console.log(`[BNR] Loaded ${rates.size} ${currency} rates for ${year}`);
  } catch (err) {
    console.error('[BNR] Fetch error:', err);
  }
}

/**
 * Get BNR rate for a specific date (RON per 1 unit of currency).
 * If exact date not available (weekend/holiday), returns most recent rate.
 *
 * For RON, returns 1.0 (identity).
 * Always fetches the year data on first access.
 */
export async function getBnrRate(
  date: Date,
  currency: string
): Promise<number> {
  if (currency === 'RON' || currency === 'LEI') return 1.0;

  const upperCurrency = currency.toUpperCase();
  if (!['EUR', 'USD', 'GBP', 'CHF'].includes(upperCurrency)) {
    console.warn(`[BNR] Unsupported currency: ${currency}`);
    return 0;
  }

  const dateStr = date.toISOString().slice(0, 10);
  const cacheKey = `${dateStr}:${upperCurrency}`;

  // Check exact date
  const exact = rateCache.get(cacheKey);
  if (exact) return exact.rate;

  // Load year
  const year = date.getFullYear();
  await fetchYearRates(year, upperCurrency);

  const afterLoad = rateCache.get(cacheKey);
  if (afterLoad) return afterLoad.rate;

  // Fallback: find closest earlier date in cache
  let closestRate = 0;
  let closestDateDiff = Infinity;
  const targetTime = date.getTime();

  for (const [key, r] of rateCache.entries()) {
    if (!key.endsWith(`:${upperCurrency}`) || key.endsWith(':loaded')) continue;
    const cachedDate = new Date(r.date);
    const diff = targetTime - cachedDate.getTime();
    if (diff >= 0 && diff < closestDateDiff) {
      closestDateDiff = diff;
      closestRate = r.rate;
    }
  }

  return closestRate;
}

/**
 * Get rate for trade at exact entry/exit times.
 * Used in bulk when computing fiscal report - avoids N network calls.
 */
export async function getBnrRatesForDates(
  dates: Date[],
  currency: string
): Promise<Map<string, number>> {
  if (currency === 'RON' || currency === 'LEI') {
    const result = new Map<string, number>();
    dates.forEach((d) => result.set(d.toISOString().slice(0, 10), 1));
    return result;
  }

  // Group by year to minimize fetches
  const years = new Set(dates.map((d) => d.getFullYear()));
  await Promise.all(Array.from(years).map((y) => fetchYearRates(y, currency.toUpperCase())));

  const result = new Map<string, number>();
  for (const date of dates) {
    const rate = await getBnrRate(date, currency);
    result.set(date.toISOString().slice(0, 10), rate);
  }
  return result;
}
