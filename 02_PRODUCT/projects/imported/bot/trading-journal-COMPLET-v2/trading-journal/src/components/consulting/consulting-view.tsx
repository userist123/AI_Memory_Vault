'use client';

import { useEffect, useState } from 'react';
import { useLocale } from 'next-intl';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';
import { Plus, MessageCircle, Clock, CheckCircle2, Loader2, User as UserIcon, Shield } from 'lucide-react';
import type { ConsultingTicket } from '@/lib/db/consulting';

const CATEGORIES = [
  { value: 'fiscal', labelRo: 'Fiscal / ANAF', labelEn: 'Tax / ANAF' },
  { value: 'trading', labelRo: 'Trading / strategie', labelEn: 'Trading / strategy' },
  { value: 'technical', labelRo: 'Tehnic / cont', labelEn: 'Technical / account' },
  { value: 'billing', labelRo: 'Facturare / plan', labelEn: 'Billing / plan' },
  { value: 'other', labelRo: 'Alt subiect', labelEn: 'Other' },
] as const;

const PRIORITIES = [
  { value: 'low', labelRo: 'Scăzută', labelEn: 'Low' },
  { value: 'normal', labelRo: 'Normală', labelEn: 'Normal' },
  { value: 'high', labelRo: 'Ridicată', labelEn: 'High' },
  { value: 'urgent', labelRo: 'Urgent', labelEn: 'Urgent' },
] as const;

export function ConsultingView({ adminMode = false }: { adminMode?: boolean }) {
  const locale = useLocale() as 'ro' | 'en';
  const [tickets, setTickets] = useState<ConsultingTicket[]>([]);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [selected, setSelected] = useState<ConsultingTicket | null>(null);
  const [loading, setLoading] = useState(true);
  const [showNew, setShowNew] = useState(false);

  const load = async () => {
    setLoading(true);
    const url = adminMode ? '/api/consulting/tickets?admin=true' : '/api/consulting/tickets';
    const res = await fetch(url);
    const data = await res.json();
    setTickets(data.tickets || []);
    setLoading(false);
  };

  const loadOne = async (id: string) => {
    const res = await fetch(`/api/consulting/tickets?id=${id}`);
    const data = await res.json();
    if (data.ticket) setSelected(data.ticket);
  };

  useEffect(() => { load(); }, [adminMode]);
  useEffect(() => {
    if (selectedId) loadOne(selectedId);
  }, [selectedId]);

  return (
    <div className="grid gap-4 lg:grid-cols-[340px_1fr]">
      {/* Sidebar */}
      <div className="rounded-xl border border-border bg-card">
        <div className="flex items-center justify-between border-b border-border p-3">
          <h3 className="text-sm font-semibold">
            {adminMode
              ? (locale === 'ro' ? 'Toate ticketele' : 'All tickets')
              : (locale === 'ro' ? 'Ticketele mele' : 'My tickets')}
          </h3>
          {!adminMode && (
            <Button size="sm" onClick={() => setShowNew(true)} className="gap-1">
              <Plus className="h-3.5 w-3.5" />
              {locale === 'ro' ? 'Nou' : 'New'}
            </Button>
          )}
        </div>
        <div className="max-h-[calc(100vh-280px)] overflow-y-auto">
          {loading ? (
            <div className="flex h-32 items-center justify-center">
              <Loader2 className="h-5 w-5 animate-spin text-muted-foreground" />
            </div>
          ) : tickets.length === 0 ? (
            <p className="p-6 text-center text-sm text-muted-foreground">
              {locale === 'ro' ? 'Niciun ticket' : 'No tickets'}
            </p>
          ) : (
            tickets.map((t) => (
              <button
                key={t._id as string}
                onClick={() => setSelectedId(t._id as string)}
                className={cn(
                  'flex w-full flex-col gap-1 border-b border-border/40 p-3 text-left transition-colors hover:bg-muted/30',
                  selectedId === t._id && 'bg-primary/5'
                )}
              >
                <div className="flex items-center justify-between gap-2">
                  <span className="truncate text-sm font-medium">{t.subject}</span>
                  <StatusBadge status={t.status} />
                </div>
                <div className="flex items-center gap-2 text-xs text-muted-foreground">
                  <span>{t.category}</span>
                  <span>·</span>
                  <span>{t.priority}</span>
                  {adminMode && <>
                    <span>·</span>
                    <span>{t.userEmail}</span>
                  </>}
                </div>
              </button>
            ))
          )}
        </div>
      </div>

      {/* Ticket detail or new form */}
      <div className="rounded-xl border border-border bg-card p-4">
        {showNew ? (
          <NewTicketForm onClose={() => setShowNew(false)} onCreated={(id) => { setShowNew(false); setSelectedId(id); load(); }} locale={locale} />
        ) : selected ? (
          <TicketDetail
            ticket={selected}
            adminMode={adminMode}
            onReply={async () => { await loadOne(selected._id as string); await load(); }}
            locale={locale}
          />
        ) : (
          <div className="flex h-48 items-center justify-center text-center text-sm text-muted-foreground">
            {locale === 'ro' ? 'Selectează un ticket sau creează unul nou' : 'Select a ticket or create a new one'}
          </div>
        )}
      </div>
    </div>
  );
}

