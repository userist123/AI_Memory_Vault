'use client';

import { useState } from 'react';
import { useLocale, useTranslations } from 'next-intl';
import { useRouter, Link } from '@/lib/i18n/routing';
import { Button } from '@/components/ui/button';
import { Loader2, AlertCircle, CheckCircle2 } from 'lucide-react';

export function SignupForm() {
  const locale = useLocale() as 'ro' | 'en';
  const t = useTranslations('auth');
  const router = useRouter();

  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [name, setName] = useState('');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const passwordStrength = (pwd: string) => {
    if (pwd.length < 8) return 0;
    let score = 1;
    if (/[A-Z]/.test(pwd)) score++;
    if (/\d/.test(pwd)) score++;
    if (/[^A-Za-z0-9]/.test(pwd)) score++;
    return score;
  };

  const strength = passwordStrength(password);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);

    if (password !== confirmPassword) {
      setError(t('passwordsDontMatch'));
      return;
    }

    if (password.length < 8) {
      setError(t('passwordTooShort'));
      return;
    }

    if (!/^(?=.*[A-Za-z])(?=.*\d)/.test(password)) {
      setError(
        locale === 'ro'
          ? 'Parola trebuie să conțină cel puțin o literă și o cifră'
          : 'Password must contain at least one letter and one digit'
      );
      return;
    }

    setLoading(true);

    try {
      const res = await fetch('/api/auth/signup', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          email,
          password,
          name: name || undefined,
          language: locale,
        }),
      });

      const data = await res.json();

      if (!res.ok) {
        if (data.error === 'email_exists') {
          setError(
            locale === 'ro'
              ? 'Există deja un cont cu acest email'
              : 'An account with this email already exists'
          );
        } else if (data.error === 'validation_failed') {
          const fieldErrors = data.details;
          const firstError = Object.values(fieldErrors || {})[0];
          setError(
            Array.isArray(firstError) ? firstError[0] : 'Validation failed'
          );
        } else {
          setError(data.details || data.error || 'Signup failed');
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

  const strengthColors = ['bg-muted', 'bg-loss', 'bg-orange-500', 'bg-primary', 'bg-profit'];
  const strengthLabels = {
    ro: ['', 'Slabă', 'Medie', 'Bună', 'Puternică'],
    en: ['', 'Weak', 'Medium', 'Good', 'Strong'],
  };

  return (
    <form onSubmit={handleSubmit} className="space-y-4">
      <div>
        <label htmlFor="name" className="text-sm font-medium">
          {locale === 'ro' ? 'Nume (opțional)' : 'Name (optional)'}
        </label>
        <input
          id="name"
          type="text"
          value={name}
          onChange={(e) => setName(e.target.value)}
          disabled={loading}
          maxLength={100}
          className="mt-2 w-full rounded-md border border-input bg-background px-3 py-2 text-sm ring-offset-background focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:opacity-50"
          placeholder={locale === 'ro' ? 'Ion Popescu' : 'John Doe'}
          autoComplete="name"
        />
      </div>

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
        <label htmlFor="password" className="text-sm font-medium">
          {t('password')}
        </label>
        <input
          id="password"
          type="password"
          required
          minLength={8}
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          disabled={loading}
          className="mt-2 w-full rounded-md border border-input bg-background px-3 py-2 text-sm ring-offset-background focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:opacity-50"
          autoComplete="new-password"
        />
        {password && (
          <div className="mt-2 space-y-1">
            <div className="flex gap-1">
              {[1, 2, 3, 4].map((i) => (
                <div
                  key={i}
                  className={`h-1 flex-1 rounded ${
                    strength >= i ? strengthColors[strength] : 'bg-muted'
                  }`}
                />
              ))}
            </div>
            <p className="text-xs text-muted-foreground">
              {strengthLabels[locale][strength]}
            </p>
          </div>
        )}
        <p className="mt-1 text-xs text-muted-foreground">{t('passwordTooShort')}</p>
      </div>

      <div>
        <label htmlFor="confirmPassword" className="text-sm font-medium">
          {t('confirmPassword')}
        </label>
        <input
          id="confirmPassword"
          type="password"
          required
          minLength={8}
          value={confirmPassword}
          onChange={(e) => setConfirmPassword(e.target.value)}
          disabled={loading}
          className="mt-2 w-full rounded-md border border-input bg-background px-3 py-2 text-sm ring-offset-background focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:opacity-50"
          autoComplete="new-password"
        />
        {confirmPassword && password === confirmPassword && (
          <div className="mt-1 flex items-center gap-1 text-xs text-profit">
            <CheckCircle2 className="h-3 w-3" />
            {locale === 'ro' ? 'Parolele se potrivesc' : 'Passwords match'}
          </div>
        )}
      </div>

      {error && (
        <div className="flex items-start gap-2 rounded-md border border-destructive/50 bg-destructive/10 p-3 text-sm">
          <AlertCircle className="mt-0.5 h-4 w-4 shrink-0 text-destructive" />
          <p className="text-destructive">{error}</p>
        </div>
      )}

      <Button type="submit" className="w-full" size="lg" disabled={loading}>
        {loading && <Loader2 className="h-4 w-4 animate-spin" />}
        {t('signUp')}
      </Button>

      <div className="text-center text-sm text-muted-foreground">
        {t('alreadyHaveAccount')}{' '}
        <Link href="/login" className="font-medium text-primary hover:underline">
          {t('signIn')}
        </Link>
      </div>
    </form>
  );
}
