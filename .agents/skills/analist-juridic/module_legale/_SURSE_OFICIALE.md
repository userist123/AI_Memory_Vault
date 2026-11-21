# Surse oficiale și fapte de procedură verificate (21.11.2026)

Copii locale cu manifest (captura_act.py v2): `D:\w\System_Prompts\Module_Legale\surse\coduri_20261121\`.
| Act | URL | Captură (UTC) | Ultima modificare încorporată | SHA-256 (.txt) |
|---|---|---|---|---|
| Codul de procedură penală (L. 135/2010) | https://legislatie.just.ro/Public/DetaliiDocument/120611 | 2026-11-21T11:41:05Z | 2026-06-13 | 469958741088d23f0047799ee91489404f3c96957f149c7dbaf5be212b81dc88 |
| Constituția României (republicată 2003) | https://legislatie.just.ro/Public/DetaliiDocument/47355 | 2026-11-21T11:41:10Z | (fără note de modificare) | 5ff10fefea1ac2f389e9cf1c296de27c8bfeba8cf4f74f12edc13089efac210a |
| Legea 24/2000 (republicată) | https://legislatie.just.ro/Public/DetaliiDocument/21698 | 2026-11-21T11:41:15Z | 2022-12-15 | 6e06763c2eaf0bc10bdc43f52a9a17f34763551fbeea965030ee9c348453ae59 |
| Codul de procedură civilă (L. 134/2010, rep.) | https://legislatie.just.ro/Public/DetaliiDocument/140271 | 2026-11-21T11:41:19Z | 2025-12-14 | 78b3485d70ec2f23a2937c80de1e0683be586d698adb00c1c6219d50e4e80ebf |

Toate articolele din tabelul de mai jos se extrag unic cu `articol.py` din aceste copii (testat 21.11.2026).
Copia anterioară, fără manifest, din `surse_skill_20261121\` e păstrată doar ca istoric.

## Ce s-a verificat pe text
| Regula | Temei | Stare |
|---|---|---|
| Legea dispune numai pentru viitor, cu excepția legii penale/contravenționale mai favorabile | Constituția art. 15 alin. (2) | VERIFICAT |
| Legea intră în vigoare la 3 zile de la publicare sau la o dată ulterioară prevăzută în text | Constituția art. 78 | VERIFICAT |
| Legi și OG (abilitare): 3 zile calendaristice, expiră la ora 24,00 a zilei a treia; OUG: la publicare (cu depunerea prealabilă la Cameră); celelalte acte: la publicare, dacă nu prevăd o dată ulterioară | L. 24/2000 art. 12 alin. (1)–(3) | VERIFICAT |
| Dispozițiile din legi, ordonanțe și regulamente constatate neconstituționale: încetarea efectelor la 45 de zile, suspendare de drept în interval; deciziile CCR general obligatorii, numai pentru viitor, de la publicare | Constituția art. 147 alin. (1), (4) | VERIFICAT |
| RIL penal obligatoriu pentru instanțe de la publicare; efectele încetează la abrogare/neconstituționalitate/modificare „cu excepția cazului în care aceasta subzistă în noua reglementare” (text verbatim; „aceasta” se raportează cel mai firesc la problema de drept) | CPP art. 474 alin. (4), art. 474^1 | VERIFICAT |
| HP penal obligatoriu pentru instanțe de la publicare | CPP art. 477 alin. (3) | VERIFICAT |
| HP civil obligatoriu de la publicare; pentru instanța care a sesizat, de la pronunțare | CPC art. 521 alin. (3) | VERIFICAT |
| RIL civil obligatoriu de la publicarea deciziei în M.Of. | CPC art. 517 alin. (4) | VERIFICAT |
| Actele de executare: în limitele normelor care le ordonă | L. 24/2000 art. 4 alin. (3) | VERIFICAT |
| Proiectul de act întocmit pe baza unui act superior nu depășește competența și nu contrazice actul superior | L. 24/2000 art. 13 lit. b) | VERIFICAT |
| Fără reproducerea normelor superioare, doar detaliere | L. 24/2000 art. 16 alin. (4) | VERIFICAT |
| Ordinele/instrucțiunile se limitează strict la cadrul actului superior | L. 24/2000 art. 78 | VERIFICAT |
| Percheziția informatică (definiție) | CPP art. 168 alin. (1) | VERIFICAT |

## Surse și ce NU oferă (de la Codex/Gemini; verificat doar unde e marcat)
| Sursă | La ce servește | Limite / capcane |
|---|---|---|
| legislatie.just.ro | forma consolidată, istoric consolidări, forma de bază, fișa actului (VERIFICAT în pagină) | formă informativă, nu autentică; normele tranzitorii din actele modificatoare (art. II, III…) nu apar în actul de bază; posibile întârzieri sau erori de consolidare |
| monitoruloficial.ro (e-Monitor) | textul publicat autentic, rectificări | acces gratuit la Partea I în PDF (declarat de Codex; NEVERIFICAT) |
| ccr.ro | decizii, dosare, comunicate | căutare pe articol parțial automatizabilă |
| scj.ro / iccj.ro | RIL, HP, jurisprudența secțiilor | idem |
| portal.just.ro | dosare, termene, soluții pe scurt (din ECRIS) | nu e bază de hotărâri motivate; întârzieri |
| rejust.ro | hotărâri anonimizate (CSM) | instrument de identificare; acoperire neexhaustivă |
| eur-lex.europa.eu | acte UE, versiunea română, consolidări, NIM (transpuneri) | forma consolidată e documentară; autentic e JOUE |
| curia.europa.eu | jurisprudența CJUE (ECLI) | CJUE nu interpretează dreptul național |
| hudoc.echr.coe.int | jurisprudența CEDO | nu toate deciziile sunt publicate; zero rezultate ≠ inexistență |
