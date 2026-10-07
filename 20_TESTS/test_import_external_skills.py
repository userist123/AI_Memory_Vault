from pathlib import Path
import importlib.util
import json
import os
import stat

import pytest

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


# --- fail closed: nothing dangerous is dropped silently ---------------------------------------

def _source(tmp_path):
 src=tmp_path/'source'; src.mkdir()
 (src/'.git').mkdir(); (src/'.git'/'HEAD').write_text('ref: refs/heads/main'); (src/'.git'/'config').write_text('[core]')
 (src/'valid.md').write_text('# Valid Markdown'); (src/'data.json').write_text('{"key": "value"}')
 return src

def _all_files(root):
 return sorted(str(p.relative_to(root)) for p in root.rglob('*') if p.is_file()) if root.exists() else []

def test_a_clean_source_is_copied_and_its_vcs_metadata_pruned(tmp_path):
 src=_source(tmp_path); dst=tmp_path/'dest'
 mod.copy_tree(src,dst)
 assert _all_files(dst)==['data.json','valid.md']
 assert (dst/'valid.md').read_text()=='# Valid Markdown'

def test_scripts_and_binaries_abort_the_import_and_every_one_is_listed(tmp_path):
 src=_source(tmp_path)
 (src/'script.py').write_text('print("hello")'); (src/'run.sh').write_text('#!/bin/bash')
 (src/'sub').mkdir(); (src/'sub'/'tool.exe').write_bytes(b'MZ'); (src/'sub'/'ok.md').write_text('# ok')
 dst=tmp_path/'dest'
 with pytest.raises(SystemExit) as raised: mod.copy_tree(src,dst)
 message=str(raised.value)
 assert 'fail-closed' in message and '3 offending path(s)' in message
 for offender in ('script.py','run.sh',os.path.join('sub','tool.exe')): assert offender in message
 assert _all_files(dst)==[], 'nothing is copied when the import is rejected'

def test_hidden_paths_other_than_the_checkouts_own_metadata_abort_the_import(tmp_path):
 src=_source(tmp_path)
 (src/'.env').write_text('TOKEN=x'); (src/'.github').mkdir(); (src/'.github'/'workflow.md').write_text('# x')
 (src/'deep').mkdir(); (src/'deep'/'.git').mkdir(); (src/'deep'/'.git'/'config').write_text('[core]')
 with pytest.raises(SystemExit) as raised: mod.copy_tree(src,tmp_path/'dest')
 message=str(raised.value)
 assert '3 offending path(s)' in message
 for offender in ('.env',os.path.join('.github','workflow.md'),os.path.join('deep','.git','config')): assert 'hidden path: '+offender in message
 assert _all_files(tmp_path/'dest')==[]

def test_an_executable_bit_aborts_the_import_even_on_an_allowed_type(tmp_path):
 src=_source(tmp_path)
 launcher=src/'notes.md'; launcher.write_text('#!/bin/sh\necho hi\n'); launcher.chmod(launcher.stat().st_mode|stat.S_IXUSR)
 with pytest.raises(SystemExit) as raised: mod.copy_tree(src,tmp_path/'dest')
 assert 'executable bit: notes.md' in str(raised.value)
 assert _all_files(tmp_path/'dest')==[]

def test_files_of_a_type_that_is_not_imported_are_reported_not_dropped_silently(tmp_path):
 src=_source(tmp_path)
 (src/'image.png').write_bytes(b'\x89PNG'); (src/'LICENSE').write_text('MIT'); (src/'docs').mkdir(); (src/'docs'/'font.woff2').write_bytes(b'wOF2')
 dst=tmp_path/'dest'; skipped=[]
 mod.copy_tree(src,dst,skipped)
 assert _all_files(dst)==['data.json','valid.md']
 assert sorted(skipped)==['LICENSE','docs/font.woff2','image.png']

def _fake_checkouts(tmp_path, extra=None):
 """A TMP with the six pinned sources laid out the way filter_imports() expects them."""
 tmp=tmp_path/'checkouts'
 layout={'github-awesome-copilot':['skills/a.md'],'web-quality-skills':['skills/b.md'],'ui-sensei':['c.md'],
         'garden-skills':['skills/web-design-engineer/d.md'],'xiaopu-web-design':['SKILL.md','references/e.md'],
         'awesome-design-skills':['skills/f.md']}
 for sid,files in layout.items():
  (tmp/sid).mkdir(parents=True); (tmp/sid/'LICENSE').write_text('MIT')
  for rel in files: (tmp/sid/rel).parent.mkdir(parents=True,exist_ok=True); (tmp/sid/rel).write_text('# '+rel)
 for rel,data in (extra or {}).items(): (tmp/rel).parent.mkdir(parents=True,exist_ok=True); (tmp/rel).write_bytes(data)
 return tmp

def test_filter_aborts_on_an_offending_file_and_stages_nothing_reportable(tmp_path,monkeypatch):
 monkeypatch.setattr(mod,'TMP',_fake_checkouts(tmp_path,{'ui-sensei/install.sh':b'#!/bin/sh'}))
 monkeypatch.setattr(mod,'STAGE',tmp_path/'stage'); monkeypatch.setattr(mod,'SKIPPED',tmp_path/'SKIPPED_FILES.json')
 with pytest.raises(SystemExit) as raised: mod.filter_imports()
 assert 'install.sh' in str(raised.value)
 assert not (tmp_path/'SKIPPED_FILES.json').exists(), 'no report is issued for an import that did not complete'

def test_filter_writes_a_manifest_of_every_file_it_did_not_import(tmp_path,monkeypatch,capsys):
 monkeypatch.setattr(mod,'TMP',_fake_checkouts(tmp_path,{'ui-sensei/logo.png':b'\x89PNG','awesome-design-skills/skills/shot.jpg':b'\xff\xd8'}))
 monkeypatch.setattr(mod,'STAGE',tmp_path/'stage'); monkeypatch.setattr(mod,'SKIPPED',tmp_path/'SKIPPED_FILES.json')
 mod.filter_imports()
 report=json.loads((tmp_path/'SKIPPED_FILES.json').read_text())
 assert report['skipped']=={'ui-sensei':['LICENSE','logo.png'],'awesome-design-skills':['shot.jpg']}
 out=capsys.readouterr().out
 assert 'skipped ui-sensei/logo.png' in out and 'skipped awesome-design-skills/shot.jpg' in out
 staged=_all_files(tmp_path/'stage')
 assert os.path.join('ui-sensei','c.md') in staged and not any(name.endswith(('.png','.jpg')) for name in staged)
