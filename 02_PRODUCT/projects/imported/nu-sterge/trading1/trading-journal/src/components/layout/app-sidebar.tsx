'use client';

import { useTranslations } from 'next-intl';
import { Link, usePathname, useRouter } from '@/lib/i18n/routing';
import { LanguageSwitcher } from '@/components/layout/language-switcher';
import { ThemeToggle } from '@/components/layout/theme-toggle';
import { UsageIndicator } from '@/components/billing/usage-indicator';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';
import type { PublicUser } from '@/types/user';
import {
  LayoutDashboard,
  LineChart,
  BookOpen,
  TrendingUp,
  Calendar,
  Calculator,
  Settings,
  LogOut,
  User as UserIcon,
  Sparkles,
  Receipt,
  Zap,
  PlayCircle,
  MessageCircle,
  HeartHandshake,
} from 'lucide-react';

const navItems = [
  { href: '/dashboard', icon: LayoutDashboard, key: 'dashboard' },
  { href: '/trades', icon: LineChart, key: 'trades' },
  { href: '/signals', icon: Zap, key: 'signals' },
  { href: '/backtest', icon: PlayCircle, key: 'backtest' },
  { href: '/journal', icon: BookOpen, key: 'journal' },
  { href: '/analytics', icon: TrendingUp, key: 'analytics' },
  { href: '/fiscal', icon: Receipt, key: 'fiscal' },
  { href: '/fiscal-chat', icon: MessageCircle, key: 'fiscalChat' },
  { href: '/consulting', icon: HeartHandshake, key: 'consulting' },
  { href: '/calendar', icon: Calendar, key: 'calendar' },
  { href: '/calculators', icon: Calculator, key: 'calculators' },
  { href: '/pricing', icon: Sparkles, key: 'pricing' },
  { href: '/settings', icon: Settings, key: 'settings' },
] as const;

export function AppSidebar({ user }: { user: PublicUser }) {
  const t = useTranslations('nav');
  const tApp = useTranslations('app');
  const pathname = usePathname();
  const router = useRouter();

  const handleLogout = async () => {
    try {
      await fetch('/api/auth/logout', { method: 'POST' });
    } catch (err) {
      console.warn('Logout error:', err);
    }
    router.push('/login');
    router.refresh();
  };

  const displayName = user.name || user.email.split('@')[0];
  const planBadge = {
    free: { label: 'Free', class: 'bg-muted text-muted-foreground' },
    pro: { label: 'Pro', class: 'bg-primary text-primary-foreground' },
    elite: { label: 'Elite', class: 'bg-gradient-to-r from-primary to-profit text-primary-foreground' },
  };
  const badge = planBadge[user.plan];

  return (
    <aside className="hidden w-64 flex-col border-r border-border/40 bg-card md:flex">
      {/* Brand */}
      <div className="flex h-16 items-center gap-2 border-b border-border/40 px-6">
        <div className="flex h-8 w-8 items-center justify-center rounded-md bg-primary">
          <TrendingUp className="h-5 w-5 text-primary-foreground" />
        </div>
        <span className="font-bold">{tApp('name')}</span>
      </div>

      {/* User info */}
      <div className="border-b border-border/40 p-4">
        <div className="flex items-center gap-3">
          <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-primary/10">
            <UserIcon className="h-4 w-4 text-primary" />
          </div>
          <div className="min-w-0 flex-1">
            <p className="truncate text-sm font-medium">{displayName}</p>
            <div className="flex items-center gap-2">
              <span className={cn('rounded px-1.5 py-0.5 text-[10px] font-semibold uppercase', badge.class)}>
                {badge.label}
              </span>
            </div>
          </div>
        </div>
      </div>

      {/* Navigation */}
      <nav className="flex-1 space-y-1 p-4">
        {navItems.map(({ href, icon: Icon, key }) => {
          const isActive = pathname === href;
          return (
            <Link
              key={href}
              href={href}
              className={cn(
                'flex items-center gap-3 rounded-lg px-3 py-2 text-sm font-medium transition-colors',
                isActive
                  ? 'bg-primary/10 text-primary'
                  : 'text-muted-foreground hover:bg-accent hover:text-foreground'
              )}
            >
              <Icon className="h-4 w-4" />
              {t(key)}
            </Link>
          );
        })}
      </nav>

      {/* Usage indicator (only for Free plan) */}
      <UsageIndicator />

      {/* Footer: theme + language + logout */}
      <div className="border-t border-border/40 p-4">
        <div className="mb-3 flex items-center gap-2">
          <LanguageSwitcher />
          <ThemeToggle />
        </div>
        <Button
          variant="ghost"
          size="sm"
          className="w-full justify-start gap-2"
          onClick={handleLogout}
        >
          <LogOut className="h-4 w-4" />
          {t('logout')}
        </Button>
      </div>
    </aside>
  );
}
