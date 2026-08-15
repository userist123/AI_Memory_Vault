import ast
import math
import uuid
import sys
import os
sys.path.insert(0, os.path.abspath("."))
from cognitive_core.learning import ContinualLearningGuard, LearningEngine
from cognitive_core.evaluation import RetrievalEvaluator
from cognitive_core.semantic import DeterministicSemanticProvider
from cognitive_core.tool_router import ToolRouter
from memory_controller.controller import MemoryController, StorageEngine
from memory_controller.authorizer import Principal
from memory_controller.core import Lifecycle

def audit_ast(filepath):
    print(f"[*] Auditing AST of {filepath}...")
    with open(filepath, "r", encoding="utf-8") as f:
        tree = ast.parse(f.read(), filename=filepath)
    
    forbidden_calls = {"eval", "exec", "__import__", "compile"}
    for node in ast.walk(tree):
        if isinstance(node, ast.Call):
            if isinstance(node.func, ast.Name) and node.func.id in forbidden_calls:
                raise AssertionError(f"FORBIDDEN CALL DETECTED: {node.func.id} in {filepath}")
    print(f"[+] AST Audit PASSED for {filepath}: 0 forbidden constructs detected.")

def audit_continual_learning_guard():
    print("[*] Auditing ContinualLearningGuard behavior under stress...")
    guard = ContinualLearningGuard()
    
    # Register multiple nodes
    nodes = [
        {"id": f"node-{i}", "content": f"Content {i}", "verification": "verified" if i % 2 == 0 else "unverified"}
        for i in range(50)
    ]
    for n in nodes:
        guard.register_anchor_node(n)
        
    assert len(guard.replay_anchor_nodes) == 50
    
    # 1. Clean verification
    ok, violations = guard.verify_no_catastrophic_regression(nodes)
    assert ok is True and len(violations) == 0, f"Clean verification failed: {violations}"
    
    # 2. Deletion of 10 nodes
    subset = nodes[10:]
    ok, violations = guard.verify_no_catastrophic_regression(subset)
    assert ok is False and len(violations) == 10, f"Expected 10 deletion violations, got {len(violations)}"
    
    # 3. Modification of content
    modified = [dict(n) for n in nodes]
    modified[0]["content"] = "Corrupted content"
    ok, violations = guard.verify_no_catastrophic_regression(modified)
    assert ok is False and len(violations) == 1
    assert "node-0" in violations[0] and "content drift" in violations[0]
    
    # 4. Downgrade of verified node
    downgraded = [dict(n) for n in nodes]
    downgraded[2]["verification"] = "unverified" # node-2 was verified
    ok, violations = guard.verify_no_catastrophic_regression(downgraded)
    assert ok is False and len(violations) == 1
    assert "node-2" in violations[0] and "downgraded" in violations[0]
    
    # 5. Malformed storage items (missing id, missing content)
    malformed = [dict(n) for n in nodes]
    malformed.append({"random_key": "val"}) # No id
    ok, violations = guard.verify_no_catastrophic_regression(malformed)
    assert ok is True # Unanchored malformed notes don't trigger regression of anchor set
    
    print("[+] ContinualLearningGuard Stress Audit PASSED.")

def audit_learning_engine_invariants():
    print("[*] Auditing LearningEngine P0-P15 trust boundary invariants...")
    storage = StorageEngine()
    controller = MemoryController(storage)
    router = ToolRouter(controller)
    engine = LearningEngine(controller, router)
    
    # Test all possible source_types for very_high promotion attempts
    all_source_types = ["execution", "inference", "ai", "user", "official", "experience", "import", "unknown", "custom"]
    
    created_notes = {}
    for st in all_source_types:
        nid = str(uuid.uuid4())
        note = {
            "id": nid,
            "type": "knowledge",
            "lifecycle": "ACTIVE",
            "category": "audit",
            "tags": ["audit"],
            "created": "2026-08-15",
            "updated": "2026-08-15",
            "provenance": {"source_type": st, "source_ref": f"ref_{st}"},
            "confidence": "high",
            "verification": "unverified",
            "relations": [{"relation": "related_to", "target": "knowledge", "target_id": str(uuid.uuid4())} for _ in range(12)],
            "content": f"Audit note testing provenance {st}"
        }
        storage.set(nid, note)
        created_notes[st] = nid
        
    promoted = engine.promote_memories(Principal.AI_AGENT)
    
    # ONLY execution provenance must be promoted to very_high
    assert created_notes["execution"] in promoted, "Execution note should have been promoted!"
    exec_record = storage.get(created_notes["execution"])
    assert exec_record["confidence"] == "very_high", f"Execution note confidence expected 'very_high', got {exec_record['confidence']}"
    assert exec_record["verification"] == "partially_verified", f"AI promotion must set partially_verified, got {exec_record['verification']}"
    
    for st in all_source_types:
        if st != "execution":
            assert created_notes[st] not in promoted, f"Source type '{st}' was illegally promoted to very_high!"
            rec = storage.get(created_notes[st])
            assert rec["confidence"] == "high", f"Source type '{st}' confidence modified illegally to {rec['confidence']}"
            assert rec["verification"] == "unverified", f"Source type '{st}' verification modified illegally to {rec['verification']}"
            
    print("[+] LearningEngine Trust Invariant Audit PASSED: only execution provenance can escalate to very_high.")

def audit_retrieval_evaluator_math():
    print("[*] Auditing RetrievalEvaluator mathematical rigor...")
    evaluator = RetrievalEvaluator()
    
    # Test NDCG calculation precision across edge cases
    # Case 1: single item
    assert evaluator.ndcg_at_k(["d1"], {"d1": 5.0}, k=1) == 1.0
    
    # Case 2: negative or zero k
    assert evaluator.ndcg_at_k(["d1"], {"d1": 5.0}, k=0) == 0.0
    assert evaluator.ndcg_at_k(["d1"], {"d1": 5.0}, k=-5) == 0.0
    assert evaluator.precision_at_k(["d1"], {"d1"}, k=-1) == 0.0
    assert evaluator.recall_at_k(["d1"], {"d1"}, k=-1) == 0.0
    
    # Case 3: Empty inputs
    assert evaluator.ndcg_at_k([], {}, k=5) == 0.0
    assert evaluator.precision_at_k([], set(), k=5) == 0.0
    assert evaluator.recall_at_k([], set(), k=5) == 1.0 # 0 relevant means 100% recalled vacuously
    assert evaluator.mean_reciprocal_rank([], []) == 0.0
    assert evaluator.reciprocal_rank([], set()) == 0.0
    
    print("[+] RetrievalEvaluator Mathematical Audit PASSED.")

if __name__ == "__main__":
    try:
        audit_ast("cognitive_core/learning.py")
        audit_ast("cognitive_core/evaluation.py")
        audit_ast("cognitive_core/tests/test_milestone5_continual_learning_eval.py")
        audit_continual_learning_guard()
        audit_learning_engine_invariants()
        audit_retrieval_evaluator_math()
        print("\n==========================================")
        print("ALL FORENSIC CHECKS PASSED EMPIRICALLY!")
        print("==========================================")
    except Exception as e:
        print(f"FORENSIC VIOLATION / ERROR: {e}", file=sys.stderr)
        sys.exit(1)
