import ast, inspect, pytest
from dataclasses import is_dataclass, fields
from pathlib import Path
import cognitive_core.model_provider as mp
import cognitive_core.fake_model_provider as fmp
import cognitive_core.model_tier_router as mtr
import cognitive_core.actual_usage_telemetry as aut
import cognitive_core.council_model_execution as cme
import cognitive_core.executive_model_execution_bridge as emeb

CORE_DIR = Path(__file__).resolve().parent.parent

def test_ast_model_provider_structure():
    tree = ast.parse((CORE_DIR / 'model_provider.py').read_text(encoding='utf-8'))
    classes = {n.name for n in ast.walk(tree) if isinstance(n, ast.ClassDef)}
    assert {'TokenUsage', 'ModelRequest', 'ModelResponse', 'ModelProvider'}.issubset(classes)

def test_ast_fake_model_provider_structure():
    tree = ast.parse((CORE_DIR / 'fake_model_provider.py').read_text(encoding='utf-8'))
    classes = {n.name for n in ast.walk(tree) if isinstance(n, ast.ClassDef)}
    assert 'FakeModelProvider' in classes

def test_ast_model_tier_router_structure():
    tree = ast.parse((CORE_DIR / 'model_tier_router.py').read_text(encoding='utf-8'))
    classes = {n.name for n in ast.walk(tree) if isinstance(n, ast.ClassDef)}
    assert {'TierConfig', 'ModelTierRouter', 'ModelTierConfigError'}.issubset(classes)
    funcs = {n.name for n in ast.walk(tree) if isinstance(n, ast.FunctionDef)}
    assert 'load_model_tier_config' in funcs

def test_ast_actual_usage_telemetry_structure():
    tree = ast.parse((CORE_DIR / 'actual_usage_telemetry.py').read_text(encoding='utf-8'))
    classes = {n.name for n in ast.walk(tree) if isinstance(n, ast.ClassDef)}
    assert {'UsageEvent', 'ActualUsageTelemetry'}.issubset(classes)

def test_ast_council_model_execution_structure():
    tree = ast.parse((CORE_DIR / 'council_model_execution.py').read_text(encoding='utf-8'))
    classes = {n.name for n in ast.walk(tree) if isinstance(n, ast.ClassDef)}
    assert 'CouncilRunWithExecution' in classes
    funcs = {n.name for n in ast.walk(tree) if isinstance(n, ast.FunctionDef)}
    assert 'run_council_with_model_execution' in funcs

def test_ast_executive_bridge_structure():
    tree = ast.parse((CORE_DIR / 'executive_model_execution_bridge.py').read_text(encoding='utf-8'))
    funcs = {n.name for n in ast.walk(tree) if isinstance(n, ast.FunctionDef)}
    assert {'derive_agent_model_tiers', 'build_model_tier_router', 'execute_council_models'}.issubset(funcs)

def test_model_provider_signatures_and_fields():
    assert is_dataclass(mp.TokenUsage)
    tf = {f.name for f in fields(mp.TokenUsage)}
    assert {'estimated_input', 'estimated_output', 'actual_input', 'actual_output', 'cached_input', 'reasoning_tokens', 'total'}.issubset(tf)
    assert hasattr(mp.TokenUsage, 'effective_total')
    assert is_dataclass(mp.ModelRequest)
    rf = {f.name for f in fields(mp.ModelRequest)}
    assert {'prompt', 'model_tier', 'system_prompt', 'tools', 'metadata'}.issubset(rf)
    assert is_dataclass(mp.ModelResponse)
    rsf = {f.name for f in fields(mp.ModelResponse)}
    assert {'content', 'provider', 'model', 'model_tier', 'usage', 'metadata'}.issubset(rsf)
    assert 'request' in inspect.signature(mp.ModelProvider.generate).parameters
    assert len(inspect.signature(mp.ModelProvider.health).parameters) >= 1

def test_fake_model_provider_signatures():
    assert hasattr(fmp.FakeModelProvider, 'generate')
    assert hasattr(fmp.FakeModelProvider, 'health')
    assert 'request' in inspect.signature(fmp.FakeModelProvider.generate).parameters
import ast, inspect, pytest
from dataclasses import is_dataclass, fields
from pathlib import Path
import cognitive_core.model_provider as mp
import cognitive_core.fake_model_provider as fmp
import cognitive_core.model_tier_router as mtr
import cognitive_core.actual_usage_telemetry as aut
import cognitive_core.council_model_execution as cme
import cognitive_core.executive_model_execution_bridge as emeb

CORE_DIR = Path(__file__).resolve().parent.parent

def test_ast_model_provider_structure():
    tree = ast.parse((CORE_DIR / 'model_provider.py').read_text(encoding='utf-8'))
    classes = {n.name for n in ast.walk(tree) if isinstance(n, ast.ClassDef)}
    assert {'TokenUsage', 'ModelRequest', 'ModelResponse', 'ModelProvider'}.issubset(classes)

def test_ast_fake_model_provider_structure():
    tree = ast.parse((CORE_DIR / 'fake_model_provider.py').read_text(encoding='utf-8'))
    classes = {n.name for n in ast.walk(tree) if isinstance(n, ast.ClassDef)}
    assert 'FakeModelProvider' in classes

def test_ast_model_tier_router_structure():
    tree = ast.parse((CORE_DIR / 'model_tier_router.py').read_text(encoding='utf-8'))
    classes = {n.name for n in ast.walk(tree) if isinstance(n, ast.ClassDef)}
    assert {'TierConfig', 'ModelTierRouter', 'ModelTierConfigError'}.issubset(classes)
    funcs = {n.name for n in ast.walk(tree) if isinstance(n, ast.FunctionDef)}
    assert 'load_model_tier_config' in funcs

