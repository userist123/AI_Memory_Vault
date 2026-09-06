'use client';

import { useEffect, useState } from 'react';
import { useLocale } from 'next-intl';
import { Button } from '@/components/ui/button';
import { Mail, MessageCircle, Bell, Loader2, CheckCircle2, ExternalLink } from 'lucide-react';

interface Prefs {
  email: { enabled: boolean; address: string };
  telegram: { enabled: boolean; chatId?: string; linkedAt?: string };
  inApp: { enabled: boolean };
  filters: {
    minStrength: number;
    minRiskReward: number;
  };
}

export default function NotificationSettings() {
  const locale = useLocale() as 'ro' | 'en';
  const [prefs, setPrefs] = useState<Prefs | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [telegramLink, setTelegramLink] = useState<string | null>(null);

  useEffect(() => {
    fetch('/api/alerts/preferences')
      .then((r) => r.json())
      .then((d) => { setPrefs(d.prefs); setLoading(false); });
  }, []);

  const save = async () => {
    if (!prefs) return;
    setSaving(true);
    await fetch('/api/alerts/preferences', {
      method: 'PATCH',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(prefs),
    });
    setSaving(false);
  };

  const linkTelegram = async () => {
    const res = await fetch('/api/alerts/telegram-link', { method: 'POST' });
    const data = await res.json();
    setTelegramLink(data.deepLink);
  };

  if (loading || !prefs) {
    return <div className="flex h-32 items-center justify-center"><Loader2 className="h-6 w-6 animate-spin" /></div>;
  }

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold">
          {locale === 'ro' ? 'Notificări' : 'Notifications'}
        </h1>
        <p className="mt-1 text-sm text-muted-foreground">
          {locale === 'ro' ? 'Unde vrei să primești alerte de trading?' : 'Where do you want trading alerts?'}
        </p>
      </div>

      {/* Channels */}
      <div className="space-y-3">
        <div className="rounded-xl border border-border bg-card p-4">
          <div className="flex items-start justify-between gap-3">
            <div className="flex items-start gap-3">
              <div className="flex h-10 w-10 items-center justify-center rounded-lg bg-primary/10">
                <Mail className="h-5 w-5 text-primary" />
              </div>
              <div>
                <h3 className="font-semibold">Email</h3>
                <p className="text-xs text-muted-foreground">
                  {locale === 'ro' ? '3000 emails/lună gratis prin Resend' : '3000 emails/month free via Resend'}
                </p>
              </div>
            </div>
            <label className="flex items-center gap-2">
              <input
                type="checkbox"
                checked={prefs.email.enabled}
                onChange={(e) => setPrefs({ ...prefs, email: { ...prefs.email, enabled: e.target.checked } })}
                className="h-4 w-4"
              />
              <span className="text-sm">{locale === 'ro' ? 'Activ' : 'On'}</span>
            </label>
          </div>
          <input
            value={prefs.email.address}
            onChange={(e) => setPrefs({ ...prefs, email: { ...prefs.email, address: e.target.value } })}
            placeholder={locale === 'ro' ? 'Email destinatar (gol = contul tău)' : 'Recipient email (blank = your account)'}
            className="mt-3 w-full rounded border border-input bg-background px-3 py-2 text-sm"
          />
        </div>

        <div className="rounded-xl border border-border bg-card p-4">
          <div className="flex items-start justify-between gap-3">
            <div className="flex items-start gap-3">
              <div className="flex h-10 w-10 items-center justify-center rounded-lg bg-blue-500/10">
                <MessageCircle className="h-5 w-5 text-blue-500" />
              </div>
              <div>
                <h3 className="font-semibold">Telegram</h3>
                <p className="text-xs text-muted-foreground">
                  {prefs.telegram.linkedAt
                    ? (locale === 'ro' ? 'Conectat' : 'Connected')
                    : (locale === 'ro' ? 'Neconectat' : 'Not connected')}
                </p>
              </div>
            </div>
            <label className="flex items-center gap-2">
              <input
                type="checkbox"
                checked={prefs.telegram.enabled}
                onChange={(e) => setPrefs({ ...prefs, telegram: { ...prefs.telegram, enabled: e.target.checked } })}
                className="h-4 w-4"
                disabled={!prefs.telegram.chatId}
              />
              <span className="text-sm">{locale === 'ro' ? 'Activ' : 'On'}</span>
            </label>
          </div>

          {!prefs.telegram.chatId ? (
            <div className="mt-3 space-y-2">
              <Button size="sm" onClick={linkTelegram}>
                {locale === 'ro' ? 'Conectează Telegram' : 'Link Telegram'}
              </Button>
              {telegramLink && (
                <div className="rounded border border-primary/30 bg-primary/5 p-3 text-sm">
                  <p className="mb-2 font-medium">
                    {locale === 'ro' ? '1. Deschide acest link în Telegram:' : '1. Open this link in Telegram:'}
                  </p>
                  <a
                    href={telegramLink}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="flex items-center gap-1 break-all text-primary hover:underline"
                  >
                    {telegramLink}
                    <ExternalLink className="h-3 w-3" />
                  </a>
                  <p className="mt-2 text-xs text-muted-foreground">
                    {locale === 'ro' ? '2. Apasă Start în bot. Link-ul expiră în 15 min.' : '2. Press Start in bot. Link expires in 15 min.'}
                  </p>
                </div>
              )}
            </div>
          ) : (
            <div className="mt-3 flex items-center gap-2 rounded bg-profit/5 p-2 text-sm">
              <CheckCircle2 className="h-4 w-4 text-profit" />
              <span>Chat ID: {prefs.telegram.chatId}</span>
            </div>
          )}
        </div>

        <div className="rounded-xl border border-border bg-card p-4">
          <div className="flex items-start justify-between gap-3">
            <div className="flex items-start gap-3">
              <div className="flex h-10 w-10 items-center justify-center rounded-lg bg-purple-500/10">
                <Bell className="h-5 w-5 text-purple-500" />
              </div>
              <div>
                <h3 className="font-semibold">In-app</h3>
                <p className="text-xs text-muted-foreground">
                  {locale === 'ro' ? 'Vezi în pagina Semnale' : 'See in Signals page'}
                </p>
              </div>
            </div>
            <label className="flex items-center gap-2">
              <input
                type="checkbox"
                checked={prefs.inApp.enabled}
                onChange={(e) => setPrefs({ ...prefs, inApp: { enabled: e.target.checked } })}
                className="h-4 w-4"
              />
              <span className="text-sm">{locale === 'ro' ? 'Activ' : 'On'}</span>
            </label>
          </div>
        </div>
      </div>

      {/* Filters */}
      <div className="rounded-xl border border-border bg-card p-4">
        <h3 className="mb-3 font-semibold">
          {locale === 'ro' ? 'Filtre alerte' : 'Alert filters'}
        </h3>
        <div className="grid gap-4 md:grid-cols-2">
          <div>
            <label className="text-xs font-medium text-muted-foreground">
              {locale === 'ro' ? 'Putere minimă semnal' : 'Min signal strength'}: {prefs.filters.minStrength}
            </label>
            <input
              type="range"
              min={50}
              max={90}
              value={prefs.filters.minStrength}
              onChange={(e) => setPrefs({ ...prefs, filters: { ...prefs.filters, minStrength: parseInt(e.target.value) } })}
              className="mt-1 w-full"
            />
          </div>
          <div>
            <label className="text-xs font-medium text-muted-foreground">
              Min R/R: {prefs.filters.minRiskReward.toFixed(1)}
            </label>
            <input
              type="range"
              min={1}
              max={5}
              step={0.5}
              value={prefs.filters.minRiskReward}
              onChange={(e) => setPrefs({ ...prefs, filters: { ...prefs.filters, minRiskReward: parseFloat(e.target.value) } })}
              className="mt-1 w-full"
            />
          </div>
        </div>
      </div>

      <Button onClick={save} disabled={saving}>
        {saving && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
        {locale === 'ro' ? 'Salvează preferințele' : 'Save preferences'}
      </Button>
    </div>
  );
}
