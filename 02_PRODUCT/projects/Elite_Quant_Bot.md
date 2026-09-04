---
id: "ae0206df-b0cb-4810-b2a2-cea462600561"
type: project
lifecycle: REVIEW
category: trading
tags: [project, trading, python, mql5, xauusd, clean_architecture, active]
created: 2026-08-09
updated: 2026-08-25
provenance:
  source_type: execution
  source_ref: "xau_kinetic/main.py"
confidence: high
verification: verified
relations:
  - relation: implements
    target: "[[XAU_Kinetic_Clean_Architecture]]"
  - relation: depends_on
    target: "[[Deploy_XAU_Kinetic_Quant_Bot]]"
---

# Elite Quant Bot / XAU_Kinetic

## Descriere
Sistem de trading algoritmic instituțional Hibrid — Python 3.12+ (Clean Architecture Quant Engine) integrat cu MetaTrader 5 + Aplicație Desktop C# .NET 8 WPF (`XAU_Kinetic.Desktop`). Implementează validare Pydantic V2, motor de risc izolat cu circuit breaker, logare de audit cryptographică SHA-256 (jurnalizare conformă Vault Rules P18) și interfață nativă Windows cu Kill Switch hardware.

## Componente Arhitecturale
- **`XAU_Kinetic.Desktop/` (C# .NET 8 WPF Application)**: Tablou de comandă desktop în timp real, arhitectură MVVM (`CommunityToolkit.Mvvm`), Dependency Injection, monitorizare risc/equity, tabel poziții activat și inspector de integritate lanț SHA-256 cu buton roșu Kill Switch de urgență.
- **`xau_kinetic/domain/`**: Modele Pydantic V2 imutabile (`SignalObject`, `Position`, `TickData`, `AccountInfo`, `OrderResult`, `AuditEvent`).
- **`xau_kinetic/application/`**: Interfețe izolate (`IBrokerClient`, `IStrategy`, `IRiskManager`, `IPersistence`) și `StrategyRunner` (event loop cu logare `ORDER_PROPOSED` și `ORDER_EXECUTED`).
- **`xau_kinetic/infrastructure/`**: `MT5Client` wrapper cu retry logic, traducere a codurilor de eroare MT5 și mod mock, plus `SQLitePersistence` în modul WAL cu jurnal SHA-256.
- **`xau_kinetic/risk/`**: Motor de risc `RiskManager` și `TPLadderManager` (scalare ordine pe TP1/TP2/TP3 și mutare automat Stop Loss la Break-Even).
- **`xau_kinetic/strategies/`**: `XAUKineticV2Strategy` — strategie pur-funcțională pentru XAUUSD pe lumânări închise (`bar[N-1]`).
- **`xau_kinetic/backtest/` & `tools/`**: Motor de backtesting istoric fără look-ahead și utilitare CLI pentru export de performanță și verificare lanț SHA-256.

## Constrângeri & Invarianți de Securitate
- Execuție thread-safe pe SQLite WAL mode cu `PRAGMA busy_timeout=5000`.
- Lanț de audit cryptographic nealterabil (SHA-256 chained hash ledger).
- Regula anti-look-ahead: semnalele folosesc doar lumânări complet închise (`iloc[-2]`).

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[XAU_Kinetic_Clean_Architecture]]
- [[Deploy_XAU_Kinetic_Quant_Bot]]
- [[12 Projects and Procedures Map]]
- [[Knowledge Graph Home]]
