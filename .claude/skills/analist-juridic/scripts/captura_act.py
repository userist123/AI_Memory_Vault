"""Captură verificabilă a unui act normativ de pe legislatie.just.ro (sau alt URL oficial).

Salvează: <nume>.html (brut), <nume>.txt (text extras, UTF-8, LF), <nume>.manifest.json
(URL, ora UTC, SHA-256 pe fișierele scrise, titlul, Monitorul Oficial, ultima modificare încorporată).
Testul de sănătate respinge paginile goale, captcha sau eroare: acestea se salvează ca <nume>.RESPINS.*
și NU suprascriu o copie bună existentă. O captură bună existentă nu se suprascrie cu alt conținut.

Acces onest: scriptul se identifică (User-Agent „analist-juridic/…”), descarcă doar prin https de pe gazdele oficiale
din ALLOWLIST și nu trece niciodată automat de o provocare anti-bot (EUR-Lex 202 = AWS WAF: folosește Cellar).
Limite: cel mult 25 MB, 180 s în total, doar Content-Type HTML/XHTML (un PDF se salvează manual).

Coduri de ieșire: 0 = OK (sau captură identică deja existentă) · 1 = eroare de rețea/HTTP/limită/tip de conținut ·
2 = pagină respinsă la testul de sănătate sau argument invalid (--nume, --url, --celex, --id) ·
3 = există deja o captură <nume> cu alt conținut (alege alt --nume, de ex. cu marcă de timp UTC).
Utilizare:
  python -I -X utf8 captura_act.py --id 120611 --nume cpp --out <dosar>
  python -I -X utf8 captura_act.py --url https://legislatie.just.ro/Public/DetaliiDocumentAfis/262035 --nume L24 --out <dosar>
  python -I -X utf8 captura_act.py --din-fisier pagina.html --url <URL-ul paginii> --nume cpp --out <dosar>
  python -I -X utf8 captura_act.py --celex 32016R0679 --nume gdpr --out <dosar>        (EUR-Lex via Cellar, RO)
  python -I -X utf8 captura_act.py --celex 62019CJ0311 --fara-articol1 --nume cj --out <dosar>   (hotărâre CJUE)
--fara-articol1: pentru hotărâri/concluzii, care nu au „Articolul 1”; testele captcha/lungime rămân active.
"""
import argparse, datetime as dt, hashlib, html, json, re, sys, time, urllib.error, urllib.parse, urllib.request
from pathlib import Path

sys.stdout.reconfigure(encoding="utf-8")
# Honest identification: name the script, never imitate a browser.
UA = "analist-juridic/2.1 (+local legal research script)"
MAX_BYTES = 25 * 1024 * 1024
DEADLINE_S = 180
ALLOWLIST = frozenset({"legislatie.just.ro", "publications.europa.eu", "eur-lex.europa.eu", "www.ccr.ro", "ccr.ro",
                       "www.iccj.ro", "iccj.ro", "www.scj.ro", "scj.ro"})
CELEX_RE = re.compile(r"[0-9A-Z]{5,20}(-[0-9]{8})?")
NUME_RE = re.compile(r"[\w.-]+")
CLASA_RO = "portal oficial; formă consolidată neoficială, cu rol consultativ; text autentic = Monitorul Oficial"

CELLAR = "https://publications.europa.eu/resource/celex/"


class CapturaEroare(Exception):
    pass


def url_celex(celex):
    return CELLAR + celex


def valid_celex(c):
    return bool(c) and CELEX_RE.fullmatch(c) is not None


def gazda_oficiala(url):
    """Return the lower-cased hostname if url is https on an allowlisted host (no userinfo, default port), else None."""
    try:
        u = urllib.parse.urlsplit(url)
        port = u.port
    except ValueError:
        return None
    host = (u.hostname or "").lower()
    if u.scheme != "https" or u.username or u.password or port not in (None, 443) or host not in ALLOWLIST:
        return None
    return host


def clasa_sursei(url):
    host = gazda_oficiala(url)
    if host == "legislatie.just.ro":
        return CLASA_RO
    if host == "publications.europa.eu" and url.startswith(CELLAR):
        return "oficial UE (Cellar, XHTML; autentic = JOUE)"
    if host:
        return "oficial (de precizat forma: consultativă sau autentică)"
    return "de stabilit"


def headere_pentru(url):
    h = {"User-Agent": UA, "Accept-Language": "ro"}
    if url.startswith(CELLAR):  # Cellar negociază conținutul: fără Accept xhtml întoarce arhive mari (zeci de MB)
        h["Accept"] = "application/xhtml+xml"
    return h


