"""Offline tests for analist-juridic scripts. Run: python -I -X utf8 tests/test_scripts.py
No network: captura_act.py is driven with --din-fisier, fixtures are built in memory."""
import hashlib, json, subprocess, sys, tempfile, unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SCR = ROOT / "scripts"
sys.path.insert(0, str(SCR))
import articol  # noqa: E402

ACT_TXT = """LEGE nr. 1 din 1 ianuarie 2020
Publicat în
MONITORUL OFICIAL nr. 10 din 2 ianuarie 2020
Cuprins
Articolul 1
Articolul 2
Capitolul I
Articolul 1
Obiectul legii este protecția datelor, conform art. 2 din
Articolul 5
care se aplică imediat.
Articolul 2
Termenul este de 45 de zile de la publicare. (la 13-06-2026, Alineatul (1) a fost modificat)
Articolul 10
Textul zecimal.
Articolul 474^1
Efectele încetează la abrogare.
Anexa
Articolul 2
Articol din anexă, diferit.
"""


def act_html(extra=""):
    body = "\n".join(f"<p>{l}</p>" for l in ACT_TXT.splitlines())
    return f"<html><body>{body}{extra}{'<p>padding text</p>' * 150}</body></html>"


def run(script, *args, cwd=None):
    return subprocess.run([sys.executable, "-I", "-B", "-X", "utf8", str(SCR / script), *map(str, args)],
                          capture_output=True, text=True, encoding="utf-8", cwd=cwd)


class TestArticol(unittest.TestCase):
    def setUp(self):
        self.d = tempfile.TemporaryDirectory()
        self.f = Path(self.d.name) / "act.txt"
        self.f.write_text(ACT_TXT, encoding="utf-8")

    def tearDown(self):
        self.d.cleanup()

    def test_unique_article_extracted_until_next_title(self):
        r = run("articol.py", self.f, "10")
        self.assertEqual(r.returncode, 0, r.stderr)
        self.assertIn("Textul zecimal", r.stdout)
        self.assertNotIn("Efectele", r.stdout)

    def test_caret_article(self):
        r = run("articol.py", self.f, "474^1")
        self.assertEqual(r.returncode, 0)
        self.assertIn("Efectele încetează", r.stdout)

    def test_cross_reference_not_title(self):
        self.assertEqual(articol.aparitii(ACT_TXT, "5"), [])

    def test_ambiguous_exit3_and_choose(self):
        r = run("articol.py", self.f, "2")
        self.assertEqual(r.returncode, 3, r.stdout)
        self.assertIn("AMBIGUU", r.stdout)
        r1 = run("articol.py", self.f, "2", "--aparitie", "1")
        self.assertEqual(r1.returncode, 0)
        self.assertIn("Termenul este de 45 de zile", r1.stdout)
        r2 = run("articol.py", self.f, "2", "--aparitie", "2")
        self.assertIn("Articol din anexă", r2.stdout)

    def test_not_found_exit1(self):
        self.assertEqual(run("articol.py", self.f, "999").returncode, 1)

    def test_out_of_range_exit4(self):
        self.assertEqual(run("articol.py", self.f, "2", "--aparitie", "9").returncode, 4)

    def test_caractere_limit(self):
        r = run("articol.py", self.f, "10", "--caractere", "12")
        self.assertLessEqual(len(r.stdout.strip()), 12)

    def test_crlf_file(self):
        f = Path(self.d.name) / "crlf.txt"
        f.write_bytes(ACT_TXT.replace("\n", "\r\n").encode("utf-8"))
        r = run("articol.py", f, "10")
        self.assertEqual(r.returncode, 0, r.stdout)
        self.assertIn("Textul zecimal", r.stdout)


