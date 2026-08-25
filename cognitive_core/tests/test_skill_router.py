from cognitive_core.skill_router import SkillRouter


def test_route_matches_relevant_skill_by_token_overlap(tmp_path):
    (tmp_path / "python-trading-systems").mkdir()
    (tmp_path / "csharp-wpf-desktop").mkdir()
    (tmp_path / "owasp-top-10-audit").mkdir()
    router = SkillRouter(tmp_path)
    matches = router.route("audit my python trading system for owasp issues", top_k=3)
    matched_skills = {m.skill for m in matches}
    assert "python-trading-systems" in matched_skills
    assert "owasp-top-10-audit" in matched_skills
    assert "csharp-wpf-desktop" not in matched_skills


def test_route_returns_empty_for_empty_task(tmp_path):
    (tmp_path / "some-skill").mkdir()
    router = SkillRouter(tmp_path)
    assert router.route("") == []


def test_list_skills_handles_missing_directory(tmp_path):
    router = SkillRouter(tmp_path / "does_not_exist")
    assert router.list_skills() == []
