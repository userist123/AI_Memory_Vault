---
name: backend-api-design
description: Standarde de Proiectare API (RESTful, GraphQL, gRPC, Versionare, Contracte Open-API & Schema First).
---

# Backend API Design & Architecture Standard

## 1. Principiile de Proiectare API
- **RESTful Best Practices**: Resurse la plural (`/api/v1/transfers`), metode HTTP semantice (`GET`, `POST`, `PUT`, `PATCH`, `DELETE`).
- **Coduri de Status HTTP**: `200 OK`, `201 Created`, `400 Bad Request`, `401 Unauthorized`, `403 Forbidden`, `404 Not Found`, `409 Conflict`, `429 Too Many Requests`, `500 Internal Error`.
- **Versionare Explicită**: Versionare în URL (`/v1/`) sau în Header-ul de Accept (`Accept: application/vnd.api.v1+json`).
- **Contract-First & OpenAPI / Swagger**: Schema JSON/Proto definită înaintea codului de implementare.

## 2. Formatul Răspunsurilor & Error Handling
```json
{
  "success": false,
  "error": {
    "code": "TRANSFER_LIMIT_EXCEEDED",
    "message": "Plafonul maxim de transfer a fost depășit.",
    "timestamp": "2026-08-24T18:17:00Z"
  }
}
```