import xml.etree.ElementTree as ET
import json
from collections import defaultdict
import os

tree = ET.parse(os.path.join(os.path.dirname(__file__), 'junit.xml'))
root = tree.getroot()

tests = []
for ts in root.iter('testcase'):
    classname = ts.attrib.get('classname', '')
    name = ts.attrib.get('name', '')
    time = float(ts.attrib.get('time', 0.0))
    file = ts.attrib.get('file', '')
    line = ts.attrib.get('line', '')
    
    status = 'passed'
    failure_msg = None
    failure_text = None
    
    fail_el = ts.find('failure')
    if fail_el is not None:
        status = 'failed'
        failure_msg = fail_el.attrib.get('message', '')
        failure_text = fail_el.text
        
    error_el = ts.find('error')
    if error_el is not None:
        status = 'error'
        failure_msg = error_el.attrib.get('message', '')
        failure_text = error_el.text
        
    skip_el = ts.find('skipped')
    if skip_el is not None:
        status = 'skipped'
        failure_msg = skip_el.attrib.get('message', '')
        failure_text = skip_el.text

    tests.append({
        'name': name,
        'classname': classname,
        'file': file or classname.replace('.', '/') + '.py',
        'line': line,
        'time': time,
        'status': status,
        'failure_msg': failure_msg,
        'failure_text': failure_text
    })

print(f"Total tests parsed: {len(tests)}")

by_status = defaultdict(int)
by_file = defaultdict(lambda: defaultdict(int))
by_subsystem = defaultdict(lambda: defaultdict(int))

def get_subsystem(file_path):
    f = file_path.replace('\\', '/').lower()
    if 'test_security' in f or 'test_authorization' in f or 'p0' in f or 'security' in f or 'boundary' in f:
        return 'Security & Invariants (P0-P15)'
    elif 'test_sqlite' in f or 'test_storage' in f or 'test_audit' in f or 'test_raw_imports' in f or 'test_lifecycle' in f or 'test_pagination' in f or 'test_cache' in f or 'test_context_economy' in f or 'test_git_isolation' in f or 'test_supersession' in f or 'test_core' in f:
        if 'memory_controller' in f:
            return 'Storage / WAL / Memory Controller Core'
    if 'cognitive_core' in f:
        if 'reasoning' in f or 'tot' in f or 'reflection' in f or 'cognitive_loop' in f or 'planning' in f or 'executive' in f:
            return 'Cognitive Loop / OODA / ToT / Executive'
        elif 'multiagent' in f or 'specialized_agents' in f or 'tool_router' in f:
            return 'Multi-Agent Worker Coordination'
        elif 'continual_learning' in f or 'evaluation_and_recall_lineage' in f or 'consolidation' in f or 'learning' in f or 'deduplication' in f:
            return 'Metrics / TRACe / IR / Continual Learning / Consolidation'
        elif 'working_memory' in f or 'activation' in f or 'continuity' in f or 'dynamic_synapses' in f or 'recall' in f or 'reconciliation' in f or 'version' in f or 'end_to_end' in f:
            return 'Cognitive Working Memory / Recall & Activation'
    return 'Other'

for t in tests:
    by_status[t['status']] += 1
    by_file[t['file']][t['status']] += 1
    sub = get_subsystem(t['file'])
    by_subsystem[sub][t['status']] += 1

print("\n--- Summary by Status ---")
for s, count in by_status.items():
    print(f"  {s}: {count}")

print("\n--- Summary by Subsystem ---")
for sub, statuses in sorted(by_subsystem.items()):
    total = sum(statuses.values())
    print(f"  {sub}: Total {total} -> {dict(statuses)}")

print("\n--- Summary by Test File ---")
for f, statuses in sorted(by_file.items()):
    total = sum(statuses.values())
    print(f"  {f}: Total {total} ({dict(statuses)})")

with open(os.path.join(os.path.dirname(__file__), 'parsed_tests.json'), 'w') as out:
    json.dump({
        'total': len(tests),
        'by_status': by_status,
        'by_subsystem': {k: dict(v) for k, v in by_subsystem.items()},
        'by_file': {k: dict(v) for k, v in by_file.items()},
        'tests': tests
    }, out, indent=2)

print("\nSaved detailed parsed test info to parsed_tests.json")
