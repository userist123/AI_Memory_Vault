"""Numbers for document-uman: one rounding routine, exact allocation, Romanian number words and the
DOOM "de" rule, plausible document numbers, and prose-vs-model reconciliation.

    import cifre_ro as cr
    cr.rotunjire(5964.5, 0)            # 5965 (ROUND_HALF_UP, like Excel ROUND; never banker's)
    cr.suma([113929.88, 5465.40])      # 119395.28, exact decimal sum rounded to bani
    cr.ro(119395.28)                   # "119.395,28"
    parti, dif = cr.repartizeaza(1800, ponderi)              # ROUND per part + visible difference
    parti, dif = cr.repartizeaza(1800, ponderi, "rest_maxim")  # largest remainder, dif == 0
    cr.cu_de(1800, "lei")              # "1.800 de lei";  cr.cu_de(103, "persoane") -> "103 persoane"
    cr.in_litere(2, "f")               # "două";  cr.suma_in_litere(1800) -> "o mie opt sute de lei"
    gen = cr.NumereDocumente(seed=7); gen.serie("Aqua", "AN{an}{n:07d}", start=812345, pas_mediu=2600)
    cr.reconciliaza_text(text, model)  # numbers in prose that are not in the data model

Rule (2026-11-22 blind review): every total, average and chart label goes through rotunjire();
Python's round() and f"{x:.2f}" round half-to-even on the binary value (5964.5 -> "5964"), Excel
rounds half away from zero (5965), so the report and the workbook disagreed by one unit.
"""
from __future__ import annotations

import random
import re
from decimal import ROUND_HALF_UP, Decimal

# ------------------------------------------------------------------------------ rounding


def _dec(x) -> Decimal:
    if isinstance(x, Decimal):
        return x
    # 15 significant digits, the precision Excel shows and rounds on: 12.344999999999999 (binary
    # noise of 12.345) -> "12.345" -> 12.35, as Excel ROUND gives; 2.675 -> 2.68
    return Decimal(f"{x:.15g}") if isinstance(x, float) else Decimal(str(x))


def rotunjire(x, zecimale: int = 2):
    """ROUND_HALF_UP to `zecimale` decimals (default: bani). Returns float (int for zecimale=0)."""
    q = Decimal(1).scaleb(-zecimale)
    v = _dec(x).quantize(q, rounding=ROUND_HALF_UP)
    return int(v) if zecimale == 0 else float(v)


def suma(valori, zecimale: int = 2):
    """Exact decimal sum, then rotunjire - no float drift (0.1 + 0.2)."""
    return rotunjire(sum((_dec(v) for v in valori), Decimal(0)), zecimale)


def ro(x, zecimale: int = 2) -> str:
    """Romanian format 1.234,56 after rotunjire (never Python's half-even formatting)."""
    v = _dec(x).quantize(Decimal(1).scaleb(-zecimale), rounding=ROUND_HALF_UP)
    neg = v < 0
    s = f"{abs(v):,.{zecimale}f}".replace(",", "X").replace(".", ",").replace("X", ".")
    return ("−" if neg else "") + s


def repartizeaza(total, ponderi, metoda: str = "rotunjire", zecimale: int = 2):
    """Distribute `total` over units proportionally to `ponderi`.
    metoda="rotunjire": each part = rotunjire(total*w/W) - the way an Excel ROUND column does it;
        returns (parts, diferenta) where diferenta = total - sum(parts) must be shown on a visible
        "Diferențe din rotunjire" line so the table still adds up.
    metoda="rest_maxim": largest-remainder method; parts sum exactly to total, diferenta == 0.
    """
    W = sum(_dec(w) for w in ponderi)
    T = _dec(total)
    unit = Decimal(1).scaleb(-zecimale)
    if W == 0:
        raise ValueError("ponderi însumează 0")
    raw = [T * _dec(w) / W for w in ponderi]
    if metoda == "rotunjire":
        parti = [r.quantize(unit, rounding=ROUND_HALF_UP) for r in raw]
    elif metoda == "rest_maxim":
        jos = [(r / unit).to_integral_value(rounding="ROUND_FLOOR") * unit for r in raw]
        rest = int(((T - sum(jos)) / unit).to_integral_value())
        ordine = sorted(range(len(raw)), key=lambda i: (-(raw[i] - jos[i]), i))
        for i in ordine[:rest]:
            jos[i] += unit
        parti = jos
    else:
        raise ValueError(metoda)
    dif = T - sum(parti)
    return [float(p) for p in parti], float(dif)


# ------------------------------------------------------------------------------ words

