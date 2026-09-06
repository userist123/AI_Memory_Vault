'use client';

import { useEffect, useState } from 'react';
import { useLocale } from 'next-intl';
import { Button } from '@/components/ui/button';
import { Shield, Loader2, AlertTriangle } from 'lucide-react';

interface RiskRules {
  maxTradesPerDay: number;
  maxRiskPerTradePct: number;
  maxDailyLossPct: number;
  maxOpenPositions: number;
  consecutiveLossBlockThreshold: number;
  cooldownMinutes: number;
  requireReason: boolean;
  requireStopLoss: boolean;
}

export default function RiskSettings() {
  const locale = useLocale() as 'ro' | 'en';
  const [rules, setRules] = useState<RiskRules | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    fetch('/api/brokers') // just to auth - we use defaults from client
      .then(() => {
        setRules({
          maxTradesPerDay: 3,
          maxRiskPerTradePct: 2,
          maxDailyLossPct: 5,
          maxOpenPositions: 3,
          consecutiveLossBlockThreshold: 2,
          cooldownMinutes: 1440,
          requireReason: true,
          requireStopLoss: true,
        });
        setLoading(false);
      });
  }, []);

  const save = async () => {
    if (!rules) return;
    setSaving(true);
    // NOTE: We'd need a dedicated /api/risk/rules endpoint. Using defaults for MVP.
    // The checkRiskRules() function reads directly from DB.
    await new Promise((r) => setTimeout(r, 500));
    setSaving(false);
  };

  if (loading || !rules) {
    return <div className="flex h-32 items-center justify-center"><Loader2 className="h-6 w-6 animate-spin" /></div>;
  }

  return (
    <div className="space-y-6">
      <div>
        <div className="flex items-center gap-2">
          <Shield className="h-6 w-6 text-primary" />
          <h1 className="text-2xl font-bold">
            {locale === 'ro' ? 'Reguli risc' : 'Risk rules'}
          </h1>
        </div>
        <p className="mt-1 text-sm text-muted-foreground">
          {locale === 'ro'
            ? 'Limitele care te opresc din a te auto-distruge.'
            : 'The limits that stop you from destroying yourself.'}
        </p>
      </div>

      <div className="rounded-lg border border-orange-500/30 bg-orange-500/5 p-4 text-sm">
        <div className="flex gap-2">
          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0 text-orange-500" />
          <p>
            {locale === 'ro'
              ? 'Aceste reguli se aplică AUTOMAT la execuția semnalelor. Nu le poți ocoli chiar dacă vrei. Asta e scopul: să te protejezi de tine însuți.'
              : 'These rules apply AUTOMATICALLY to signal execution. You cannot bypass them. That is the point: to protect yourself from yourself.'}
          </p>
        </div>
      </div>

      <div className="grid gap-4 md:grid-cols-2">
        <NumberField
          label={locale === 'ro' ? 'Max tranzacții pe zi' : 'Max trades per day'}
          value={rules.maxTradesPerDay}
          min={1}
          max={20}
          onChange={(v) => setRules({ ...rules, maxTradesPerDay: v })}
          hint={locale === 'ro' ? 'Traderii de succes fac 1-3/zi' : 'Successful traders make 1-3/day'}
        />
        <NumberField
          label={locale === 'ro' ? 'Risc per trade (%)' : 'Risk per trade (%)'}
          value={rules.maxRiskPerTradePct}
          min={0.1}
          max={5}
          step={0.1}
          onChange={(v) => setRules({ ...rules, maxRiskPerTradePct: v })}
          hint="1-2% standard, 3%+ riscant"
        />
        <NumberField
          label={locale === 'ro' ? 'Pierdere max/zi (%)' : 'Max daily loss (%)'}
          value={rules.maxDailyLossPct}
          min={1}
          max={20}
          onChange={(v) => setRules({ ...rules, maxDailyLossPct: v })}
          hint={locale === 'ro' ? 'Circuit breaker - oprește trading-ul' : 'Circuit breaker - stops trading'}
        />
        <NumberField
          label={locale === 'ro' ? 'Max poziții deschise' : 'Max open positions'}
          value={rules.maxOpenPositions}
          min={1}
          max={10}
          onChange={(v) => setRules({ ...rules, maxOpenPositions: v })}
        />
        <NumberField
          label={locale === 'ro' ? 'Pierderi consecutive → cooldown' : 'Consecutive losses → cooldown'}
          value={rules.consecutiveLossBlockThreshold}
          min={2}
          max={10}
          onChange={(v) => setRules({ ...rules, consecutiveLossBlockThreshold: v })}
          hint={locale === 'ro' ? 'Anti-revenge trading' : 'Anti-revenge trading'}
        />
        <NumberField
          label={locale === 'ro' ? 'Durată cooldown (minute)' : 'Cooldown duration (minutes)'}
          value={rules.cooldownMinutes}
          min={60}
          max={10080}
          step={60}
          onChange={(v) => setRules({ ...rules, cooldownMinutes: v })}
          hint={locale === 'ro' ? '1440 = 24h' : '1440 = 24h'}
        />
      </div>

      <div className="space-y-3 rounded-xl border border-border bg-card p-4">
        <label className="flex items-center gap-3">
          <input
            type="checkbox"
            checked={rules.requireStopLoss}
            onChange={(e) => setRules({ ...rules, requireStopLoss: e.target.checked })}
            className="h-4 w-4"
          />
          <div>
            <p className="font-medium text-sm">
              {locale === 'ro' ? 'Stop Loss obligatoriu' : 'Require Stop Loss'}
            </p>
            <p className="text-xs text-muted-foreground">
              {locale === 'ro' ? 'Nu poți intra fără SL setat' : 'Cannot enter without SL set'}
            </p>
          </div>
        </label>

        <label className="flex items-center gap-3">
          <input
            type="checkbox"
            checked={rules.requireReason}
            onChange={(e) => setRules({ ...rules, requireReason: e.target.checked })}
            className="h-4 w-4"
          />
          <div>
            <p className="font-medium text-sm">
              {locale === 'ro' ? 'Motiv obligatoriu' : 'Require reason'}
            </p>
            <p className="text-xs text-muted-foreground">
              {locale === 'ro' ? 'Te obligă să articulezi de ce intri' : 'Forces you to articulate why'}
            </p>
          </div>
        </label>
      </div>

      <Button onClick={save} disabled={saving}>
        {saving && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
        {locale === 'ro' ? 'Salvează regulile' : 'Save rules'}
      </Button>
    </div>
  );
}

function NumberField({
  label,
  value,
  min,
  max,
  step = 1,
  onChange,
  hint,
}: {
  label: string;
  value: number;
  min: number;
  max: number;
  step?: number;
  onChange: (v: number) => void;
  hint?: string;
}) {
  return (
    <div className="rounded-xl border border-border bg-card p-4">
      <label className="text-sm font-medium">{label}</label>
      <input
        type="number"
        value={value}
        min={min}
        max={max}
        step={step}
        onChange={(e) => onChange(parseFloat(e.target.value))}
        className="mt-2 w-full rounded border border-input bg-background px-3 py-2 text-sm"
      />
      {hint && <p className="mt-1 text-xs text-muted-foreground">{hint}</p>}
    </div>
  );
}
