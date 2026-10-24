# todo-claude-windows-suite

- **Task:** make the full `20_TESTS` suite pass on Windows; on 2026-10-22 it had 14 failures and 2 errors that also failed on a clean `origin/main` checkout.
- **Branch:** `claude/windows-test-fixes`
- **Causes and fixes:**
  - `core.autocrlf=true` checked text files out as CRLF, so verbatim reads and byte hashes disagreed with Linux CI (b2m leakage report, B06/B03 packets). `.gitattributes`: `* text=auto eol=lf`, `*.bat`/`*.cmd` CRLF; the existing `-text` rules stay last. `git add --renormalize .` changes nothing: the index was already LF (18,276 files; the 2 CRLF files are `-text` on purpose).
  - `write_text()` without `newline=` writes CRLF on Windows: `20_TESTS/vault_access_fixture.py` (vault_read verbatim/sha256, 8 failures) and the B03 packet writer `lifecycle/validation/book_to_memory_real_ablation.py` (its `--check` compares bytes). Both now write `newline="\n"`.
  - `test_unreadable_or_unusual_frontmatter_fails_closed`: the generated test id held Long.md's whole text, and `PYTEST_CURRENT_TEST` exceeded Windows' 32,767-char env-var limit (2 errors). Ids are now the file names.
  - `test_an_executable_bit_aborts_the_import...`: NTFS has no POSIX executable bit, so the case cannot be built. Now skipped on `nt` with the reason; the importer only runs on ubuntu-latest, where the test still runs.
- **Existing Windows clones:** git does not rewrite unchanged files when `.gitattributes` changes. Run once: `git rm --cached -r -q . && git reset --hard` (or a fresh clone).
- **Key files:** `.gitattributes`, `20_TESTS/vault_access_fixture.py`, `20_TESTS/test_vault_access_core.py`, `20_TESTS/test_import_external_skills.py`, `03_IMPLEMENTATION/packages/lifecycle/validation/book_to_memory_real_ablation.py`.
