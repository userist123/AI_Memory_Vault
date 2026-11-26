"""Extrage textul unui articol dintr-o copie locală .txt făcută cu captura_act.py.

Un titlu „Articolul N” e considerat titlu real de articol numai dacă:
 - nu e linie de cuprins (rândul următor nu e alt titlu scurt: Articolul/Capitolul/Titlul/Secțiunea/Partea/Cartea);
 - nu e o trimitere din text (rândul următor nu începe cu punctuație, iar rândul anterior nu se termină cu
   „din”, „de”, „la”, „și”, „prevăzut”, „potrivit”, „conform”).
Corpul articolului se întinde până la următorul titlu real de articol.
Dacă articolul are mai multe apariții reale (anexe, alte acte incluse în pagină), iese cu cod 3 și listează contextul;
alegi cu --aparitie k.

Coduri de ieșire: 0 = OK · 1 = negăsit · 3 = ambiguu · 4 = --aparitie în afara intervalului.
Utilizare: python -I -X utf8 articol.py cpp.txt 168 [--aparitie 2] [--caractere 20000]
"""
import argparse, re, sys
from pathlib import Path

TITLU = re.compile(
    r"^(Articolul \S+$|(Capitolul|Titlul|Secţiunea|Secțiunea|Partea|Cartea|Anexa)\s+([IVXLC]+\b|\d|a\s|[A-ZĂÎÂȘȚŞŢ]{3,}))")
LEGATURI = ("din", "de", "la", "și", "şi", "prevăzut", "prevăzute", "potrivit", "conform", "cu", "în")


def _linie_urm(t, poz):
    rest = t[poz:poz + 400].split("\n")
    return next((l.strip() for l in rest if l.strip()), "")


def _linie_ant(t, poz):
    inainte = t[max(0, poz - 300):poz].rstrip("\n").split("\n")
    return inainte[-1].strip() if inainte else ""


def este_titlu(t, m):
    if m.start() > 0 and t[m.start() - 1] != "\n":
        return False
    urm = _linie_urm(t, m.end())
    if not urm or re.match(r"^[,.;:)\]]", urm):
        return False
    if TITLU.match(urm) and len(urm) < 40:  # cuprins
        return False
    ant = _linie_ant(t, m.start())
    if ant and ant.split()[-1].lower().strip(",") in LEGATURI:
        return False
    return True


def titluri(t):
    return [m for m in re.finditer(r"Articolul (\S+)\n", t) if este_titlu(t, m)]


def aparitii(t, nr):
    return [m.start() for m in titluri(t) if m.group(1) == nr]


def extrage(t, poz, caractere=20000):
    urm = next((m.start() for m in titluri(t) if m.start() > poz), None)
    sf = min(urm if urm else len(t), poz + caractere)
    return t[poz:sf]


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    ap = argparse.ArgumentParser()
    ap.add_argument("fisier")
    ap.add_argument("nr", help="ex. 168, 474^1, 1.131")
    ap.add_argument("--aparitie", type=int)
    ap.add_argument("--caractere", type=int, default=20000)
    a = ap.parse_args()
    t = Path(a.fisier).read_text(encoding="utf-8")
    poz = aparitii(t, a.nr)
    if not poz:
        print(f"NEGĂSIT: Articolul {a.nr} în {a.fisier}")
        sys.exit(1)
    if a.aparitie is not None:
        if not 1 <= a.aparitie <= len(poz):
            print(f"--aparitie trebuie să fie între 1 și {len(poz)}")
            sys.exit(4)
        print(extrage(t, poz[a.aparitie - 1], a.caractere))
        return
    if len(poz) > 1:
        print(f"AMBIGUU: Articolul {a.nr} apare de {len(poz)} ori. Alege cu --aparitie k:")
        for k, p in enumerate(poz, 1):
            ctx = re.sub(r"\s+", " ", t[max(0, p - 200):p])[-120:]
            dupa = re.sub(r"\s+", " ", t[p:p + 120])  # fără backslash în f-string: compatibil Python < 3.12
            print(f"  {k}) …{ctx} ‖ {dupa}")
        sys.exit(3)
    print(extrage(t, poz[0], a.caractere))


if __name__ == "__main__":
    main()
