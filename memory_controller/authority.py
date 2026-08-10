'''Authority scoring utilities.

Provides a deterministic mapping from provenance.source_type to a numeric authority score.
The score is derived at runtime and is not persisted in note frontmatter.
'''

# Mapping of source_type to authority score (higher = more authoritative)
_SOURCE_AUTHORITY_MAP = {
    "user": 0.5,
    "official": 0.9,
    "execution": 0.7,
    "experience": 0.6,
    "ai": 0.4,
    "inference": 0.3,
    "import": 0.8,
    "unknown": 0.2,
}

def get_authority_score(note: dict) -> float:
    """Return the authority score for a note based on its provenance.

    The function looks at ``note['provenance']['source_type']`` and returns a
    deterministic float in the range [0, 1]. If the field is missing or unknown,
    ``unknown`` mapping is used.
    """
    provenance = note.get('provenance', {})
    source_type = provenance.get('source_type', 'unknown')
    return _SOURCE_AUTHORITY_MAP.get(source_type, _SOURCE_AUTHORITY_MAP['unknown'])
