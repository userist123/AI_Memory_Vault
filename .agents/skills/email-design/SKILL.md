---
name: email-design
description: Încarcă acest skill când creezi email-uri HTML (tranzacționale, onboarding, newsletter, notificări de produs) sau template-uri de email pentru un SaaS. Impune HTML compatibil cu clienții de email, ierarhie de conținut și reguli de deliverability.
---

# Email Design

Email-ul HTML trăiește în 1999: fără flexbox garantat, fără fonturi custom garantate, Outlook randează cu engine de Word. Proiectează pentru cel mai prost client, nu pentru Gmail web.

## Reguli tehnice nederogabile

- **Layout pe `<table>`**, lățime max 600px, single column (multi-column se sparge pe mobil și Outlook).
- **CSS inline pe fiecare element** — `<style>` din `<head>` e ignorat de unii clienți. Folosește un inliner la build.
- Fonturi: stack de sistem (`Arial, Helvetica, sans-serif` sau `Georgia, serif`). Fonturile web sunt bonus progresiv, nu fundație.
- Imagini: presupune că sunt BLOCATE by default → email-ul trebuie să funcționeze 100% doar din text. `alt` descriptiv pe toate, dimensiuni fixe în atribute.
- Dark mode: nu forța fundal alb pe imagini transparente; testează inversarea automată din Gmail/Outlook.
- Butonul CTA: „bulletproof button" (table + padding + bgcolor), min 44px înălțime, NU imagine.

## Ierarhia conținutului

1. **Subject:** 30-45 caractere, beneficiu sau informație concretă, fără CAPS și „!!!".
2. **Preheader:** 40-90 caractere care continuă subject-ul (nu „View in browser" ca prim text).
3. **Un email = un scop = un CTA.** Newsletter-ul poate avea secțiuni, dar tranzacționalul are exact o acțiune.
4. Primele 2 rânduri spun tot; restul e detaliu pentru cine derulează.

## Tipuri și tonuri

| Tip | Reguli |
|---|---|
| Tranzacțional (confirmare, reset, alertă) | Instant la subiect, zero marketing, datele critice bold, plain-first |
| Onboarding | 1 acțiune per email, secvență de 3-5, progres vizibil |
| Notificare de produs (ex. alertă trading) | Cifra/evenimentul în subject, context minim, link direct la obiect |
| Newsletter | Max 3-4 secțiuni, un highlight dominant, restul linkuri scurte |

## Deliverability

- Raport text:imagine sănătos (min ~60% text). Email-ul doar-imagine = spam folder.
- Link de unsubscribe vizibil (legal + protejează reputația domeniului).
- Adresa expeditor umană (`owner@`, nu `no-reply@`) unde e posibil.
- Testează în: Gmail web, Gmail app dark mode, Outlook desktop. Minim aceste trei.

## Anti-pattern-uri
- GIF-uri hero de 2MB. Butoane-imagine. 6 CTA-uri egale. Fonturi custom fără fallback testat.

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[Master_Skills_Catalog_251]]
- [[14 Subagents Council Map]]
- [[Knowledge Graph Home]]
