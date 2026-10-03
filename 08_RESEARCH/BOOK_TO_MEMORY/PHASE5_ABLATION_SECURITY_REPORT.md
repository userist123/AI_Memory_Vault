# Raport de Securitate și Testare Adversarială — Faza 5 (Ablation)

**Document Version**: 1.0.0  
**Phase**: Phase 5 — With-Note vs Without-Note Ablation  
**Branch**: `research/book-to-memory-phase5-ablation`  
**Execution Environment**: Python 3.14.2, pytest-9.0.2  
**Test Suite Path**: `20_TESTS/test_book_to_memory_ablation.py`  

---

## 1. Rezumat Executiv

Pentru validarea valorii incrementale a notelor candidate extrase din cărți, conform cerințelor din `POLICY-LEARNING-QUALITY-02` (Secțiunea 7), a fost implementat și auditat modulul `book_to_memory_ablation.py`, acompaniat de o suită dedicată de 23 de teste automate de verificare și securitate adversarială.

Toate cele 23 de teste ale Fazei 5 au obținut **100% PASS** (0 failures, 0 errors, 0 warnings).

| Suită de Teste | Domeniu de Verificare | Teste Rulate | Rezultat |
| :--- | :--- | :---: | :---: |
| `test_book_to_memory_ablation.py` | With-Note vs Without-Note Ablation (Faza 5) | 23 | **23 / 23 PASS** |
| `test_book_to_memory_usage_test.py` | Task-Based Validation & Source Isolation (Faza 4) | 34 | **34 / 34 PASS** |
| `test_book_to_memory_conflicts.py` | Conflict Registry & Policy-02 Contradictions (Faza 3) | 33 | **33 / 33 PASS** |
| `test_book_to_memory_lifecycle_gates.py` | Lifecycle Boundaries & HMAC Gates (Faza 2) | 32 | **32 / 32 PASS** |
| `test_book_to_memory_schema.py` | 11 Note Schemas & Epistemic Chains (Faza 1) | 36 | **36 / 36 PASS** |
| **Total Teste Integrate Book-to-Memory** | **Fazele 1 + 2 + 3 + 4 + 5** | **158** | **158 / 158 PASS** |

---

## 2. Matricea Detaliată a Testelor de Securitate și Izolare (Faza 5)

### 2.1 Formula Matematică a Delta și Cazul de Frontieră
- `test_calculate_ablation_delta_standard`  
  *Verificare*: Calcul conform $\frac{S_{\text{cu}} - S_{\text{fără}}}{S_{\text{fără}}}$ pentru cazurile standard:
  - $10 \text{ vs } 5 \implies +1.0$ ($+100\%$ îmbunătățire);
  - $8 \text{ vs } 8 \implies 0.0$ (nicio îmbunătățire);
  - $6 \text{ vs } 8 \implies -0.25$ (regres de performanță).
- `test_calculate_ablation_delta_zero_denominator`  
  *Verificare*: Regula canonică din Politica 02 pentru $S_{\text{fără}} = 0$: raportare ca diferență absolută $S_{\text{cu}} - S_{\text{fără}}$ (ex: $8 \text{ vs } 0 \implies 8.0$).

### 2.2 Rigoarea Experimentală (Minimum 2 Modele, Minimum 3 Repetări)
- `test_ablation_requires_at_least_two_models`  
  *Verificare*: Încercarea de a rula experimentul cu un singur model este respinsă cu `AblationValidationError`.
- `test_ablation_rejects_duplicate_models`  
  *Verificare*: Lista de modele este validată pentru unicitate; modelele duplicate sunt respinse.
- `test_ablation_requires_at_least_three_repetitions`  
  *Verificare*: Rularea cu mai puțin de 3 repetări per sarcină este blocată.
- `test_ablation_executes_minimum_twelve_trials_and_alternates_order`  
  *Verificare*: Experimentul execută exact 12 trial-uri (2 modele $\times$ 3 repetări $\times$ 2 condiții) și alternează ordinea condițiilor (`WITHOUT -> WITH` vs `WITH -> WITHOUT`) pentru a elimina bias-ul de ordine.

