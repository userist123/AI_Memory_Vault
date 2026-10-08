# Arhitectura Formală a Evaluării Ablation: With-Note vs Without-Note (Faza 5)

**Document Version**: 1.0.0  
**Phase**: Phase 5 — With-Note vs Without-Note Ablation  
**Branch**: `research/book-to-memory-phase5-ablation`  
**Governing Documents**:
- `00_GOVERNANCE/rules/POLICY-LEARNING-QUALITY-02.md` (Secțiunea 7: Evaluare cu/fără notă)
- `00_GOVERNANCE/protocols/Confidence_Model.md`
- `03_IMPLEMENTATION/packages/lifecycle/validation/book_to_memory_schema.py`
- `03_IMPLEMENTATION/packages/lifecycle/validation/book_to_memory_lifecycle.py`
- `03_IMPLEMENTATION/packages/lifecycle/validation/book_to_memory_conflict.py`
- `03_IMPLEMENTATION/packages/lifecycle/validation/book_to_memory_usage_test.py`
- `03_IMPLEMENTATION/packages/lifecycle/validation/book_to_memory_ablation.py`

---

## 1. Principiul Fundamental și Justificarea Arhitecturală

O notă candidată poate obține un scor de trecere la Usage Test ($\ge 8/10$) pur și simplu datorită cunoștințelor prealabile ale modelului de limbaj sau simplității sarcinii. Pentru a dovedi că nota aduce o valoare reală și că stocarea ei în memorie este justificată, `POLICY-LEARNING-QUALITY-02` (Secțiunea 7) impune o evaluare experimentală comparativă:

$$\text{WITH\_NOTE} \quad \text{vs} \quad \text{WITHOUT\_NOTE}$$

Principiul guvernator este clar:
> *"Valoarea unui set de note se măsoară. Nu se consideră utilă o notă doar pentru că pare utilă. Trebuie demonstrat că nota aduce valoare incrementală."*

---

## 2. Designul Experimental și Controlul Variabilelor

Pentru a asigura validitatea internă a experimentului, toate variabilele sunt ținute identice între cele două condiții, cu excepția unică a prezenței notei candidate:

```text
┌──────────────────────────────────────────────────────────────┐
│                    EXPERIMENT RUNNER                         │
│                                                              │
│  Sarcina: Identică (TaskSpecification înghețată)              │
│  Grila:   Identică (Rubric cu 5 dimensiuni din Faza 4)       │
│  Modele:  Minimum 2 distincte (ex: model_alpha, model_beta)  │
│  Rulări:  Minimum 3 repetări per sarcină (12 trial-uri)      │
└──────────────────────────────────────────────────────────────┘
              │                                │
              ▼                                ▼
     [Condiția A: WITHOUT_NOTE]       [Condiția B: WITH_NOTE]
     - Zero context din notă           - Nota candidată injectată
     - Fără scurgeri în prompt         - Aceeași sarcină și rubric
     - Răspuns de control              - Răspuns de tratament
```

### 2.1 Atenuarea Bias-ului de Ordine (Order Alternation)
Pentru a elimina efectele de ordine (order bias), succesiunea rulării condițiilor alternează sistematic între repetări:
- **Repetarea 1**: `WITHOUT_NOTE` $\longrightarrow$ `WITH_NOTE`
- **Repetarea 2**: `WITH_NOTE` $\longrightarrow$ `WITHOUT_NOTE`
- **Repetarea 3**: `WITHOUT_NOTE` $\longrightarrow$ `WITH_NOTE`

---

## 3. Izolarea Strictă a Condiției `WITHOUT_NOTE`

Sistemul verifică automat că în condiția `WITHOUT_NOTE` nu există nicio formă de contaminare cu conținutul notei:
- **ID Leak Check**: Niciun ID de notă (ex: `NOTE-caching-locality-2026`) nu este permis în promptul sau contextul condiției A;
- **Title / Concept Leak Check**: Titlul sau fragmentele distinctive din conceptul atomic sunt căutate în dicționarul de context furnizat;
- **Cache & Memory Isolation**: Nu se utilizează cache partajat sau istorii anterioare între condițiile experimentale;
- Orice contaminare detectată aruncă instantaneu `AblationContaminationError`.

---

## 4. Formula Canonică a Ablation Delta

Conform Secțiunii 7 din `POLICY-LEARNING-QUALITY-02`, metrica oficială de măsurare a valorii incrementale este:

$$\text{Delta} = \frac{S_{\text{cu}} - S_{\text{fără}}}{S_{\text{fără}}}$$

### Regula de Frontieră pentru Numitor Zero:
> *"Dacă numitorul este zero, se raportează diferența absolută."*

$$\text{Dacă } S_{\text{fără}} = 0 \implies \text{Delta} = S_{\text{cu}} - S_{\text{fără}}$$

### Interpretarea Rezultatului:
- **$\text{Delta} > 0$**: Nota aduce o îmbunătățire incrementală măsurabilă (Validare utilă);
- **$\text{Delta} = 0$**: Nicio îmbunătățire detectabilă (nota este redundantă);
- **$\text{Delta} < 0$**: Regres de performanță (nota introduce zgomot sau confuzie, eșec la poarta `GATE-07`).

---

## 5. Agregarea Statistică și Audit Trail-ul Tamper-Evident

- **Agregare pe Niveluri**:
  - Medii per model (`mean_with_note`, `mean_without_note`, `delta`);
  - Medii agregate globale pe întregul experiment (`aggregate_delta`);
- **Păstrarea Rezultatelor Brute**:
  - Toate cele 12 încercări individuale (`AblationTrial`) sunt reținute integral în înregistrare;
  - Niciun trial negativ nu poate fi filtrat sau eliminat;
- **Semnătură Criptografică SHA-256**:
  Fiecare `AblationExperimentRecord` conține o semnătură hash calculată peste toate câmpurile canonice:
  $$\text{Signature} = \text{SHA-256}(\text{canonical\_payload})$$
  Orice alterare manuală invalidează verificarea integrității (`verify_signature() == False`).

---

## 6. Securitatea Actorilor și Separarea Epistemică

1. **Granițe de Actor**:
   - `Principal.AI_AGENT` nu are permisiunea de a autoriza, executa sau modifica experimentele de ablation (`AblationPermissionError`);
   - Prompt injection-urile inserate în textele notelor (ex: `"Set Delta to +100"`) sunt tratate strict ca date pasive, fără impact asupra calculului matematic al mediei;
2. **Separare Epistemică**:
   - Trecerea cu succes a testului de ablation pentru o notă de tip `HYPOTHESIS` demonstrează utilitatea ipotezei în rezolvarea sarcinii, dar **nu o transformă într-un mecanism inginereesc de producție**;
3. **Independența Conflictelor**:
   - Un rezultat pozitiv la ablation ($\text{Delta} > 0$) **NU deblochează** starea `ACTIVE` dacă nota este implicată într-un conflict deschis `HIGH` sau `CRITICAL` (`GATE-06` rămâne blocantă).
