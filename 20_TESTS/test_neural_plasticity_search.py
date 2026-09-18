"""Tests for neural plasticity connected to search, spreading activation, and durable edge invariance."""
import os
import sys
import unittest
from pathlib import Path

import pytest

REPO_ROOT = Path(__file__).resolve().parents[1]
PACKAGES_DIR = REPO_ROOT / "03_IMPLEMENTATION" / "packages"
for p in (str(REPO_ROOT), str(PACKAGES_DIR)):
    if p not in sys.path:
        sys.path.insert(0, p)


from memory_controller.authorizer import Principal
from memory_controller.controller import MemoryController
from memory_controller.storage.file_engine import FileStorageEngine
from graph.synapse_store import SynapseStore, Synapse, is_durable
from graph.plasticity import PlasticityEngine, PlasticityJournal
from retrieval.vault_index import VaultIndex


@pytest.fixture(autouse=True)
def _hmac_secret(monkeypatch):
    """Set per test, not at import: a module-level default is gone by the time
    this runs if an earlier test in the session removed it."""
    monkeypatch.setenv("MEMORY_CONTROLLER_HMAC_SECRET", "0" * 32)


class TestNeuralPlasticitySearch(unittest.TestCase):
    def setUp(self):
        self.storage = FileStorageEngine(str(REPO_ROOT))
        self.index = VaultIndex.load(REPO_ROOT, lifecycles=["ACTIVE", "VERIFIED"])
        self.store = SynapseStore()

    def test_1_planted_false_edge_depressed_and_atrophied(self):
        """Test 1: A planted false edge is traversed, penalized on failure, and atrophied."""
        # Find two real notes
        notes = [n for n in self.index.notes if not n.is_raw and not n.is_archived][:5]
        self.assertGreaterEqual(len(notes), 2)
        n_a, n_b = notes[0], notes[1]

        # Plant a machine-proposed false edge
        false_syn = Synapse(
            source_id=n_a.id,
            target_id=n_b.id,
            relation="related_to",
            weight=0.60,
            origin="proposed",
        )
        self.store.add(false_syn)
        initial_weight = false_syn.weight

        controller = MemoryController(
            storage=self.storage,
            index=self.index,
            synapse_store=self.store,
            enable_graph_expansion=True,
            enable_spreading_activation=True,
            graph_expansion_budget=10,
        )

        # Search using note A's title
        pack = controller.search(Principal.HUMAN, n_a.title, page_size=10)
        trace = pack.get("candidate_trace", {})
        self.assertTrue(trace.get("spreading_activation_enabled"))
        self.assertGreater(false_syn.activations, 0)

        # Apply a verified failure outcome (e.g., human or test detected hallucination/wrong answer)
        plasticity = PlasticityEngine()
        outcome = {
            "run_id": "test_run_fail_01",
            "outcome": "fail",
            "verification_method": "pytest",
        }
        res = plasticity.apply_outcome(
            synapse_store=self.store,
            candidate_trace=trace,
            outcome_record=outcome,
            used_memory_ids=[n_b.id],
            run_id="test_run_fail_01",
        )

        self.assertEqual(res.status, "applied")
        self.assertGreater(res.applied_count, 0)
        
        # Verify weight depression
        depressed_weight = false_syn.weight
        delta = depressed_weight - initial_weight
        self.assertLessEqual(delta, -0.05, f"Expected weight depression <= -0.05, got {delta}")

        # Run decay and prune: the depressed false edge should fall below prune threshold
        self.store.decay_unused(factor=0.8)
        pruned_count = self.store.prune(threshold=0.55, keep_durable=True)
        self.assertGreaterEqual(pruned_count, 1, "Planted false edge was not pruned")

    def test_2_correct_edge_stable_under_noise_and_reinforced(self):
        """Test 2: A correct edge is stable under noise queries and reinforced on verified success."""
        notes = [n for n in self.index.notes if not n.is_raw and not n.is_archived][:5]
        n_a, n_c = notes[2], notes[3]

        valid_syn = Synapse(
            source_id=n_a.id,
            target_id=n_c.id,
            relation="depends_on",
            weight=0.50,
            origin="proposed",
        )
        self.store.add(valid_syn)
        initial_weight = valid_syn.weight

        controller = MemoryController(
            storage=self.storage,
            index=self.index,
            synapse_store=self.store,
            enable_graph_expansion=True,
            enable_spreading_activation=True,
            graph_expansion_budget=10,
        )

        # 1. Run 3 unrelated noise queries: valid_syn must not be modified
        plasticity = PlasticityEngine()
        for i in range(3):
            noise_pack = controller.search(Principal.HUMAN, f"arbitrary query noise {i}", page_size=5)
            # Unrelated outcome that did NOT use n_c
            noise_outcome = {"run_id": f"noise_{i}", "outcome": "success", "verification_method": "ci"}
            res = plasticity.apply_outcome(
                synapse_store=self.store,
                candidate_trace=noise_pack.get("candidate_trace", {}),
                outcome_record=noise_outcome,
                used_memory_ids=["unrelated_note_xyz"],
                run_id=f"noise_{i}",
            )
            # Must have no attributed edges for our valid_syn
            self.assertEqual(valid_syn.weight, initial_weight)

        # 2. Run query activating n_a and using n_c in verified success
        pack = controller.search(Principal.HUMAN, n_a.title, page_size=10)
        trace = pack.get("candidate_trace", {})

        success_outcome = {"run_id": "test_success_01", "outcome": "success", "verification_method": "pytest"}
        res = plasticity.apply_outcome(
            synapse_store=self.store,
            candidate_trace=trace,
            outcome_record=success_outcome,
            used_memory_ids=[n_c.id],
            run_id="test_success_01",
        )

        self.assertEqual(res.status, "applied")
        self.assertGreater(valid_syn.weight, initial_weight)
        self.assertGreaterEqual(valid_syn.reinforcements, 1)

    def test_3_r005_durable_edge_invariance_under_30_decay_cycles(self):
        """Test 3: Scenariul r005 — 30 de cicluri de decădere fără activare NU afectează muchiile durabile."""
        store = SynapseStore()

        # Add durable edges (declared, inferred, wikilink)
        durable_synapses = [
            Synapse("doc_1", "doc_2", relation="depends_on", weight=0.9, origin="declared"),
            Synapse("doc_2", "doc_1", relation="related_to", weight=0.25, origin="inferred"),
            Synapse("doc_1", "doc_3", relation="related_to", weight=0.2, origin="wikilink"),
        ]
        for s in durable_synapses:
            self.assertTrue(is_durable(s.origin))
            store.add(s)

        # Add ephemeral machine-proposed edge
        ephemeral_syn = Synapse("doc_3", "doc_4", relation="related_to", weight=0.3, origin="proposed")
        self.assertFalse(is_durable(ephemeral_syn.origin))
        store.add(ephemeral_syn)

        durable_initial_weights = {s.key: s.weight for s in durable_synapses}

        # Run 30 consecutive decay cycles without any activations
        for _ in range(30):
            store.decay_unused(factor=0.90)

        # Durable edges must have exactly the same initial weights (not decayed)
        for s in durable_synapses:
            self.assertEqual(store._by_key[s.key].weight, durable_initial_weights[s.key],
                             f"Durable edge {s.key} decayed unexpectedly!")

        # Ephemeral edge must have decayed severely
        self.assertLess(store._by_key[ephemeral_syn.key].weight, 0.05)

        # Call prune with keep_durable=True (default contract)
        pruned = store.prune(threshold=0.12, keep_durable=True)
        self.assertEqual(pruned, 1, "Only the ephemeral edge should be pruned")
        self.assertNotIn(ephemeral_syn.key, store._by_key)

        # 100% of durable edges survived
        for s in durable_synapses:
            self.assertIn(s.key, store._by_key)


if __name__ == "__main__":
    unittest.main()
