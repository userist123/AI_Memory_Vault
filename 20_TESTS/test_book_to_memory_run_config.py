"""Tests for the explicit run config and the comparability guard (PR #209 B08)."""
import json
from pathlib import Path

import pytest

from lifecycle.validation.book_to_memory_run_config import (
    NO_MODEL_ID,
    ConfigMismatchError,
    RunConfig,
    RunConfigError,
    aggregate_results,
    assert_comparable,
    compare_results,
    config_of,
    differing_fields,
    prompt_template_hash,
    stamp,
)


def cfg(**kw):
    base = dict(model_id="model-a", temperature=0.0, seed=1, max_tokens=256, prompt_template="Q: {q}")
    base.update(kw)
    return RunConfig.build(**base)


def result(c, **extra):
    return stamp({"score": 0.5, **extra}, c)


# ---------------------------------------------------------------- construction
def test_prompt_template_hash_ignores_line_endings_and_trailing_space():
    assert prompt_template_hash("a \r\nb\n") == prompt_template_hash("a\nb")
    assert prompt_template_hash("a") != prompt_template_hash("b")


@pytest.mark.parametrize("kw", [
    dict(model_id=""), dict(temperature=-0.1), dict(temperature=3.0), dict(temperature=True),
    dict(seed=1.5), dict(seed=True), dict(max_tokens=-1), dict(max_tokens=2.5),
])
def test_invalid_fields_are_refused(kw):
    with pytest.raises(RunConfigError):
        cfg(**kw)


def test_template_hash_must_be_sha256_hex():
    with pytest.raises(RunConfigError):
        RunConfig("m", 0.0, 1, 10, "abc")
    with pytest.raises(RunConfigError):
        RunConfig.build(model_id="m", temperature=0, seed=1, max_tokens=1)  # neither template nor hash
    with pytest.raises(RunConfigError):
        RunConfig.build(model_id="m", temperature=0, seed=1, max_tokens=1, prompt_template="x",
                        prompt_template_hash_value=prompt_template_hash("x"))


def test_controls_must_be_scalars_and_are_order_independent():
    with pytest.raises(RunConfigError):
        cfg(controls={"k": [1, 2]})
    assert cfg(controls={"a": 1, "b": 2}) == cfg(controls={"b": 2, "a": 1})


def test_round_trip_through_dict_and_json():
    c = cfg(controls={"page_size": 10})
    again = RunConfig.from_dict(json.loads(json.dumps(c.to_dict())))
    assert again == c and again.digest() == c.digest()


def test_from_dict_rejects_missing_fields():
    d = cfg().to_dict()
    del d["seed"]
    with pytest.raises(RunConfigError):
        RunConfig.from_dict(d)


def test_retrieval_config_names_no_model():
    c = RunConfig.for_retrieval({"ranking_arm": "fused_score"})
    assert c.model_id == NO_MODEL_ID and c.temperature == 0.0 and c.max_tokens == 0


# ---------------------------------------------------------------- stamping
def test_stamp_records_config_and_digest():
    r = result(cfg())
    assert config_of(r) == cfg()
    assert r["run_config_sha256"] == cfg().digest()


def test_stamp_requires_a_real_config():
    with pytest.raises(RunConfigError):
        stamp({}, None)
    with pytest.raises(RunConfigError):
        stamp({}, {"model_id": "x"})


def test_result_without_config_is_not_comparable():
    with pytest.raises(RunConfigError, match="no run_config"):
        config_of({"score": 1})
    with pytest.raises(RunConfigError):
        aggregate_results([result(cfg()), {"score": 1}])


def test_edited_config_is_detected_by_its_digest():
    r = result(cfg())
    r["run_config"]["temperature"] = 0.7
    with pytest.raises(RunConfigError, match="edited"):
        config_of(r)


# ---------------------------------------------------------------- comparison
def test_differing_fields_lists_control_changes():
    a, b = cfg(controls={"graph": False}), cfg(controls={"graph": True}, seed=2)
    assert differing_fields(a, b) == ["controls.graph", "seed"]


