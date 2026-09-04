from memory_controller.context.query_classifier import Intent, QueryClassifier


def test_unverified_does_not_imply_verified_lifecycle():
    result = QueryClassifier().classify("retrieve unverified memories")
    assert result["lifecycle_filters"] == []
    assert result["intent"] is Intent.READ


def test_verified_is_still_detected_as_whole_word():
    result = QueryClassifier().classify("search verified procedures")
    assert result["lifecycle_filters"] == ["VERIFIED"]
    assert result["target_types"] == ["procedure"]
    assert result["intent"] is Intent.SEARCH


def test_unverified_review_does_not_create_contradictory_verified_filter():
    result = QueryClassifier().classify("find unverified review items")
    assert result["lifecycle_filters"] == ["REVIEW"]
    assert "VERIFIED" not in result["lifecycle_filters"]
    assert result["intent"] is Intent.REVIEW


def test_other_lifecycle_terms_remain_exact_word_matches():
    classifier = QueryClassifier()
    assert classifier.classify("active database architecture")["lifecycle_filters"] == ["ACTIVE"]
    assert classifier.classify("superseded storage models")["lifecycle_filters"] == ["SUPERSEDED"]
    assert classifier.classify("archived system configurations")["lifecycle_filters"] == ["ARCHIVED"]


def test_intent_and_target_keywords_do_not_match_inside_larger_words():
    classifier = QueryClassifier()
    result = classifier.classify("projector lessons are not a project lesson")
    assert result["intent"] is Intent.READ
    assert result["target_types"] == ["project", "lesson"]
