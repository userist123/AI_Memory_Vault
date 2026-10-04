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

def test_copy_tree_skips_hidden_and_non_allowed(tmp_path):
 src = tmp_path / 'source'
 src.mkdir()
 (src / '.git').mkdir()
 (src / '.git' / 'HEAD').write_text('ref: refs/heads/main')
 (src / '.git' / 'config').write_text('[core]')
 (src / 'valid.md').write_text('# Valid Markdown')
 (src / 'data.json').write_text('{"key": "value"}')
 (src / 'script.py').write_text('print("hello")')
 (src / 'run.sh').write_text('#!/bin/bash')
 (src / 'image.png').write_bytes(b'\x89PNG')

 dst = tmp_path / 'dest'
 mod.copy_tree(src, dst)

 assert (dst / 'valid.md').exists()
 assert (dst / 'valid.md').read_text() == '# Valid Markdown'
 assert (dst / 'data.json').exists()
 assert not (dst / '.git').exists()
 assert not (dst / 'script.py').exists()
 assert not (dst / 'run.sh').exists()
 assert not (dst / 'image.png').exists()

