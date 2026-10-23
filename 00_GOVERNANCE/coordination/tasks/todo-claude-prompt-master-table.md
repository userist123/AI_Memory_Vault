# todo-claude-prompt-master-table

- **Task:** add the Prompt Master Table (20 mega prompts + routing + orchestration protocol) to `00_GOVERNANCE/coordination/`, and close the "Prompting: partial" gap — no PASS/FAIL schema for prompts and no golden prompt set run after changes.
- **Branch:** `claude/prompt-master-table`
- **Done:**
  - `PROMPT_MASTER_TABLE.md` (protocol, routing, master table, 20 full prompts, PASS/FAIL rubric, request log); companion section in `PROMPT_MASTER_PORTABLE.md`.
  - `prompt_master_routing.json`: executable routing, kept identical to the table (check D5).
  - `07_EVALUATION/prompting/`: `golden_prompts.json` (20 cases, every rule covered), `prompt_eval_schema.json`, 6 reference outputs (3 PASS, 3 FAIL).
  - `30_SCRIPTS/prompt/prompt_master_eval.py` (`check` / `route` / `score`, exit 0/1/2); `20_TESTS/test_prompt_master_eval.py` (37 tests); workflow `prompt-master-eval.yml`.
- **Owner decision:** publishing the prompt collection in this public repository was explicitly approved by the owner (2026-10-22).
- **Next:** owner review of the draft PR; score real model answers with `score --case <id> --output <file>` and add them as reference outputs; append rows to "Registru cerințe".
- **Blockers:** none. Live model runs are manual by design (the gate makes no provider call).
- **Key files:** the paths above.
