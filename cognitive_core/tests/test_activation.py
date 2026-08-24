import time
import pytest
from cognitive_core.activation import (
    base_level_activation,
    ActivationRecord,
    ActivationTracker,
    DORMANT_THRESHOLD
)

def test_base_level_activation_decay_monotonicity():
    now = time.time()
    # High frequency access recently
    recent_accesses = [now - 1.0, now - 0.5, now - 0.1]
    act_recent = base_level_activation(recent_accesses, decay=0.5, current_time=now)

    # Access far in the past
    old_accesses = [now - 100.0, now - 200.0, now - 300.0]
    act_old = base_level_activation(old_accesses, decay=0.5, current_time=now)

    assert act_recent > act_old, "Recent access must yield higher activation score than old access"

def test_activation_record_dormancy():
    record = ActivationRecord("test_note_1")
    now = time.time()
    
    # Record access long ago
    record.record_access(now - 10000.0)
    assert record.is_dormant(threshold=DORMANT_THRESHOLD, current_time=now) is True

    # Record recent access
    record.record_access(now)
    assert record.is_dormant(threshold=DORMANT_THRESHOLD, current_time=now) is False

def test_activation_tracker_singleton():
    tracker1 = ActivationTracker.get_instance()
    tracker2 = ActivationTracker.get_instance()
    assert tracker1 is tracker2

    tracker1.record_access("note_abc", time.time())
    assert tracker2.get_activation("note_abc") > DORMANT_THRESHOLD
