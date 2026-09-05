import pytest
import os
import shutil
import tempfile
import uuid
import yaml
from pathlib import Path

from memory_controller.storage.file_engine import FileStorageEngine
from memory_controller.storage.serializer import serialize, deserialize
from memory_controller.storage.path_resolver import resolve_path, sanitize_filename

@pytest.fixture
def temp_vault():
    tmp_dir = tempfile.mkdtemp()
    folders = [
        "00_CORE", "01_KNOWLEDGE", "02_PROJECTS", "03_PROCEDURES",
        "04_MEMORY", "05_RESOURCES", "06_INBOX/RAW_IMPORTS",
        "90_TEMPLATES", "99_SYSTEM"
    ]
    for folder in folders:
        os.makedirs(os.path.join(tmp_dir, folder))
    yield tmp_dir
    shutil.rmtree(tmp_dir)

def create_valid_note(override=None):
    base = {
        "id": str(uuid.uuid4()),
        "type": "knowledge",
        "lifecycle": "ACTIVE",
        "category": "test_cat",
        "content": "This is a test note."
    }
    if override:
        base.update(override)
    return base

def test_frontmatter_roundtrip():
    # Test CRLF, empty body, {{date}} in body, "---" in body
    note = create_valid_note({"content": "Hello\n---\n{{date}}\nWorld"})
    note["custom_field"] = "preserved"
    
    serialized = serialize(note)
    # Convert to CRLF for robustness test
    crlf_serialized = serialized.replace('\n', '\r\n')
    
    deserialized = deserialize(crlf_serialized)
    assert deserialized["id"] == note["id"]
    # We expect the exact body back, which now has CRLF
    expected_body = "Hello\n---\n{{date}}\nWorld".replace('\n', '\r\n')
    assert deserialized["content"] == expected_body
    assert deserialized["custom_field"] == "preserved"

def test_path_resolution():
    base = "C:\\Vault" if os.name == 'nt' else "/Vault"  # hygiene: intentional-absolute-path
    assert "01_KNOWLEDGE" in resolve_path(base, {"type": "knowledge", "category": "sec", "id": "123"})
    assert "02_PROJECTS" in resolve_path(base, {"type": "project", "category": "dev", "id": "123"})
    
def test_filename_safety():
    assert sanitize_filename("invalid:name*?") == "invalid_name__"
    assert sanitize_filename("CON") == "CON_"
    assert sanitize_filename("trailing. ") == "trailing"
    long_name = "A" * 300
    assert len(sanitize_filename(long_name)) == 100

def test_path_traversal_storage(temp_vault):
    engine = FileStorageEngine(temp_vault)
    bad_inputs = [
        {"id": "../../../malicious", "category": "safe"},
        {"id": "safe", "category": "../../../malicious"},
        {"id": "safe", "category": "C:\\Windows\\System32"},  # hygiene: intentional-absolute-path
        {"id": "safe", "category": "/etc/passwd"},  # hygiene: intentional-absolute-path
    ]
    
    for bad in bad_inputs:
        note = create_valid_note(bad)
        try:
            engine.set(note["id"], note)
            # If it succeeded, it MUST be safely inside the vault due to sanitization
            path = engine.id_to_path[note["id"]]
            assert os.path.commonpath([os.path.realpath(path), os.path.realpath(temp_vault)]) == os.path.realpath(temp_vault)
        except ValueError:
            pass # Traversal correctly blocked by raising

def test_id_invariant(temp_vault):
    engine = FileStorageEngine(temp_vault)
    note = create_valid_note()
    # Mismatch note_id vs data["id"]
    with pytest.raises(ValueError, match="ID mismatch"):
        engine.set("different_id", note)

def test_filesystem_write_persists(temp_vault):
    engine = FileStorageEngine(temp_vault)
    note = create_valid_note({"type": "knowledge"})
    engine.set(note["id"], note)
    
    knowledge_dir = os.path.join(temp_vault, "01_KNOWLEDGE")
    files = os.listdir(knowledge_dir)
    assert len(files) == 1
    