class TestCaptura(unittest.TestCase):
    def setUp(self):
        self.d = tempfile.TemporaryDirectory()
        self.dir = Path(self.d.name)
        self.src = self.dir / "pagina.html"
        self.src.write_text(act_html(), encoding="utf-8")
        self.out = self.dir / "out"

    def tearDown(self):
        self.d.cleanup()

    def cap(self, *extra, nume="act"):
        return run("captura_act.py", "--din-fisier", self.src, "--url",
                   "https://legislatie.just.ro/Public/DetaliiDocument/1", "--nume", nume, "--out", self.out, *extra)

    def test_ok_manifest_and_hash(self):
        r = self.cap()
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        m = json.loads((self.out / "act.manifest.json").read_text(encoding="utf-8"))
        self.assertEqual(m["test_sanatate"], "OK")
        self.assertEqual(m["sha256_txt"], hashlib.sha256((self.out / "act.txt").read_bytes()).hexdigest())
        self.assertEqual(m["ultima_modificare_incorporata"], "2026-06-13")
        self.assertEqual(m["publicat"], "MONITORUL OFICIAL nr. 10 din 2 ianuarie 2020")
        self.assertTrue(m["accesat_utc"].endswith("Z"))

    def test_rejected_page_keeps_good_copy(self):
        self.assertEqual(self.cap().returncode, 0)
        good = (self.out / "act.txt").read_bytes()
        self.src.write_text("<html><body>Access Denied captcha</body></html>", encoding="utf-8")
        r = self.cap()
        self.assertEqual(r.returncode, 2)
        self.assertEqual((self.out / "act.txt").read_bytes(), good)
        self.assertTrue((self.out / "act.RESPINS.txt").exists())

    def test_missing_source_file_exit1(self):
        r = run("captura_act.py", "--din-fisier", self.dir / "nu_exista.html", "--url", "https://x", "--nume", "a",
                "--out", self.out)
        self.assertEqual(r.returncode, 1)

    def test_judgment_without_article1_needs_flag(self):
        body = "<html><body>" + "".join(
            f"<p>HOTĂRÂREA CURȚII punctul {i} motivare lungă a instanței</p>" for i in range(80)) + "</body></html>"
        self.src.write_text(body, encoding="utf-8")
        self.assertEqual(self.cap().returncode, 2)
        r = self.cap("--fara-articol1", nume="cj")
        self.assertEqual(r.returncode, 0, r.stdout)

    def test_flag_does_not_disable_captcha_check(self):
        self.src.write_text("<html><body>" + "Please enable javascript. " * 120 + "</body></html>", encoding="utf-8")
        self.assertEqual(self.cap("--fara-articol1").returncode, 2)

    def test_celex_helpers(self):
        import captura_act
        self.assertEqual(captura_act.url_celex("62019CJ0311"),
                         "https://publications.europa.eu/resource/celex/62019CJ0311")
        h = captura_act.headere_pentru("https://publications.europa.eu/resource/celex/62019CJ0311")
        self.assertEqual(h["Accept"], "application/xhtml+xml")
        self.assertNotIn("Accept", captura_act.headere_pentru("https://legislatie.just.ro/x"))


    # --- hardening (v2.1 review) ---
    def test_nume_traversal_rejected_exit2(self):
        r = self.cap(nume="../escaped")
        self.assertEqual(r.returncode, 2, r.stdout + r.stderr)
        self.assertFalse((self.dir / "escaped.txt").exists())
        self.assertFalse((self.dir / "escaped.html").exists())

    def test_nume_absolute_rejected_exit2(self):
        target = self.dir / "abs_target"
        r = self.cap(nume=str(target))
        self.assertEqual(r.returncode, 2, r.stdout + r.stderr)
        self.assertFalse(Path(str(target) + ".txt").exists())

    def test_nume_normal_ok(self):
        r = self.cap(nume="L24_2000-v1.rep")
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        self.assertTrue((self.out / "L24_2000-v1.rep.txt").exists())

    def test_overwrite_different_hash_exit3(self):
        self.assertEqual(self.cap().returncode, 0)
        good = (self.out / "act.txt").read_bytes()
        self.src.write_text(act_html("<p>Articolul 99 text nou</p>"), encoding="utf-8")
        r = self.cap()
        self.assertEqual(r.returncode, 3, r.stdout + r.stderr)
        self.assertIn("--nume", r.stdout + r.stderr)
        self.assertEqual((self.out / "act.txt").read_bytes(), good)

    def test_overwrite_identical_hash_noop_ok(self):
        self.assertEqual(self.cap().returncode, 0)
        m1 = (self.out / "act.manifest.json").read_bytes()
        r = self.cap()
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        self.assertEqual((self.out / "act.manifest.json").read_bytes(), m1)

    def run_url(self, *args):
        return run("captura_act.py", *args, "--nume", "x", "--out", self.out)

    def test_url_http_rejected(self):
        r = self.run_url("--url", "http://legislatie.just.ro/Public/DetaliiDocument/1")
        self.assertEqual(r.returncode, 2, r.stdout + r.stderr)

    def test_url_file_scheme_rejected(self):
        r = self.run_url("--din-fisier", self.src, "--url", self.src.as_uri())
        self.assertEqual(r.returncode, 2, r.stdout + r.stderr)

    def test_url_host_not_allowlisted_rejected_for_download(self):
        for u in ("https://legislatie.just.ro.evil.example/x", "https://evil.example/legislatie.just.ro",
                  "https://user@legislatie.just.ro.evil.example/x"):
            r = self.run_url("--url", u)
            self.assertEqual(r.returncode, 2, u + r.stdout + r.stderr)

    def test_celex_invalid_rejected(self):
        for c in ("32016R0679/../x", "abc", "32016R0679?x=1"):
            self.assertEqual(self.run_url("--celex", c).returncode, 2, c)

    def test_source_class_labels(self):
        import captura_act
        self.assertIn("formă consolidată neoficială, cu rol consultativ; text autentic = Monitorul Oficial",
                      captura_act.clasa_sursei("https://legislatie.just.ro/Public/DetaliiDocument/1"))
        self.assertEqual(captura_act.clasa_sursei("https://rejust.ro/x"), "de stabilit")
        self.assertTrue(captura_act.valid_celex("02016R0679-20160504"))
        self.assertTrue(captura_act.valid_celex("62019CJ0311"))
        self.assertFalse(captura_act.valid_celex("3201"))

    def test_honest_user_agent(self):
        import captura_act
        ua = captura_act.headere_pentru("https://legislatie.just.ro/x")["User-Agent"]
        self.assertTrue(ua.startswith("analist-juridic/"), ua)
        self.assertNotIn("Mozilla", ua)


