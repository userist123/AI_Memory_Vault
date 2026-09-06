'use client';

import { useEffect, useState } from 'react';
import { useLocale } from 'next-intl';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';
import { Plug, Unplug, ShieldAlert, CheckCircle2, Loader2, AlertTriangle, ExternalLink } from 'lucide-react';

type BrokerId = 'binance' | 'alpaca' | 'ibkr';

interface ConnectedBroker {
  brokerId: BrokerId;
  testnet: boolean;
  label?: string;
  permissions: string[];
  createdAt: string;
}

const BROKER_CONFIGS = {
  binance: {
    name: 'Binance',
    description: 'Crypto spot trading',
    apiKeyUrl: 'https://www.binance.com/en/my/settings/api-management',
    testnetUrl: 'https://testnet.binance.vision/',
    docs: 'https://binance-docs.github.io/apidocs/spot/en/',
    color: 'text-yellow-500',
    bg: 'bg-yellow-500/10',
  },
  alpaca: {
    name: 'Alpaca',
    description: 'US stocks & crypto',
    apiKeyUrl: 'https://app.alpaca.markets/paper/dashboard/overview',
    testnetUrl: 'https://alpaca.markets/docs/market-data/',
    docs: 'https://alpaca.markets/docs/',
    color: 'text-orange-500',
    bg: 'bg-orange-500/10',
  },
  ibkr: {
    name: 'Interactive Brokers',
    description: 'Coming soon',
    apiKeyUrl: 'https://www.interactivebrokers.com/',
    testnetUrl: '',
    docs: 'https://interactivebrokers.github.io/cpwebapi/',
    color: 'text-blue-500',
    bg: 'bg-blue-500/10',
  },
};

