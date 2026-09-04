import pytest
import uuid
from memory_controller.controller import MemoryController, Principal, Lifecycle, _storage_engine

@pytest.fixture
def controller():
    storage = _storage_engine
    storage.store.clear()
    return MemoryController(storage)

def test_raw_note_excluded_from_search(controller):
    # Create a RAW note manually
    raw_id = str(uuid.uuid4())
    raw_note = {
        'id': raw_id,
        'type': 'knowledge',
        'category': 'test',
        'tags': [],
        'created': '2023-01-01',
        'updated': '2023-01-01',
        'provenance': {'source_type': 'user', 'source_ref': 'test'},
        'confidence': 'high',
        'verification': 'unverified',
        'relations': [],
        'lifecycle': Lifecycle.RAW.value,
    }
    # Store directly in the storage engine (bypassing controller)
    controller.storage.set(raw_id, raw_note)
    # Perform a search – RAW notes must be excluded from results
    pack = controller.search(Principal.HUMAN, "any query")
    results = pack.get('results', [])
    assert all(note.get('lifecycle') != Lifecycle.RAW for note in results)

def test_raw_note_cannot_be_read(controller):
    raw_id = str(uuid.uuid4())
    raw_note = {
        'id': raw_id,
        'type': 'knowledge',
        'category': 'test',
        'tags': [],
        'created': '2023-01-01',
        'updated': '2023-01-01',
        'provenance': {'source_type': 'user', 'source_ref': 'test'},
        'confidence': 'high',
        'verification': 'unverified',
        'relations': [],
        'lifecycle': Lifecycle.RAW.value,
    }
    controller.storage.set(raw_id, raw_note)
    # READ operation should raise because only ACTIVE notes are readable
    with pytest.raises(ValueError):
        controller.read(Principal.HUMAN, raw_id)