def test_restart_persistence(temp_vault):
    engine1 = FileStorageEngine(temp_vault)
    note = create_valid_note()
    engine1.set(note["id"], note)
    
    engine2 = FileStorageEngine(temp_vault)
    assert engine2.get(note["id"])["id"] == note["id"]

def test_uuid_survives_filename_change(temp_vault):
    engine = FileStorageEngine(temp_vault)
    note = create_valid_note({"category": "old_title"})
    engine.set(note["id"], note)
    path1 = engine.id_to_path[note["id"]]
    
    note["category"] = "new_title"
    engine.set(note["id"], note)
    path2 = engine.id_to_path[note["id"]]
    
    assert path1 != path2
    assert not os.path.exists(path1)
    assert os.path.exists(path2)

def test_update_persists(temp_vault):
    engine = FileStorageEngine(temp_vault)
    note = create_valid_note()
    engine.set(note["id"], note)
    
    note["content"] = "Updated body"
    engine.set(note["id"], note)
    assert FileStorageEngine(temp_vault).get(note["id"])["content"] == "Updated body"

def test_lifecycle_persists(temp_vault):
    engine = FileStorageEngine(temp_vault)
    note = create_valid_note({"lifecycle": "REVIEW"})
    engine.set(note["id"], note)
    
    note["lifecycle"] = "ACTIVE"
    engine.set(note["id"], note)
    assert FileStorageEngine(temp_vault).get(note["id"])["lifecycle"] == "ACTIVE"

def test_raw_imports_untouched(temp_vault):
    engine = FileStorageEngine(temp_vault)
    raw_dir = os.path.join(temp_vault, "06_INBOX/RAW_IMPORTS")
    dummy_path = os.path.join(raw_dir, "dummy.md")
    with open(dummy_path, 'w') as f:
        f.write("---\nid: raw123\n---\nRaw")
        
    engine2 = FileStorageEngine(temp_vault)
    # Should not index it
    assert engine2.get("raw123") is None
    
    # Writing to RAW should fail
    note = create_valid_note({"id": "raw123"})
    with pytest.raises(ValueError):
        resolve_path(temp_vault, {"type": "inbox"}) # Mappings shouldn't even allow it

def test_duplicate_uuid_detection(temp_vault):
    note = create_valid_note()
    p1 = os.path.join(temp_vault, "01_KNOWLEDGE", f"A_{note['id']}.md")
    p2 = os.path.join(temp_vault, "02_PROJECTS", f"B_{note['id']}.md")
    with open(p1, 'w') as f: f.write(serialize(note))
    with open(p2, 'w') as f: f.write(serialize(note))
    
    with pytest.raises(ValueError, match="Duplicate UUID"):
        FileStorageEngine(temp_vault)

def test_malformed_frontmatter(temp_vault):
    bad_path = os.path.join(temp_vault, "01_KNOWLEDGE", "bad.md")
    with open(bad_path, 'w') as f:
        f.write("---\n[invalid\n---\nContent")
    
    # Should not crash, should be skipped
    engine = FileStorageEngine(temp_vault)
    assert len(engine.id_to_path) == 0

def test_90_templates_exclusion(temp_vault):
    template_path = os.path.join(temp_vault, "90_TEMPLATES", "temp.md")
    with open(template_path, 'w') as f:
        f.write("---\nid: {{date}}\n---\nTemplate")
        
    engine = FileStorageEngine(temp_vault)
    assert len(engine.id_to_path) == 0

def test_atomic_write(temp_vault):
    engine = FileStorageEngine(temp_vault)
    note = create_valid_note()
    engine.set(note["id"], note)
    
    files = os.listdir(os.path.join(temp_vault, "01_KNOWLEDGE"))
    # Ensure no .tmp_ files are left behind
    assert len(files) == 1
    assert not files[0].startswith(".tmp_")