def test_compare_accepts_exactly_the_declared_variable():
    a, b = result(cfg(model_id="m1")), result(cfg(model_id="m2"))
    out = compare_results(a, b, ["model_id"])
    assert out["differs_in"] == ["model_id"]


def test_compare_refuses_a_second_difference_each_field_alone():
    base = cfg()
    variants = {
        "temperature": cfg(model_id="m2", temperature=0.5),
        "seed": cfg(model_id="m2", seed=9),
        "max_tokens": cfg(model_id="m2", max_tokens=999),
        "prompt_template_hash": cfg(model_id="m2", prompt_template="other"),
        "controls.x": cfg(model_id="m2", controls={"x": 1}),
    }
    for name, other in variants.items():
        with pytest.raises(ConfigMismatchError, match=name.replace(".", r"\.")):
            compare_results(result(base), result(other), ["model_id"])


def test_compare_refuses_when_the_declared_variable_did_not_vary():
    with pytest.raises(ConfigMismatchError, match="identical"):
        compare_results(result(cfg()), result(cfg()), ["seed"])


def test_compare_needs_a_declared_variable_and_a_valid_name():
    with pytest.raises(RunConfigError):
        compare_results(result(cfg()), result(cfg(seed=2)), [])
    with pytest.raises(RunConfigError):
        compare_results(result(cfg()), result(cfg(seed=2)), ["colour"])


def test_compare_supports_control_variables():
    a = result(cfg(controls={"enable_graph_expansion": False, "page_size": 10}))
    b = result(cfg(controls={"enable_graph_expansion": True, "page_size": 10}))
    assert compare_results(a, b, ["controls.enable_graph_expansion"])["differs_in"] == ["controls.enable_graph_expansion"]
    c = result(cfg(controls={"enable_graph_expansion": True, "page_size": 20}))
    with pytest.raises(ConfigMismatchError):
        compare_results(a, c, ["controls.enable_graph_expansion"])


def test_assert_comparable_needs_two_configs():
    with pytest.raises(RunConfigError):
        assert_comparable([cfg()])


# ---------------------------------------------------------------- aggregation
def test_aggregate_replicates_with_identical_config():
    rs = [result(cfg()), result(cfg()), result(cfg())]
    assert aggregate_results(rs) == cfg()


def test_aggregate_refuses_a_seed_change_unless_declared():
    rs = [result(cfg(seed=1)), result(cfg(seed=2))]
    with pytest.raises(ConfigMismatchError):
        aggregate_results(rs)
    assert aggregate_results(rs, ["seed"]).seed == 1


def test_aggregate_refuses_temperature_or_template_mixing():
    with pytest.raises(ConfigMismatchError):
        aggregate_results([result(cfg()), result(cfg(temperature=0.7))])
    with pytest.raises(ConfigMismatchError):
        aggregate_results([result(cfg()), result(cfg(prompt_template="different"))])


def test_aggregate_of_nothing_is_an_error():
    with pytest.raises(RunConfigError):
        aggregate_results([])


# ---------------------------------------------------------------- the H1 runners record a config
REPO = Path(__file__).resolve().parents[1]


def _runner_source(name):
    return (REPO / "08_RESEARCH" / "BOOK_TO_MEMORY" / name).read_text(encoding="utf-8")


@pytest.mark.parametrize("name", ["run_h1_baseline.py", "run_h1_associative_experiment.py"])
def test_h1_runners_stamp_every_result_file_with_a_run_config(name):
    src = _runner_source(name)
    assert "book_to_memory_run_config" in src
    assert "stamp(" in src
    assert "RunConfig" in src


def test_associative_runner_guards_its_arms_with_the_comparison_check():
    src = _runner_source("run_h1_associative_experiment.py")
    assert "compare_results(" in src
    assert "controls.enable_graph_expansion" in src
