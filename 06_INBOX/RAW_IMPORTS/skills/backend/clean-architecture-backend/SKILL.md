---
name: clean-architecture-backend
description: Clean Architecture, Domain-Driven Design (DDD), Dependency Injection și CQRS pe Backend.
---

# Clean Architecture & DDD Backend Standard

## 1. Straturile Aplicației
- **Domain Layer**: Entități de business pure, fără dependențe terțe sau ORM.
- **Application Layer**: Use-case-uri, comenzi, interfețe (CQRS - Command Query Responsibility Segregation).
- **Infrastructure Layer**: Persistență, baze de date, clienți HTTP, integrări terțe.
- **API / Presentation Layer**: Controllers, Minimal APIs, DTOs.

## 2. Regulă de Aur
Dependențele indică doar spre interior (Infrastructure depinde de Application/Domain, niciodată invers).

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[10 Imports and Sources Map]]
- [[Master_Skills_Catalog_251]]
- [[Knowledge Graph Home]]
