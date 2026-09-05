"""Compatibility namespace for the XAU Kinetic engine after repository migration."""
from pathlib import Path

_ENGINE = Path(__file__).resolve().parents[2] / "products" / "xau_kinetic" / "engine"
__path__ = [str(_ENGINE)]
__all__ = []
