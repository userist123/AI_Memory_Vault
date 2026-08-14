---
id: "3d063fab-d6d1-411b-89b2-d31222f4b937"
type: knowledge
category: printing.papercut
tags: [papercut, xerox, secure-print, eip]
created: 2026-08-14
updated: 2026-08-14
status: review
provenance:
  source_type: ai_conversation
  source_ref: perplexity_conversation_2026-04-27_2026-04-30
  source_date: 2026-04-30
  original_path: not_applicable
  extraction_date: 2026-08-14
  redaction: not_applicable
confidence: medium
verification: unverified
lifecycle: REVIEW
provenance_status: incomplete
relations: ["[[Security_Practices]]"]
---

# PaperCut MF Secure Access on Xerox VersaLink (EIP 3.7)

## Summary

Configurarea PaperCut MF Secure Access pe imprimante Xerox VersaLink (ex. C7120) foloseste protocolul EIP 3.7 si necesita port dedicat, acces admin la CWIS si la PaperCut MF Admin.

## Core Concept

Xerox VersaLink C7120 este un dispozitiv EIP 3.7 compatibil PaperCut MF Secure Access; fluxul de printare securizata cere: IP fix pentru imprimanta, card reader (USB sau retea), configurare port pe firewall, si asociere card/PIN per utilizator in PaperCut.

## Key Points

- Setup: acces admin CWIS (interfata web imprimanta) + acces admin PaperCut MF Admin + IP fix + card reader compatibil.
- Testare: creezi un user de test, ii asociezi manual un numar de cartela (User Details > Card/Identity Numbers), trimiti un print job, apoi swipe la imprimanta — jobul trebuie sa apara in coada de secure print release.
- Diagnosticare: daca imprimanta nu inregistreaza log-uri, verifici Logs > Job Log (nu "job list") in PaperCut, si daca imprimanta e in lista de dispozitive monitorizate.
- Eroare frecventa: "please change this printer to use papercut tcp/ip port" apare cand este activata optiunea "Validate page counts after printing" (hardware page count validation); daca aceasta optiune e activa, coada fizica Xerox trebuie convertita la PaperCut TCP/IP Port, altfel Standard TCP/IP Port este suficient.

## Examples

Setup tipic: imprimanta Xerox VersaLink C7120 + PaperCut MF + card reader la imprimanta, cu autentificare pe cartela/PIN pentru release de printuri.

## Related Concepts

- [[Security_Practices]]
- Compliance MS111/2024, HG585, MS172/191 pentru documente neclasificate.

## References

- Conversatii AI din 2026-04-27 si 2026-04-30 despre configurare si troubleshooting PaperCut + Xerox VersaLink.

## Caveats

Configurarea exacta a porturilor de firewall nu a fost confirmata cu numere de port specifice in sursa; de verificat cu documentatia oficiala PaperCut/Xerox EIP.

## Verification

- [ ] Source checked
- [ ] Scope/environment checked
- [ ] Links checked

## Changelog

- 2026-08-14: nota creata din memoria conversatiilor.
