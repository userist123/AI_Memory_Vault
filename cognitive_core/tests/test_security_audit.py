from cognitive_core.security_audit import SecurityAuditor


def test_detects_hardcoded_secret_and_debug_flag(tmp_path):
    target = tmp_path / "app.py"
    target.write_text(
        "API_KEY = 'sk-supersecretvalue123'\nDEBUG = True\nsubprocess.run(cmd, shell=True)\n",
        encoding="utf-8",
    )
    report = SecurityAuditor(tmp_path).run()
    rules_found = {f.rule for f in report.findings}
    assert "hardcoded_secret" in rules_found
    assert "debug_enabled" in rules_found
    assert "shell_injection_risk" in rules_found


def test_ignores_non_text_extensions_and_skip_dirs(tmp_path):
    (tmp_path / "__pycache__").mkdir()
    (tmp_path / "__pycache__" / "cache.pyc").write_bytes(b"\x00\x01")
    (tmp_path / "image.png").write_bytes(b"\x89PNG")
    report = SecurityAuditor(tmp_path).run()
    assert report.files_scanned == 0
    assert report.findings == []


def test_to_candidates_produces_raw_lesson_dicts(tmp_path):
    target = tmp_path / "app.py"
    target.write_text("eval(user_input)\n", encoding="utf-8")
    report = SecurityAuditor(tmp_path).run()
    candidates = report.to_candidates()
    assert candidates and candidates[0]["type"] == "lesson"
    assert candidates[0]["category"] == "security_audit"
