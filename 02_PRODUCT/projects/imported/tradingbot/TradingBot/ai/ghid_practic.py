"""
Trading Bot — Ghid Practic
Genereaza analize educationale on-demand pentru toate activele din watchlist.
Explica DE CE s-a miscat, CE pattern exista, CE oportunitate ofera, CE lectie practica se extrage.
"""
import logging
from dataclasses import dataclass, field
from typing import List, Dict
from datetime import datetime

log = logging.getLogger("tradingbot.ghid")


@dataclass
class GhidEntry:
    """O intrare in ghid pentru un singur activ."""
    name: str
    symbol: str
    price: float
    var_zi: float
    var_sapt: float
    semnal: str
    score: float
    confidence: float
    rsi: float
    adx: float
    trend: str
    momentum: str
    volatility: str
    regime: str
    # Explicatii generate
    de_ce_s_a_miscat: str = ""
    oportunitate: str = ""
    pattern_detectat: str = ""
    lectia_zilei: str = ""
    warning: str = ""


# ══════════════════════════════════════════════════════════════════
# GHID PERMANENT DE BUNE PRACTICI
# ══════════════════════════════════════════════════════════════════

GHID_BUNE_PRACTICI = [
    {
        "titlu": "1. MANAGEMENTUL RISCULUI — REGULA DE AUR",
        "sectiuni": [
            ("De ce e cel mai important lucru", 
             "Poți avea dreptate in 70% din tranzacții și tot să pierzi bani dacă "
             "nu controlezi riscul. Un singur trade prost fără stop loss poate șterge "
             "câștigurile a 20 de trade-uri bune.\n\n"
             "REGULI CONCRETE:\n"
             "• Nu risca mai mult de 1-2% din capital pe un singur trade\n"
             "• Pune ÎNTOTDEAUNA stop loss ÎNAINTE de a intra în poziție\n"
             "• Risk/Reward minim 1:2 — dacă riști 100$, țintește minim 200$\n"
             "• Nu muta stop loss-ul în direcția pierderii — NICIODATĂ\n"
             "• Dacă pierzi 3 trade-uri la rând, oprește-te. Revino mâine"),
            ("Cum calculezi dimensiunea pozitiei",
             "Formula: Pozitie = (Capital × Risc%) / (Entry - StopLoss)\n\n"
             "Exemplu: Capital 10.000$, Risc 2%, Entry 100$, SL 95$\n"
             "Pozitie = (10.000 × 0.02) / (100 - 95) = 200$ / 5$ = 40 actiuni\n"
             "Deci cumperi maxim 40 actiuni. Daca pretul ajunge la 95$, pierzi exact 200$ (2%).\n\n"
             "IMPORTANT: Aceasta formula e integrata in Trading Bot — campul\n"
             "'Pozitie recomandata' din AI Panel face exact acest calcul automat."),
        ]
    },
    {
        "titlu": "2. CITIREA GRAFICELOR — CANDLESTICK BASICS",
        "sectiuni": [
            ("Ce e un candlestick",
             "Fiecare candlestick (lumanare) arata 4 preturi: Open, High, Low, Close.\n\n"
             "• Corpul (dreptunghiul) = distanta intre Open si Close\n"
             "• Fitilul de sus = High (pretul maxim atins)\n"
             "• Fitilul de jos = Low (pretul minim atins)\n"
             "• VERDE/ALB = Close > Open (pretul a crescut)\n"
             "• ROSU/NEGRU = Close < Open (pretul a scazut)\n\n"
             "Corp mare = miscare puternica, decisiva\n"
             "Corp mic = indecizii, incertitudine\n"
             "Fitil lung = rejectie de pret (cineva a impins pretul dar a fost respins)"),
            ("Pattern-uri cheie",
             "BULLISH (semnale de crestere):\n"
             "• Hammer — corp mic sus, fitil lung jos → cumparatorii au respins scaderea\n"
             "• Bullish Engulfing — lumanare verde mare 'inghite' rosie anterioara → putere de cumparare\n"
             "• Morning Star — 3 lumanari: rosie mare, corp mic, verde mare → inversare de fond\n"
             "• Three White Soldiers — 3 verzi consecutive crescande → trend bullish confirmat\n\n"
             "BEARISH (semnale de scadere):\n"
             "• Shooting Star — corp mic jos, fitil lung sus → vanzatorii au respins cresterea\n"
             "• Bearish Engulfing — lumanare rosie mare 'inghite' verde anterioara → presiune de vanzare\n"
             "• Evening Star — 3 lumanari: verde mare, corp mic, rosie mare → inversare de top\n"
             "• Three Black Crows — 3 rosii consecutive descrescande → trend bearish confirmat\n\n"
             "NEUTRU:\n"
             "• Doji — Open ≈ Close, fitiluri egale → indecizii totala, asteapta confirmare"),
        ]
    },
    {
        "titlu": "3. INDICATORI TEHNICI — CE INSEAMNA FIECARE",
        "sectiuni": [
            ("RSI (Relative Strength Index)",
             "Masoara VITEZA si MAGNITUDINEA miscarilor de pret. Scara 0-100.\n\n"
             "• RSI > 70 = SUPRACUMPARARE → pretul a crescut prea repede, risc de corectie\n"
             "  NU inseamna 'vinde acum' — poate sta supracumparat saptamani in trend puternic\n"
             "  Inseamna: fii ATENT, nu cumpara aici, asteapta pullback\n\n"
             "• RSI < 30 = SUPRAVANZARE → pretul a scazut prea repede, potential bounce\n"
             "  NU inseamna 'cumpara acum' — poate scadea si mai mult\n"
             "  Inseamna: cauta CONFIRMARI de inversare (pattern bullish + volum)\n\n"
             "• RSI 40-60 = zona neutra, nu da semnal clar\n"
             "• DIVERGENTA RSI: pretul face high nou dar RSI nu → semnal de slabiciune"),
            ("MACD (Moving Average Convergence Divergence)",
             "Arata DIRECTIA si PUTEREA trendului. Are 3 componente:\n"
             "• Linia MACD (albastra) = EMA12 - EMA26\n"
             "• Linia Signal (portocalie) = EMA9 a MACD\n"
             "• Histograma = diferenta intre cele doua linii\n\n"
             "SEMNALE:\n"
             "• MACD trece PESTE Signal (crossover bullish) → potential de cumparare\n"
             "• MACD trece SUB Signal (crossover bearish) → potential de vanzare\n"
             "• Histograma creste = momentum creste\n"
             "• Histograma scade = momentum scade\n\n"
             "CEL MAI BUN SEMNAL: crossover confirmat de volum crescut + trend ADX > 25"),
            ("Bollinger Bands",
             "Arata VOLATILITATEA si zonele de pret extreme. 3 linii:\n"
             "• Banda superioara = SMA20 + 2×deviatie standard\n"
             "• Linia mijloc = SMA20\n"
             "• Banda inferioara = SMA20 - 2×deviatie standard\n\n"
             "INTERPRETARE:\n"
             "• Pret la banda superioara = potential supracumparare (nu neaparat vinde)\n"
             "• Pret la banda inferioara = potential supravanzare (nu neaparat cumpara)\n"
             "• Benzi INGUSTE (squeeze) = volatilitate scazuta → BREAKOUT IMINENT\n"
             "• Benzi LARGI = volatilitate ridicata, miscari mari\n\n"
             "STRATEGIE: cand benzile se strang foarte mult, pregateste-te —\n"
             "cand pretul iese din squeeze, urmareste directia breakout-ului"),
            ("ADX (Average Directional Index)",
             "Masoara PUTEREA trendului, nu directia. Scara 0-100.\n\n"
             "• ADX < 15 = NU exista trend, piata laterala → evita trend-following\n"
             "• ADX 15-25 = trend slab, posibil inceput\n"
             "• ADX 25-40 = trend puternic → urmeaza trendul\n"
             "• ADX > 40 = trend foarte puternic → fii atent la epuizare\n\n"
             "+DI > -DI = trend bullish\n"
             "-DI > +DI = trend bearish\n\n"
             "REGULA: Nu intra in trade de trend daca ADX < 20"),
            ("Ichimoku Cloud",
             "Cel mai complet indicator — arata trend, support, resistance, momentum.\n\n"
             "• Pret PESTE cloud = BULLISH\n"
             "• Pret SUB cloud = BEARISH\n"
             "• Pret IN cloud = INDECIS, nu tranzactiona\n"
             "• Cloud verde (Span A > Span B) = bullish\n"
             "• Cloud rosu (Span A < Span B) = bearish\n"
             "• Conversion Line > Base Line = momentum bullish\n\n"
             "REGULA SIMPLA: tranzactioneaza doar in directia cloud-ului"),
        ]
    },
    {
        "titlu": "4. SUPORT SI REZISTENTA",
        "sectiuni": [
            ("Ce sunt si de ce conteaza",
             "SUPORTUL = nivel de pret unde cumparatorii intervin repetat → pretul tinde sa nu scada sub el\n"
             "REZISTENTA = nivel de pret unde vanzatorii intervin repetat → pretul tinde sa nu creasca peste el\n\n"
             "Cu cat un nivel e testat de mai multe ori FARA sa fie spart, cu atat e mai puternic.\n"
             "Cand un suport e spart, devine rezistenta (si invers).\n\n"
             "APLICARE PRACTICA:\n"
             "• Cumpara APROAPE de suport (cu SL sub suport)\n"
             "• Vinde APROAPE de rezistenta (cu SL peste rezistenta)\n"
             "• Nu cumpara in mijlocul range-ului — risc/reward slab\n"
             "• Breakout peste rezistenta + volum mare = semnal de cumparare\n"
             "• Breakdown sub suport + volum mare = semnal de vanzare"),
        ]
    },
    {
        "titlu": "5. REGIMUL PIETEI — ADAPTEAZA STRATEGIA",
        "sectiuni": [
            ("Cele 4 regimuri",
             "1. TRENDING (ADX > 25, miscare directionala clara)\n"
             "   → Foloseste: EMA crossover, MACD, trend-following\n"
             "   → NU folosi: mean-reversion, counter-trend\n\n"
             "2. RANGING / LATERAL (ADX < 20, pret intre suport si rezistenta)\n"
             "   → Foloseste: RSI la extremitati, Bollinger bounce, suport/rezistenta\n"
             "   → NU folosi: trend-following, breakout fals\n\n"
             "3. SQUEEZE / PRE-BREAKOUT (Bollinger inguste, ATR scazut)\n"
             "   → Pregateste-te: breakout iminent, nu intra inca\n"
             "   → Asteapta: directia breakout + confirmare volum\n\n"
             "4. TRANZITIE (schimbari de trend, volatilitate crescuta)\n"
             "   → Reduce dimensiunea pozitiilor\n"
             "   → Cauta: divergente RSI/MACD, pattern-uri de inversare"),
        ]
    },
    {
        "titlu": "6. PSIHOLOGIE — CEL MAI IMPORTANT CAPITOL",
        "sectiuni": [
            ("Greseli fatale si cum le eviti",
             "1. REVENGE TRADING — dupa o pierdere, intri imediat sa 'recuperezi'\n"
             "   → SOLUTIE: dupa 2 pierderi la rand, pauza 1 ora. Dupa 3, pauza 1 zi.\n\n"
             "2. FOMO (Fear Of Missing Out) — cumperi pentru ca 'toti cumpara'\n"
             "   → SOLUTIE: daca nu ai plan scris INAINTE, nu intri. Punct.\n\n"
             "3. OVERTRADING — faci 20 trade-uri pe zi fara setup clar\n"
             "   → SOLUTIE: maxim 3-5 trade-uri pe zi. Calitate > cantitate.\n\n"
             "4. NU pui STOP LOSS — 'o sa revina'\n"
             "   → SOLUTIE: SL obligatoriu. Fara exceptii. Pune-l INAINTE de entry.\n\n"
             "5. MUTI SL in directia pierderii — 'mai astept putin'\n"
             "   → SOLUTIE: odata pus, nu se muta decat in PROFIT (trailing stop).\n\n"
             "6. ALL-IN pe un singur trade\n"
             "   → SOLUTIE: maxim 2% risc per trade. NICIODATĂ mai mult de 5%."),
            ("Reguli de disciplina",
             "• Tine un jurnal de trading — noteaza FIECARE trade: de ce ai intrat, "
             "de ce ai iesit, ce ai simtit, ce ai invatat\n"
             "• Revizuieste jurnalul saptamanal — cauta PATTERN-URI in greseli\n"
             "• Seteaza obiective REALISTE: 1-3% pe luna e EXCELENT\n"
             "• Nu te compara cu altii pe social media — cei care se lauda sunt minoritatea\n"
             "• Accepta ca vei pierde — 40-50% pierderi e NORMAL chiar si la profesioniști\n"
             "• Profitul vine din CONSISTENCY, nu din trade-uri spectaculoase"),
        ]
    },
    {
        "titlu": "7. CHECKLIST INAINTE DE FIECARE TRADE",
        "sectiuni": [
            ("Verifica TOATE punctele",
             "□ Am identificat trendul pe timeframe-ul mai mare? (1D sau 1W)\n"
             "□ ADX > 20? (exista trend sau e piata laterala?)\n"
             "□ Am un setup clar? (pattern + indicator confirmat)\n"
             "□ Am setat ENTRY, STOP LOSS si TAKE PROFIT inainte?\n"
             "□ Risk/Reward e minim 1:2?\n"
             "□ Dimensiunea pozitiei respecta regula 1-2% risc?\n"
             "□ Volumul confirma miscarea?\n"
             "□ NU sunt in stare emotionala (furios, euforic, obosit)?\n"
             "□ NU e revenge trading dupa o pierdere?\n"
             "□ Am verificat calendarul economic pentru stiri importante?\n\n"
             "Daca NU trece TOATE punctele → NU INTRA IN TRADE"),
        ]
    },
]