class FakeResp:
    def __init__(self, body=b"", ctype="text/html; charset=utf-8", url="https://legislatie.just.ro/x", chunk=None):
        self.body, self.pos, self.ctype, self.url, self.chunk = body, 0, ctype, url, chunk
        self.headers = {"Content-Type": ctype}

    def read(self, n=-1):
        if self.chunk:
            n = min(n, self.chunk) if n and n > 0 else self.chunk
        if n is None or n < 0:
            n = len(self.body) - self.pos
        b = self.body[self.pos:self.pos + n]
        self.pos += len(b)
        return b

    def geturl(self):
        return self.url

    def __enter__(self):
        return self

    def __exit__(self, *a):
        return False


class TestDescarca(unittest.TestCase):
    def setUp(self):
        import captura_act
        self.c = captura_act

    def dl(self, resp, **kw):
        return self.c.descarca("https://legislatie.just.ro/x", opener=lambda req, timeout: resp, **kw)

    def test_ok_html(self):
        self.assertEqual(self.dl(FakeResp(b"<html>ok</html>")), b"<html>ok</html>")

    def test_xhtml_ok(self):
        self.assertEqual(self.dl(FakeResp(b"<x/>", ctype="application/xhtml+xml;charset=UTF-8")), b"<x/>")

    def test_too_large(self):
        with self.assertRaises(self.c.CapturaEroare):
            self.dl(FakeResp(b"a" * 101), max_bytes=100)
        self.assertEqual(len(self.dl(FakeResp(b"a" * 100), max_bytes=100)), 100)

    def test_pdf_rejected_with_manual_hint(self):
        with self.assertRaises(self.c.CapturaEroare) as e:
            self.dl(FakeResp(b"%PDF-1.7", ctype="application/pdf"))
        self.assertIn("PDF", str(e.exception))
        self.assertIn("manual", str(e.exception))

    def test_other_content_type_rejected(self):
        with self.assertRaises(self.c.CapturaEroare):
            self.dl(FakeResp(b"PK..", ctype="application/zip"))

    def test_deadline(self):
        ticks = iter(range(0, 10000, 100))
        with self.assertRaises(self.c.CapturaEroare) as e:
            self.dl(FakeResp(b"a" * 50, chunk=10), deadline_s=180, ceas=lambda: next(ticks))
        self.assertIn("180", str(e.exception))

    def test_redirect_off_allowlist_rejected(self):
        with self.assertRaises(self.c.CapturaEroare):
            self.dl(FakeResp(b"<html/>", url="https://evil.example/x"))

    def test_user_agent_sent(self):
        seen = {}
        def opener(req, timeout):
            seen["ua"] = req.get_header("User-agent")
            return FakeResp(b"<html/>")
        self.c.descarca("https://legislatie.just.ro/x", opener=opener)
        self.assertEqual(seen["ua"], self.c.UA)

