import math

from cognitive_core.spreading_activation import SpreadingActivationEngine


class FakeGraph:
    def __init__(self, weight):
        self.weight = weight

    def neighbors(self, node_id):
        if node_id == "seed":
            return [("target", {"weight": self.weight})]
        return []


class FakeMultiGraph:
    def __init__(self, weight):
        graph = FakeGraph(weight)
        self.semantic = graph
        self.temporal = graph
        self.causal = graph
        self.entity = graph


def test_edge_weight_changes_activation():
    low = SpreadingActivationEngine(FakeMultiGraph(0.2), decay=0.5, max_hops=1).activate({"seed": 1.0})
    high = SpreadingActivationEngine(FakeMultiGraph(0.8), decay=0.5, max_hops=1).activate({"seed": 1.0})
    assert high["target"] > low["target"]


def test_edge_weight_invalid_values_fail_safe():
    for weight in (0.0, -1.0, math.nan):
        result = SpreadingActivationEngine(FakeMultiGraph(weight), decay=0.5, max_hops=1).activate({"seed": 1.0})
        assert result.get("target", 0.0) == 0.0
    result_inf = SpreadingActivationEngine(FakeMultiGraph(math.inf), decay=0.5, max_hops=1).activate({"seed": 1.0})
    assert math.isfinite(result_inf["target"])


def test_multi_hop_decay_is_bounded():
    class ChainGraph:
        def neighbors(self, node_id):
            return [("b", {"weight": 1.0})] if node_id == "a" else [("c", {"weight": 1.0})] if node_id == "b" else []

    class ChainMultiGraph:
        semantic = ChainGraph()
        temporal = ChainGraph()
        causal = ChainGraph()
        entity = ChainGraph()

    result = SpreadingActivationEngine(ChainMultiGraph(), decay=0.5, max_hops=2).activate({"a": 1.0})
    assert result["b"] > result["c"] > 0.0
    assert result["c"] <= 0.25