function StatusBadge({ status }: { status: ConsultingTicket['status'] }) {
  const configs: Record<ConsultingTicket['status'], { bg: string; text: string; label: string }> = {
    open: { bg: 'bg-blue-500/10', text: 'text-blue-500', label: 'Open' },
    waiting_owner: { bg: 'bg-orange-500/10', text: 'text-orange-500', label: '→ Suport' },
    waiting_user: { bg: 'bg-purple-500/10', text: 'text-purple-500', label: '→ Tu' },
    resolved: { bg: 'bg-profit/10', text: 'text-profit', label: 'Rezolvat' },
    closed: { bg: 'bg-muted/30', text: 'text-muted-foreground', label: 'Închis' },
  };
  const c = configs[status];
  return <span className={`rounded px-1.5 py-0.5 text-[10px] font-medium ${c.bg} ${c.text}`}>{c.label}</span>;
}

function NewTicketForm({
  onClose,
  onCreated,
  locale,
}: {
  onClose: () => void;
  onCreated: (id: string) => void;
  locale: 'ro' | 'en';
}) {
  const [subject, setSubject] = useState('');
  const [message, setMessage] = useState('');
  const [category, setCategory] = useState<'fiscal' | 'trading' | 'technical' | 'billing' | 'other'>('fiscal');
  const [priority, setPriority] = useState<'low' | 'normal' | 'high' | 'urgent'>('normal');
  const [submitting, setSubmitting] = useState(false);

  const submit = async () => {
    if (subject.length < 3 || message.length < 10) return;
    setSubmitting(true);
    const res = await fetch('/api/consulting/tickets', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ subject, message, category, priority }),
    });
    const data = await res.json();
    if (res.ok) onCreated(data.ticketId);
    setSubmitting(false);
  };

  return (
    <div className="space-y-4">
      <h3 className="font-semibold">
        {locale === 'ro' ? 'Ticket nou' : 'New ticket'}
      </h3>
      <input
        value={subject}
        onChange={(e) => setSubject(e.target.value)}
        placeholder={locale === 'ro' ? 'Subiect (min 3 caractere)' : 'Subject (min 3 chars)'}
        className="w-full rounded border border-input bg-background px-3 py-2 text-sm"
      />
      <div className="grid grid-cols-2 gap-3">
        <select value={category} onChange={(e) => setCategory(e.target.value as typeof category)} className="rounded border border-input bg-background px-3 py-2 text-sm">
          {CATEGORIES.map((c) => (
            <option key={c.value} value={c.value}>
              {locale === 'ro' ? c.labelRo : c.labelEn}
            </option>
          ))}
        </select>
        <select value={priority} onChange={(e) => setPriority(e.target.value as typeof priority)} className="rounded border border-input bg-background px-3 py-2 text-sm">
          {PRIORITIES.map((p) => (
            <option key={p.value} value={p.value}>
              {locale === 'ro' ? p.labelRo : p.labelEn}
            </option>
          ))}
        </select>
      </div>
      <textarea
        value={message}
        onChange={(e) => setMessage(e.target.value)}
        placeholder={locale === 'ro' ? 'Descrie problema... (min 10 caractere)' : 'Describe your issue... (min 10 chars)'}
        rows={6}
        className="w-full rounded border border-input bg-background px-3 py-2 text-sm"
      />
      <div className="flex gap-2">
        <Button onClick={submit} disabled={submitting || subject.length < 3 || message.length < 10}>
          {submitting && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
          {locale === 'ro' ? 'Trimite' : 'Send'}
        </Button>
        <Button variant="outline" onClick={onClose}>
          {locale === 'ro' ? 'Anulează' : 'Cancel'}
        </Button>
      </div>
    </div>
  );
}

