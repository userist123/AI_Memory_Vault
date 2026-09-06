"""Persistence layer for ML model weights (JSON).

If the stored feature dim no longer matches the current FEATURE_DIM, the
old file is left in place but ignored (model keeps fresh zero weights).
"""
from __future__ import annotations

import json
import os

import config
from ml.model import OnlineLogReg


class MLStore:
    def __init__(self, path: str = config.ML_WEIGHTS_FILE) -> None:
        self.path = path

    def save_model(self, model: OnlineLogReg) -> None:
        os.makedirs(os.path.dirname(self.path) or ".", exist_ok=True)
        try:
            with open(self.path, "w") as f:
                json.dump(model.to_dict(), f)
        except OSError:
            pass

    def load_model(self, model: OnlineLogReg) -> None:
        if not os.path.exists(self.path):
            return
        try:
            with open(self.path) as f:
                d = json.load(f)
            stored_dim = int(d.get("dim", -1))
            if stored_dim != model.dim:
                # incompatible (feature schema changed) → start fresh
                return
            model.load_dict(d)
        except Exception:
            pass
