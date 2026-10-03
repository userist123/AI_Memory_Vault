from pathlib import Path
import importlib.util
ROOT=Path(__file__).resolve().parents[1]
s=importlib.util.spec_from_file_location('wsa',ROOT/'30_SCRIPTS/verification/workflow_security_audit.py'); m=importlib.util.module_from_spec(s); s.loader.exec_module(m)
def check(tmp,text):
 p=tmp/'x.yml'; p.write_text(text); return m.audit_file(p)
def test_old_tag_fails(tmp_path):
 f=check(tmp_path,'name: x\non:\n  pull_request:\njobs:\n  x:\n    runs-on: ubuntu-latest\n    timeout-minutes: 5\n    steps:\n      - uses: actions/checkout@v4\n')
 assert any('unpinned action' in x for x in f)
def test_missing_permissions_fails(tmp_path):
 f=check(tmp_path,'name: x\non:\n  workflow_dispatch:\njobs:\n  x:\n    runs-on: ubuntu-latest\n    timeout-minutes: 5\n    steps: []\n')
 assert any('permissions' in x for x in f)
def test_target_pr_head_fails(tmp_path):
 f=check(tmp_path,'name: x\non:\n  pull_request_target:\njobs:\n  x:\n    runs-on: ubuntu-latest\n    timeout-minutes: 5\n    steps:\n      - uses: actions/checkout@1234567890123456789012345678901234567890\n        with:\n          ref: ${{ github.event.pull_request.head.sha }}\n')
 assert any('pull_request_target' in x for x in f)
def test_secret_on_pr_fails(tmp_path):
 f=check(tmp_path,'name: x\non:\n  pull_request:\njobs:\n  x:\n    runs-on: ubuntu-latest\n    timeout-minutes: 5\n    steps:\n      - run: echo ${{ secrets.MY_SECRET }}\n')
 assert any('secret transmitted' in x for x in f)
def test_push_without_approval_fails(tmp_path):
 f=check(tmp_path,'name: x\non:\n  workflow_dispatch:\njobs:\n  x:\n    runs-on: ubuntu-latest\n    timeout-minutes: 5\n    steps:\n      - run: git push origin main\n')
 assert any('git push' in x for x in f)
def test_missing_timeout_fails(tmp_path):
 f=check(tmp_path,'name: x\non:\n  workflow_dispatch:\npermissions:\n  contents: read\njobs:\n  x:\n    runs-on: ubuntu-latest\n    steps: []\n')
 assert any('timeout' in x for x in f)
