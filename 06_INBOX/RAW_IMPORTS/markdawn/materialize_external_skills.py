#!/usr/bin/env python3
from __future__ import annotations

import argparse, base64, hashlib, json, os, re, time
from pathlib import Path
from urllib.error import HTTPError, URLError
from urllib.parse import quote, urlparse
from urllib.request import Request, urlopen

API_ROOT = "https://api.github.com"
EXTERNAL_ROOT = Path("01_KNOWLEDGE/EXTERNAL_SKILLS")
KNOWN_COLLECTIONS = [
    "VoltAgent/awesome-agent-skills",
    "ComposioHQ/awesome-claude-skills",
    "sickn33/agentic-awesome-skills",
    "bergside/awesome-design-skills",
]
GITHUB_RE = re.compile(r'https?://github\.com/[^\s<>"\]\)]+')
SKILL_DIRS = {"references", "reference", "assets", "templates", "examples", "scripts"}

def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()

class GitHub:
    def __init__(self, token=None):
        self.token = token
        self.last = 0.0

    def get(self, url):
        delay = 0.08 - (time.time() - self.last)
        if delay > 0:
            time.sleep(delay)
        headers = {
            "Accept": "application/vnd.github+json",
            "User-Agent": "AI-Memory-Vault-External-Skill-Materializer/1.0",
            "X-GitHub-Api-Version": "2022-11-28",
        }
        if self.token:
            headers["Authorization"] = f"Bearer {self.token}"
        req = Request(url, headers=headers)
        try:
            with urlopen(req, timeout=30) as r:
                self.last = time.time()
                return json.loads(r.read().decode("utf-8"))
        except (HTTPError, URLError) as e:
            raise RuntimeError(str(e)) from e

    def repo(self, full_name):
        return self.get(f"{API_ROOT}/repos/{full_name}")

    def contents(self, full_name, path="", ref=None):
        url = f"{API_ROOT}/repos/{full_name}/contents/{quote(path, safe='/')}"
        if ref:
            url += f"?ref={quote(ref, safe='')}"
        return self.get(url)

def github_parts(url):
    p = urlparse(url)
    parts = [x for x in p.path.split("/") if x]
    if len(parts) < 2:
        raise ValueError("not a github repo URL")
    owner, repo = parts[0], parts[1].removesuffix(".git")
    rest = parts[2:]
    ref = None
    repo_path = ""
    if rest and rest[0] in {"blob", "tree", "raw"} and len(rest) >= 2:
        ref = rest[1]
        repo_path = "/".join(rest[2:])
    else:
        repo_path = "/".join(rest)
    return owner, repo, ref, repo_path

def read_bytes(api, item):
    if item.get("encoding") == "base64" and item.get("content"):
        return base64.b64decode(item["content"], validate=False)
    if item.get("download_url"):
        req = Request(item["download_url"], headers={"User-Agent": "AI-Memory-Vault-External-Skill-Materializer/1.0"})
        with urlopen(req, timeout=30) as r:
            return r.read()
    raise RuntimeError(f"no content source for {item.get('path')}")

def find_skill_files(api, full_name, start_path="", ref=None, max_nodes=4000):
    q = [start_path]
    seen = set()
    found = []
    while q and len(seen) < max_nodes:
        path = q.pop(0)
        if path in seen:
            continue
        seen.add(path)
        data = api.contents(full_name, path, ref)
        if isinstance(data, dict):
            if data.get("type") == "file" and data.get("name", "").lower() == "skill.md":
                found.append(data)
            continue
        if not isinstance(data, list):
            continue
        for item in data:
            if item.get("type") == "file" and item.get("name", "").lower() == "skill.md":
                found.append(item)
            elif item.get("type") == "dir":
                q.append(item["path"])
    return found

def skill_name(data, fallback):
    text = data.decode("utf-8", errors="replace")
    for line in text.splitlines()[:100]:
        m = re.match(r"^\s*name\s*:\s*['\"]?([^'\"#]+)", line, re.I)
        if m:
            return m.group(1).strip()
        if line.startswith("# "):
            return line[2:].strip()
    return fallback

def license_name(api, full_name):
    try:
        lic = api.repo(full_name).get("license") or {}
        return str(lic.get("spdx_id") or lic.get("name") or "UNKNOWN")
    except Exception:
        return "UNKNOWN"

def default_branch_sha(api, full_name, ref=None):
    try:
        repo = api.repo(full_name)
        branch = ref or repo.get("default_branch")
        return api.get(f"{API_ROOT}/repos/{full_name}/commits/{quote(branch, safe='')}").get("sha")
    except Exception:
        return None

def extract_links(root):
    refs = []
    seen = set()
    for p in root.rglob("*"):
        if not p.is_file() or p.name.startswith("_EXTRACTION_"):
            continue
        if p.suffix.lower() not in {".md", ".txt", ".json", ".yaml", ".yml", ".url"}:
            continue
        try:
            text = p.read_text(encoding="utf-8", errors="replace")
        except OSError:
            continue
        for url in GITHUB_RE.findall(text):
            url = url.rstrip(".,;")
            key = (str(p), url)
            if key in seen:
                continue
            seen.add(key)
            try:
                owner, repo, ref, repo_path = github_parts(url)
                refs.append((p, url, owner, repo, ref, repo_path))
            except ValueError:
                pass
    return refs

