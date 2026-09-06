import type { BrokerImporter, Broker } from '@/types/trade';
import { binanceImporter } from './binance';
import { mt5Importer } from './mt5';
import { trading212Importer } from './trading212';
import { xtbImporter } from './xtb';
import { genericCsvImporter } from './generic';

// Order matters: specific importers first, generic last as fallback
export const importers: BrokerImporter[] = [
  mt5Importer, // HTML - detected first
  binanceImporter,
  trading212Importer,
  xtbImporter,
  genericCsvImporter, // Fallback
];

export function getImporterByBroker(broker: Broker): BrokerImporter | null {
  return importers.find((i) => i.broker === broker) || null;
}

/**
 * Auto-detect which importer to use based on file content.
 * Returns the most specific match.
 */
export async function detectImporter(file: File): Promise<BrokerImporter> {
  const content = await file.slice(0, 10000).text(); // First 10KB is enough

  // Try each importer's signature (except generic, which is last resort)
  for (const importer of importers.slice(0, -1)) {
    try {
      if (importer.detectSignature(content)) {
        return importer;
      }
    } catch (err) {
      console.warn(`[Detect] ${importer.broker} signature check failed:`, err);
    }
  }

  // Fallback to generic
  return genericCsvImporter;
}

export { binanceImporter, mt5Importer, trading212Importer, xtbImporter, genericCsvImporter };
