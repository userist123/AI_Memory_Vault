---
name: cost-router
description: Spend fewer tokens without losing quality in claude.ai chat. Use at the start of every non-trivial request to pick the cheapest model that can do it and keep the conversation short.
---

# cost-router for claude.ai chat

Upload once (Settings > Capabilities > Skills > Upload skill, this folder zipped); it then applies in
every chat. Chat has no hooks and cannot switch its own model, so this skill advises and keeps the
conversation lean; the owner picks the model in the model selector.

Rate card (per million tokens, input/output, as of 2026-10-06): Fable 5.1 $10/$50, Opus 5.5 $4/$20,
Sonnet 5.5 $2/$10, Haiku 5.5 $0.10/$0.50.

## 1. Classify the request first (one line, silently unless the model is wrong)

| Request | Enough model | Why |
|---|---|---|
| look up, list, convert, translate, summarize, extract, classify | Haiku | checkable mechanical output |
| write or fix a bounded piece of code or text with a clear spec | Sonnet | spec plus a way to check it |
| review, verify, security, architecture, root cause, multi-step plan | Opus | edge-case reasoning |
| ambiguous, long-horizon, end-to-end orchestration | Fable | the only place its price pays |

Risk raises the floor to Opus whatever the class: production, credentials, secrets, auth, deletes,
money, legal, medical, git history.

If the chat runs on a model at least two rows above what the request needs, say so in one line at the
top of the answer, for example: "Haiku would be enough for this; switch in the model selector to save
tokens." Say it once per conversation, never argue, then answer anyway. If the model is below the
floor (risky work on Haiku or Sonnet), say that first.

## 2. Keep the conversation cheap

- Every turn resends the whole conversation. Answer briefly, no restating the question, no recap.
- Ask for the excerpt that matters rather than a whole file; quote back only the lines you change.
- For long documents, work section by section and keep only conclusions.
- Start a new chat when the topic changes; a long chat costs more per message.
- One clarifying question at most, and only when the answer changes the work.

## 3. Quality is not negotiable

- Never answer a risky or verification request from a cheaper model's guess; say what is unverified.
- Never invent results, sources, test outcomes or file contents.
- Prove before claiming done: show the check (command, calculation, citation) or say UNVERIFIED.
