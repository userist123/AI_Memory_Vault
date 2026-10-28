**Pasul 1 — Mega-prompt intern** (#2 Full Startup MVP + #13 Designing Beautiful UIs + #8 Senior Frontend)

> Act like a senior full-stack engineer building a production-ready MVP for a dental clinic presentation site with online booking (#2).
> Design a responsive web landing page and booking flow for a dental clinic (#13). User goal: find a service and book a slot in under a minute.
> Design system: modern, minimal, calm medical palette (teal + warm white). Key elements: hero, services, doctors, prices, booking widget, map, reviews.
> Build reusable, accessible components with loading, empty and error states (#8).
> Stack: Next.js 15 App Router, TypeScript, Tailwind, shadcn/ui, Postgres + Drizzle, Vercel. Edge cases: double booking, time zones, closed days, no-JS fallback for contact.

**Pasul 2 — Execuție**

Arhitectura: pagini statice pentru prezentare (ISR), o rută `/programare` cu formular server action, tabela `appointments` cu index unic pe `(doctor_id, slot_start)` ca să elimine dubla rezervare la nivel de bază de date, nu doar în UI.

```ts
// db/schema.ts
export const appointments = pgTable("appointments", {
  id: uuid("id").primaryKey().defaultRandom(),
  doctorId: uuid("doctor_id").notNull(),
  slotStart: timestamp("slot_start", { withTimezone: true }).notNull(),
  patientName: text("patient_name").notNull(),
  phone: text("phone").notNull(),
}, (t) => ({ uniqSlot: uniqueIndex("uniq_slot").on(t.doctorId, t.slotStart) }));
```

Restul livrabilelor (structura fișierelor, API, componentele UI și stările lor) urmează aceeași structură.