function TicketDetail({
  ticket,
  adminMode,
  onReply,
  locale,
}: {
  ticket: ConsultingTicket;
  adminMode: boolean;
  onReply: () => void;
  locale: 'ro' | 'en';
}) {
  const [reply, setReply] = useState('');
  const [sending, setSending] = useState(false);

  const send = async () => {
    if (!reply.trim()) return;
    setSending(true);
    await fetch('/api/consulting/tickets', {
      method: 'PATCH',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ ticketId: ticket._id, message: reply }),
    });
    setReply('');
    setSending(false);
    onReply();
  };

  const resolve = async () => {
    await fetch('/api/consulting/tickets', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ ticketId: ticket._id, status: 'resolved' }),
    });
    onReply();
  };

  return (
    <div className="space-y-4">
      <div className="flex items-start justify-between gap-3">
        <div>
          <h3 className="font-semibold">{ticket.subject}</h3>
          <div className="mt-1 flex flex-wrap gap-2 text-xs text-muted-foreground">
            <span className="rounded bg-muted px-2 py-0.5">{ticket.category}</span>
            <span className="rounded bg-muted px-2 py-0.5">{ticket.priority}</span>
            <StatusBadge status={ticket.status} />
            {adminMode && <span>{ticket.userEmail} · {ticket.userPlan}</span>}
          </div>
        </div>
        {adminMode && ticket.status !== 'resolved' && (
          <Button size="sm" variant="outline" onClick={resolve} className="gap-1">
            <CheckCircle2 className="h-3.5 w-3.5" />
            {locale === 'ro' ? 'Rezolvă' : 'Resolve'}
          </Button>
        )}
      </div>

      {/* Messages */}
      <div className="max-h-96 space-y-3 overflow-y-auto rounded-lg border border-border bg-muted/20 p-3">
        {ticket.messages.map((m, i) => (
          <div key={i} className={cn('flex gap-2', m.from === 'user' ? '' : 'flex-row-reverse')}>
            <div className={cn(
              'flex h-7 w-7 shrink-0 items-center justify-center rounded-full',
              m.from === 'user' ? 'bg-blue-500/20 text-blue-500' : 'bg-primary/20 text-primary'
            )}>
              {m.from === 'user' ? <UserIcon className="h-3.5 w-3.5" /> : <Shield className="h-3.5 w-3.5" />}
            </div>
            <div className={cn(
              'max-w-[80%] rounded-lg p-3 text-sm',
              m.from === 'user' ? 'bg-muted/50' : 'bg-primary/10'
            )}>
              <p className="whitespace-pre-wrap">{m.content}</p>
              <p className="mt-1 text-[10px] text-muted-foreground">
                {new Date(m.createdAt).toLocaleString()}
              </p>
            </div>
          </div>
        ))}
      </div>

      {/* Reply */}
      {ticket.status !== 'resolved' && ticket.status !== 'closed' && (
        <div className="space-y-2">
          <textarea
            value={reply}
            onChange={(e) => setReply(e.target.value)}
            placeholder={locale === 'ro' ? 'Răspunde...' : 'Reply...'}
            rows={3}
            className="w-full rounded border border-input bg-background px-3 py-2 text-sm"
          />
          <Button onClick={send} disabled={sending || !reply.trim()}>
            {sending && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
            {locale === 'ro' ? 'Trimite răspuns' : 'Send reply'}
          </Button>
        </div>
      )}
    </div>
  );
}