# ══════════════════════════════════════════════════════════════════
# GENERATOR EXPLICATII PER ACTIV
# ══════════════════════════════════════════════════════════════════

def explica_miscare(entry: GhidEntry) -> str:
    """Explica DE CE s-a miscat activul."""
    lines = []
    v = entry.var_zi

    if abs(v) < 0.3:
        lines.append(f"Miscare minima azi ({v:+.2f}%). Piata e calma pe {entry.name}.")
    elif v > 3:
        lines.append(f"CRESTERE PUTERNICA +{v:.2f}% azi!")
        lines.append("Posibile cauze: stiri pozitive, breakout tehnic, sau flux institutional de cumparare.")
    elif v > 1:
        lines.append(f"Crestere moderata +{v:.2f}%.")
        lines.append("Momentum pozitiv sustinut de cumparatori.")
    elif v < -3:
        lines.append(f"SCADERE PUTERNICA {v:.2f}% azi!")
        lines.append("Posibile cauze: stiri negative, breakdown tehnic, vanzare in panica sau profit-taking.")
    elif v < -1:
        lines.append(f"Scadere moderata {v:.2f}%.")
        lines.append("Presiune de vanzare, posibil corectie normala dupa o crestere.")
    else:
        lines.append(f"Miscare mica ({v:+.2f}%). Consolidare sau asteptare catalizator.")

    # RSI context
    rsi = entry.rsi
    if rsi > 75:
        lines.append(f"\nRSI la {rsi:.1f} — ZONA DE SUPRACUMPARARE. Atentie la corectie.")
    elif rsi < 25:
        lines.append(f"\nRSI la {rsi:.1f} — ZONA DE SUPRAVANZARE. Potential bounce.")
    elif rsi > 60:
        lines.append(f"\nRSI la {rsi:.1f} — momentum bullish, dar nu in extrema.")
    elif rsi < 40:
        lines.append(f"\nRSI la {rsi:.1f} — momentum bearish, dar nu in extrema.")

    # Regime
    lines.append(f"\nRegim piata: {entry.regime}")
    if entry.regime == "SQUEEZE (pre-breakout)":
        lines.append("Volatilitate foarte scazuta — breakout iminent. Urmareste directia!")
    elif entry.regime == "TRENDING":
        lines.append("Trend clar in desfasurare — urmeaza directia, nu contra-tranzactiona.")

    return "\n".join(lines)


