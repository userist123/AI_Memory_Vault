"""Security-boundary components.

The implementation package remains the legacy import root. Its namespace also
searches the repository-level security package so newer boundary components
remain available without changing legacy PYTHONPATH ordering.
"""
from pathlib import Path

_ROOT_SECURITY = Path(__file__).resolve().parents[3] / "security"
if _ROOT_SECURITY.is_dir():
    __path__.append(str(_ROOT_SECURITY))