MEMO = """# Memo
## Răspuns scurt
Termenul este de 45 de zile [C1] (grad A).
## Registrul de citări
| C# | Act | Articol | Fișier local | URL |
|---|---|---|---|---|
| C1 | L1 | 2 (apariția 1) | `{f}` | u |
## Analiză
(TEXT) Textul prevede „Termenul este de 45 de zile de la publicare” [C1].
{extra}
## Concluzii
| # | Concluzie | Grad | Temei |
|---|---|---|---|
| 1 | Termen 45 de zile | A | [C1] |
## Piste neverificate
| P1 | art. 99 alin. (2) cred că există | descarcă |
"""


class TestVerificaMemo(unittest.TestCase):
    def setUp(self):
        self.d = tempfile.TemporaryDirectory()
        self.dir = Path(self.d.name)
        self.act = self.dir / "act.txt"
        self.act.write_text(ACT_TXT, encoding="utf-8")

    def tearDown(self):
        self.d.cleanup()

    def memo(self, extra="", mutate=None, fisier=None):
        p = self.dir / "memo.md"
        t = MEMO.format(f=fisier or self.act, extra=extra)
        p.write_text(mutate(t) if mutate else t, encoding="utf-8")
        return run("verifica_memo.py", p)

    def test_clean_memo_ok(self):
        r = self.memo()
        self.assertEqual(r.returncode, 0, r.stdout)
        self.assertIn("OK", r.stdout)

    def test_fabricated_quote_flagged(self):
        r = self.memo("(TEXT) Legea spune „Termenul este de 90 de zile de la publicare” [C1].")
        self.assertEqual(r.returncode, 1)
        self.assertIn("nu se regăsește literal", r.stdout)

    def test_norm_without_citation_flagged(self):
        r = self.memo("Conform art. 7 alin. (1) se plătește o amendă.")
        self.assertEqual(r.returncode, 1)
        self.assertIn("fără trimitere", r.stdout)

    def test_norm_on_pista_only_flagged(self):
        r = self.memo("Art. 99 alin. (2) prevede ceva [P1].")
        self.assertEqual(r.returncode, 1)
        self.assertIn("pistă", r.stdout)

    def test_unknown_citation_id_flagged(self):
        r = self.memo("Termenul de 45 de zile [C7].")
        self.assertEqual(r.returncode, 1)
        self.assertIn("C7", r.stdout)

    def test_ambiguous_article_in_registry_flagged(self):
        r = self.memo(mutate=lambda t: t.replace("2 (apariția 1)", "2"))
        self.assertEqual(r.returncode, 1)
        self.assertIn("apariții", r.stdout)

    def test_missing_file_flagged(self):
        r = self.memo(fisier=self.dir / "lipsa.txt")
        self.assertEqual(r.returncode, 1)
        self.assertIn("nu există", r.stdout)

    def test_conclusion_row_without_citation_flagged(self):
        r = self.memo(mutate=lambda t: t.replace("| A | [C1] |", "| A | - |"))
        self.assertEqual(r.returncode, 1)
        self.assertIn("concluzie", r.stdout)

    def test_ellipsis_quote_in_order(self):
        r = self.memo("(TEXT) „Termenul este […] de la publicare” [C1].")
        self.assertEqual(r.returncode, 0, r.stdout)

    def test_ellipsis_quote_out_of_order_flagged(self):
        r = self.memo("(TEXT) „de la publicare […] Termenul este” [C1].")
        self.assertEqual(r.returncode, 1)

    def test_invalid_grade_flagged(self):
        r = self.memo(mutate=lambda t: t.replace("| A | [C1] |", "| E | [C1] |"))
        self.assertEqual(r.returncode, 1, r.stdout)
        self.assertIn("grad", r.stdout.lower())

    def test_crlf_memo(self):
        p = self.dir / "memo.md"
        p.write_bytes(MEMO.format(f=self.act, extra="").replace("\n", "\r\n").encode("utf-8"))
        r = run("verifica_memo.py", p)
        self.assertEqual(r.returncode, 0, r.stdout)


if __name__ == "__main__":
    unittest.main(verbosity=1)
