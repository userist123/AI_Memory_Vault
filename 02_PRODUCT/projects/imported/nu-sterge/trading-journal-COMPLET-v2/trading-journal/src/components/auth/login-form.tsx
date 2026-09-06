'use client';

import { useState } from 'react';
import { useLocale, useTranslations } from 'next-intl';
import { useRouter, Link } from '@/lib/i18n/routing';
import { Button } from '@/components/ui/button';
import { Loader2, AlertCircle } from 'lucide-react';

export function LoginForm() {
  const locale = useLocale() as 'ro' | 'en';
  const t = useTranslations('auth');
  const router = useRouter();

  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    setLoading(true);

    try {
      const res = await fetch('/api/auth/login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email, password }),
      });

      const data = await res.json();

      if (!res.ok) {
        if (data.error === 'invalid_credentials') {
          setError(
            locale === 'ro'
              ? 'Email sau parolă incorectă'
              : 'Invalid email or password'
          );
        } else if (data.error === 'validation_failed') {
          setError(t('invalidEmail'));
        } else {
          setError(data.details || data.error || 'Login failed');
        }
        return;
      }

      // Success - redirect to dashboard
      router.push('/dashboard');
      router.refresh();
    } catch (err: unknown) {
      const e = err as { message?: string };
      setError(e.message || 'Network error');
    } finally {
      setLoading(false);
    }
  };

  return (
    <form onSubmit={handleSubmit} className="space-y-4">
      <div>
        <label htmlFor="email" className="text-sm font-medium">
          {t('email')}
        </label>
        <input
          id="email"
          type="email"
          required
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          disabled={loading}
          className="mt-2 w-full rounded-md border border-input bg-background px-3 py-2 text-sm ring-offset-background focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:opacity-50"
          placeholder="you@example.com"
          autoComplete="email"
        />
      </div>

      <div>
        <div className="flex items-center justify-between">
          <label htmlFor="password" className="text-sm font-medium">
            {t('password')}
          </label>
          <Link
            href="/signup"
            className="text-xs text-primary hover:underline"
          >
            {t('dontHaveAccount')}
          </Link>
        </div>
        <input
          id="password"
          type="password"
          required
          minLength={8}
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          disabled={loading}
          className="mt-2 w-full rounded-md border border-input bg-background px-3 py-2 text-sm ring-offset-background focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:opacity-50"
          autoComplete="current-password"
        />
      </div>

      {error && (
        <div className="flex items-start gap-2 rounded-md border border-destructive/50 bg-destructive/10 p-3 text-sm">
          <AlertCircle className="mt-0.5 h-4 w-4 shrink-0 text-destructive" />
          <p className="text-destructive">{error}</p>
        </div>
      )}

      <Button type="submit" className="w-full" size="lg" disabled={loading}>
        {loading && <Loader2 className="h-4 w-4 animate-spin" />}
        {t('signIn')}
      </Button>

      <div className="text-center text-sm text-muted-foreground">
        {t('dontHaveAccount')}{' '}
        <Link href="/signup" className="font-medium text-primary hover:underline">
          {t('signUp')}
        </Link>
      </div>
    </form>
  );
}
