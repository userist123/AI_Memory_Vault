# 20_TESTS — TEST SUITES

Scop: toate testele active ale repository-ului, separate de codul de produs.

Permise: `unit/`, `integration/`, `e2e/`, `regression/`, `adversarial/`, `fixtures/`.

Interzise: secrete reale, date private/brute, path-uri absolute dependente de un workstation și output-uri generate versionate.

Trust: fixture-urile trebuie să fie `SANITIZED_TEST_FIXTURE`; testele care exercită trust boundaries trebuie să folosească payload-uri sintetice și să nu ridice authority.

Validare: `repository_hygiene.py` trebuie să eșueze pentru path-uri absolute și artefacte interzise.
