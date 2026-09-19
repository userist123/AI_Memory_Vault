---
id: "proc-stats-reporting-0001"
type: procedure
lifecycle: ACTIVE
category: evaluation-methodology
tags: [statistics, confidence-intervals, wilson-score, mcnemar-test, curriculum-reporting, small-samples]
created: 2026-09-19T14:50:00Z
updated: 2026-09-19T14:50:00Z
provenance:
  source_type: official
  source_ref: "openstax-introductory-statistics-2e-ch08"
confidence: very_high
verification: verified
relations: []
---

# 📊 Procedură Canonică: Raportarea Proporțiilor pe Eșantioane Mici în Evaluări și Benchmark-uri

Această procedură stabilește standardul canonic obligatoriu pentru raportarea proporțiilor, ratelor de succes și transferului cognitiv în rapoartele de evaluare și benchmark ale AI Memory Vault. Standardul derivă direct din principiile matematice de inferență statistică predate în *OpenStax Introductory Statistics 2e*, Capitolul 8 (*Confidence Intervals*).

---

## 🎯 1. Principiu de Bază

> O proporție empirică obținută pe un eșantion finit (ex: $\hat{p} = 6/12 = 50\%$) este o **estimare punctuală**, nu un parametru fix. Fără precizarea dimensiunii eșantionului $n$ și a intervalului de încredere asociat, orice raportare de performanță este invalidă din punct de vedere epistemologic și statistic.

---

## 📖 2. Fundamentare Teoretică și Citate Verbatim din Capitolul 8

Prezenta procedură transpune principiile din *OpenStax Introductory Statistics 2e*, Chapter 8 ("Confidence Intervals"):

### A. Estimare Punctuală vs. Interval de Încredere (Secțiunea 8.0 & 8.1)
> "A point estimate is a single number calculated from a sample used to estimate a population parameter."
> "A confidence interval is an interval of numbers that is expected to contain the true population parameter."

*Implicație pentru Vault*: Niciun raport nu va consemna o rată de succes exclusiv printr-un procentaj scalar (ex: „Acuratețe 50%”). Fiecare estimare punctuală $\hat{p}$ trebuie însoțită de dimensiunea eșantionului și limitele inferioară și superioară ale intervalului.

### B. Semnificația Nivelului de Încredere (Secțiunea 8.1)
> "The confidence level is the percentage of all other possible samples that can be expected to include the true population parameter."
> "To say that we are 95% confident that the unknown population mean is in the interval means that we think that 95% of the confidence intervals that could be constructed from repeated random samples will contain the parameter."

*Implicație pentru Vault*: Nivelul de încredere standard prescris pentru toate evaluările este de **95%** ($\alpha = 0.05$).

### C. Dependența de Dimensiunea Eșantionului $n$ (Secțiunea 8.1 & 8.4)
> "Increasing the sample size causes the error bound to decrease, making the confidence interval narrower. Decreasing the sample size causes the error bound to increase, making the confidence interval wider."
> "Always round the answer UP to the next higher integer to ensure that the sample size is large enough."

*Implicație pentru Vault*: Pe eșantioane mici ($n \le 30$, frecvente în seturile de testare calitativă sau curricula held-out de 12-20 întrebări), lățimea intervalului este considerabilă. De exemplu, pentru $0/12$, intervalul Wilson 95% este $[0.000, 0.242]$, demonstrând că eroarea de eșantionare permite o valoare reală de până la 24.2%.

### D. Ajustarea pentru Eșantioane Mici: Metoda Plus-Four / Wilson Score (Secțiunea 8.3)
> "Fortunately, there is a simple adjustment that allows us to produce more accurate confidence intervals for small samples."
> "The 'plus four' method for calculating confidence intervals is an attempt to balance the error introduced by using estimates of the population proportion... Simply imagine four additional trials in the study; two are successes and two are failures."
> "When sample sizes are small, this method has been demonstrated to provide more accurate confidence intervals than the standard formula used for larger samples."

