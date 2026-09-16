"""
engine/costs.py — Modelul determinist de costuri de tranzacționare și finanțare.

Costuri modelate:
1. Spread la deschidere/închidere (jumătate de spread la fiecare tranziție de poziție).
2. Comision broker per lot noțional (4 paliere testate: 0, 2, 5, 7 USD/lot).
3. Finanțare swap peste noapte, cu zi de swap triplu (miercuri pentru FX/metale, vineri pentru crypto).
"""

from dataclasses import dataclass
from typing import Optional


@dataclass
class CostModel:
    spread_points: float
    point: float
    contract_size: float
    commission_per_lot: float = 0.0  # USD per round-turn lot
    swap_long_points: float = 0.0
    swap_short_points: float = 0.0
    swap_rollover3days: int = 3      # 3 = Wednesday, 5 = Friday

    def calculate_trade_cost(self, price: float, delta_pos: float) -> float:
        """
        Calculează costul unei modificări de poziție (intrare, ieșire sau inversare).
        delta_pos este valoarea absolută a schimbării de expunere (|pos_new - pos_old|).
        Pentru 1:1 unleveraged, costul este exprimat ca fracțiune din capitalul noțional.
        """
        if delta_pos == 0.0 or price <= 0:
            return 0.0

        # Cost spread: jumătate de spread per unitate de poziție schimbată
        spread_price = self.spread_points * self.point
        half_spread_frac = (spread_price / (2.0 * price)) * delta_pos

        # Cost comision: comision_per_lot / (contract_size * price) * delta_pos / 2
        # (jumătate din comisionul round-turn la fiecare parte a tranzacției)
        comm_frac = 0.0
        if self.commission_per_lot > 0 and self.contract_size > 0:
            comm_frac = (self.commission_per_lot / (2.0 * self.contract_size * price)) * delta_pos

        return half_spread_frac + comm_frac

    def calculate_overnight_swap_cost(self, position: float, price: float, weekday: int) -> float:
        """
        Calculează costul swap-ului pentru menținerea unei poziții peste noapte.
        weekday: 0=Monday, ..., 6=Sunday.
        Dacă poziția este 0, costul este 0.
        """
        if position == 0.0 or price <= 0:
            return 0.0

        # Număr de zile de rollover
        # MT5: weekday corespunde zilei în care se aplică rollover-ul (00:00 server)
        # swap_rollover3days: 3=Wednesday (noaptea de miercuri spre joi poartă 3 zile de swap)
        days = 3 if weekday == (self.swap_rollover3days - 1) else 1

        if position > 0:
            # Long: swap_long este de obicei în puncte per lot
            # Randament din swap = (swap_long * point * days) / price
            # Cost swap = - swap_return
            swap_pts = self.swap_long_points
        else:
            # Short
            swap_pts = self.swap_short_points

        # Dacă swap_mode este puncte:
        swap_val = swap_pts * self.point * days
        swap_drag = -(swap_val / price) * abs(position)
        return swap_drag
