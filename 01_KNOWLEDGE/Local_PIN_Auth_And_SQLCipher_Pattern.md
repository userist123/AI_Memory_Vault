---
id: "4c5884fe-f24e-426e-a012-1414dbddae23"
type: knowledge
category: security.desktop_apps
tags: [sqlcipher, dpapi, pin-auth, csharp, python]
created: 2026-08-14
updated: 2026-08-14
status: review
provenance:
  source_type: ai_conversation
  source_ref: perplexity_conversation_2026-08-14
  source_date: 2026-08-14
  original_path: not_applicable
  extraction_date: 2026-08-14
  redaction: not_applicable
confidence: medium
verification: unverified
lifecycle: REVIEW
provenance_status: incomplete
relations: ["[[Registru_de_transferuri]]", "[[HG585_MS111_Compliance_Requirements]]", "[[Tech_Stack]]"]
---

# Local PIN Authentication + Encrypted Storage Pattern for Air-Gapped Desktop Apps

## Summary

Pentru aplicatii desktop air-gapped (fara retea), autentificarea trebuie sa fie simpla, locala si auditabila: PIN numeric hash-uit cu salt (SHA-256), fara JWT/sesiuni; iar stocarea trebuie criptata cu SQLCipher, cheia protejata via DPAPI.

## Core Concept

Modelul de securitate evolueaza pe doua straturi: (1) autentificare operator prin PIN + hash+salt, (2) criptare la nivel de baza de date (SQLCipher AES-256-CBC) cu cheia derivata/protejata prin DPAPI, astfel incat baza sa fie indescifrabila pe alta masina.

## Key Points

- Python prototip: `hashlib.sha256(f"{salt}{pin}".encode()).hexdigest()` cu salt generat prin `secrets.token_hex(16)`.
- Nu se recomanda BCrypt ("overkill pentru air-gapped") si nici JWT/sesiuni pentru un instrument desktop local.
- Versiunea C#/.NET 8 a adaugat: SQLCipher cu `kdf_iter=256000`, cheie ca byte literal, si `ProtectedData.Protect(LocalMachine)` (DPAPI) pentru cheia master.
- Memorie sensibila: folosire de `SecureBuffer`/pinned arrays si `CryptographicOperations.ZeroMemory` pentru a curata date sensibile din RAM.
- Elemente incomplete/dependente de hardware real: `Pkcs11Interop` pentru token QSCD, PAdES-LTA complet cu `BouncyCastle PdfSigner` + `TSAClient RFC 3161` + OCSP/CRL, Cryptographic Erase pe SED prin `DeviceIoControl`, si `CardRemoved` real prin WinRT.

## Examples

Proiectul `Registru-de-transferuri`: v2.0 (PyQt6, fara criptare robusta) -> v3.0 (Python, PIN hash+salt) -> v3.1 (C#/WPF/.NET 8, SQLCipher + DPAPI + PAdES-LTA).

## Related Concepts

- [[Registru_de_transferuri]]
- [[HG585_MS111_Compliance_Requirements]]
- [[Tech_Stack]]

## References

- Conversatie AI din 2026-08-14 despre rescrierea Registru-de-transferuri din Python in C#.

## Caveats

Partile care depind de hardware real (token QSCD, SED) nu au fost testate/verificate; codul "compileaza si ruleaza cu dotnet build/dotnet test" dar functionalitatea criptografica hardware ramane neconfirmata.

## Verification

- [ ] Source checked
- [ ] Scope/environment checked
- [ ] Links checked

## Changelog

- 2026-08-14: nota creata din memoria conversatiilor.
