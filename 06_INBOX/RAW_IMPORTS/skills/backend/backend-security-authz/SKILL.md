---
name: backend-security-authz
description: Securitate Backend (OAuth2, JWT, RBAC, Rate Limiting, OWASP Backend Top 10, Anti-Injection).
---

# Backend Security & Authorization Standard

## 1. Autentificare & Autorizare Strictă
- **OAuth2 / OIDC & JWT**: Tokens semnate cu chei asimetrice (RS256 / Ed25519). Validare expirare, issuer și audience pe orice cerere.
- **RBAC (Role-Based Access Control)**: Verificare explicită a rolului și drepturilor per endpoint.

## 2. Protecție OWASP Backend
- **SQL Injection Prevention**: Parametrizare obligatorie în toate Interogările / ORM (EF Core, Dapper, SQLAlchemy).
- **Rate Limiting**: Limitare cereri per IP/Token (ex. 100 req/minut) via Redis token bucket.
- **Header-e de Securitate**: `Content-Security-Policy`, `X-Content-Type-Options: nosniff`, `Strict-Transport-Security`.