def explica_oportunitate(entry: GhidEntry) -> str:
    """Explica CE oportunitate de trading exista."""
    sig = entry.semnal
    score = entry.score
    conf = entry.confidence

    if sig == "BUY":
        lines = [
            f"OPORTUNITATE DE CUMPARARE — Scor AI {score:.0f}/100, Confidenta {conf:.0f}%",
            f"Trend: {entry.trend}",
            f"Momentum: {entry.momentum}",
            "",
            "CE TREBUIE SA FACI:",
            f"1. Verifica ca ADX > 20 (acum: {entry.adx:.1f})",
            f"2. Confirma ca RSI nu e in extrema (acum: {entry.rsi:.1f})",
            "3. Cauta confirmare pe volum (volum > media)",
            "4. Seteaza SL sub ultimul suport",
            "5. TP la urmatoarea rezistenta",
        ]
        if score >= 80:
            lines.append("\nSETUP PUTERNIC — toate filtrele aliniate.")
        elif score >= 65:
            lines.append("\nSetup decent dar nu perfect. Reduce dimensiunea pozitiei.")
    elif sig == "SELL":
        lines = [
            f"OPORTUNITATE DE VANZARE — Scor AI {score:.0f}/100, Confidenta {conf:.0f}%",
            f"Trend: {entry.trend}",
            f"Momentum: {entry.momentum}",
            "",
            "CE TREBUIE SA FACI:",
            f"1. Confirma trend bearish (ADX: {entry.adx:.1f})",
            f"2. RSI nu e in supravanzare (acum: {entry.rsi:.1f})",
            "3. Cauta pattern bearish pe candlestick",
            "4. SL peste ultimul swing high",
            "5. TP la urmatorul suport",
        ]
    else:
        lines = [
            f"HOLD / ASTEAPTA — Scor AI {score:.0f}/100",
            "",
            "NU exista setup clar acum. Piata e indecisa.",
            "CE TREBUIE SA FACI:",
            "1. NU intra in trade — lipsa de setup = risc inutil",
            "2. Monitorizeaza pentru formarea unui pattern",
            "3. Asteapta breakout din consolidare",
            "4. Verifica din nou la urmatoarea lumanare",
        ]

    return "\n".join(lines)


