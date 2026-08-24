---
name: python-trading-systems
description: Încarcă acest skill când lucrezi la boți de trading, integrare MetaTrader 5, backtesting, strategii sau jurnal de tranzacții în Python. Impune disciplină de risc, separarea strategie/execuție și reguli anti-look-ahead.
---

# Python Trading Systems

Un bug în cod obișnuit pierde timp; un bug în cod de trading pierde bani. Fiecare regulă de aici există dintr-un motiv financiar.

## Arhitectura obligatorie (separare strictă)

```
data/       → feed, candles, normalizare (NICIODATĂ logică de decizie aici)
strategy/   → semnale pure: primesc date, returnează intenție (fără side effects)
risk/       → position sizing, max drawdown, daily loss limit (VETO peste strategie)
execution/  → ordine MT5, retry, slippage handling (fără logică de decizie)
journal/    → fiecare tranzacție logată cu context complet ÎNAINTE de execuție
```
- Strategia nu vorbește direct cu brokerul. Risk manager-ul are drept de veto absolut.
- O strategie = o clasă cu interfață comună (`generate_signal(data) -> Signal`) — la 300+ strategii, uniformitatea e singura salvare.

## Reguli MT5 specifice

- `mt5.initialize()` verificat la fiecare start + reconnect logic; terminalul moare silențios.
- Verifică `retcode` la FIECARE `order_send` — tratează explicit requote, off-quotes, market closed.
- Nu presupune fill la prețul cerut: loghează prețul cerut vs. executat (slippage tracking).
- Time handling: MT5 returnează server time — convertește explicit, nu ghici timezone-ul.
- Rulează operațiile MT5 într-un thread separat de UI (Tkinter îngheață altfel).

## Anti-look-ahead (backtesting)

- Semnalul de pe bara N folosește DOAR date până la bara N-1 închisă. Bara curentă neînchisă = interzisă în decizii.
- Fără `df.shift(-1)`, fără normalizare fitted pe tot setul (fit doar pe train).
- Costuri reale în backtest: spread + comision + slippage estimat. Backtestul fără costuri e ficțiune.
- Raportează întotdeauna: win rate, profit factor, max drawdown, nr. tranzacții — nu doar profitul total.

## Risc (hardcodat, nu configurabil de strategie)

- Max risc per tranzacție: % fix din echitate, calculat din SL, nu invers.
- Daily loss limit → oprire completă pe ziua respectivă, nu „încă una să recuperez".
- Kill switch global accesibil instant din UI.

## Calitate cod

- Type hints peste tot; `Decimal`/int (puncte) pentru bani, NU float pentru comparații de preț.
- Config în fișier (YAML/JSON), zero valori magice în cod.
- Logging structurat cu context: strategie, simbol, semnal, motiv — nu `print`.

---

## 🔗 Legături de Memorie & Graf Obsidian
- [[10 Imports and Sources Map]]
- [[Master_Skills_Catalog_251]]
- [[Knowledge Graph Home]]
