---
name: data-viz-design
description: Încarcă acest skill când construiești grafice, dashboards, KPI cards sau orice vizualizare de date (Chart.js, D3, matplotlib, Excel, slide-uri). Impune alegerea corectă a tipului de grafic, paletă armonioasă și data-ink ratio maxim.
---

# Data Viz Design

Fiecare pixel prezintă date. Tot ce nu prezintă date se șterge.

## Alegerea tipului de grafic

| Întrebarea din date | Grafic | Note |
|---|---|---|
| Schimbare în timp? | Line | Date continue, trenduri |
| Comparație categorii? | Bar vertical | Comparații discrete |
| Clasament? | Bar orizontal | Etichete ușor de citit |
| Parte din întreg? | Stacked bar / treemap | NU pie (rar corect) |
| Distribuție? | Histogramă / box plot | Spread, outliers |
| Relație? | Scatter | Corelație, clustere |
| Flux/proces? | Sankey / funnel | Conversie, pași |

**Interzis:** grafice 3D, pie cu 5+ felii, dual-axis.

## Culoare

- Secvență categorială curată (în ordine): `#20808D` teal, `#A84B2F` terra, `#1B474D` teal închis, `#BCE2E7` cyan deschis, `#944454` mauve, `#FFC553` gold, `#848456` olive, `#6E522B` maro.
- Max 5 serii per grafic; peste — small multiples.
- Date secvențiale: un singur hue, luminozitate variabilă. Divergente: teal pozitiv, roșu `#A13544` negativ.
- Seria cheie la opacitate 100%, restul la 40-60%.
- Colorblind: niciodată doar culoare — adaugă etichete/markere/pattern-uri. Evită roșu/verde singure; albastru+portocaliu e sigur.
- Dacă proiectul are paletă proprie, derivă culorile graficelor din ea — nu hue-uri fără legătură.

## Reguli de execuție

1. **Data-ink ratio** — șterge gridlines decorative, borders, backgrounds, legende inutile.
2. **Etichetare directă** — pe/lângă punctele de date, nu legendă separată (legendă doar când etichetarea directă aglomerează).
3. **Titlul spune insight-ul** — „Veniturile au crescut 23% în Q4", nu „Grafic venituri".
4. **Font body în grafice** — niciodată display fonts. Axe: 12-14px. `tabular-nums` pe toate valorile numerice.
5. **Animații de tranziție** — numerele cresc, barele se ridică, liniile se desenează (600-800ms). Zero efecte gratuite.

## KPI Cards

- Valoarea: mare, bold — elementul dominant.
- Eticheta: mică, muted. Delta: săgeată colorată + % (teal/verde sus, roșu jos, gri neutru).
- Sparkline opțional: linie mică de trend, fără axe.

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[Master_Skills_Catalog_251]]
- [[14 Subagents Council Map]]
- [[Knowledge Graph Home]]
