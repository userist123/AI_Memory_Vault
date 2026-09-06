"""Online logistic regression with L2 regularisation, learning-rate decay,
probability clipping and optional exponential forgetting.

No external ML libs (pure Python). Target = 1 (win) / 0 (loss).
"""
from __future__ import annotations

import math
from typing import List

from ml.features import FEATURE_DIM


CLIP_LO, CLIP_HI = 1e-3, 1.0 - 1e-3


class OnlineLogReg:
    def __init__(self, dim: int = FEATURE_DIM, base_lr: float = 0.05,
                 l2: float = 1e-4, lr_decay: float = 1e-4,
                 forget_lambda: float = 1.0) -> None:
        """forget_lambda < 1 applies exponential weight decay each update
        (recent samples matter more); 1.0 disables forgetting."""
        self.dim = dim
        self.base_lr = base_lr
        self.l2 = l2
        self.lr_decay = lr_decay
        self.forget_lambda = forget_lambda
        self.w: List[float] = [0.0] * dim
        self.b: float = 0.0
        self.trained_samples: int = 0

    # ----------------------------------------------------------- forward
    @staticmethod
    def _sigmoid(z: float) -> float:
        if z >= 0:
            return 1.0 / (1.0 + math.exp(-z))
        ez = math.exp(z)
        return ez / (1.0 + ez)

    def predict_proba(self, x: List[float]) -> float:
        if len(x) != self.dim:
            return 0.5
        z = self.b + sum(wi * xi for wi, xi in zip(self.w, x))
        p = self._sigmoid(z)
        return max(CLIP_LO, min(CLIP_HI, p))

    # ----------------------------------------------------------- update
    def _lr(self) -> float:
        return self.base_lr / (1.0 + self.lr_decay * self.trained_samples)

    def update(self, x: List[float], y: int) -> None:
        if len(x) != self.dim:
            return
        p = self.predict_proba(x)
        err = p - y                       # dL/dz for BCE + sigmoid
        lr = self._lr()
        if self.forget_lambda < 1.0:
            self.w = [wi * self.forget_lambda for wi in self.w]
        # gradient step with L2
        self.w = [wi - lr * (err * xi + self.l2 * wi)
                  for wi, xi in zip(self.w, x)]
        self.b -= lr * err
        self.trained_samples += 1

    # ----------------------------------------------------------- io
    def to_dict(self) -> dict:
        return {"w": self.w, "b": self.b, "base_lr": self.base_lr,
                "l2": self.l2, "lr_decay": self.lr_decay,
                "forget_lambda": self.forget_lambda, "dim": self.dim,
                "trained_samples": self.trained_samples}

    def load_dict(self, d: dict) -> None:
        dim = int(d.get("dim", self.dim))
        if dim != self.dim:
            # incompatible weights → keep zero init; do not crash.
            return
        self.w = list(d.get("w", self.w))
        self.b = float(d.get("b", 0.0))
        self.base_lr = float(d.get("base_lr", self.base_lr))
        self.l2 = float(d.get("l2", self.l2))
        self.lr_decay = float(d.get("lr_decay", self.lr_decay))
        self.forget_lambda = float(d.get("forget_lambda", self.forget_lambda))
        self.trained_samples = int(d.get("trained_samples", 0))