_UNU = {"m": "unu", "f": "una", "n": "unu"}
_DOI = {"m": "doi", "f": "două", "n": "două"}
_0_19 = ["zero", None, None, "trei", "patru", "cinci", "șase", "șapte", "opt", "nouă", "zece",
         "unsprezece", None, "treisprezece", "paisprezece", "cincisprezece", "șaisprezece",
         "șaptesprezece", "optsprezece", "nouăsprezece"]
_ZECI = {2: "douăzeci", 3: "treizeci", 4: "patruzeci", 5: "cincizeci", 6: "șaizeci", 7: "șaptezeci",
         8: "optzeci", 9: "nouăzeci"}


def _sub_100(n: int, gen: str, atribut: bool = False) -> str:
    """atribut=True: the form before a noun ("un apartament", "o persoană", "două sute")."""
    if n == 1:
        return ("un" if gen in ("m", "n") else "o") if atribut else _UNU[gen]
    if n == 2:
        return _DOI[gen]
    if n == 12:
        return "doisprezece" if gen == "m" else "douăsprezece"
    if n < 20:
        return _0_19[n]
    z, u = divmod(n, 10)
    if u == 0:
        return _ZECI[z]
    unit = "unu" if u == 1 else (_DOI[gen] if u == 2 else _0_19[u])
    return f"{_ZECI[z]} și {unit}"


def _sub_1000(n: int, gen: str, atribut: bool) -> str:
    s, r = divmod(n, 100)
    parts = []
    if s == 1:
        parts.append("o sută")
    elif s > 1:
        parts.append(f"{_DOI['f'] if s == 2 else _0_19[s]} sute")
    if r:
        parts.append(_sub_100(r, gen, atribut))
    return " ".join(parts)


def _grup(n: int, sing: str, plural: str) -> str:
    # mii / milioane / miliarde are feminine/neuter nouns -> "două mii", "douăzeci de mii"
    if n == 1:
        return f"{'o' if sing == 'mie' else 'un'} {sing}"
    w = _sub_1000(n, "f", True)
    return f"{w} {'de ' if necesita_de(n) else ''}{plural}"


def in_litere(n: int, gen: str = "m", atribut: bool = True) -> str:
    """Integer in Romanian words. gen: 'm' (doi lei), 'f' (două persoane), 'n' (două apartamente).
    atribut=True gives the form used before a noun ("un", "o")."""
    if n < 0:
        return "minus " + in_litere(-n, gen, atribut)
    if n == 0:
        return "zero"
    parts = []
    for val, sing, plural in ((10 ** 9, "miliard", "miliarde"), (10 ** 6, "milion", "milioane"),
                              (1000, "mie", "mii")):
        q, n = divmod(n, val)
        if q:
            parts.append(_grup(q, sing, plural))
    if n:
        # "un"/"o" only for the number 1 itself; compounds end in "unu"/"una" (o sută unu lei)
        parts.append(_sub_1000(n, gen, atribut and not parts and n == 1))
    return " ".join(parts)


def necesita_de(n) -> bool:
    """DOOM: "de" between a numeral and its noun when the number ends in 20-99 or 00 (20 de lei,
    100 de lei, 1.800 de lei, 2.400 de lei); none after 1-19 and x01-x19 (103 persoane, 119 lei).
    Decimal amounts are read with "lei" directly (16,21 lei)."""
    if isinstance(n, float) and not n.is_integer():
        return False
    n = abs(int(n))
    r = n % 100
    return r >= 20 or (r == 0 and n > 0)


def cu_de(n, substantiv: str, zecimale: int | None = None) -> str:
    """'1.800 de lei', '103 persoane', '16,21 lei' (with a no-break space before the noun)."""
    if zecimale is None:
        zecimale = 0 if float(n).is_integer() else 2
    txt = ro(n, zecimale)
    # unit symbols are written without "de" (2.788 mc, 210 mp, 40 km); words take it (40 de metri)
    simbol = substantiv in ("mp", "mc", "m", "km", "kg", "t", "kWh", "%", "cm", "mm", "ha")
    de = "de " if zecimale == 0 and necesita_de(n) and not simbol else ""
    return f"{txt} {de}{substantiv}"


def suma_in_litere(x, moneda=("leu", "lei"), subunitate=("ban", "bani")) -> str:
    """'o mie opt sute de lei', 'o sută unu lei', 'doi lei și cincizeci de bani'."""
    v = _dec(x).quantize(Decimal("0.01"), rounding=ROUND_HALF_UP)
    lei, bani = int(v), int((v - int(v)) * 100)

    def bucata(n, sg, pl):
        if n == 1:
            return f"un {sg}"
        return f"{in_litere(n, 'm', atribut=False)} {'de ' if necesita_de(n) else ''}{pl}"
    out = bucata(lei, *moneda) if lei or not bani else ""
    if bani:
        out = (out + " și " if out else "") + bucata(bani, *subunitate)
    return out


