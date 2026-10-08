from pathlib import Path
import importlib.util
ROOT=Path(__file__).parents[2]
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

def test_apisec_and_fortify_scan_outcome_checked_before_reporting_passed():
    apisec_path = ROOT / '.github/workflows/apisec-scan.yml'
    fortify_path = ROOT / '.github/workflows/fortify.yml'
    assert apisec_path.exists() and fortify_path.exists()
    
    apisec_text = apisec_path.read_text(encoding='utf-8')
    assert 'id: apisec_scan' in apisec_text
    assert 'steps.apisec_scan.outcome' in apisec_text
    assert 'EXECUTED_FAILED' in apisec_text
    # Ensure it doesn't just unconditionally echo EXECUTED_PASSED when configured == true
    assert 'if [[ "$scan_outcome" == "success"' in apisec_text
    
    fortify_text = fortify_path.read_text(encoding='utf-8')
    assert 'id: fortify_scan' in fortify_text
    assert 'steps.fortify_scan.outcome' in fortify_text
    assert 'EXECUTED_FAILED' in fortify_text
    assert 'if [[ "$scan_outcome" == "success"' in fortify_text

