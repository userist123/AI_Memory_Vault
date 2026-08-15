import json
import importlib.metadata
import sys
import platform

with open('.agents/survey_test_explorer_1/test_structure_info.json', 'r', encoding='utf-8') as f:
    files_info = json.load(f)

with open('.agents/survey_test_explorer_1/parsed_tests.json', 'r', encoding='utf-8') as f:
    parsed_tests = json.load(f)

all_fixtures = set()
all_imports = set()
for fi in files_info:
    all_fixtures.update(fi['fixtures'])
    all_imports.update(fi['imports'])

print("=== Fixtures Defined in Tests ===")
for fi in files_info:
    if fi['fixtures']:
        print(f"  {fi['file']}: {fi['fixtures']}")

print("\n=== External & Internal Module Imports in Tests ===")
for imp in sorted(all_imports):
    print(f"  {imp}")

print("\n=== Python Runtime & Environment ===")
print(f"  Python executable: {sys.executable}")
print(f"  Python version: {sys.version}")
print(f"  Platform: {platform.platform()}")