export default function BrokerSettings() {
  const locale = useLocale() as 'ro' | 'en';
  const [brokers, setBrokers] = useState<ConnectedBroker[]>([]);
  const [loading, setLoading] = useState(true);
  const [showConnectFor, setShowConnectFor] = useState<BrokerId | null>(null);

  const load = async () => {
    const res = await fetch('/api/brokers');
    const data = await res.json();
    setBrokers(data.brokers || []);
    setLoading(false);
  };

  useEffect(() => { load(); }, []);

  const disconnect = async (brokerId: BrokerId, testnet: boolean) => {
    if (!confirm(locale === 'ro' ? 'Sigur deconectezi brokerul?' : 'Disconnect broker?')) return;
    await fetch(`/api/brokers?brokerId=${brokerId}&testnet=${testnet}`, { method: 'DELETE' });
    await load();
  };

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold">
          {locale === 'ro' ? 'Brokeri conectați' : 'Connected brokers'}
        </h1>
        <p className="mt-1 text-sm text-muted-foreground">
          {locale === 'ro'
            ? 'Conectează-ți contul de broker pentru execuție one-click. Cheile sunt criptate AES-256.'
            : 'Connect your broker for one-click execution. Keys are AES-256 encrypted.'}
        </p>
      </div>

      <div className="rounded-lg border border-yellow-500/30 bg-yellow-500/5 p-4">
        <div className="flex gap-2">
          <ShieldAlert className="mt-0.5 h-4 w-4 shrink-0 text-yellow-500" />
          <div className="text-sm">
            <p className="font-semibold text-yellow-500">
              {locale === 'ro' ? 'Reguli de siguranță' : 'Safety rules'}
            </p>
            <ul className="mt-2 space-y-1 text-xs text-muted-foreground">
              <li>• {locale === 'ro' ? 'DEZACTIVEAZĂ permisiunea "Enable Withdrawals" în Binance' : 'DISABLE "Enable Withdrawals" permission on Binance'}</li>
              <li>• {locale === 'ro' ? 'Începe cu Testnet (bani virtuali) 2-4 săptămâni' : 'Start with Testnet (fake money) for 2-4 weeks'}</li>
              <li>• {locale === 'ro' ? 'Setează IP restriction dacă folosești servere fixe' : 'Set IP restriction if using fixed servers'}</li>
              <li>• {locale === 'ro' ? 'Nu partaja niciodată aceste chei cu nimeni' : 'Never share these keys with anyone'}</li>
            </ul>
          </div>
        </div>
      </div>

      {loading ? (
        <div className="flex h-32 items-center justify-center">
          <Loader2 className="h-6 w-6 animate-spin text-muted-foreground" />
        </div>
      ) : (
        <div className="grid gap-3">
          {(Object.keys(BROKER_CONFIGS) as BrokerId[]).map((bid) => {
            const config = BROKER_CONFIGS[bid];
            const connected = brokers.filter((b) => b.brokerId === bid);
            return (
              <div key={bid} className="rounded-xl border border-border bg-card p-4">
                <div className="flex flex-wrap items-start justify-between gap-3">
                  <div className="flex items-start gap-3">
                    <div className={cn('flex h-10 w-10 items-center justify-center rounded-lg', config.bg)}>
                      <Plug className={cn('h-5 w-5', config.color)} />
                    </div>
                    <div>
                      <h3 className="font-semibold">{config.name}</h3>
                      <p className="text-xs text-muted-foreground">{config.description}</p>
                    </div>
                  </div>
                  {bid !== 'ibkr' && (
                    <Button size="sm" variant="outline" onClick={() => setShowConnectFor(bid)}>
                      {locale === 'ro' ? 'Conectează' : 'Connect'}
                    </Button>
                  )}
                </div>

                {connected.length > 0 && (
                  <div className="mt-4 space-y-2 border-t border-border pt-3">
                    {connected.map((b) => (
                      <div key={`${b.brokerId}-${b.testnet}`} className="flex items-center justify-between rounded bg-muted/30 p-2 text-sm">
                        <div className="flex items-center gap-2">
                          <CheckCircle2 className="h-4 w-4 text-profit" />
                          <span>{b.label || b.brokerId}</span>
                          <span className="rounded bg-muted px-1.5 py-0.5 text-[10px]">
                            {b.testnet ? 'Testnet' : 'LIVE'}
                          </span>
                        </div>
                        <Button size="sm" variant="ghost" onClick={() => disconnect(b.brokerId, b.testnet)}>
                          <Unplug className="h-4 w-4" />
                        </Button>
                      </div>
                    ))}
                  </div>
                )}

                {showConnectFor === bid && (
                  <ConnectForm
                    brokerId={bid}
                    onClose={() => setShowConnectFor(null)}
                    onConnected={() => { setShowConnectFor(null); load(); }}
                    locale={locale}
                  />
                )}
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}

function ConnectForm({
  brokerId,
  onClose,
  onConnected,
  locale,
}: {
  brokerId: BrokerId;
  onClose: () => void;
  onConnected: () => void;
  locale: 'ro' | 'en';
}) {
  const [apiKey, setApiKey] = useState('');
  const [apiSecret, setApiSecret] = useState('');
  const [testnet, setTestnet] = useState(true);
  const [label, setLabel] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const config = BROKER_CONFIGS[brokerId];

  const submit = async () => {
    setSubmitting(true);
    setError(null);
    try {
      const res = await fetch('/api/brokers', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ brokerId, apiKey, apiSecret, testnet, label: label || undefined }),
      });
      const data = await res.json();
      if (!res.ok) {
        setError(data.message || data.details || data.error);
        return;
      }
      onConnected();
    } catch (err: unknown) {
      const e = err as { message?: string };
      setError(e.message || 'Failed');
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="mt-4 space-y-3 rounded-lg border border-primary/30 bg-primary/5 p-4">
      <div className="flex items-center justify-between">
        <h4 className="text-sm font-semibold">
          {locale === 'ro' ? 'Conectează' : 'Connect'} {config.name}
        </h4>
        <a
          href={testnet && config.testnetUrl ? config.testnetUrl : config.apiKeyUrl}
          target="_blank"
          rel="noopener noreferrer"
          className="flex items-center gap-1 text-xs text-primary hover:underline"
        >
          {locale === 'ro' ? 'Obține chei API' : 'Get API keys'}
          <ExternalLink className="h-3 w-3" />
        </a>
      </div>

      <label className="flex items-center gap-2 text-sm">
        <input type="checkbox" checked={testnet} onChange={(e) => setTestnet(e.target.checked)} />
        <span>{locale === 'ro' ? 'Testnet (recomandat pentru început)' : 'Testnet (recommended to start)'}</span>
      </label>

      <input
        type="password"
        value={apiKey}
        onChange={(e) => setApiKey(e.target.value)}
        placeholder="API Key"
        className="w-full rounded border border-input bg-background px-3 py-2 text-sm font-mono"
      />
      <input
        type="password"
        value={apiSecret}
        onChange={(e) => setApiSecret(e.target.value)}
        placeholder="API Secret"
        className="w-full rounded border border-input bg-background px-3 py-2 text-sm font-mono"
      />
      <input
        value={label}
        onChange={(e) => setLabel(e.target.value)}
        placeholder={locale === 'ro' ? 'Etichetă (opțional)' : 'Label (optional)'}
        className="w-full rounded border border-input bg-background px-3 py-2 text-sm"
      />

      {error && (
        <div className="flex items-start gap-2 rounded border border-destructive/50 bg-destructive/10 p-2 text-xs">
          <AlertTriangle className="h-3 w-3 shrink-0 text-destructive" />
          <p className="text-destructive">{error}</p>
        </div>
      )}

      <div className="flex gap-2">
        <Button size="sm" onClick={submit} disabled={submitting || !apiKey || !apiSecret}>
          {submitting && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
          {locale === 'ro' ? 'Verifică și conectează' : 'Verify & connect'}
        </Button>
        <Button size="sm" variant="outline" onClick={onClose}>
          {locale === 'ro' ? 'Anulează' : 'Cancel'}
        </Button>
      </div>
    </div>
  );
}
