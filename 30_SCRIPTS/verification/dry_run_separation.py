import os
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]

# Explicit separation proposal rules: (source_rel, dest_repo/strategy, reason, dependencies)
SEPARATION_RULES = [
    {
        'source': '.agents',
        'destination': 'repo: userist123/ai-agents-skills (or git submodule / package)',
        'reason': 'Contains ~15,575 skill files and prompts (~283 MB). Isolates agent skill development from core vault.',
        'dependencies': 'Skill router and agent manifests (.agents/agents) need path pointer to external skill directory.'
    },
    {
        'source': '07_EVALUATION',
        'destination': 'repo: userist123/ai-memory-evaluations (or external artifact store / Git LFS)',
        'reason': 'Contains ~166 MB of non-executable benchmark ledgers, token audit dumps, and ablation runs.',
        'dependencies': 'Evaluation harness scripts (40_EXPERIMENTS) require path to historical benchmark outputs.'
    },
    {
        'source': '08_OBSERVABILITY/audit/audit_log.jsonl',
        'destination': 'external: runtime disk logs (excluded via .gitignore / Git LFS)',
        'reason': '67.94 MB dynamic append-only operational audit log that should not be tracked directly in Git history.',
        'dependencies': 'memory_controller/audit/logger.py writes to configured audit_path.'
    },
    {
        'source': '02_PRODUCT/projects/workspaces/jarvis_web/voice_models/ro_RO-mihai-medium.onnx',
        'destination': 'Git LFS / external model cache (e.g. HuggingFace Hub / releases)',
        'reason': '60.27 MB binary ONNX neural TTS voice model; large binaries cause git repository bloat.',
        'dependencies': 'jarvis_web audio synthesis engine requires download on first run if missing.'
    },
    {
        'source': 'projects',
        'destination': 'repos: userist123/jarvis-suite, userist123/loganalyzer-dfir, userist123/registru-transferuri',
        'reason': 'Contains ~670 MB of independent application code, zip archives, and compiled .NET binaries in bin/Debug.',
        'dependencies': 'Independent project build systems and CI pipelines.'
    },
    {
        'source': 'xau_kinetic, XAU_Kinetic.Desktop, XAU_Kinetic_Standalone',
        'destination': 'repo: userist123/xau-kinetic',
        'reason': 'Triplicate copies of trading application and desktop binaries (~51 MB total). De-duplicate into dedicated repo.',
        'dependencies': 'Stand-alone desktop application.'
    },
    {
        'source': '06_INBOX/RAW_IMPORTS/markdawn/*.zip',
        'destination': 'excluded from clone (local temporary inbox by contract in AGENTS.md)',
        'reason': 'Contains 332 MB of duplicate repository zip archives from external manual imports.',
        'dependencies': 'None; raw imports are non-canonical evidence.'
    },
    {
        'source': 'AI_Memory_Vault_OBSIDIAN',
        'destination': 'excluded: local Obsidian vault profile (add to .gitignore)',
        'reason': '42.46 MB redundant vault mirror containing duplicated audit_log.jsonl and settings.',
        'dependencies': 'Obsidian UI workspace profile.'
    },
]

def format_size(num_bytes):
    for unit in ['B', 'KB', 'MB', 'GB']:
        if num_bytes < 1024.0:
            return f'{num_bytes:3.2f} {unit}'
        num_bytes /= 1024.0
    return f'{num_bytes:.2f} TB'

def get_stats(path_str):
    p = REPO_ROOT / path_str
    if '*' in path_str:
        # Glob pattern
        parent = REPO_ROOT / Path(path_str).parent
        pat = Path(path_str).name
        total_sz = 0
        total_cnt = 0
        if parent.exists():
            for f in parent.glob(pat):
                if f.is_file():
                    total_sz += f.stat().st_size
                    total_cnt += 1
        return total_sz, total_cnt

    if not p.exists():
        return 0, 0
    if p.is_file():
        return p.stat().st_size, 1
    
    total_sz = 0
    total_cnt = 0
    for root, dirs, files in os.walk(p):
        for f in files:
            fp = Path(root) / f
            try:
                total_sz += fp.stat().st_size
                total_cnt += 1
            except:
                pass
    return total_sz, total_cnt

def run_dry_run():
    print('================================================================================')
    print('                   P0.1 REPOSITORY SEPARATION DRY-RUN REPORT                    ')
    print('================================================================================')
    print('NOTE: DRY-RUN ONLY. Zero filesystem modifications or deletions executed.')
    print('--------------------------------------------------------------------------------\n')
    
    total_proposed_size = 0
    total_proposed_files = 0
    
    for rule in SEPARATION_RULES:
        sources = [s.strip() for s in rule['source'].split(',')]
        rule_sz = 0
        rule_cnt = 0
        for s in sources:
            sz, cnt = get_stats(s)
            rule_sz += sz
            rule_cnt += cnt
            
        total_proposed_size += rule_sz
        total_proposed_files += rule_cnt
        
        src = rule['source']
        dst = rule['destination']
        rsn = rule['reason']
        dep = rule['dependencies']
        print(f'SOURCE:       {src}')
        print(f'DESTINATION:  {dst}')
        print(f'SIZE:         {format_size(rule_sz)} ({rule_sz} bytes)')
        print(f'FILE COUNT:   {rule_cnt}')
        print(f'REASON:       {rsn}')
        print(f'DEPENDENCIES: {dep}')
        print('-' * 80)
        
    print('\n================================================================================')
    print('                               SUMMARY IMPACT                                   ')
    print('================================================================================')
    print(f'Total Size Proposed for Separation:       {format_size(total_proposed_size)}')
    print(f'Total Files Proposed for Separation:      {total_proposed_files}')
    print('Estimated Post-Separation Vault Size:     ~80 MB (< 2,500 files)')
    print('Estimated Post-Separation Clone Time:     < 3.0 seconds (vs 23.09s baseline)')
    print('================================================================================')

if __name__ == '__main__':
    run_dry_run()
