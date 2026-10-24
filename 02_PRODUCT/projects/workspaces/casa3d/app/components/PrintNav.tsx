'use client';
// Page navigator for the print package (/p/<id>/print): the cover and every numbered sheet, the current one
// highlighted while scrolling (IntersectionObserver), jump links, previous / next. A fixed rail on wide screens,
// a sticky bar with a collapsible list elsewhere; never printed.
import { useEffect, useRef, useState, type RefObject } from 'react';
import { activePage, pageTitle, stepPage } from '@/lib/print-nav';
import type { Translate } from '@/lib/i18n';
import Icon from './Icon';

interface Page { id: string; n: number | null; title: string }

export default function PrintNav({ root, t, ready }: { root: RefObject<HTMLElement | null>; t: Translate; ready: unknown }){
  const [pages, setPages] = useState<Page[]>([]), [cur, setCur] = useState<string | null>(null), [open, setOpen] = useState(false);
  const curRef = useRef<string | null>(null), listId = 'pnav-list';
  // read the pages after every render of the package (titles depend on language and data); stable ids for jump links
  useEffect(() => { const el = root.current; if (!el) return;
    const cover = el.querySelector<HTMLElement>('[data-testid="cover"]'), sheets = [...el.querySelectorAll<HTMLElement>('section.print-break')];
    const next: Page[] = [];
    if (cover){ if (!cover.id) cover.id = 'pg-cover'; next.push({ id: cover.id, n: null, title: t('pnav.cover') }); }
    sheets.forEach((s, k) => { if (!s.id) s.id = `pg-${k + 1}`; next.push({ id: s.id, n: k + 1, title: pageTitle(s.dataset.title, s.querySelector('h2')?.firstChild?.textContent, t('pnav.page', { n: k + 1 })) }); });
    setPages(p => p.length === next.length && p.every((x, i) => x.id === next[i]!.id && x.title === next[i]!.title) ? p : next); });
  // current page: the first one crossing a band a little below the top of the window
  useEffect(() => { const el = root.current; if (!el || !pages.length || typeof IntersectionObserver === 'undefined') return;
    const order = pages.map(p => p.id), visible = new Set<string>();
    const io = new IntersectionObserver(es => { for (const e of es){ if (e.isIntersecting) visible.add(e.target.id); else visible.delete(e.target.id); }
      const a = activePage(order, visible, curRef.current); if (a !== curRef.current){ curRef.current = a; setCur(a); } }, { rootMargin: '-20% 0px -70% 0px' });
    for (const id of order){ const s = document.getElementById(id); if (s) io.observe(s); }
    return () => io.disconnect(); }, [pages, root, ready]);
  // Escape closes the list; the active link stays in view in the rail
  useEffect(() => { if (!open) return; const k = (e: KeyboardEvent) => { if (e.key === 'Escape'){ setOpen(false); document.getElementById('pnav-toggle')?.focus(); } };
    addEventListener('keydown', k); return () => removeEventListener('keydown', k); }, [open]);
  useEffect(() => { if (!cur) return; const a = document.querySelector<HTMLElement>(`.pnav-list a[href="#${cur}"]`); a?.scrollIntoView?.({ block: 'nearest' }); }, [cur]);
  if (pages.length < 2) return null;
  const go = (id: string | null) => { if (!id) return; const s = document.getElementById(id); if (!s) return;
    const reduce = typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches;
    s.scrollIntoView({ behavior: reduce ? 'auto' : 'smooth', block: 'start' }); if (!s.hasAttribute('tabindex')) s.setAttribute('tabindex', '-1'); s.focus({ preventScroll: true });
    curRef.current = id; setCur(id); setOpen(false); history.replaceState?.(null, '', `#${id}`); };
  const order = pages.map(p => p.id), idx = cur ? order.indexOf(cur) : 0, page = pages[Math.max(0, idx)]!;
  const label = (p: Page) => p.n == null ? p.title : `${String(p.n).padStart(2, '0')} · ${p.title}`;
  return (<nav className={`pnav no-print ${open ? 'is-open' : ''}`} aria-label={t('pnav.aria')}>
    <div className="pnav-bar">
      <button type="button" id="pnav-toggle" className="pnav-toggle" aria-expanded={open} aria-controls={listId} onClick={() => setOpen(o => !o)}>
        <span className="pnav-count">{t('pnav.count', { n: idx + 1, total: pages.length })}</span><span className="pnav-cur">{page.title}</span><Icon name={open ? 'chevronUp' : 'chevronDown'} size={16} /></button>
      <button type="button" className="btn ghost icon-btn" aria-label={t('pnav.prev')} title={t('pnav.prev')} disabled={!stepPage(order, cur ?? order[0]!, -1)} onClick={() => go(stepPage(order, cur ?? order[0]!, -1))}><Icon name="chevronLeft" /></button>
      <button type="button" className="btn ghost icon-btn" aria-label={t('pnav.next')} title={t('pnav.next')} disabled={!stepPage(order, cur ?? order[0]!, 1)} onClick={() => go(stepPage(order, cur ?? order[0]!, 1))}><Icon name="chevronRight" /></button>
    </div>
    <p className="pnav-title" aria-hidden="true">{t('pnav.title')}</p>
    <ol id={listId} className="pnav-list">{pages.map(p => <li key={p.id}><a href={`#${p.id}`} aria-current={p.id === (cur ?? order[0]) ? 'page' : undefined}
      onClick={e => { e.preventDefault(); go(p.id); }}><i aria-hidden="true">{p.n == null ? '—' : String(p.n).padStart(2, '0')}</i><span>{p.title}</span><span className="sr-only">{p.n == null ? '' : ` (${label(p)})`}</span></a></li>)}</ol>
  </nav>);
}
