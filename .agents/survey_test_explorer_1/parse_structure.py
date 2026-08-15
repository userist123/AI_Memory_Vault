import os
import ast
import json

test_files_info = []

for root_dir in ['cognitive_core/tests', 'memory_controller/tests']:
    for root, dirs, files in os.walk(root_dir):
        for f in sorted(files):
            if f.endswith('.py'):
                path = os.path.join(root, f).replace('\\', '/')
                with open(path, 'r', encoding='utf-8') as pyf:
                    code = pyf.read()
                try:
                    tree = ast.parse(code)
                except Exception as e:
                    print(f"Error parsing {path}: {e}")
                    continue
                
                imports = []
                fixtures = []
                test_funcs = []
                classes = []
                
                for node in ast.walk(tree):
                    if isinstance(node, ast.Import):
                        for n in node.names:
                            imports.append(n.name)
                    elif isinstance(node, ast.ImportFrom):
                        imports.append(f"{node.module}")
                    elif isinstance(node, ast.FunctionDef):
                        # check decorators for pytest.fixture
                        is_fixture = any(
                            (isinstance(d, ast.Name) and d.id == 'fixture') or
                            (isinstance(d, ast.Attribute) and d.attr == 'fixture')
                            for d in node.decorator_list
                        )
                        if is_fixture:
                            fixtures.append(node.name)
                        elif node.name.startswith('test_'):
                            test_funcs.append({
                                'name': node.name,
                                'line': node.lineno,
                                'args': [a.arg for a in node.args.args],
                                'docstring': ast.get_docstring(node)
                            })
                    elif isinstance(node, ast.ClassDef):
                        if node.name.startswith('Test'):
                            classes.append(node.name)

                test_files_info.append({
                    'file': path,
                    'is_conftest': f == 'conftest.py',
                    'num_lines': len(code.splitlines()),
                    'imports': sorted(list(set([i for i in imports if i]))),
                    'fixtures': fixtures,
                    'classes': classes,
                    'test_count': len(test_funcs),
                    'test_functions': test_funcs
                })

with open('.agents/survey_test_explorer_1/test_structure_info.json', 'w', encoding='utf-8') as out:
    json.dump(test_files_info, out, indent=2)

print(f"Parsed structure of {len(test_files_info)} python files.")
