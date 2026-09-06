"""strategy.py
Defines a simple Strategy class and a factory to create multiple dummy strategies.
"""
import random
from typing import Tuple

class Strategy:
    def __init__(self, name: str, win_rate: float):
        self.name = name
        self.win_rate = win_rate  # probability that the strategy's suggestion is correct

    def decide(self, price: float) -> int:
        """Return -1 (sell), 0 (hold), or 1 (buy).
        The decision is random but biased by win_rate: higher win_rate means
        higher chance to suggest the direction that historically performed better.
        For simplicity we treat win_rate as probability to suggest BUY.
        """
        # Randomly decide direction biasing towards BUY if win_rate > 0.5
        r = random.random()
        if r < self.win_rate:
            return 1  # BUY
        elif r < self.win_rate + (1 - self.win_rate) / 2:
            return -1  # SELL
        else:
            return 0  # HOLD

def create_strategies(num: int = 20) -> list[Strategy]:
    """Create a list of dummy strategies with random win rates.
    The win_rate is drawn from a uniform distribution between 0.4 and 0.6 to
    simulate modest predictive power.
    """
    strategies = []
    for i in range(num):
        win = random.uniform(0.4, 0.6)
        strategies.append(Strategy(name=f"Strategy_{i+1}", win_rate=win))
    return strategies
