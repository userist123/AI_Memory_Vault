"""Validator determinist pentru un Legal Memo (șablon: _SABLON_memo.md).

Verifică:
 1. Registrul de citări (rânduri C1, C2…): fișier local existent; coloana „Articol” se rezolvă la UN articol din copie
    (pentru apariții multiple scrie „74 (apariția 2)”); altfel defect.
 2. Citatele „…” și "…" (≥12 caractere, și pe mai multe rânduri): trimiterile [Cn] de pe rândurile citatului sunt
    obligatorii; citatul apare literal în articolul din registru; fragmentele separate de […] apar în ordine.
 3. În secțiunile analizate (toate, în afară de Registru, Fapte, Piste neverificate, Jurnal): orice frază cu normă
    (art./Art./Articolul/Articolele, alin., decizie … nr., RIL/HP nr., termen sau cuantum: „45 de zile”, „5.000 de lei”)
    are o trimitere [Cn]. O normă susținută doar de [Pn] e defect: pistele stau numai în secțiunea lor.
 4. Răspuns scurt și Concluzii: fără [Pn]; fiecare rând din tabelul de Concluzii are [Cn].
 5. Orice grad de certitudine (coloana „Grad” sau „(grad X)”) este A, B, C sau D.
Ieșire: lista defectelor; exit 1 dacă există defecte.
Utilizare: python -I -X utf8 verifica_memo.py memo.md [--baza <dosar pentru căi relative>]
"""
import argparse, re, sys
from pathlib import Path

sys.stdout.reconfigure(encoding="utf-8")
sys.path.insert(0, str(Path(__file__).parent))
from articol import aparitii, extrage  # noqa: E402

ap = argparse.ArgumentParser()
ap.add_argument("memo")
ap.add_argument("--baza", default=None)
a = ap.parse_args()
memo = Path(a.memo)
baza = Path(a.baza) if a.baza else memo.parent
text = memo.read_text(encoding="utf-8")
linii = text.splitlines()
norm = lambda s: re.sub(r"\s+", " ", s.replace("’", "'").replace("\xa0", " ")).strip().lower()
defecte = []

# 1. registrul
registru = {}
NR_ART = re.compile(r"(?:art\.?|articolul)\s*(\d+(?:\.\d+)?(?:\^\d+)?)", re.I)
for linie in linii:
    cel = [c.strip() for c in linie.strip().strip("|").split("|")]
    if len(cel) < 4 or not re.fullmatch(r"C\d+", cel[0]):
        continue
    cid = cel[0]
    cale = next((c.strip("` ") for c in cel if re.search(r"\.(txt|html)\b", c)), None)
    if not cale:
        defecte.append(f"{cid}: fără fișier local .txt în registru")
        continue
    p = Path(cale) if Path(cale).is_absolute() else baza / cale
    if not p.exists():
        defecte.append(f"{cid}: fișierul nu există: {p}")
        continue
    t = p.read_text(encoding="utf-8", errors="replace")
    col = cel[2]
    m = NR_ART.search(col) or re.match(r"\s*(\d+(?:\.\d+)?(?:\^\d+)?)", col)
    if not m:
        defecte.append(f"{cid}: coloana „Articol” nu conține un număr de articol: {col!r}")
        continue
    nr = m.group(1)
    poz = aparitii(t, nr)
    k = re.search(r"apari[țţt]ia\s*(\d+)", col, re.I)
    if k and 1 <= int(k.group(1)) <= len(poz):
        poz = [poz[int(k.group(1)) - 1]]
    if len(poz) != 1:
        defecte.append(f"{cid}: art. {nr} are {len(poz)} apariții în {p.name}" +
                       (" (precizează „(apariția k)” în coloana Articol)" if poz else " (negăsit)"))
        continue
    registru[cid] = norm(extrage(t, poz[0]))

# secțiuni
SARITE = ("registr", "fapte", "piste", "jurnal")
FARA_PISTE = ("răspuns", "raspuns", "concluz")
sect_de_linie, curent = {}, ""
for i, l in enumerate(linii, 1):
    if l.startswith("#"):
        curent = l.lower()
    sect_de_linie[i] = curent
verificat = lambda i: not any(k in sect_de_linie[i] for k in SARITE) and not linii[i - 1].startswith("#")

# 2. citate (pe textul întreg, ca să prindă citatele pe mai multe rânduri și cu puncte în interior)
offset_linie = [0]
for l in linii:
    offset_linie.append(offset_linie[-1] + len(l) + 1)