def explica_pattern(entry: GhidEntry) -> str:
    """Explica pattern-ul grafic detectat."""
    lines = [f"Trend curent: {entry.trend}"]
    lines.append(f"Volatilitate: {entry.volatility}")

    if "BULLISH PUTERNIC" in entry.trend:
        lines.append("\nPretul e PESTE toate EMA-urile (20, 50, 200) — configuratie bullish ideala.")
        lines.append("Golden Cross posibil activ (EMA50 > EMA200).")
    elif "BEARISH PUTERNIC" in entry.trend:
        lines.append("\nPretul e SUB toate EMA-urile — configuratie bearish clara.")
        lines.append("Death Cross posibil activ (EMA50 < EMA200).")
    elif "NEUTRAL" in entry.trend:
        lines.append("\nPretul oscileaza in jurul EMA-urilor — lipsa de directie.")
        lines.append("Asteapta un breakout clar inainte de a actiona.")

    if "COMPRESIE" in entry.volatility:
        lines.append("\nBollinger Bands se strang — SQUEEZE activ!")
        lines.append("Breakout iminent. Pregateste ordine in ambele directii.")

    return "\n".join(lines)


def lectia_zilei(entry: GhidEntry) -> str:
    """Genereaza o lectie practica bazata pe situatia actuala."""
    rsi = entry.rsi
    adx = entry.adx
    var = entry.var_zi

    lessons = []

    if abs(var) > 5:
        lessons.append(
            "LECTIE: Miscarile mari (>5%) sunt tentante dar periculoase. "
            "Nu alerga dupa tren. Daca ai ratat intrarea, asteapta pullback. "
            "Cel mai prost moment sa cumperi e dupa o crestere de 5%+ — "
            "riscul de corectie e maxim."
        )
    elif rsi > 80:
        lessons.append(
            "LECTIE: RSI > 80 nu inseamna 'vinde imediat' — in trenduri puternice, "
            "RSI poate sta supracumparat saptamani. Dar inseamna: NU cumpara aici. "
            "Asteapta RSI sa coboare sub 70 si apoi sa revina — asta e pullback-ul."
        )
    elif rsi < 20:
        lessons.append(
            "LECTIE: RSI < 20 sugereaza supravanzare extrema. Istoric, multe "
            "bounce-uri apar din aceasta zona. Dar NU cumpara orbeste — "
            "cauta CONFIRMARE: pattern bullish + volum crescut + divergenta RSI."
        )
    elif adx < 15:
        lessons.append(
            "LECTIE: ADX < 15 inseamna ca NU exista trend. In aceasta situatie, "
            "strategiile de trend-following (EMA crossover, MACD) vor da semnale FALSE. "
            "Foloseste in schimb RSI la extremitati si suport/rezistenta."
        )
    elif "SQUEEZE" in entry.regime:
        lessons.append(
            "LECTIE: Cand Bollinger Bands se strang (squeeze), piata se pregateste "
            "de o miscare mare. NU ghici directia — asteapta breakout-ul si "
            "urmareste-l. Pune ordine pendinte pe ambele parti daca vrei."
        )
    else:
        lessons.append(
            "LECTIE: Zi normala de piata. Respecta planul, respecta regulile de risc, "
            "nu forta trade-uri cand nu exista setup-uri clare. "
            "Zilele profitabile vin singure cand esti disciplinat."
        )

    return "\n".join(lessons)


def generate_ghid_entries(all_data: Dict[str, dict]) -> List[GhidEntry]:
    """
    Transforma datele din advisor reports in GhidEntry cu explicatii.
    all_data: {symbol: AdviceReport}
    """
    entries = []
    for symbol, report in all_data.items():
        e = GhidEntry(
            name=symbol,
            symbol=symbol,
            price=report.price,
            var_zi=0,  # calculated from df if available
            var_sapt=0,
            semnal=report.signal.direction,
            score=report.score,
            confidence=report.signal.confidence,
            rsi=report.key_indicators.get("RSI", 50),
            adx=report.key_indicators.get("ADX", 20),
            trend=report.trend,
            momentum=report.momentum,
            volatility=report.volatility,
            regime=report.market_regime,
        )
        e.de_ce_s_a_miscat = explica_miscare(e)
        e.oportunitate = explica_oportunitate(e)
        e.pattern_detectat = explica_pattern(e)
        e.lectia_zilei = lectia_zilei(e)
        if report.warnings:
            e.warning = "\n".join(report.warnings)
        entries.append(e)
    return entries


def get_bune_practici() -> list:
    """Returneaza ghidul permanent de bune practici."""
    return GHID_BUNE_PRACTICI