_CUVINTE = {"un": 1, "o": 1, "unu": 1, "una": 1, "doi": 2, "două": 2, "trei": 3, "patru": 4,
            "cinci": 5, "șase": 6, "șapte": 7, "opt": 8, "nouă": 9, "zece": 10, "unsprezece": 11,
            "doisprezece": 12, "douăsprezece": 12}


# ------------------------------------------------------------------------------ document numbers

class NumereDocumente:
    """Plausible reference numbers: monotonic per issuer, irregular gaps (an issuer bills many
    clients between two of ours), realistic formats. Never an arithmetic progression.

        g = NumereDocumente(seed=11)
        g.serie("aqua", "{an}{n:08d}", start=10482211, pas_mediu=3100)
        g.urmator("aqua", an=2026)  -> "202610485123" ...
    pas_mediu: average gap; the gaps are drawn from a lognormal distribution (cv about 0.5).
    secvential=True: our own registers (chitanțier, registru de intrare-ieșire) - step 1 with
    occasional gaps only where documents were cancelled (anulate)."""

    def __init__(self, seed=None):
        self._rng = random.Random(seed)
        self._serii = {}

    def serie(self, cheie, fmt, start, pas_mediu=1, secvential=False, prob_anulare=0.0):
        self._serii[cheie] = dict(fmt=fmt, n=start, pas=pas_mediu, sec=secvential, anul=prob_anulare,
                                  emise=[])

    def urmator(self, cheie, **campuri):
        s = self._serii[cheie]
        if s["emise"]:
            if s["sec"]:
                s["n"] += 2 if self._rng.random() < s["anul"] else 1
            else:
                g = self._rng.lognormvariate(0, 0.5) * s["pas"]
                s["n"] += max(1, int(g))
        nr = s["fmt"].format(n=s["n"], **campuri)
        s["emise"].append(s["n"])
        return nr

    def emise(self, cheie):
        return list(self._serii[cheie]["emise"])


def progresie_aritmetica(numere, minim=4) -> bool:
    """True if the numbers have a constant step (the synthetic-data tell the reviewer caught)."""
    if len(numere) < minim:
        return False
    d = {b - a for a, b in zip(numere, numere[1:])}
    return len(d) == 1


# ------------------------------------------------------------------------------ reconciliation

_NUM = re.compile(r"(?<![\w.,/])(\d{1,3}(?:\.\d{3})+|\d+)(,\d+)?(?![\w/]|\.\d)")
_DATA = re.compile(r"\b\d{1,2}\.\d{1,2}\.\d{4}\b")
_UNITATI = r"(?:de\s)?(?:lei|leu|persoane|persoană|apartamente|apartament|mp|mc|zile|luni|liste|%|ori)"


def valori_model(model, out=None):
    """Flatten every int/float in a nested dict/list/tuple."""
    out = set() if out is None else out
    if isinstance(model, bool):
        return out
    if isinstance(model, (int, float, Decimal)):
        out.add(float(model))
    elif isinstance(model, dict):
        for v in model.values():
            valori_model(v, out)
    elif isinstance(model, (list, tuple, set)):
        for v in model:
            valori_model(v, out)
    return out


def numere_in_text(text):
    """(token, value, has_decimals, followed_by_unit) for every number in Romanian format,
    dates (dd.mm.yyyy) excluded; number words before a noun ('șase apartamente') included."""
    text = text.replace(" ", " ").replace("−", "-")
    fara_date = _DATA.sub(" ", text)
    res = []
    for m in _NUM.finditer(fara_date):
        intreg, zec = m.group(1), m.group(2)
        val = float(intreg.replace(".", "") + (zec.replace(",", ".") if zec else ""))
        dupa = fara_date[m.end():m.end() + 16]
        unit = bool(re.match(r"\s?" + _UNITATI + r"\b", dupa)) or dupa.startswith("%")
        res.append((m.group(0), val, bool(zec), unit))
    for m in re.finditer(r"\b(" + "|".join(_CUVINTE) + r")\s+(apartamente|contracte|persoane|"
                         r"liste|luni|oferte|lucrări|ședințe|restanțieri|furnizori|facturi)\b",
                         text, flags=re.I):
        res.append((m.group(0), float(_CUVINTE[m.group(1).lower()]), False, True))
    return res