def descarca(url, opener=urllib.request.urlopen, max_bytes=MAX_BYTES, deadline_s=DEADLINE_S, ceas=time.monotonic):
    """Fetch url with an overall deadline, a size cap (reads at most max_bytes + 1) and an HTML/XHTML-only filter."""
    start = ceas()
    req = urllib.request.Request(url, headers=headere_pentru(url))
    with opener(req, timeout=min(60, deadline_s)) as r:
        final = r.geturl() if hasattr(r, "geturl") else url
        if not gazda_oficiala(final):  # redirects must stay on the allowlist
            raise CapturaEroare(f"redirecționare către o gazdă neoficială sau ne-https: {final}")
        ctype = (r.headers.get("Content-Type") or "").split(";")[0].strip().lower()
        if ctype == "application/pdf":
            raise CapturaEroare("răspuns PDF: descarcă manual fișierul, calculează SHA-256 și citează pagina + fișierul "
                                "(references/acces_surse.md, rețeta D); scriptul acceptă doar HTML/XHTML")
        if ctype not in ("text/html", "application/xhtml+xml"):
            raise CapturaEroare(f"Content-Type neacceptat: {ctype or '(lipsă)'}; doar text/html sau application/xhtml+xml")
        buf = bytearray()
        while True:
            if ceas() - start > deadline_s:
                raise CapturaEroare(f"termen total depășit ({deadline_s} s)")
            bucata = r.read(min(65536, max_bytes + 1 - len(buf)))
            if not bucata:
                break
            buf += bucata
            if len(buf) > max_bytes:
                raise CapturaEroare(f"răspuns prea mare (> {max_bytes} octeți)")
        return bytes(buf)


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
    ap.add_argument("--celex", help="ex. 32016R0679, 02016R0679-20160504, 62019CJ0311 (descarcă de la Cellar)")
    ap.add_argument("--fara-articol1", action="store_true")
    ap.add_argument("--din-fisier")
    ap.add_argument("--nume", required=True)
    ap.add_argument("--out", required=True)
    a = ap.parse_args()
    if not NUME_RE.fullmatch(a.nume) or a.nume in (".", ".."):
        ap.error(f"--nume invalid: {a.nume!r} (doar litere, cifre, _ . -; fără căi)")
    if a.celex and not valid_celex(a.celex):
        ap.error(f"--celex invalid: {a.celex!r} (format: ^[0-9A-Z]{{5,20}}(-[0-9]{{8}})?$)")
    if a.id and not re.fullmatch(r"[0-9]{1,10}", a.id):
        ap.error(f"--id invalid: {a.id!r} (doar cifre)")
    url = a.url or (url_celex(a.celex) if a.celex else None) or (
        f"https://legislatie.just.ro/Public/DetaliiDocument/{a.id}" if a.id else None)
    if not url:
        sys.exit("Lipsește --id, --url sau --celex")
    if urllib.parse.urlsplit(url).scheme.lower() != "https":
        ap.error(f"--url trebuie să fie https: {url!r}")
    if not a.din_fisier and not gazda_oficiala(url):
        ap.error(f"gazdă neoficială pentru descărcare: {url!r} (permise: {', '.join(sorted(ALLOWLIST))}); "
                 "pentru alte surse salvează pagina și folosește --din-fisier")
    out = Path(a.out)
    out_r = out.resolve()
    if (out_r / f"{a.nume}.html").resolve().parent != out_r:
        ap.error(f"--nume iese din --out: {a.nume!r}")
    try:
        raw = Path(a.din_fisier).read_bytes() if a.din_fisier else descarca(url)
    except (urllib.error.URLError, OSError, CapturaEroare) as e:
        print(json.dumps({"url": url, "eroare": str(e)}, ensure_ascii=False))
        sys.exit(1)
    txt = text_din_html(raw)

    probleme = []
    if len(txt) < 2000:
        probleme.append(f"text prea scurt ({len(txt)} caractere)")
    if not a.fara_articol1 and not re.search(r"Articolul 1\b|Art\. 1\b", txt):
        probleme.append("nu conține „Articolul 1”")
    if re.search(r"(?i)captcha|access denied|forbidden|enable javascript", txt[:3000]):
        probleme.append("pare pagină de blocare/captcha")

    baza = a.nume if not probleme else f"{a.nume}.RESPINS"
    txt_b = txt.encode("utf-8")
    if not probleme:
        existente = [s for s in (".html", ".txt", ".manifest.json") if (out / f"{baza}{s}").exists()]
        if existente:
            vechi = out / f"{baza}.txt"
            if vechi.exists() and hashlib.sha256(vechi.read_bytes()).hexdigest() == hashlib.sha256(txt_b).hexdigest():
                print(json.dumps({"nume": baza, "stare": "captură identică deja existentă; nimic suprascris"},
                                 ensure_ascii=False))
                sys.exit(0)
            stamp = dt.datetime.now(dt.timezone.utc).strftime("%Y%m%dT%H%M%SZ")
            print(json.dumps({"nume": baza, "eroare": "există deja o captură cu alt conținut; nu suprascriu",
                              "sugestie": f"alege alt --nume, de ex. --nume {a.nume}_{stamp}"}, ensure_ascii=False))
            sys.exit(3)
    out.mkdir(parents=True, exist_ok=True)
    h_html = scrie(out / f"{baza}.html", raw)
    h_txt = scrie(out / f"{baza}.txt", txt_b)

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
        "clasa_sursei": clasa_sursei(url),
        "test_sanatate": "OK" if not probleme else "RESPINS: " + "; ".join(probleme),
    }
    (out / f"{baza}.manifest.json").write_bytes(json.dumps(manifest, ensure_ascii=False, indent=2).encode("utf-8"))
    print(json.dumps(manifest, ensure_ascii=False, indent=2))
    sys.exit(2 if probleme else 0)


if __name__ == "__main__":
    main()
