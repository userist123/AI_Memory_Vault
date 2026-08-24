---
name: nextjs-saas-frontend
description: Încarcă acest skill când construiești aplicații web Next.js/React (SaaS de tip trading journal, landing + app, dashboard-uri web) cu buget minim („zero-dollar stack"). Impune App Router corect, animații performante și disciplină de cost.
---

# Next.js SaaS Frontend

Construit pentru „zero-dollar stack": totul trebuie să ruleze pe tier gratuit până există venit.

## App Router — reguli de bază

- **Server Components by default;** `"use client"` doar la frunzele care au nevoie de interactivitate/hooks — nu la nivel de pagină.
- Data fetching pe server (în componente async), nu `useEffect` + fetch pe client pentru date inițiale.
- Mutații prin Server Actions cu validare pe server (Zod) — clientul nu e niciodată de încredere.
- `loading.tsx` + `error.tsx` per rută; fără pagini care „albesc" la navigare.
- Bilingv RO/EN: rute `[locale]/`, dicționare per limbă în fișiere, NU stringuri hardcodate în componente.

## Zero-dollar stack (defaulturile tale)

- Hosting: Vercel free tier. DB: Postgres gratuit (Neon/Supabase) + Drizzle/Prisma.
- Auth: Auth.js sau Supabase Auth — nu implementa auth manual.
- Atenție la limite: fără cron-uri agresive, fără imagini neoptimizate (`next/image` obligatoriu), ISR în loc de SSR unde datele permit.

## Animații (Framer Motion / GSAP) — performanță întâi

- Animează doar `transform` + `opacity`. Layout animations (`layout` prop) cu măsură — sunt scumpe pe liste mari.
- `whileInView` cu `viewport={{ once: true }}` pentru scroll-reveal; nu re-animа la fiecare scroll.
- GSAP doar unde Framer nu ajunge (timeline-uri complexe, scroll-driven); nu ambele pe aceeași componentă.
- `prefers-reduced-motion` respectat prin `useReducedMotion()`.
- Dashboard ≠ landing: în app, animațiile scad la feedback de 100-200ms; „wow"-ul e doar pe pagina de marketing.

## Structură

```
app/[locale]/(marketing)/   → landing, pricing (statice, ISR)
app/[locale]/(app)/          → dashboard-ul autentificat
components/ui/               → primitive reutilizabile (shadcn-style)
lib/                         → db, auth, validări, utils
```

## Disciplină

- TypeScript strict; zero `any` nemotivat.
- Stările de UI complete la fiecare fetch: loading / error / empty / success — toate patru proiectate.
- Lighthouse pe landing înainte de deploy: LCP < 2.5s, fără font blocking.
- Env vars prin `.env.local` + validare la boot (aplicația refuză să pornească cu config lipsă).

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[10 Imports and Sources Map]]
- [[Master_Skills_Catalog_251]]
- [[Knowledge Graph Home]]
