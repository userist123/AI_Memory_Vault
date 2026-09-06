'use client';

import { useEffect, useRef, useState } from 'react';
import { useLocale } from 'next-intl';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';
import { Send, Loader2, Sparkles, Receipt, MessageCircle } from 'lucide-react';

interface Message {
  role: 'user' | 'assistant';
  content: string;
}

const SUGGESTED_QUESTIONS_RO = [
  'Cât impozit am de plătit anul ăsta?',
  'Ce secțiuni trebuie să completez în D212?',
  'Dacă vând 1 BTC acum, cât impozit plătesc?',
  'Trebuie să plătesc CASS?',
];

const SUGGESTED_QUESTIONS_EN = [
  'How much tax do I owe this year?',
  'Which D212 sections do I fill?',
  'If I sell 1 BTC now, how much tax?',
  'Do I need to pay CASS?',
];

export function FiscalChatView() {
  const locale = useLocale() as 'ro' | 'en';
  const [messages, setMessages] = useState<Message[]>([]);
  const [input, setInput] = useState('');
  const [loading, setLoading] = useState(false);
  const [year, setYear] = useState(new Date().getFullYear() - 1);
  const scrollRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    scrollRef.current?.scrollTo({ top: scrollRef.current.scrollHeight, behavior: 'smooth' });
  }, [messages]);

  const send = async (text?: string) => {
    const message = text ?? input.trim();
    if (!message || loading) return;

    const userMsg: Message = { role: 'user', content: message };
    const newMessages = [...messages, userMsg];
    setMessages(newMessages);
    setInput('');
    setLoading(true);

    try {
      const res = await fetch('/api/fiscal/chat', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          message,
          history: messages.slice(-6), // last 3 exchanges
          year,
        }),
      });
      const data = await res.json();

      if (!res.ok) {
        setMessages([
          ...newMessages,
          {
            role: 'assistant',
            content: data.message || data.details || `Eroare: ${data.error}`,
          },
        ]);
        return;
      }

      setMessages([...newMessages, { role: 'assistant', content: data.reply }]);
    } catch (err: unknown) {
      const e = err as { message?: string };
      setMessages([...newMessages, { role: 'assistant', content: `Eroare: ${e.message}` }]);
    } finally {
      setLoading(false);
    }
  };

  const currentYear = new Date().getFullYear();
  const suggestions = locale === 'ro' ? SUGGESTED_QUESTIONS_RO : SUGGESTED_QUESTIONS_EN;

  return (
    <div className="flex h-[calc(100vh-200px)] flex-col rounded-xl border border-border bg-card">
      {/* Header */}
      <div className="flex items-center justify-between border-b border-border p-4">
        <div className="flex items-center gap-2">
          <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-primary/10">
            <Receipt className="h-4 w-4 text-primary" />
          </div>
          <div>
            <h3 className="text-sm font-semibold">
              {locale === 'ro' ? 'Consultant fiscal AI' : 'AI fiscal advisor'}
            </h3>
            <p className="text-xs text-muted-foreground">
              {locale === 'ro' ? 'Bazat pe datele tale reale' : 'Based on your actual data'}
            </p>
          </div>
        </div>
        <select
          value={year}
          onChange={(e) => setYear(parseInt(e.target.value))}
          className="rounded border border-input bg-background px-2 py-1 text-xs"
        >
          {[currentYear, currentYear - 1, currentYear - 2].map((y) => (
            <option key={y} value={y}>{y}</option>
          ))}
        </select>
      </div>

      {/* Messages */}
      <div ref={scrollRef} className="flex-1 overflow-y-auto p-4">
        {messages.length === 0 ? (
          <div className="flex h-full flex-col items-center justify-center gap-4 text-center">
            <Sparkles className="h-10 w-10 text-muted-foreground/30" />
            <p className="max-w-md text-sm text-muted-foreground">
              {locale === 'ro'
                ? 'Întreabă-mă orice despre impozitele tale pe trading. Cunosc legislația RO 2025-2026 și am acces la datele tale din jurnal.'
                : 'Ask me anything about your trading taxes. I know RO 2025-2026 legislation and have access to your journal data.'}
            </p>
            <div className="flex flex-wrap justify-center gap-2">
              {suggestions.map((q) => (
                <button
                  key={q}
                  onClick={() => send(q)}
                  disabled={loading}
                  className="rounded-full border border-border bg-background px-3 py-1.5 text-xs transition-colors hover:border-primary/50 hover:bg-primary/5"
                >
                  {q}
                </button>
              ))}
            </div>
          </div>
        ) : (
          <div className="space-y-4">
            {messages.map((m, i) => (
              <div
                key={i}
                className={cn(
                  'flex gap-3',
                  m.role === 'user' ? 'flex-row-reverse' : ''
                )}
              >
                <div
                  className={cn(
                    'flex h-8 w-8 shrink-0 items-center justify-center rounded-lg',
                    m.role === 'user' ? 'bg-primary text-primary-foreground' : 'bg-muted'
                  )}
                >
                  {m.role === 'user' ? (
                    <span className="text-xs font-semibold">Tu</span>
                  ) : (
                    <Sparkles className="h-4 w-4" />
                  )}
                </div>
                <div
                  className={cn(
                    'max-w-[80%] rounded-lg p-3 text-sm',
                    m.role === 'user'
                      ? 'bg-primary text-primary-foreground'
                      : 'bg-muted/50'
                  )}
                >
                  <p className="whitespace-pre-wrap">{m.content}</p>
                </div>
              </div>
            ))}
            {loading && (
              <div className="flex gap-3">
                <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-muted">
                  <Sparkles className="h-4 w-4 animate-pulse" />
                </div>
                <div className="flex items-center gap-1 rounded-lg bg-muted/50 p-3">
                  <div className="h-1.5 w-1.5 animate-bounce rounded-full bg-muted-foreground" style={{ animationDelay: '0ms' }} />
                  <div className="h-1.5 w-1.5 animate-bounce rounded-full bg-muted-foreground" style={{ animationDelay: '150ms' }} />
                  <div className="h-1.5 w-1.5 animate-bounce rounded-full bg-muted-foreground" style={{ animationDelay: '300ms' }} />
                </div>
              </div>
            )}
          </div>
        )}
      </div>

      {/* Input */}
      <div className="border-t border-border p-4">
        <div className="flex gap-2">
          <input
            value={input}
            onChange={(e) => setInput(e.target.value)}
            onKeyDown={(e) => e.key === 'Enter' && !e.shiftKey && (e.preventDefault(), send())}
            placeholder={locale === 'ro' ? 'Întreabă despre impozite...' : 'Ask about taxes...'}
            disabled={loading}
            className="flex-1 rounded-lg border border-input bg-background px-3 py-2 text-sm"
          />
          <Button onClick={() => send()} disabled={loading || !input.trim()} size="icon">
            <Send className="h-4 w-4" />
          </Button>
        </div>
        <p className="mt-2 text-[10px] text-muted-foreground">
          {locale === 'ro'
            ? 'AI-ul poate greși. Verifică cu un contabil pentru decizii importante.'
            : 'AI can make mistakes. Verify with an accountant for important decisions.'}
        </p>
      </div>
    </div>
  );
}
