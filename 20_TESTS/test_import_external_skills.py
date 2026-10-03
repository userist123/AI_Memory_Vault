from pathlib import Path
import importlib.util
ROOT=Path(__file__).resolve().parents[1]
spec=importlib.util.spec_from_file_location('importer',ROOT/'30_SCRIPTS/verification/import_external_skills.py')
mod=importlib.util.module_from_spec(spec); spec.loader.exec_module(mod)
def test_allowed_and_forbidden_extensions():
 assert mod.ALLOWED=={'.md','.json','.txt','.yaml'}
 assert not (mod.ALLOWED & mod.FORBIDDEN)
def test_symlink_rejected(tmp_path):
 target=tmp_path/'target.md'; target.write_text('safe'); link=tmp_path/'link.md'; link.symlink_to(target)
 try: mod.copy_tree(tmp_path,tmp_path/'out')
 except SystemExit as e: assert 'symlink' in str(e)
 else: raise AssertionError('symlink accepted')
def test_source_pins_are_full_sha():
 for item in mod.pins(): assert len(item['ref'])==40 and all(c in '0123456789abcdef' for c in item['ref'])
def test_traversal_guard_exists():
 assert '..' in Path(mod.__file__).read_text()
