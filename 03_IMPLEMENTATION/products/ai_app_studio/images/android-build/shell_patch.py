"""Edge-to-edge shell patch run by aas-build-android on the COPY of the web build (never on /dist).
usage: shell_patch.py <index.html> <aas-safe-area.css>
Makes sure the viewport meta has viewport-fit=cover and injects the safe-area CSS once, before </head>."""
import re
import sys

index, css_path = sys.argv[1], sys.argv[2]
html = open(index, encoding="utf-8").read()
css = re.sub(r"/\*.*?\*/", "", open(css_path, encoding="utf-8").read(), flags=re.S)
css = " ".join(css.split())
if "viewport-fit=cover" not in html:
    m = re.search(r'<meta[^>]*name=["\']viewport["\'][^>]*>', html, re.I)
    if m:
        tag = m.group(0)
        new = re.sub(r'content=(["\'])(.*?)\1', lambda c: f"content={c.group(1)}{c.group(2)}, viewport-fit=cover{c.group(1)}", tag, count=1)
        html = html.replace(tag, new, 1)
    else:
        html = html.replace("</head>", '<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover"></head>', 1)
if 'id="aas-safe-area"' not in html and "</head>" in html:
    html = html.replace("</head>", f'<style id="aas-safe-area">{css}</style></head>', 1)
open(index, "w", encoding="utf-8").write(html)
print("shell patched:", "viewport-fit=cover" in html, 'id="aas-safe-area"' in html)