def reconciliaza_text(text, model, ignora=(), toleranta=0.0):
    """Numbers in prose that cannot be found in the data model.
    Checked: every amount with decimals, every integer followed by a unit/noun (lei, persoane,
    apartamente, mp, zile, %), every count written in words before a noun.
    model: dict/list (flattened) or a set of values. ignora: extra allowed values (legal rates,
    article numbers already followed by a unit, e.g. 30 zile from the law).
    Values are compared after rounding to the shown decimals (2 -> bani, 1 -> one decimal, ...)."""
    vals = model if isinstance(model, set) else valori_model(model)
    vals = set(vals) | {float(x) for x in ignora}
    out = []
    for tok, v, zec, unit in numere_in_text(text):
        if not (zec or unit):
            continue
        nz = len(tok.split(",")[1]) if "," in tok and zec else 0
        ok = any(abs(abs(rotunjire(x, nz)) - v) <= toleranta + 1e-9 for x in vals)  # sign is not parsed
        if not ok:
            out.append(tok)
    return out


def lipsa_de(text):
    """DOOM check on prose: '1.800 lei' (needs de), '103 de persoane' (must not have de)."""
    text = text.replace(" ", " ")
    subst = r"(lei|euro|bani|persoane|apartamente|zile|luni|ani|ore|metri|locatari|proprietari|oferte|contracte)"
    gresit = []
    for m in re.finditer(r"(?<![\w.,/])(\d{1,3}(?:\.\d{3})+|\d+)(?![,.]\d)\s+(de\s+)?" + subst + r"\b", text):
        n = int(m.group(1).replace(".", ""))
        are_de = bool(m.group(2))
        prev = text[max(0, m.start() - 5):m.start()].lower()
        if re.search(r"(nr|art|alin|pct|ap|lit)\.\s*$", prev):
            continue
        if necesita_de(n) and not are_de:
            gresit.append(f"{m.group(0)!r} -> {m.group(1)} de {m.group(3)}")
        elif are_de and not necesita_de(n):
            gresit.append(f"{m.group(0)!r} -> {m.group(1)} {m.group(3)} (fără de)")
    for m in re.finditer(r"\b(douăzeci|treizeci|patruzeci|cincizeci|șaizeci|șaptezeci|optzeci|nouăzeci|"
                         r"sute|mii|milioane)\s+" + subst + r"\b", text.lower()):
        gresit.append(f"{m.group(0)!r} -> … de {m.group(2)}")
    return gresit


if __name__ == "__main__":
    import sys
    sys.stdout.reconfigure(encoding="utf-8")
    assert rotunjire(5964.5, 0) == 5965 and rotunjire(2.675) == 2.68 and rotunjire(-1.005) == -1.01
    assert suma([113929.88, 5465.40]) == 119395.28 and rotunjire(12.344999999999999) == 12.35
    assert ro(119395.28) == "119.395,28" and ro(5964.5, 0) == "5.965"
    p, dif = repartizeaza(100, [1, 1, 1])
    assert p == [33.33] * 3 and abs(dif - 0.01) < 1e-9
    p, dif = repartizeaza(100, [1, 1, 1], "rest_maxim")
    assert sum(p) == 100 and dif == 0
    assert cu_de(1800, "lei") == "1.800 de lei" and cu_de(103, "persoane") == "103 persoane"
    assert cu_de(2788, "mc") == "2.788 mc"
    assert cu_de(16.21, "lei") == "16,21 lei" and cu_de(120, "lei").endswith("de lei")
    assert in_litere(2, "f") == "două" and in_litere(6, "n") == "șase" and in_litere(1, "n") == "un"
    assert suma_in_litere(1800) == "o mie opt sute de lei", suma_in_litere(1800)
    assert suma_in_litere(101) == "o sută unu lei", suma_in_litere(101)
    assert suma_in_litere(2400) == "două mii patru sute de lei"
    assert suma_in_litere(22000) == "douăzeci și două de mii de lei", suma_in_litere(22000)
    assert lipsa_de("cotizația de 1.800 lei pe lună") and lipsa_de("au locuit 103 de persoane")
    assert not lipsa_de("1.800 de lei, 103 persoane, 16,21 lei, nr. 37 lei")
    g = NumereDocumente(seed=3)
    g.serie("a", "AN{n:07d}", start=1048221, pas_mediu=2600)
    nums = [g.urmator("a") for _ in range(10)]
    assert not progresie_aritmetica(g.emise("a")) and g.emise("a") == sorted(g.emise("a"))
    assert reconciliaza_text("total 119.395,25 lei, adică 40 de apartamente", {119395.28, 40}) == ["119.395,25"]
    assert reconciliaza_text("șase apartamente", {5}) == ["șase apartamente"]
    print("cifre_ro OK", nums[:3])
