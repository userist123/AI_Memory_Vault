import re
from pathlib import Path

target_files = [
    Path("memory_controller/financial_schema.py"),
    Path("tests/financial/test_schema.py"),
]

secret_patterns = [
    re.compile(r'(?i)(?:api_key|apikey|password|bearer|auth_token)\s*=\s*[\'"][A-Za-z0-9_\-]{8,}[\'"]'),
    re.compile(r'(?i)-----BEGIN\s+[A-Z\s]+PRIVATE\s+KEY-----'),
    re.compile(r'(?i)ghp_[0-9a-zA-Z]{36}'),
    re.compile(r'(?i)sk-[0-9a-zA-Z]{48}')
]

found = []
for p in target_files:
    content = p.read_text(encoding="utf-8")
    for pat in secret_patterns:
        for m in pat.finditer(content):
            found.append((p.as_posix(), m.group(0)))

print(f"Secret findings: {len(found)}")
for item in found:
    print(" ", item)

assert len(found) == 0, "Hardcoded secrets detected!"
print("ZERO SECRETS CONFIRMED!")
