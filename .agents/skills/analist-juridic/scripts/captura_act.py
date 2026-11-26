"""Captură verificabilă a unui act normativ de pe legislatie.just.ro (sau alt URL oficial).

Salvează: <nume>.html (brut), <nume>.txt (text extras, UTF-8, LF), <nume>.manifest.json
(URL, ora UTC, SHA-256 pe fișierele scrise, titlul, Monitorul Oficial, ultima modificare încorporată).
Testul de sănătate respinge paginile goale, captcha sau eroare: acestea se salvează ca <nume>.RESPINS.*
și NU suprascriu o copie bună existentă.

Coduri de ieșire: 0 = OK · 1 = eroare de rețea/HTTP · 2 = pagină respinsă la testul de sănătate.
Utilizare:
  python -I -X utf8 captura_act.py --id 120611 --nume cpp --out <dosar>
  python -I -X utf8 captura_act.py --url https://legislatie.just.ro/Public/DetaliiDocumentAfis/262035 --nume L24 --out <dosar>
  python -I -X utf8 captura_act.py --din-fisier pagina.html --url <URL-ul paginii> --nume cpp --out <dosar>
"""
import argparse, datetime as dt, hashlib, html, json, re, sys, urllib.error, urllib.request
from pathlib import Path

sys.stdout.reconfigure(encoding="utf-8")
UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)"


def descarca(url):
    req = urllib.request.Request(url, headers={"User-Agent": UA, "Accept-Language": "ro"})
    with urllib.request.urlopen(req, timeout=120) as r:
        return r.read()


def text_din_html(raw):
    t = raw.decode("utf-8", "replace")
    t = re.sub(r"(?is)<(script|style)[^>]*>.*?</\1>", " ", t)
    t = html.unescape(re.sub(r"<[^>]+>", "\n", t))
    t = re.sub(r"[ \t\r\f\v]+", " ", t)
    return re.sub(r"\n\s*\n+", "\n", t).strip() + "\n"


def scrie(p, date):
    p.write_bytes(date)
    return hashlib.sha256(p.read_bytes()).hexdigest()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--id")
    ap.add_argument("--url")
    ap.add_argument("--din-fisier")
    ap.add_argument("--nume", required=True)
    ap.add_argument("--out", required=True)
    a = ap.parse_args()
    url = a.url or (f"https://legislatie.just.ro/Public/DetaliiDocument/{a.id}" if a.id else None)
    if not url:
        sys.exit("Lipsește --id sau --url")
    try:
        raw = Path(a.din_fisier).read_bytes() if a.din_fisier else descarca(url)
    except (urllib.error.URLError, OSError) as e:
        print(json.dumps({"url": url, "eroare": str(e)}, ensure_ascii=False))
        sys.exit(1)
    txt = text_din_html(raw)

    probleme = []
    if len(txt) < 2000:
        probleme.append(f"text prea scurt ({len(txt)} caractere)")
    if not re.search(r"Articolul 1\b|Art\. 1\b", txt):
        probleme.append("nu conține „Articolul 1”")
    if re.search(r"(?i)captcha|access denied|forbidden|enable javascript", txt[:3000]):
        probleme.append("pare pagină de blocare/captcha")

    out = Path(a.out)
    out.mkdir(parents=True, exist_ok=True)
    baza = a.nume if not probleme else f"{a.nume}.RESPINS"
    h_html = scrie(out / f"{baza}.html", raw)
    h_txt = scrie(out / f"{baza}.txt", txt.encode("utf-8"))

    mo = re.search(r"Publicat în\s*\n?\s*(MONITORUL OFICIAL nr\. ?[\w/]+ din [^\n]+)", txt)
    titlu = re.search(r"Forma printabilă\s*\n([^\n]+)", txt)
    note = re.findall(r"\(la (\d{2})-(\d{2})-(\d{4})", txt)
    ultima = max((f"{y}-{m}-{d}" for d, m, y in note), default=None)
    manifest = {
        "url": url,
        "sursa_fisier": a.din_fisier,
        "accesat_utc": dt.datetime.now(dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "fisier_txt": f"{baza}.txt",
        "sha256_txt": h_txt,
        "sha256_html": h_html,
        "titlu": titlu.group(1).strip() if titlu else None,
        "publicat": mo.group(1).strip() if mo else None,
        "ultima_modificare_incorporata": ultima,
        "note_de_modificare": len(note),
        "clasa_sursei": "oficial informativ (formă consolidată)" if "legislatie.just.ro" in url else "de stabilit",
        "test_sanatate": "OK" if not probleme else "RESPINS: " + "; ".join(probleme),
    }
    (out / f"{baza}.manifest.json").write_bytes(json.dumps(manifest, ensure_ascii=False, indent=2).encode("utf-8"))
    print(json.dumps(manifest, ensure_ascii=False, indent=2))
    sys.exit(2 if probleme else 0)


if __name__ == "__main__":
    main()
