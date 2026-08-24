---
name: dashboard-admin-ui
description: Încarcă acest skill când construiești un dashboard, admin panel, SOC command center sau orice UI dens în date (LogAnalyzer, trading journal, monitoring). Impune ierarhie informațională, densitate controlată și pattern-uri standard de admin.
---

# Dashboard / Admin UI

Un dashboard răspunde în 5 secunde la: „E totul OK? Dacă nu, unde e problema?" Tot restul e secundar.

## Ierarhia informațională (de sus în jos)

1. **Rândul de KPI** — 3-5 carduri max: valoare mare bold, label mic muted, delta colorată (verde/teal sus, roșu jos), sparkline opțional. Nu 12 KPI-uri — dacă totul e critic, nimic nu e.
2. **Zona de tendințe** — 1-2 grafice principale (line/bar), cel mai important ocupă 60-70% din lățime.
3. **Zona de detaliu** — tabel cu date brute, filtrabil, paginat. Detaliul se cere, nu se afișează by default.
4. **Alertele plutesc deasupra ierarhiei** — banner/badge doar pentru stări anormale; zero alerte vizibile când totul e OK.

## Reguli de densitate

- Dark mode e default legitim pentru monitoring/SOC (sesiuni lungi, contrast cu alertele).
- Spacing mai strâns decât la site-uri de marketing: 8/12/16px între elemente, dar NICIODATĂ text sub 12px.
- `tabular-nums` obligatoriu pe orice coloană numerică; aliniere la dreapta pentru numere, la stânga pentru text.
- Timestamp-uri relative („acum 3 min") cu absolute la hover.
- Statusuri: dot colorat + text („● Online"), niciodată doar culoare.

## Tabele (nucleul oricărui admin)

- Header sticky, sortare pe click, filtrare per coloană pentru > 20 rânduri.
- Row height 40-48px; zebra striping SAU border-uri subtile, nu ambele.
- Acțiuni per rând într-un meniu „⋯", nu 5 iconițe vizibile.
- Empty state proiectat explicit: ce înseamnă lipsa datelor + acțiunea următoare.
- Bulk actions apar doar la selecție (checkbox-uri).

## Pattern-uri obligatorii

- **Loading:** skeleton pentru layout, nu spinner global.
- **Refresh:** auto-refresh cu indicator discret + buton manual; niciodată refresh care resetează filtrele.
- **Filtre persistente:** starea filtrelor în URL (query params) — share-abilă și supraviețuiește la reload.
- **Confirmare la distructiv:** delete cere confirmare cu numele obiectului scris sau buton pe timer.

## Anti-pattern-uri

- Gauge-uri și pie 3D „executive".
- Grafice decorative fără întrebare de business în spate.
- Scroll orizontal pe tabele fără coloane înghețate.

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[Master_Skills_Catalog_251]]
- [[14 Subagents Council Map]]
- [[Knowledge Graph Home]]
