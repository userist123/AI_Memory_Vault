---
id: 5d85d011-c38f-5c43-a35a-3055b27b1b9d
type: knowledge
lifecycle: REVIEW
category: mlops/realtime_streaming_features
tags:
- mlops
- huyen
- streaming-features
- tumbling-window
- sliding-window
- watermarking
- online-inference
- flink
created: '2026-09-04'
updated: '2026-09-04'
provenance:
  source_type: ai
  source_ref: 06_INBOX/RAW_IMPORTS/BOOKS/Chip-Huyen-ML-Systems-Ch8-9
confidence: high
verification: unverified
relations:
- relation: references
  target: 01_KNOWLEDGE/BOOKS/Production_ML_Systems_and_Continual_Learning.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/MASTERY_MLOps_Model_Quantization_and_KV_Cache.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/Caiet_Teme_Aplicatii_Practice_Carti.md
---

# MLOps Expert: Prelucrarea Caracteristicilor ?n Timp Real & Ferestre de Flux (Streaming Windows)

**Surs?**: Chip Huyen, *Designing Machine Learning Systems* (Capitolele 8 & 9)  
**Domeniu**: Arhitecturi de Streaming, Calcul de Caracteristici ?n Timp Real & Gestiunea Timpului de Eveniment

---

## 1. Caracteristici Statice vs Batch vs Caracteristici ?n Timp Real

Modelele ML de ?nalt? performan?? (detec?ie de fraud?, recomandare ?n sesiune, securitate dinamic?) depind critic de informa?ia din ultimele c?teva secunde sau minute:

```text
[ Eveniment Utilizator ] ---> [ Broker de Mesaje (Kafka / Redpanda) ]
                                    |
                                    v
                       [ Motor de Procesare de Flux ]
                        (Fereastr?: Ultimele 5 minute)
                                    |
                                    v
                       [ Magazin de Caracteristici Online (Redis) ]
                                    |
                                    v
                       [ Inferen?? cu Laten?? < 10ms ]
```

---

## 2. Tipuri Fundamentale de Ferestre Temporale

Calculul agregatelor (num?r de log?ri e?uate, volum de tranzac?ii, rat? de erori) pe fluxuri nesf?r?ite necesit? delimitarea prin ferestre:

### A. Ferestre Ne-suprapuse (*Tumbling Windows*)
- Fereastr? de durat? fix? $D$. C?nd o fereastr? se ?ncheie, ?ncepe imediat urm?toarea f?r? suprapunere:
  $$[0, 5\text{m}), [5\text{m}, 10\text{m}), [10\text{m}, 15\text{m})$$
- Fiecare eveniment apar?ine exact unei singure ferestre.

### B. Ferestre Glisante (*Sliding / Hopping Windows*)
- Definite prin dou? dimensiuni: Durata ferestrei $D$ ?i Pasul de glisare $S$ (cu $S < D$):
  $$[0, 10\text{m}), [1\text{m}, 11\text{m}), [2\text{m}, 12\text{m})$$
- Evenimentele se pot suprapune ?n mai multe ferestre, oferind o perspectiv? continuu actualizat?.

### C. Ferestre de Sesiune (*Session Windows*)
- Se deschid la primul eveniment ?i r?m?n active c?t timp intervalul dintre dou? evenimente consecutive este mai mic dec?t un prag de inactivitate (*gap* $G$).

---

## 3. Timpul Evenimentului vs Timpul Proces?rii & Watermarks

- **Event Time**: Momentul fizic ?n care evenimentul a fost generat pe dispozitivul surs?.
- **Processing Time**: Momentul ?n care serverul a procesat efectiv mesajul.
- **Watermarking**: Semnal de progres temporal emis ?n flux care garanteaz? c? sistemul nu se mai a?teapt? la evenimente cu timestamp mai mic de $t - \Delta$. Evenimentele sosite dup? acest prag sunt clasificate drept *late data* ?i redirec?ionate c?tre o conduct? de corec?ie (*Dead Letter Queue*).