*Implicație pentru Vault*: Pentru proporții binomiale pe eșantioane finite, aproximarea normală Wald ($\hat{p} \pm z \sqrt{\hat{p}\hat{q}/n}$) devine instabilă la valori extreme ($0/n$ sau $n/n$). Standardul impune **Intervalul Wilson Score** (echivalentul matematic analitic continuu al ajustării plus-four).

### E. Cerința Fundamentală de Independență a Observațiilor
> Fiecare întrebare de test sau unitate de eșantionare trebuie să fie independentă. Pseudoreplicarea (adresarea de variante ale aceleiași întrebări pentru a umfla artificial $n$) este strict interzisă.

---

## 📋 3. Protocolul Obligatoriu de Raportare

Orice raport de evaluare sau sinteză a unui benchmark curricular (Markdown sau JSON) trebuie să respecte următoarele 4 reguli invariante:

### Regula 1: Exprimarea Fracționară Explicită a Dimensiunii Eșantionului
- **Interzis**: „Rata de succes a fost de 58%.”
- **Obligatoriu**: „Rata de succes a fost de 7/12 (58.3%).” Dimensiunea eșantionului $n$ trebuie să figureze explicit în titlul sau antetul tabelului (`(n=12)`).

### Regula 2: Acomodarea Intervalului de Încredere Wilson 95%
- Fiecare proporție de răspunsuri susținute trebuie raportată alături de intervalul Wilson score 95%:
  $$\text{Interval Format: } [L_{95}, U_{95}]$$
  Exemplu: `7/12 ([0.320, 0.807])`.

### Regula 3: Testul Împerecheat McNemar pe Perechi Discordante
- În evaluările comparative dual-arm (Control vs. Tratament pe aceleași întrebări), autorii nu pot declara o îmbunătățire exclusiv pe baza diferenței brute (ex: $+6$ sau $+7$).
- Raportul trebuie să consemneze tabela de contingență împerecheată:
  * $a$: ambele brațe corecte
  * $b$: control incorect, tratament corect (îmbunătățire)
  * $c$: control corect, tratament incorect (regresie)
  * $d$: ambele brațe incorecte
- Se calculează testul **exact McNemar cu două cozi** pe perechile discordante ($b$ și $c$):
  $$P(\text{exact}) = 2 \times \sum_{i=\max(b,c)}^{b+c} \binom{b+c}{i} 0.5^{b+c}$$

### Regula 4: Pragul Decizional de Semnificație Statistică ($\alpha = 0.05$)
- Dacă $p \ge 0.05$, raportul este obligat să menționeze explicit:
  * `Tratamentul nu poate fi distins de fluctuația aleatoare (zgomot de eșantionare).`
- Dacă $p < 0.05$, raportul conchide:
  * `Diferență semnificativă statistic (p < 0.05). Câștigul reprezintă transfer autentic.`

---

## 🛠️ 4. Formule Matematice de Referință

### Formula Wilson Score Interval (95% Two-Sided):
$$\tilde{p} = \frac{x + \frac{z^2}{2}}{n + z^2}, \quad \text{Margin} = \frac{z}{1 + \frac{z^2}{n}} \sqrt{\frac{\hat{p}(1-\hat{p})}{n} + \frac{z^2}{4n^2}}$$
$$L = \max\left(0.0, \frac{\hat{p} + \frac{z^2}{2n} - z \sqrt{\frac{\hat{p}(1-\hat{p})}{n} + \frac{z^2}{4n^2}}}{1 + \frac{z^2}{n}}\right), \quad U = \min\left(1.0, \frac{\hat{p} + \frac{z^2}{2n} + z \sqrt{\frac{\hat{p}(1-\hat{p})}{n} + \frac{z^2}{4n^2}}}{1 + \frac{z^2}{n}}\right)$$
*(unde $z = 1.95996$ pentru încredere 95%). La $x=0$, $L=0.0$; la $x=n$, $U=1.0$.*

---

## 🔒 5. Verificare Automată prin Gate
Respectarea acestui standard este auditată și blocată în mod automat la nivel de CI de către scriptul:
- `30_SCRIPTS/verification/gate_curriculum_report_statistics.py`
Orice raport curricular care conține proporții dar omite mărimea eșantionului $n$ sau intervalele de încredere este respins cu cod de eroare non-zero.
