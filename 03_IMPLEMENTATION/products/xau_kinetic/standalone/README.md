# XAU_Kinetic Standalone Quantitative Trading System (v2.0)

Self-contained production quantitative algorithmic trading solution for MetaTrader 5 (XAUUSD Gold), featuring a Python 3.12+ Clean Architecture Quant Engine and a C# .NET 8 WPF Enterprise Desktop Control Center.

## 📁 System Architecture

```text
XAU_Kinetic_Standalone/
├── xau_kinetic/               # Python Quant Engine Package
│   ├── domain/                # Pydantic V2 Zero-Trust Schemas
│   ├── application/           # Clean Abstract Base Classes & StrategyRunner
│   ├── infrastructure/        # MT5 Client Wrapper & SQLite WAL Persistence
│   ├── risk/                  # Circuit Breakers & Multi-TP Ladder Engine
│   ├── strategies/            # Pure Functional EMA/RSI/ATR Strategy Engine
│   ├── backtest/              # Bar-by-Bar Anti-Look-Ahead Backtesting Engine
│   ├── tools/                 # SHA-256 Audit Verification & Performance Export
│   └── tests/                 # Complete 20-Unit-Test Suite
├── XAU_Kinetic.Desktop/       # C# .NET 8 WPF Desktop Control Center
│   ├── Models/                # Domain Record Data Structures
│   ├── Services/              # TradingEngineService & IDialogService
│   ├── ViewModels/            # MainViewModel (CommunityToolkit.Mvvm)
│   ├── App.xaml / App.xaml.cs # Dependency Injection & Exception Handlers
│   └── MainWindow.xaml        # Dark-Themed WPF UI & Red Kill Switch
├── config.json                # System Configuration Parameters
├── requirements.txt           # Python Package Dependencies
├── run_desktop.bat            # One-Click Launcher: C# WPF Desktop App
├── run_engine.bat             # One-Click Launcher: Python Quant Engine Loop
└── run_tests.bat              # One-Click Verification Test Suite
```

## 🚀 Quick Start Guide

### 1. Run Verification Test Suite
Double-click `run_tests.bat` or run from command line:
```cmd
run_tests.bat
```

### 2. Launch C# .NET 8 WPF Desktop Control Center
Double-click `run_desktop.bat` or run from command line:
```cmd
run_desktop.bat
```

### 3. Launch Python Quant Engine Directly
Double-click `run_engine.bat` or run from command line:
```cmd
run_engine.bat
```

### 4. Verify SHA-256 Audit Log Integrity
```cmd
python -m xau_kinetic.tools.verify_audit_log --db xau_kinetic_audit.db
```

### 5. Export Performance & Audit Reports
```cmd
python -m xau_kinetic.tools.export_performance --db xau_kinetic_audit.db --out-json audit_report.json --out-csv audit_events.csv
```
