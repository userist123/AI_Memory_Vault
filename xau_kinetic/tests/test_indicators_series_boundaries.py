import pandas as pd
import pytest

from xau_kinetic.financial_ingestion.indicators import compute_all_indicators


def _frame(length: int) -> pd.DataFrame:
    closes = [100.0 + i for i in range(length)]
    return pd.DataFrame(
        {
            "Open": closes,
            "High": [value + 1.0 for value in closes],
            "Low": [value - 1.0 for value in closes],
            "Close": closes,
            "Volume": [1_000] * length,
        }
    )


@pytest.mark.parametrize(
    ("length", "field", "expected"),
    [
        (5, "var_sapt_pct", 0.0),
        (6, "var_sapt_pct", 5.0),
        (7, "var_sapt_pct", 5.0),
        (8, "var_sapt_pct", 5.0),
        (9, "var_sapt_pct", 5.0),
        (10, "var_sapt_pct", 5.0),
        (20, "var_luna_pct", 0.0),
        (21, "var_luna_pct", 20.0),
        (22, "var_luna_pct", 20.0),
        (23, "var_luna_pct", 20.0),
        (24, "var_luna_pct", 20.0),
        (25, "var_luna_pct", 20.0),
    ],
)
def test_indicator_series_boundary_lengths(
    length: int, field: str, expected: float
) -> None:
    result = compute_all_indicators(_frame(length), name="boundary", ticker="TEST")
    assert result[field] == expected
