from cognitive_core.conflict_detector import ConflictDetector
from cognitive_core.extraction import AtomicMemoryExtractor
from cognitive_core.proposal_queue import MemoryProposalQueue
from cognitive_core.queue_promoter import QueuePromoter


class _FakeStorage:
    def __init__(self):
        self.store = {}

    def get(self, note_id):
        return self.store.get(note_id)

    def set(self, note_id, data):
        self.store[note_id] = data.copy()


class _FakePrincipal:
    value = "ai_agent"


class _FakeController:
    def __init__(self):
        self.storage = _FakeStorage()
        self.calls = []

    def propose(self, principal, note_data):
        self.calls.append((principal, note_data))
        note_id = note_data["id"]
        self.storage.set(note_id, {**note_data, "lifecycle": "RAW"})
        return note_id


def test_conflict_detector_flags_overlap_same_category():
    detector = ConflictDetector(overlap_threshold=0.2)
    candidate = {"category": "architecture", "content": "folosim SQLite WAL pentru index local"}
    existing = [{"id": "n1", "lifecycle": "ACTIVE", "category": "architecture",
                 "content": "folosim SQLite WAL pentru index local vault"}]
    flags = detector.detect(candidate, existing)
    assert flags and flags[0]["note_id"] == "n1"


def test_queue_promoter_promotes_only_approved(tmp_path):
    queue = MemoryProposalQueue(tmp_path / "queue.jsonl")
    candidates = AtomicMemoryExtractor().extract("Am decis: folosim WAL.", "session:test")
    queue.enqueue(candidates)
    pending_id = queue.pending()[0]["candidate_id"]
    queue.mark(pending_id, "APPROVED", reviewer="human")

    controller = _FakeController()
    promoter = QueuePromoter(queue, controller, _FakePrincipal())
    promoted_ids = promoter.promote_approved()

    assert len(promoted_ids) == 1
    assert controller.calls[0][1]["content"] == "folosim WAL."
    assert queue._load()[0]["queue_status"] == "PROMOTED"


def test_queue_promoter_ignores_pending_and_rejected(tmp_path):
    queue = MemoryProposalQueue(tmp_path / "queue.jsonl")
    candidates = AtomicMemoryExtractor().extract("Todo: revizuie\u0219te planul.", "session:test")
    queue.enqueue(candidates)
    controller = _FakeController()
    promoter = QueuePromoter(queue, controller, _FakePrincipal())
    assert promoter.promote_approved() == []
