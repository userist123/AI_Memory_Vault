"""Applies the hand judgements (made while reading review_readable.txt) back
into review_worksheet.json, and computes the final precision figure.

Each judgement was made by reading both notes' actual content (not just the
evidence_entities list) and asking: does this specific pair of documents
show genuine shared meaning, or only coincidental/generic shared vocabulary?
The reasoning for each is in `judgement_reason` below and is the auditable
record requirement 2 asks for -- not only the rate.
"""
import json
from pathlib import Path

HERE = Path(__file__).resolve().parent

# review_id -> (judgement, reason)
JUDGEMENTS = {
    1: ("correct", "Two files with byte-identical body text (same trading bot README duplicated under two paths)."),
    2: ("wrong", "Both are Romanian classified-info regulations, but the matched entities (acces, angajament, aprob, cerere, clasificare) are generic administrative/legal vocabulary common to any such regulation -- same 'legal furniture' pattern r013 documented, not evidence specific to this pair."),
    3: ("correct", "Source is literally an audit report of the exact system the target document specifies (same codebase, same bot name in the audit's own title)."),
    4: ("correct", "Opening paragraphs are verbatim identical -- same project description duplicated under two paths/types."),
    5: ("correct", "Byte-identical deployment guide duplicated under two workspace paths for the same project."),
    6: ("wrong", "DORA and MiCA are both EU financial regulations; matched entities (ABE, BCE, DPO, EIOPA, ESMA) are generic EU institutional acronyms appearing in virtually every EU financial regulation, not evidence these two specifically relate."),
    7: ("wrong", "AI Act and MiCA; matched entities (dpo, furnizorii, imm, metsola, parlamentului, prezentul) are generic EU legislative-procedure vocabulary, same failure class as #6."),
    8: ("correct", "Both describe the same Elite Quant Bot MT5/Python trading system (a reconstruction prompt and one of its versioned application docs)."),
    9: ("correct", "Same as #8, against the V11 version doc."),
    10: ("correct", "Development standards doc for the Registru Transferuri project, linked part_of the project itself; tags overlap directly (registru-transferuri, wpf, dotnet10, air-gapped, infosec)."),
    11: ("correct", "Security threat-model doc and workspace spec for the same Registru Transferuri project; both cite the identical compliance standard list (HG 585/2002, NATO AC/35-D/1022 etc.)."),
    12: ("correct", "Reverse direction of #11, same project, same reasoning."),
    13: ("correct", "The two notes already cross-reference each other by target_id in their own frontmatter relations -- a declared, not just inferred, connection."),
    14: ("correct", "PIN-auth/SQLCipher security pattern doc, part_of the Registru project whose own stack line names SQLite WAL/SQLCipher explicitly."),
    15: ("correct", "M172/2021 storage-media legislation linked to the Registru workspace spec, which is specifically an air-gapped classified-media-transfer application built to comply with exactly this class of regulation."),
    16: ("correct", "UI remodel workflow procedure, part_of the Registru project; tags match directly (registru-transferuri, obsidian-tactical)."),
    17: ("correct", "Registru project linked to its own SECURITY.md; same project, direct relation."),
    18: ("correct", "Same as #17, different SECURITY.md path variant of the same project."),
    19: ("correct", "PIN-auth/DPAPI/JWT security pattern doc linked to a compliance-requirements doc that specifically enumerates DPAPI-class technical requirements (HG585/MS111) -- specific technical overlap, not generic legal language."),
    20: ("correct", "Project overview and its own workspace specification for the same system (Jarvis Cognitive Brain)."),
    21: ("correct", "Both declare relations to the same target_id in their own frontmatter -- part of the same P0/P1 retrieval-research thread."),
    22: ("correct", "The bootstrap doc's own text explicitly instructs reading the memory-protocol doc by name as step 2 of its procedure -- an explicit, stated dependency."),
    23: ("correct", "HG585/MS111 compliance-requirements doc, part_of the Registru project, which cites HG 585/2002 directly in its own compliance section."),
    24: ("correct", "HG585 legislation doc linked to the Registru workspace spec, which cites 'HG 585/2002' verbatim in its compliance section -- direct regulatory citation match."),
    25: ("correct", "An index of the M172/2021 order linked to the HG585/2002 legislation doc; both concern physical storage-media handling for classified information -- a specific, substantive regulatory-domain overlap, not generic legal boilerplate."),
    26: ("correct", "Trading bot and trading journal share specific trading-indicator vocabulary (ATR/EMA/MACD/RSI/SMA) and are plausibly the same author's related trading toolset."),
    27: ("correct", "Same reasoning as #26, an archive-path copy of the journal."),
    28: ("correct", "Same reasoning as #26, reverse pairing."),
    29: ("correct", "M172/2021 legal index linked to the Registru desktop app, which explicitly cites NATO/classified-info handling standards the order governs."),
    30: ("correct", "ADR about lifecycle transitions and the review queue both cite the identical invariant codes (I-001..I-012, P0-001..P0-015) as the operative governance contract for the same lifecycle-review process."),
    31: ("wrong", "Multi-agent construction protocol and a lifecycle-transition ADR share only the vault's standard invariant-code citations (I-001..I-012, P0-001..P0-015), which appear across many unrelated governance docs -- same 'furniture' pattern as #6/#7, not specific evidence these two documents relate."),
    32: ("wrong", "Construction protocol linked to a generic vault navigational index; shared entities (dfir, evtx, secops) do not appear central to either document's actual subject based on the visible content -- looks like coincidental token overlap."),
    33: ("correct", "Atomic legal note is specifically about hardware-serial storage-media tracking under M172 articles 193-199; the security doc it's linked to is the threat model for an application whose entire purpose is tracking physical storage media for classified transfers."),
    34: ("correct", "Same as #33, workspace-path variant of the same security doc."),
    35: ("correct", "Both are governance docs about the same subject: R001 is the canonical lifecycle-policy authority record, and the gaps doc explicitly discusses lifecycle-transition authorization closed against that same policy."),
    36: ("correct", "Skill matrix doc is explicitly tagged 'jarvis-command-center'; the target is literally the Jarvis Command Center specification."),
    37: ("correct", "The original request's own text specifies 'autonomous Cognitive Brain with integrated multi-agent execution' -- the enterprise integration doc is a downstream architecture blueprint for exactly that vision (multi-agent, memory-v6 tags)."),
    38: ("correct", "Compliance-requirements doc explicitly tagged 'hg585', directly citing the same source law as the target."),
    39: ("correct", "Both declare relations to the identical target_id in their own frontmatter -- part of the same declared architecture-document set."),
    40: ("correct", "Both describe the same multi-agent cognitive_core pipeline (critic-agent/verifier-agent/self-refine terms are specific, shared, and central to both documents' actual subjects)."),
    41: ("correct", "Atomic obligation note derived from HG585 articles 236-258, linked to the legal index of that exact same source act (source_act matches: [[HG_585_2002]] in both)."),
    42: ("correct", "The original request's text specifically names the OODA loop and continuous self-reflection; the construction protocol implements agent orchestration around exactly those named concepts, not merely generic technical terms."),
    43: ("correct", "HG585/MS111 compliance doc linked to the Registru app, which cites 'HG 585/2002' verbatim as one of its stated compliance standards."),
    44: ("correct", "Atomic DORA obligation note (articles 6-16) linked to the legal index of the identical source act ([[Regulament_UE_2022_2554_DORA]] in both) -- direct index-to-content relationship."),
    45: ("correct", "A lesson about EventLogReader exception handling in DFIR apps, linked to a Windows forensics tool that specifically ingests .evtx logs via EventLogReader -- a lesson directly applicable to the tool it's linked to."),
    46: ("correct", "Both declare relations to the same target_id in their own frontmatter, part of the same P0/P1 retrieval-research thread (same pattern as #13/#21)."),
    47: ("wrong", "DORA index linked to MiCA regulation via generic EU institutional acronyms (EIOPA, ESMA) -- same 'legal furniture' pattern as #6/#7/#31."),
    48: ("correct", "A web-engineering benchmark report and a UI design-philosophy doc share specific web-design vocabulary (CSS, CTA) and are both squarely about the same subject: visual/UI design quality."),
    49: ("correct", "Multi-agent pipeline architecture and cognitive-brain architecture spec share specific agent-role vocabulary (critic-agent, verifier-agent) central to both documents, and the brain spec declares relations to the same target_id pattern seen in #39/#40."),
    50: ("wrong", "An index cataloging 67 design-skill files will generically share 'skill'/'license'/'mit' with any one of the skills it catalogs -- the same structural problem as #52 (a broad catalog/index document matching everything it lists), not evidence specific to ui-sensei."),
    51: ("correct", "The brief-compiling procedure's own operational chain (compile_task_prompt.py) explicitly reads live state from VAULT_STATE.md, per VAULT_STATE.md's own section 8 -- a declared, direct operational dependency."),
    52: ("wrong", "A specific secops procedure linked to the generic master vault navigational index, which by design covers 'all verified knowledge domains' -- an index this broad will superficially match almost any single document; not evidence of a specific relation to this one procedure."),
    53: ("correct", "A SQLCipher/DPAPI encryption pattern doc linked to GDPR Article 32, which is specifically 'security of processing' -- a direct, substantive match between a security-of-processing regulation and a security-of-processing implementation pattern, not generic legal language."),
    54: ("wrong", "Two DIFFERENT projects (Registru Transferuri and LogAnalyzer DFIR) linked only via generic Windows security API names (DPAPI, SHA-256, WMI) that countless unrelated Windows security apps would share -- coincidental tech-stack overlap, not a substantive connection between these two specific projects."),
    55: ("wrong", "Same pair as #54, reverse direction; same reasoning."),
}


def main() -> int:
    path = HERE / "review_worksheet.json"
    data = json.loads(path.read_text(encoding="utf-8"))
    missing = [it["review_id"] for it in data["items"] if it["review_id"] not in JUDGEMENTS]
    if missing:
        raise SystemExit(f"missing judgements for review_id(s): {missing}")

    for item in data["items"]:
        judgement, reason = JUDGEMENTS[item["review_id"]]
        item["judgement"] = judgement
        item["judgement_reason"] = reason

    n = len(data["items"])
    correct = sum(1 for it in data["items"] if it["judgement"] == "correct")
    wrong = [it for it in data["items"] if it["judgement"] == "wrong"]
    precision = correct / n

    data["precision"] = {
        "n": n,
        "correct": correct,
        "wrong": n - correct,
        "precision": round(precision, 4),
        "bar": 0.70,
        "decision": "GO" if precision >= 0.70 else "NO-GO",
        "wrong_review_ids": [it["review_id"] for it in wrong],
    }

    path.write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n", encoding="utf-8", newline="\n")
    print(f"n={n} correct={correct} wrong={n-correct} precision={precision:.3f} "
          f"decision={data['precision']['decision']}")
    print("wrong review_ids:", data["precision"]["wrong_review_ids"])
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
