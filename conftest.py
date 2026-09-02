import sys
import types
from pathlib import Path

root = Path(__file__).parent.resolve()

eval_path = root / "07_EVALUATION"
if eval_path.exists() and "evaluation" not in sys.modules:
    eval_mod = types.ModuleType("evaluation")
    eval_mod.__path__ = [str(eval_path)]
    eval_mod.__file__ = str(eval_path / "__init__.py")
    sys.modules["evaluation"] = eval_mod