lin = lambda off: next(i for i in range(1, len(offset_linie)) if offset_linie[i] > off)
for mq in re.finditer(r"„([^”]{12,})”|\"([^\"\n]{12,}(?:\n[^\"\n]*)?)\"", text):
    l0, l1 = lin(mq.start()), lin(mq.end() - 1)
    if not verificat(l0):
        continue
    citat = mq.group(1) or mq.group(2)
    refs = re.findall(r"\[(C\d+)\]", "\n".join(linii[l0 - 1:l1]))
    if not refs:
        defecte.append(f"r.{l0}: citat fără [Cn]: „{norm(citat)[:60]}…”")
        continue
    parti = [norm(x) for x in re.split(r"\[…\]|\[\.\.\.\]|\(…\)|\(\.\.\.\)|…", citat) if len(x.strip()) >= 8]
    ok = False
    for r in refs:
        corp, poz = registru.get(r, ""), 0
        for parte in parti:
            j = corp.find(parte, poz)
            if j < 0:
                break
            poz = j + len(parte)
        else:
            ok = bool(corp)
        if ok:
            break
    if not ok:
        defecte.append(f"r.{l0}: citatul nu se regăsește literal (în ordine) în articolul din {refs}: „{norm(citat)[:60]}…”")

# 3–4. norme fără [Cn], piste, concluzii
NORMA = re.compile(
    r"\b(?:art\.|articol(?:ul|ele|elor)?)\s*\d|\balin\.\s*\(\d"
    r"|\bdecizi(?:a|ei|e|ile)\b[^.\n]{0,30}?\bnr\.\s*\d|\b(?:RIL|HP)\s+(?:nr\.\s*)?\d"
    r"|\b\d+(?:[.,]\d+)*\s*(?:de\s+)?(?:lei|euro|EUR|zile|luni|ani|ore)\b", re.I)
SEP = r"(?<!art\.)(?<!Art\.)(?<!alin\.)(?<!nr\.)(?<!pct\.)(?<!lit\.)(?<=[.;!?])\s+(?=[A-ZĂÂÎȘȚ„\"(\[])"
for i, l in enumerate(linii, 1):
    if not verificat(i) or not l.strip() or re.fullmatch(r"[\s|:-]+", l):
        continue
    sect = sect_de_linie[i]
    for r in re.findall(r"\[(C\d+)\]", l):
        if r not in registru and not any(d.startswith(f"{r}:") for d in defecte):
            defecte.append(f"r.{i}: {r} nu există în registrul de citări (sau a fost respins mai sus)")
    if "concluz" in sect and l.strip().startswith("|") and re.match(r"^\|\s*\d", l.strip()) and not re.search(r"\[C\d+\]", l):
        defecte.append(f"r.{i}: rând de concluzie fără temei [Cn]: {norm(l)[:80]}")
    for fraza in re.split(SEP, l):
        are_c, are_p = re.search(r"\[C\d+\]", fraza), re.search(r"\[P\d+\]", fraza)
        if NORMA.search(fraza) and not are_c:
            motiv = "normă susținută doar de o pistă neverificată" if are_p else "normă/termen/cuantum fără trimitere [Cn]"
            defecte.append(f"r.{i}: {motiv}: {norm(fraza)[:90]}")
        elif are_p and any(k in sect for k in FARA_PISTE):
            defecte.append(f"r.{i}: concluzie sprijinită pe o pistă neverificată: {norm(fraza)[:90]}")

# 5. every certainty grade is one of A-D (tables with a "Grad" column, and inline "(grad X)")
GRADE = {"A", "B", "C", "D"}
col_grad = None
for i, l in enumerate(linii, 1):
    s = l.strip()
    if not s.startswith("|"):
        col_grad = None
    else:
        cel = [c.strip() for c in s.strip("|").split("|")]
        hdr = [c.lower() for c in cel]
        if "grad" in hdr:
            col_grad = hdr.index("grad")
        elif col_grad is not None and not re.fullmatch(r"[\s|:-]+", s) and col_grad < len(cel):
            if cel[col_grad] not in GRADE:
                defecte.append(f"r.{i}: grad invalid {cel[col_grad]!r} (permise: A, B, C, D)")
    for mg in re.finditer(r"\(grad(?:ul)?\s+([^\s)]+)\)", l, re.I):
        if mg.group(1) not in GRADE:
            defecte.append(f"r.{i}: grad invalid {mg.group(1)!r} (permise: A, B, C, D)")

print(f"Citări valide în registru: {len(registru)}")
print("\n".join(defecte) if defecte else "OK: niciun defect găsit")
sys.exit(1 if defecte else 0)
