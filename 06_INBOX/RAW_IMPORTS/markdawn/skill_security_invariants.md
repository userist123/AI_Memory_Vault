# Skill: Security & Compliance Invariants
# Locație: .agents/skills/security-invariants/skill.md (Google Antigravity)

## Când se activează

Orice task care touch-ează: control dispozitive (Modulul 3), audit (Modulul 6), sanitizare, chei de criptare, gestiune operatori/clearance (Modulul 7), sau puntea cu vault-ul cognitiv (Modulul 4).

## Regulă

Proiectul respectă HG 585/2002, NATO AC/35-D/1022, EUCI 2013/488/UE, NIST SP 800-88r2 și un set de invariante interne P0-P18 (deja definite în codul existent — NU le regenerezi, le citești din sursă înainte de a propune modificări).

Reguli stricte:

1. **Air-gapped**: aplicația nu inițiază niciodată trafic de rețea către altceva decât `127.0.0.1`. Orice cod nou care apelează un client HTTP/socket trebuie verificat manual pentru acest lucru.
2. **Audit-first**: orice operațiune destructivă (sanitizare NIST SP 800-88r2, distrugere cheie MEK conform HG 585 Art. 65) se loghează în audit ÎNAINTE de execuție, nu după — dacă operațiunea eșuează la mijloc, jurnalul trebuie să reflecte intenția, nu doar rezultatul.
3. **4-Eyes**: acțiunile marcate ca necesitând semnare duală (transfer de date clasificate) nu se activează în UI până nu există confirmare din 2 conturi de operator distincte.
4. **Clearance mapping**: orice UI care afișează nivel de clasificare trebuie să folosească exact accentul semantic corespunzător (Amber = Secret de Serviciu/NATO Confidential, Crimson = Strict Secret/SSID) — nu culori arbitrare.

## Când modifici o invariantă existentă

Marchezi explicit în commit message și în comentariul de cod: `⚠️ IMPACT INVARIANTĂ P{n}: <descriere impact>`. Nu faci acest tip de modificare fără să semnalezi.

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[Knowledge Graph Home]]
- [[00 Core Map]]
- [[Knowledge Graph Home]]