def test_ast_actual_usage_telemetry_structure():
    tree = ast.parse((CORE_DIR / 'actual_usage_telemetry.py').read_text(encoding='utf-8'))
    classes = {n.name for n in ast.walk(tree) if isinstance(n, ast.ClassDef)}
    assert {'UsageEvent', 'ActualUsageTelemetry'}.issubset(classes)

def test_ast_council_model_execution_structure():
    tree = ast.parse((CORE_DIR / 'council_model_execution.py').read_text(encoding='utf-8'))
    classes = {n.name for n in ast.walk(tree) if isinstance(n, ast.ClassDef)}
    assert 'CouncilRunWithExecution' in classes
    funcs = {n.name for n in ast.walk(tree) if isinstance(n, ast.FunctionDef)}
    assert 'run_council_with_model_execution' in funcs

def test_ast_executive_bridge_structure():
    tree = ast.parse((CORE_DIR / 'executive_model_execution_bridge.py').read_text(encoding='utf-8'))
    funcs = {n.name for n in ast.walk(tree) if isinstance(n, ast.FunctionDef)}
    assert {'derive_agent_model_tiers', 'build_model_tier_router', 'execute_council_models'}.issubset(funcs)

def test_model_provider_signatures_and_fields():
    assert is_dataclass(mp.TokenUsage)
    tf = {f.name for f in fields(mp.TokenUsage)}
    assert {'estimated_input', 'estimated_output', 'actual_input', 'actual_output', 'cached_input', 'reasoning_tokens', 'total'}.issubset(tf)
    assert hasattr(mp.TokenUsage, 'effective_total')
    assert is_dataclass(mp.ModelRequest)
    rf = {f.name for f in fields(mp.ModelRequest)}
    assert {'prompt', 'model_tier', 'system_prompt', 'tools', 'metadata'}.issubset(rf)
    assert is_dataclass(mp.ModelResponse)
    rsf = {f.name for f in fields(mp.ModelResponse)}
    assert {'content', 'provider', 'model', 'model_tier', 'usage', 'metadata'}.issubset(rsf)
    assert 'request' in inspect.signature(mp.ModelProvider.generate).parameters
    assert len(inspect.signature(mp.ModelProvider.health).parameters) >= 1

def test_fake_model_provider_signatures():
    assert hasattr(fmp.FakeModelProvider, 'generate')
    assert hasattr(fmp.FakeModelProvider, 'health')
    assert 'request' in inspect.signature(fmp.FakeModelProvider.generate).parameters

def test_model_tier_router_signatures():
    assert is_dataclass(mtr.TierConfig)
    tier_fields = {f.name for f in fields(mtr.TierConfig)}
    assert {'provider', 'model'}.issubset(tier_fields)

    load_sig = inspect.signature(mtr.load_model_tier_config)
    assert 'path' in load_sig.parameters

    router = mtr.ModelTierRouter
    assert hasattr(router, 'from_config_file')
    assert hasattr(router, 'resolve')
    assert hasattr(router, 'model_for_tier')

    resolve_sig = inspect.signature(router.resolve)
    assert 'model_tier' in resolve_sig.parameters


def test_actual_usage_telemetry_signatures():
    assert is_dataclass(aut.UsageEvent)
    event_fields = {f.name for f in fields(aut.UsageEvent)}
    assert {'provider', 'model', 'model_tier', 'kind', 'input_tokens',
            'output_tokens', 'cached_tokens', 'reasoning_tokens', 'source'}.issubset(event_fields)

    telemetry = aut.ActualUsageTelemetry
    assert hasattr(telemetry, 'record_specialist_actual')
    assert hasattr(telemetry, 'record_synthesis_actual')
    assert hasattr(telemetry, 'actual_total_tokens')
    assert hasattr(telemetry, 'actual_input_tokens')
    assert hasattr(telemetry, 'actual_output_tokens')
    assert hasattr(telemetry, 'has_real_provider_usage')

    spec_sig = inspect.signature(telemetry.record_specialist_actual)
    assert {'usage', 'provider', 'model', 'model_tier'}.issubset(spec_sig.parameters)
    synth_sig = inspect.signature(telemetry.record_synthesis_actual)
    assert {'usage', 'provider', 'model', 'model_tier'}.issubset(synth_sig.parameters)


def test_council_model_execution_signatures():
    assert is_dataclass(cme.CouncilRunWithExecution)
    run_fields = {f.name for f in fields(cme.CouncilRunWithExecution)}
    assert {'council_run', 'model_execution_enabled', 'specialist_results',
            'synthesis_result', 'actual_usage'}.issubset(run_fields)
    assert hasattr(cme.CouncilRunWithExecution, 'agent_packs')
    assert hasattr(cme.CouncilRunWithExecution, 'estimated_telemetry')

    run_func_sig = inspect.signature(cme.run_council_with_model_execution)
    params = set(run_func_sig.parameters.keys())
    assert {'council_run', 'model_tier_router', 'task', 'agent_model_tiers',
            'synthesis_model_tier', 'model_execution_enabled'}.issubset(params)


def test_executive_model_execution_bridge_signatures():
    assert 'subagent_specs' in inspect.signature(emeb.derive_agent_model_tiers).parameters
    assert {'config_path', 'allowed_providers', 'provider_factories'}.issubset(inspect.signature(emeb.build_model_tier_router).parameters)
    assert {'council_run', 'subagent_specs', 'task'}.issubset(inspect.signature(emeb.execute_council_models).parameters)