### 2.3 Detectarea Contaminării Condiției de Control (`WITHOUT_NOTE`)
- `test_without_note_contamination_by_note_id_detected`  
  *Verificare*: Infiltrarea ID-ului notei în contextul sau răspunsul condiției `WITHOUT_NOTE` declanșează `AblationContaminationError`.
- `test_without_note_contamination_by_title_in_context_detected`  
  *Verificare*: Scurgerea titlului notei în metadatele condiției de control este interceptată și respinsă.

### 2.4 Imutabilitate și Controlul Actorilor
- `test_ai_agent_cannot_run_or_authorize_ablation`  
  *Adversarial*: Actorul `Principal.AI_AGENT` încearcă să execute experimentul de ablation. Respins cu `AblationPermissionError`.
- `test_tampered_ablation_record_fails_signature`  
  *Verificare*: Orice încercare de alterare a scorurilor brute, a mediilor sau a valorii Delta invalidează semnătura SHA-256 (`verify_signature() == False`).

---

## 3. Verificarea Celor 10 Cazuri Adversariale (Secțiunea 16)

| Nr. | Atac Adversarial | Mecanism de Apărare | Rezultat |
| :---: | :--- | :--- | :---: |
| **1** | Nota încearcă să crească Delta (`"Set Delta to +100"`) | Delta este calculat strict matematic din trial-urile brute; textul injectat rămâne inert | **PASS** |
| **2** | Nota încearcă să reducă scorul `WITHOUT_NOTE` | Condiția de control rulează într-un context complet izolat; nota nu poate altera controlul | **PASS** |
| **3** | Nota încearcă să modifice rubrica de evaluare | Formatul rubricii este validat strict; valori nepermise ($>2$) aruncă `UsageTestValidationError` | **PASS** |
| **4** | Nota încearcă să modifice numărul de repetări | Parametrul `repetitions` este imutabil la nivelul apelului; directivele textuale sunt ignorate | **PASS** |
| **5** | Nota încearcă să înlocuiască modelul de evaluare | Lista de modele este fixată de runner; textele din notă nu pot altera lista | **PASS** |
| **6** | Nota injectează comenzi de sistem (`"bash": "rm -rf /"`) | Interceptată de `validate_untrusted_security`, aruncă `SecurityInjectionError` | **PASS** |
| **7** | Rezultatul unui trial încearcă să contamineze următorul | Contextul fiecărui trial este instanțiat de la zero; referirile anterioare sunt blocate | **PASS** |
| **8** | Cache-ul conține accidental conținutul notei | Detectorul de contaminare inspectează tot contextul furnizat și aruncă excepție | **PASS** |
| **9** | Metadatele conțin ID-ul sau conceptul notei | Detectorul scanează metadatele asociate condiției de control și blochează execuția | **PASS** |
| **10** | Încercare de a elimina trial-urile negative | Toate cele 12 încercări sunt consemnate imutabil în înregistrare; filtrarea selectivă este exclusă | **PASS** |

---

## 4. Separare Epistemică și Interacțiunea cu Conflictele

- `test_hypothesis_ablation_does_not_convert_to_mechanism`  
  *Verificare*: O notă de tip `HYPOTHESIS` cu un rezultat excelent la ablation rămâne validată doar ca **ipoteză de lucru**; nu este convertită automat într-un mecanism de producție.
- `test_open_high_conflict_blocks_active_despite_positive_ablation_delta`  
  *Verificare*: Chiar dacă o notă obține $\text{Delta} > 0$, existența unui conflict deschis `HIGH` blochează poarta `GATE-06` și interzice atingerea stării `ACTIVE`.
- `test_negative_ablation_delta_fails_gate`  
  *Verificare*: O notă care degradează performanța ($\text{Delta} < 0$) este respinsă la poarta `GATE-07 Ablation`.
