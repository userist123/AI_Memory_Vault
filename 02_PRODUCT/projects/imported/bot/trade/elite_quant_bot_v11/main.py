"""Application entry point.

  pip install -r requirements.txt
  python main.py
"""
from __future__ import annotations

import sys

import config
from core.audit import AuditLogger
from core.execution import Executor
from core.journal import Journal
from core.mt5_client import MT5Client
from core.paper_executor import PaperExecutor
from core.risk_manager import RiskManager
from core.state_machine import StateMachine
from data.feed import DataFeed
from ml.model import OnlineLogReg
from ml.store import MLStore
from ml.trainer import Trainer
from strategies.ensemble import Ensemble
from strategies.factory import StrategyFactory
from ui.app import App


def main() -> int:
    client = MT5Client()
    if not client.connect():
        print("WARNING: MT5 connection failed. Make sure the MT5 terminal is "
              "installed, running, and logged in. UI will still start.")

    journal = Journal()
    audit = AuditLogger(journal=journal)
    executor = PaperExecutor(client) if config.PAPER_TRADING else Executor(client)
    risk = RiskManager()
    feed = DataFeed(client)

    factory = StrategyFactory()
    factory.build_all()
    print(f"Strategy factory built {len(factory.instances)} instances.")

    ensemble = Ensemble()
    model = OnlineLogReg()
    store = MLStore()
    store.load_model(model)
    trainer = Trainer(model, store)

    # Warm up the model from past journal trades if it hasn't trained enough.
    if model.trained_samples < config.ML_MIN_TRAINED_SAMPLES:
        from ml.warmup import warmup_from_journal
        n = warmup_from_journal(model, journal)
        if n:
            store.save_model(model)
            print(f"ML warmup: {n} updates from journal "
                  f"(trained_samples={model.trained_samples}).")


    sm = StateMachine(client, executor, risk, feed, factory,
                      ensemble, model, trainer,
                      audit=audit, journal=journal)
    sm.start()

    app = App(sm, audit=audit, factory=factory)
    try:
        app.run()
    finally:
        sm.stop()
        client.shutdown()
    return 0


if __name__ == "__main__":
    sys.exit(main())
