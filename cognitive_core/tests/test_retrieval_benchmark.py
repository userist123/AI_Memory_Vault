from cognitive_core.benchmarks.metrics import mean_reciprocal_rank, precision_at_k, recall_at_k
from cognitive_core.benchmarks.retrieval_benchmark import BenchmarkCase, RetrievalBenchmark


def test_precision_recall_mrr_basic():
    retrieved = ["a", "b", "c", "d", "e"]
    relevant = ["b", "e"]
    assert precision_at_k(retrieved, relevant, 5) == 2 / 5
    assert recall_at_k(retrieved, relevant, 5) == 1.0
    assert mean_reciprocal_rank(retrieved, relevant) == 1 / 2


def test_retrieval_benchmark_runs_end_to_end():
    cases = [
        BenchmarkCase(query="wal", relevant_ids=["n1"]),
        BenchmarkCase(query="unrelated", relevant_ids=["n2"]),
    ]
    benchmark = RetrievalBenchmark(cases)

    def fake_retrieval(query):
        return ["n1", "n3"] if query == "wal" else ["n9"]

    report = benchmark.run(fake_retrieval, k=5)
    assert report["summary"]["cases"] == 2
    assert 0.0 <= report["summary"]["avg_precision_at_5"] <= 1.0
