# Python Trading Systems Summary
- Strict separation of data, strategy, risk, execution, journal.
- Anti-lookahead: decisions use closed bars (bar N uses data up to bar N-1).
- Decimal/exact integer precision for money/price comparisons where floating point instability can corrupt logic.
- Type hints, configuration-driven, robust error handling and structured logging.
