from pathlib import Path

from importlib.util import module_from_spec, spec_from_file_location


SCRIPT = Path(__file__).resolve().parents[2] / "30_SCRIPTS" / "verification" / "write_path_audit.py"


def _load_module():
    spec = spec_from_file_location("write_path_audit", SCRIPT)
    assert spec and spec.loader
    module = module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def test_write_path_audit_finds_direct_storage_mutation(tmp_path):
    module = _load_module()
    source = tmp_path / "candidate.py"
    source.write_text(
        "class X:\n"
        "    def f(self, note):\n"
        "        self.controller.storage.set(note['id'], note)\n"
        "        self.controller.storage.delete(note['id'])\n",
        encoding="utf-8",
    )

    findings = module.audit(tmp_path)

    assert [(item.classification, item.expression) for item in findings] == [
        ("DIRECT_STORAGE_MUTATION", "self.controller.storage.set"),
        ("DIRECT_STORAGE_MUTATION", "self.controller.storage.delete"),
    ]


def test_write_path_audit_finds_common_filesystem_mutators(tmp_path):
    module = _load_module()
    source = tmp_path / "candidate.py"
    source.write_text(
        "from pathlib import Path\n"
        "import os\n"
        "import shutil\n"
        "\n"
        "def f(path):\n"
        "    Path(path).write_text('x')\n"
        "    Path(path).write_bytes(b'x')\n"
        "    Path(path).unlink()\n"
        "    os.remove(path)\n"
        "    os.unlink(path)\n"
        "    shutil.move(path, path)\n",
        encoding="utf-8",
    )

    findings = module.audit(tmp_path)

    assert [item.expression for item in findings] == [
        "Path.write_text",
        "Path.write_bytes",
        "Path.unlink",
        "os.remove",
        "os.unlink",
        "shutil.move",
    ]
    assert all(item.classification == "FILE_WRITE" for item in findings)


def test_write_path_audit_classifies_canonical_controller_separately(tmp_path):
    module = _load_module()
    controller = tmp_path / "memory_controller" / "controller.py"
    controller.parent.mkdir()
    controller.write_text(
        "def persist(storage, note):\n"
        "    storage.set(note['id'], note)\n",
        encoding="utf-8",
    )

    findings = module.audit(tmp_path)

    assert len(findings) == 1
    assert findings[0].classification == "CANONICAL_CONTROLLER"


def test_write_path_audit_is_read_only(tmp_path):
    module = _load_module()
    source = tmp_path / "candidate.py"
    source.write_text("def f(storage, note):\n    storage.set(note['id'], note)\n", encoding="utf-8")
    before = source.read_bytes()

    module.audit(tmp_path)

    assert source.read_bytes() == before
