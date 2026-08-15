import os
import sys
sys.path.insert(0, os.path.abspath("."))
import math
import random
import string
import traceback
from cognitive_core.evaluation import RetrievalEvaluator
from cognitive_core.semantic import SemanticProvider

class RobustMockSemanticProvider(SemanticProvider):
    def __init__(self, fixed_score=0.75):
        self.fixed_score = fixed_score

    def compute_similarity(self, text1: str, text2: str) -> float:
        if not text1 or not text2:
            return 0.0
        return self.fixed_score

    def generate_embedding(self, text: str):
        return [0.1] * 128

def run_evaluation_stress_suite():
    evaluator = RetrievalEvaluator(semantic_provider=RobustMockSemanticProvider())
    
    findings = []
    
    # 1. Parameter sweeps: k < 0, k = 0, k = 10^6
    print("--- Probing k parameter sweeps ---")
    try:
        assert evaluator.precision_at_k(["a", "b"], {"a"}, k=-5) == 0.0
        assert evaluator.recall_at_k(["a", "b"], {"a"}, k=-5) == 0.0
        assert evaluator.ndcg_at_k(["a", "b"], {"a": 1.0}, k=-5) == 0.0
        assert evaluator.precision_at_k(["a", "b"], {"a"}, k=0) == 0.0
        assert evaluator.recall_at_k(["a", "b"], {"a"}, k=0) == 0.0
        assert evaluator.ndcg_at_k(["a", "b"], {"a": 1.0}, k=0) == 0.0
        assert evaluator.precision_at_k(["a", "b"], {"a"}, k=1000000) == 0.5
        assert evaluator.recall_at_k(["a", "b"], {"a"}, k=1000000) == 1.0
        assert evaluator.ndcg_at_k(["a", "b"], {"a": 1.0}, k=1000000) == 1.0
        print("  -> k parameter sweeps passed.")
    except Exception as e:
        findings.append(("k_sweep", str(e), traceback.format_exc()))

    # 2. Mathematical validation: DCG, IDCG, NDCG, MRR
    print("--- Probing mathematical precision ---")
    try:
        retrieved = ["d1", "d2", "d3", "d4", "d5", "d6"]
        scores = {"d1": 3.0, "d2": 2.0, "d3": 3.0, "d4": 0.0, "d5": 1.0, "d6": 2.0}
        expected_dcg = 3.0/math.log2(2) + 2.0/math.log2(3) + 3.0/math.log2(4) + 0.0/math.log2(5) + 1.0/math.log2(6)
        expected_idcg = 3.0/math.log2(2) + 3.0/math.log2(3) + 2.0/math.log2(4) + 2.0/math.log2(5) + 1.0/math.log2(6)
        expected_ndcg = expected_dcg / expected_idcg
        actual_ndcg = evaluator.ndcg_at_k(retrieved, scores, k=5)
        assert abs(actual_ndcg - expected_ndcg) < 1e-12
        print(f"  -> NDCG math validated: {actual_ndcg} == {expected_ndcg}")

        # MRR
        rankings = [["a", "b", "c"], ["x", "a", "z"], ["m", "n", "o", "a"], ["u", "v", "w"]]
        rel_sets = [{"a"}, {"a"}, {"a"}, {"a"}]
        mrr = evaluator.mean_reciprocal_rank(rankings, rel_sets)
        assert abs(mrr - 0.4375) < 1e-12
        print(f"  -> MRR math validated: {mrr} == 0.4375")
    except Exception as e:
        findings.append(("math_val", str(e), traceback.format_exc()))

    # 3. None content in notes payload
    print("--- Probing None-value payload structures in TRACe metrics ---")
    note_with_none = [{"id": "n1", "content": None}]
    
    # 3a. utilization
    try:
        u = evaluator.utilization(note_with_none, "some response text")
        print(f"  -> utilization with None content: {u}")
    except Exception as e:
        print(f"  -> BUG FOUND in utilization(content=None): {type(e).__name__}: {e}")
        findings.append(("utilization_none_content", f"{type(e).__name__}: {e}", traceback.format_exc()))

    # 3b. adherence
    try:
        adh = evaluator.adherence("some response text", note_with_none)
        print(f"  -> adherence with None content: {adh}")
    except Exception as e:
        print(f"  -> BUG FOUND in adherence(content=None): {type(e).__name__}: {e}")
        findings.append(("adherence_none_content", f"{type(e).__name__}: {e}", traceback.format_exc()))

    # 3c. relevance
    try:
        rel = evaluator.relevance(note_with_none, "query")
        print(f"  -> relevance with None content: {rel}")
    except Exception as e:
        print(f"  -> BUG FOUND in relevance(content=None): {type(e).__name__}: {e}")
        findings.append(("relevance_none_content", f"{type(e).__name__}: {e}", traceback.format_exc()))

    # 3d. completeness
    try:
        comp = evaluator.completeness(note_with_none, ["n1", None])
        print(f"  -> completeness with None content/gold_id: {comp}")
    except Exception as e:
        print(f"  -> BUG FOUND in completeness(gold=[None]): {type(e).__name__}: {e}")
        findings.append(("completeness_none_id", f"{type(e).__name__}: {e}", traceback.format_exc()))

    # 4. Non-string types in note payloads
    print("--- Probing non-string types (int, list, dict) in note content ---")
    note_with_int = [{"id": 12345, "content": 67890}]
    try:
        evaluator.utilization(note_with_int, "67890 response")
    except Exception as e:
        print(f"  -> BUG in utilization(content=int): {type(e).__name__}: {e}")
        findings.append(("utilization_int_content", f"{type(e).__name__}: {e}", traceback.format_exc()))

    # 5. Unicode and extreme strings
    print("--- Probing Unicode and extreme strings ---")
    unicode_notes = [{"id": "u1", "content": "🔥 🚀 💡 人工智能 记忆系统 持续学习 检索评估"}]
    u_unicode = evaluator.utilization(unicode_notes, "人工智能 记忆系统 持续学习")
    adh_unicode = evaluator.adherence("人工智能 记忆系统", unicode_notes)
    rel_unicode = evaluator.relevance(unicode_notes, "持续学习")
    print(f"  -> Unicode tests: utilization={u_unicode}, adherence={adh_unicode}, relevance={rel_unicode}")

    # 6. Float inf / nan in relevance_scores
    print("--- Probing inf / nan / negative relevance scores in NDCG ---")
    try:
        ndcg_inf = evaluator.ndcg_at_k(["d1", "d2"], {"d1": float('inf'), "d2": 1.0}, k=2)
        print(f"  -> NDCG with inf relevance: {ndcg_inf}")
    except Exception as e:
        findings.append(("ndcg_inf", f"{type(e).__name__}: {e}", traceback.format_exc()))

    try:
        ndcg_nan = evaluator.ndcg_at_k(["d1", "d2"], {"d1": float('nan'), "d2": 1.0}, k=2)
        print(f"  -> NDCG with nan relevance: {ndcg_nan}")
    except Exception as e:
        findings.append(("ndcg_nan", f"{type(e).__name__}: {e}", traceback.format_exc()))

    try:
        ndcg_neg = evaluator.ndcg_at_k(["d1", "d2"], {"d1": -1.0, "d2": -2.0}, k=2)
        print(f"  -> NDCG with negative relevance: {ndcg_neg}")
    except Exception as e:
        findings.append(("ndcg_neg", f"{type(e).__name__}: {e}", traceback.format_exc()))

    print("\n================ SUMMARY OF FINDINGS ================")
    print(f"Total findings/bugs detected: {len(findings)}")
    for name, desc, tb in findings:
        print(f"[-] {name}: {desc}")

if __name__ == "__main__":
    run_evaluation_stress_suite()
