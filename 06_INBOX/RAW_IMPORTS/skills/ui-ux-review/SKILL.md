---
name: ui-ux-review
description: Încarcă acest skill când trebuie să auditezi, critici sau îmbunătățești un UI existent (screenshot, cod, site live). Rulează o evaluare euristică structurată (Nielsen + WCAG) și produce un raport cu severitate și fix-uri concrete.
---

# UI/UX Review

Audit sistematic de utilizabilitate. Nu opinii vagi — fiecare problemă are severitate, locație și fix propus.

## Procedură de audit (în ordine)

### 1. Ierarhie vizuală (5-second test)
- Care e primul element pe care cade ochiul? Este cel mai important?
- Există UN singur punct focal per ecran? Dacă totul strigă, nimic nu se aude.
- Squint test: mijește ochii la ecran — structura trebuie să rămână lizibilă.

### 2. Euristicile Nielsen (scor 0-4 severitate fiecare)
1. Vizibilitatea stării sistemului (loading, progres, feedback)
2. Potrivire sistem-lume reală (limbaj natural, nu jargon)
3. Control și libertate (undo, cancel, escape)
4. Consistență și standarde (patterns interne + convenții platformă)
5. Prevenirea erorilor (confirmări la acțiuni distructive)
6. Recunoaștere > reamintire (opțiuni vizibile, nu memorate)
7. Flexibilitate și eficiență (shortcuts pentru useri avansați)
8. Design minimalist (fiecare element concurează pentru atenție)
9. Recuperare din erori (mesaje clare: ce s-a întâmplat + ce fac acum)
10. Ajutor și documentație (contextual, nu manual separat)

### 3. Accesibilitate (blocante, nu opționale)
- Contrast: 4.5:1 body, 3:1 text mare — măsurat, nu estimat.
- Navigare completă din tastatură; focus states vizibile.
- Sensul transmis NICIODATĂ doar prin culoare.
- Touch targets ≥ 44×44px pe mobil.
- `prefers-reduced-motion` respectat.

### 4. Consistență micro
- Aliniere pe grid (elementele „aproape aliniate" sunt mai rele decât cele evident diferite).
- Spacing consecvent între grupuri identice de elemente.
- Aceeași acțiune = același stil de buton peste tot.

## Format raport

| # | Problemă | Locație | Euristică | Severitate (0-4) | Fix propus |
|---|----------|---------|-----------|------------------|------------|

Severitate: 0=cosmetic, 1=minor, 2=mediu, 3=major, 4=blocant/catastrofal.
Ordonează raportul descrescător după severitate. Max 3 recomandări „quick wins" la final.

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[10 Imports and Sources Map]]
- [[Master_Skills_Catalog_251]]
- [[Knowledge Graph Home]]
