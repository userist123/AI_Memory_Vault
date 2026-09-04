# 30_SCRIPTS — OPERATIONAL TOOLING

Scop: scripturi de dezvoltare, mentenanță, migrare și verificare reproductibilă.

Permise: subdirectoare cu scop explicit, fără date brute sau secrete.

Interzise: credentiale, dumps, cache-uri, notebook-uri de experiment, output-uri locale sau path-uri absolute dependente de un workstation.

Trust: cod executabil controlat; scripturile de verificare trebuie să fie fail-closed pentru condițiile de securitate și structură declarate.

Relație: scripturile pot valida `03_IMPLEMENTATION`, `20_TESTS`, `07_EVALUATION` și structura repository-ului, dar nu pot promova singure memorie sau evidence.