def add_known(refs):
    existing = {(r[2].lower(), r[3].lower()) for r in refs}
    for full in KNOWN_COLLECTIONS:
        owner, repo = full.split("/", 1)
        if (owner.lower(), repo.lower()) not in existing:
            refs.append((None, f"https://github.com/{full}", owner, repo, None, ""))
    return refs

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--repo-root", type=Path, default=Path("."))
    ap.add_argument("--token", default=os.environ.get("GITHUB_TOKEN"))
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--no-known-collections", action="store_true")
    args = ap.parse_args()

    root = args.repo_root.resolve()
    external = root / EXTERNAL_ROOT
    if not external.is_dir():
        raise SystemExit(f"ERROR: {external} does not exist")

    api = GitHub(args.token)
    refs = extract_links(external)
    if not args.no_known_collections:
        refs = add_known(refs)

    results, failures = [], []
    existing_by_sha = {}
    for p in external.rglob("SKILL.md"):
        try:
            existing_by_sha[sha256(p.read_bytes())] = str(p.relative_to(root))
        except OSError:
            pass

    for src_file, original_url, owner, repo, ref, repo_path in refs:
        full_name = f"{owner}/{repo}"
        try:
            items = find_skill_files(api, full_name, repo_path, ref)
            if not items:
                failures.append({"url": original_url, "status": "NO_SKILL_MD_FOUND"})
                continue
            lic = license_name(api, full_name)
            commit = default_branch_sha(api, full_name, ref)
            category = (src_file.parent.name if src_file else "_RAW")
            category = re.sub(r"[^A-Za-z0-9._-]+", "-", category) or "_RAW"

            for item in items:
                data = read_bytes(api, item)
                digest = sha256(data)
                source_path = item["path"]
                fallback = Path(source_path).parent.name or repo
                name = re.sub(r"[^A-Za-z0-9._-]+", "-", skill_name(data, fallback)).strip("-") or "unnamed-skill"
                dest_dir = external / category / name

                if dest_dir.exists():
                    prior = dest_dir / "SKILL.md"
                    if prior.exists() and prior.read_bytes() != data:
                        dest_dir = external / category / f"{name}__{re.sub(r'[^A-Za-z0-9._-]+', '-', repo)}"

                status = "EXACT_DUPLICATE" if digest in existing_by_sha else "EXTRACTED"
                if not args.dry_run:
                    dest_dir.mkdir(parents=True, exist_ok=True)
                    (dest_dir / "SKILL.md").write_bytes(data)
                    prov = {
                        "source_repository": full_name,
                        "source_url": original_url,
                        "source_path": source_path,
                        "source_ref": ref or "",
                        "source_commit": commit or "",
                        "license": lic,
                        "retrieved_at_utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
                        "sha256": digest,
                        "status": "RAW_EXTERNAL",
                    }
                    (dest_dir / "PROVENANCE.json").write_text(json.dumps(prov, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
                    existing_by_sha[digest] = str((dest_dir / "SKILL.md").relative_to(root))

                results.append({
                    "local_path": str((dest_dir / "SKILL.md").relative_to(root)),
                    "repository": full_name,
                    "source_url": original_url,
                    "source_path": source_path,
                    "source_commit": commit,
                    "license": lic,
                    "sha256": digest,
                    "status": status,
                })
        except Exception as e:
            failures.append({"url": original_url, "repository": full_name, "status": "FAILED", "error": str(e)})

    if not args.dry_run:
        idx = {
            "generated_at_utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
            "links_discovered": len(refs),
            "skills_processed": len(results),
            "failures": len(failures),
            "skills": results,
            "failures_detail": failures,
        }
        (external / "_EXTRACTION_INDEX.json").write_text(json.dumps(idx, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
        report = [
            "# External Skill Extraction Report",
            "",
            f"- Links discovered: **{len(refs)}**",
            f"- Skills processed: **{len(results)}**",
            f"- Failures: **{len(failures)}**",
            "",
            "## Skills",
            "",
            "| Local path | Repository | Source path | License | SHA-256 | Status |",
            "|---|---|---|---|---|---|",
        ]
        for r in results:
            report.append(f"| `{r['local_path']}` | `{r['repository']}` | `{r['source_path']}` | `{r['license']}` | `{r['sha256']}` | `{r['status']}` |")
        if failures:
            report += ["", "## Failures", ""] + [f"- `{f.get('url')}` — **{f.get('status')}** {f.get('error','')}" for f in failures]
        (external / "_EXTRACTION_REPORT_2026-09.md").write_text("\n".join(report) + "\n", encoding="utf-8")

    print("EXTERNAL_SKILL_EXTRACTION")
    print(f"LINKS_DISCOVERED={len(refs)}")
    print(f"SKILLS_PROCESSED={len(results)}")
    print(f"FAILURES={len(failures)}")
    print(f"DRY_RUN={'YES' if args.dry_run else 'NO'}")
    return 0 if not failures else 1

if __name__ == "__main__":
    raise SystemExit(main())
