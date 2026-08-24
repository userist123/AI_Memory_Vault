# External Skill Sources

| Source | Type | Import treatment |
|---|---|---|
| github/awesome-copilot | Skill/agent library | Preserve individual skills; do not import unrelated repo infrastructure |
| addyosmani/web-quality-skills | Web quality skills | Preserve SKILL.md and supporting references |
| gbrasil720/ui-sensei | UI/design skill | Preserve skill plus references/scripts where needed |
| ConardLi/garden-skills | Skill collection | Import web-design-engineer skill only from requested path |
| xiaopu-ai/web-design | Web design skill | Import SKILL.md and referenced materials |
| bergside/awesome-design-skills | Design skill collection | Preserve individual skills and source attribution |

## Discovery-only URLs

The GitHub topic/collection URLs supplied by the user are discovery indexes. They are not copied into the skill corpus as if they were skills.

## Canonical-memory boundary

These files are external knowledge. They are not canonical memory and must not introduce a second memory store, RAG system, orchestrator, authorization layer, or ToolRouter. Integration must extend the existing vault abstractions.
