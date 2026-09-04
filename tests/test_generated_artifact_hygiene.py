from scripts.check_generated_artifact_hygiene import find_malformed


def test_repository_has_no_malformed_generated_citation_markers():
    assert find_malformed() == []
