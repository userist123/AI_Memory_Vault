"""
07_EVALUATION package-scoped test configuration.
Ensures modules within 07_EVALUATION/ resolve cleanly within its own package boundary.
"""
import sys
import types
from pathlib import Path

eval_dir = Path(__file__).resolve().parent.parent

if "evaluation" not in sys.modules:
    eval_mod = types.ModuleType("evaluation")
    eval_mod.__path__ = [str(eval_dir)]
    eval_mod.__file__ = str(eval_dir / "__init__.py")
    sys.modules["evaluation"] = eval_mod

if str(eval_dir) not in sys.path:
    sys.path.insert(0, str(eval_dir))
