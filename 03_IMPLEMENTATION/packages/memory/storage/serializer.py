import yaml
import re
from enum import Enum

# Configure PyYAML to serialize Enums using their .value
def enum_representer(dumper, data):
    return dumper.represent_scalar('tag:yaml.org,2002:str', str(data.value))

yaml.add_multi_representer(Enum, enum_representer, Dumper=yaml.SafeDumper)

def serialize(note: dict) -> str:
    """Serializes a dictionary into YAML Frontmatter + Markdown Body."""
    note_copy = note.copy()
    content = note_copy.pop("content", "")
    
    # We must ensure we use safe dumping
    frontmatter = yaml.safe_dump(note_copy, default_flow_style=False, sort_keys=False, allow_unicode=True)
    
    # Construct the final Markdown file string
    result = f"---\n{frontmatter}---\n{content}"
    return result

def deserialize(file_content: str) -> dict:
    """Deserializes YAML Frontmatter + Markdown Body into a dictionary."""
    # Robust regex to extract YAML frontmatter exactly between the first two ---
    # Handles LF, CRLF, and empty body
    match = re.match(r'^---\r?\n(.*?)\r?\n---\r?\n?(.*)', file_content, re.DOTALL)
    
    if not match:
        raise ValueError("Malformed YAML: Missing opening or closing --- delimiters at the start of the file")
        
    yaml_text = match.group(1)
    body = match.group(2)
    
    try:
        data = yaml.safe_load(yaml_text)
    except yaml.YAMLError as e:
        raise ValueError(f"Malformed YAML: {e}")
        
    if not isinstance(data, dict):
        raise ValueError("Malformed YAML: Frontmatter must be a dictionary")
        
    data["content"] = body
    return data
