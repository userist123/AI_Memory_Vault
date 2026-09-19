# Sarcină pentru ChatGPT: bugetul grafului și un benchmark de regăsire etichetat independent

Ești evaluatorul independent al unui sistem de memorie pentru agenți AI (depozitul public `userist123/AI_Memory_Vault_CODEX_READY`). Tu nu ai construit sistemul și nu vei ajusta parametrii lui. Tocmai de aceea îți cerem etichetarea: cine alege parametrii nu are voie să scrie și testul pe care îi alege.

Ai primit două fișiere:

- `vault_corpus.json` — cele 379 de note pe care căutarea le poate returna. Pentru fiecare: `id`, `title`, `type`, `lifecycle`, `path`, `excerpt` (primele ~700 de caractere). Plus `links`: cele 466 de legături dintre note (`source`, `target`, `relation`, `origin`).
- `GRAPH_BUDGET_REPORT.md` — măsurătoarea actuală a bugetului de expansiune a grafului.

Ai două sarcini, separate.

---

## Sarcina A — Bugetul grafului

Căutarea găsește întâi note după cuvinte (până la 200), apoi poate adăuga note noi urmând legăturile din graf. Câte note noi are voie să adauge se numește **bugetul**. Valoarea implicită e `min(2·n, 20) − n`, unde `n` e numărul de note găsite după cuvinte. Deci bugetul e **zero** ori de câte ori căutarea după cuvinte găsește 20 de note sau mai multe, adică aproape mereu.

Din raport: la bugetul implicit, rezultatele sunt identice cu graful oprit; cu orice buget fix de 5, 10 sau 20 se câștigă aceleași 2 cazuri din 29, fără nicio pierdere; costul crește cu bugetul (în medie 3,91 / 7,81 / 15,62 note noi pe interogare).

**Ce îți cerem:**

1. **O recomandare pentru valoarea implicită** (implicit / 5 / 10 / 20 / altă valoare), cu motivarea, ținând cont și de costul în context, nu doar de recall.
2. **O regulă de decizie preînregistrată** pentru benchmark-ul nou de la Sarcina B, scrisă **înainte** de orice măsurătoare pe el. De exemplu: „adoptăm bugetul X dacă, pe benchmark-ul nou, câștigă cel puțin K cazuri față de graful oprit, pierde cel mult L, testul McNemar exact dă p < 0,05, iar contextul mediu crește cu cel mult Y note". Alegi tu valorile și le justifici.
3. Spune explicit ce **nu** se poate concluziona din datele actuale: 29 de cazuri, 2 câștiguri.

---

## Sarcina B — Benchmark de regăsire: 160 de cazuri

### Tipurile de cazuri

| Clasă | Câte | Ce testează |
|---|---|---|
| `direct` | 60 | întrebare la care răspunde o notă anume. **La cel puțin jumătate** nu folosești cuvintele distinctive din titlul notei: parafrazezi, cum ar întreba un om care nu știe cum se numește nota |
| `multi_hop` | 40 | nota corectă **nu** conține cuvintele distinctive ale întrebării, dar e legată în `links` de o notă care le conține. Doar urmând legătura ajungi la răspuns. Folosește **numai legături care există** în `links` |
| `conceptual` | 30 | întrebarea cere înțelegerea conținutului, nu potrivirea unui cuvânt |
| `abstain` | 30 | întrebări plauzibile despre acest vault, al căror răspuns **nu se află în nicio notă**. Corect e ca sistemul să nu găsească nimic |

### Reguli

1. Notele corecte (`gold_relevant_notes`) se aleg **doar dintre id-urile din `vault_corpus.json`**. Între 1 și 3 pe caz. La `abstain`, lista e goală.
2. `required_facts`: 1–3 fragmente scurte care trebuie să apară **exact, cuvânt cu cuvânt**, în `excerpt`-ul unei note corecte. Le vom verifica automat, deci copiază-le exact.
3. La `multi_hop`, completezi `hops` cu drumul: `[id_nota_de_plecare, id_nota_corecta]`, plus `relation` din `links`. Legătura trebuie să existe exact așa în fișier.
4. Nu folosi note cu `excerpt` gol sau aproape gol, note de test sau note-șablon.
5. Împrăștie cazurile pe zone (`path`) și pe tipuri. Nu mai mult de 3 cazuri cu aceeași notă corectă.
6. Scrie întrebarea în limba notei (română sau engleză).
7. La `abstain`, verifică tu că răspunsul chiar nu apare în niciun `excerpt`. Evită întrebările absurde: trebuie să pară că vault-ul ar putea ști.
8. Nu ai voie să vezi sau să folosești rezultatele sistemului de căutare. Etichetezi doar din note.

### Formatul — un singur fișier JSON

```json
{
  "schema_version": "retrieval-benchmark-v3",
  "labeller": "chatgpt",
  "vault_commit": "<copiat din vault_corpus.json>",
  "cases": [
    {
      "id": "R3-001",
      "class": "direct | multi_hop | conceptual | abstain",
      "query": "…",
      "expected_answer": "… (gol la abstain)",
      "gold_relevant_notes": ["<id>"],
      "required_facts": ["<fragment exact din excerpt>"],
      "hops": [],
      "relation": "",
      "abstain": false,
      "language": "ro | en"
    }
  ]
}
```

La final, un rezumat scurt: câte cazuri pe clasă, pe zonă și pe limbă, plus orice caz la care ai avut dubii.

**Livrare:** creează fișierul `retrieval_benchmark_v3.json` pentru descărcare. Dacă nu poți produce fișierul, trimite JSON-ul în 4 mesaje a câte 40 de cazuri: R3-001…040, 041…080, 081…120, 121…160.
