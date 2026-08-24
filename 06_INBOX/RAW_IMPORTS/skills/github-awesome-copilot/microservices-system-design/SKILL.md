---
name: microservices-system-design
description: Proiectare Microservicii, Event-Driven Architecture, Cozi de Mesaje (Kafka, RabbitMQ) și Idempotență.
---

# Microservices & System Design Standard

## 1. Arhitectură Event-Driven
- **Pub/Sub Messaging**: Decuplare asincronă prin RabbitMQ / Kafka.
- **Idempotență**: Fiecare mesaj procesat conține un `idempotency_key` unic (UUIDv4) stocat în cache/bază pentru a preveni procesarea dublă.
- **Outbox Pattern**: Scrie în baza de date locală și în tabela `outbox` în aceeași tranzacție atomică înainte de a publica în coada de mesaje.

## 2. Resiliență & Izolare
- **Circuit Breaker**: Întrerupe apelurile către servicii terțe după $N$ eșecuri consecutive (Polly / Resilience4j).
- **Graceful Degradation**: Fallback pe cache local dacă microserviciul secundar este indisponibil.