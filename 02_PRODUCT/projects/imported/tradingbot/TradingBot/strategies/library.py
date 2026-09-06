"""
Trading Bot — Biblioteca de Strategii
12 strategii clasice built-in, editabile, cu reguli clare.
Fiecare strategie are: nume, descriere, conditii entry/exit, reguli de risc, cand sa o folosesti si cand NU.
"""
from dataclasses import dataclass, field
from typing import List


@dataclass
class StrategyRule:
    name: str
    description: str
    category: str           # "trend", "momentum", "mean_reversion", "breakout", "scalping", "swing"
    timeframes: List[str]   # timeframes recomandate
    # Entry conditions
    entry_rules: List[str]
    exit_rules: List[str]
    # Risk
    stop_loss_rule: str
    take_profit_rule: str
    risk_per_trade: str
    risk_reward_min: float
    # When to use / avoid
    when_to_use: str
    when_to_avoid: str
    # Indicator settings
    indicators_needed: List[str]
    # Notes
    notes: str = ""


# ══════════════════════════════════════════════════════════════════
# 12 STRATEGII BUILT-IN
# ══════════════════════════════════════════════════════════════════

STRATEGY_LIBRARY: List[StrategyRule] = [

    # ── 1. EMA CROSSOVER ────────────────────────────────────────
    StrategyRule(
        name="EMA Crossover (Golden/Death Cross)",
        description=(
            "Cea mai clasica strategie de trend-following. "
            "Cumpara cand EMA rapida trece PESTE EMA lenta, "
            "vinde cand trece SUB. Simpla, eficienta in trenduri clare."
        ),
        category="trend",
        timeframes=["1D", "4h", "1h"],
        entry_rules=[
            "BUY: EMA 20 trece PESTE EMA 50 (sau EMA 9 peste EMA 21 pentru mai rapid)",
            "SELL: EMA 20 trece SUB EMA 50",
            "CONFIRMARE: ADX > 25 (trend exista)",
            "CONFIRMARE: Volumul creste la momentul crossover-ului",
            "FILTRU: Pretul e peste EMA 200 pentru BUY (trend mare bullish)",
        ],
        exit_rules=[
            "Crossover invers (EMA rapida trece sub cea lenta)",
            "Sau: trailing stop de 2× ATR",
            "Sau: RSI > 80 (supracumparare extrema)",
        ],
        stop_loss_rule="Sub EMA lenta (EMA 50) sau sub ultimul swing low",
        take_profit_rule="Trailing stop 2× ATR sau la urmatoarea rezistenta majora",
        risk_per_trade="1-2% din capital",
        risk_reward_min=2.0,
        when_to_use="Piata in TREND clar (ADX > 25). Functioneaza excelent pe trenduri lungi.",
        when_to_avoid="Piata LATERALA (ADX < 20) — va da multe semnale FALSE (whipsaw).",
        indicators_needed=["EMA 9", "EMA 20", "EMA 50", "EMA 200", "ADX", "Volum"],
    ),

    # ── 2. RSI DIVERGENCE ───────────────────────────────────────
    StrategyRule(
        name="RSI Divergence",
        description=(
            "Detecteaza slabiciunea trendului inainte ca pretul sa se inverseze. "
            "Cand pretul face un nou high dar RSI nu confirma — divergenta bearish. "
            "Cand pretul face un nou low dar RSI nu confirma — divergenta bullish."
        ),
        category="momentum",
        timeframes=["1D", "4h", "1h"],
        entry_rules=[
            "BULLISH: Pretul face LOW mai jos, RSI face LOW mai sus → divergenta bullish",
            "BEARISH: Pretul face HIGH mai sus, RSI face HIGH mai jos → divergenta bearish",
            "CONFIRMARE: Pattern candlestick de inversare (hammer, engulfing)",
            "CONFIRMARE: Divergenta apare in zona RSI extrema (<30 sau >70)",
        ],
        exit_rules=[
            "Take profit la rezistenta/suport anterior",
            "RSI revine in zona neutra (40-60)",
            "Sau: trailing stop",
        ],
        stop_loss_rule="Sub ultimul low (bullish) sau peste ultimul high (bearish)",
        take_profit_rule="La suport/rezistenta anterioara sau 2-3× ATR",
        risk_per_trade="1-2%",
        risk_reward_min=2.0,
        when_to_use="La sfarsitul unui trend extins. Functioneaza bine pe trenduri epuizate.",
        when_to_avoid="In trenduri puternice noi — divergenta poate persista mult timp.",
        indicators_needed=["RSI(14)", "Suport/Rezistenta"],
    ),

    # ── 3. BOLLINGER SQUEEZE BREAKOUT ───────────────────────────
    StrategyRule(
        name="Bollinger Squeeze Breakout",
        description=(
            "Cand Bollinger Bands se strang (bandwidth scade), volatilitatea e minima "
            "si un breakout mare e iminent. Intri in directia breakout-ului."
        ),
        category="breakout",
        timeframes=["1D", "4h", "1h"],
        entry_rules=[
            "DETECTEAZA SQUEEZE: BB Width < 0.03 (sau sub media pe 120 perioade)",
            "ASTEAPTA BREAKOUT: Pretul inchide PESTE banda superioara → BUY",
            "Sau: Pretul inchide SUB banda inferioara → SELL",
            "CONFIRMARE: Volum crescut la breakout (> 1.5× media)",
            "CONFIRMARE: ADX incepe sa creasca de la nivele joase",
        ],
        exit_rules=[
            "Cand pretul revine in interiorul benzilor",
            "Sau: trailing stop de 1.5× ATR",
            "Sau: BB Width se extinde si incepe sa se contracte din nou",
        ],
        stop_loss_rule="In interiorul benzilor, la SMA 20 sau la cealalta banda",
        take_profit_rule="2-3× distanta banda la breakout sau la S/R major",
        risk_per_trade="1-2%",
        risk_reward_min=2.5,
        when_to_use="Dupa perioade lungi de consolidare. Squeeze-ul precede miscarile mari.",
        when_to_avoid="Nu folosi daca squeeze-ul e prea scurt (<10 perioade). Asteapta compresie reala.",
        indicators_needed=["Bollinger Bands(20,2)", "BB Width", "ADX", "Volum"],
    ),

    # ── 4. MACD + RSI COMBO ─────────────────────────────────────
    StrategyRule(
        name="MACD + RSI Combo",
        description=(
            "Combina momentum (MACD) cu conditii de supracumparare/supravanzare (RSI). "
            "Intra doar cand ambii indicatori confirma."
        ),
        category="momentum",
        timeframes=["1D", "4h", "1h"],
        entry_rules=[
            "BUY: MACD crossover bullish (MACD > Signal) + RSI > 50 dar < 70",
            "SELL: MACD crossover bearish (MACD < Signal) + RSI < 50 dar > 30",
            "BONUS: Histograma MACD creste (momentum in crestere)",
            "FILTRU: Pretul e peste EMA 200 pentru BUY",
        ],
        exit_rules=[
            "MACD crossover invers",
            "RSI atinge extrema (>80 sau <20)",
            "Sau: trailing stop",
        ],
        stop_loss_rule="Sub/peste ultimul swing point + 1× ATR buffer",
        take_profit_rule="La rezistenta/suport sau cand RSI atinge extrema",
        risk_per_trade="1-2%",
        risk_reward_min=2.0,
        when_to_use="Confirmare dubla reduce semnalele false. Bun pe trenduri moderate.",
        when_to_avoid="Piata laterala stransa — ambii indicatori vor da semnale contradictorii.",
        indicators_needed=["MACD(12,26,9)", "RSI(14)", "EMA 200"],
    ),

    # ── 5. SUPORT/REZISTENTA BOUNCE ─────────────────────────────
    StrategyRule(
        name="Suport/Rezistenta Bounce",
        description=(
            "Cumpara la suport, vinde la rezistenta. Cea mai simpla strategie mean-reversion. "
            "Functioneaza excelent in piete laterale."
        ),
        category="mean_reversion",
        timeframes=["1D", "4h", "1h", "15m"],
        entry_rules=[
            "BUY: Pretul atinge suport testat de 2-3 ori + pattern bullish (hammer, engulfing)",
            "SELL: Pretul atinge rezistenta testata de 2-3 ori + pattern bearish",
            "CONFIRMARE: RSI in zona extrema (<35 pentru BUY, >65 pentru SELL)",
            "CONFIRMARE: Volum scade la atingerea nivelului (epuizare miscare)",
        ],
        exit_rules=[
            "La nivel opus (suport → rezistenta, rezistenta → suport)",
            "Sau: la SMA 20 (mijlocul range-ului)",
            "Sau: daca nivelul e spart (breakdown/breakout) → iesire imediata",
        ],
        stop_loss_rule="Sub suport (BUY) sau peste rezistenta (SELL) + buffer 0.5× ATR",
        take_profit_rule="La nivelul opus de S/R",
        risk_per_trade="1%",
        risk_reward_min=2.0,
        when_to_use="Piata LATERALA clara cu S/R bine definite. ADX < 25.",
        when_to_avoid="Piata in TREND puternic — nivelele vor fi sparte.",
        indicators_needed=["Suport/Rezistenta", "RSI", "Volum", "Pattern-uri candlestick"],
    ),

    # ── 6. ICHIMOKU TREND ───────────────────────────────────────
    StrategyRule(
        name="Ichimoku Cloud Trend",
        description=(
            "Foloseste cloud-ul Ichimoku ca filtru de trend si zona de S/R dinamic. "
            "Tranzactioneaza DOAR in directia cloud-ului."
        ),
        category="trend",
        timeframes=["1D", "4h"],
        entry_rules=[
            "BUY: Pretul e PESTE cloud + Conversion > Base + Cloud e verde (Span A > Span B)",
            "SELL: Pretul e SUB cloud + Conversion < Base + Cloud e rosu",
            "CONFIRMARE: Lagging Span e peste pretul de acum 26 perioade",
            "FILTRU: Nu intra cand pretul e IN cloud (zona de indecizii)",
        ],
        exit_rules=[
            "Pretul intra inapoi in cloud",
            "Conversion trece sub Base (pentru long)",
            "Sau: trailing stop sub cloud",
        ],
        stop_loss_rule="Sub cloud (pentru long) sau peste cloud (pentru short)",
        take_profit_rule="Trailing stop sau la rezistenta majora",
        risk_per_trade="1-2%",
        risk_reward_min=2.0,
        when_to_use="Trenduri pe timeframe mare (1D, 4h). Ichimoku e cel mai bun pe trenduri clare.",
        when_to_avoid="Piata laterala sau timeframe-uri mici (<1h) — prea mult zgomot.",
        indicators_needed=["Ichimoku Cloud complet", "Volum"],
    ),

    # ── 7. VWAP SCALPING ───────────────────────────────────────
    StrategyRule(
        name="VWAP Intraday",
        description=(
            "VWAP (Volume-Weighted Average Price) actioneaza ca magnet intraday. "
            "Cumpara sub VWAP, vinde peste VWAP. Folosita de institutionali."
        ),
        category="scalping",
        timeframes=["5m", "15m", "30m"],
        entry_rules=[
            "BUY: Pretul scade sub VWAP si revine peste el → long",
            "SELL: Pretul creste peste VWAP si revine sub el → short",
            "CONFIRMARE: Volum crescut la revenire",
            "FILTRU: Tranzactioneaza doar in primele 3-4 ore de piata (lichiditate maxima)",
        ],
        exit_rules=[
            "La deviatie standard 1 sau 2 fata de VWAP",
            "Sau: la sfarsitul zilei",
            "Sau: daca pretul depaseste 2× ATR intraday de la VWAP",
        ],
        stop_loss_rule="1× ATR de la entry sau sub/peste VWAP",
        take_profit_rule="1-2 deviatii standard de VWAP",
        risk_per_trade="0.5-1%",
        risk_reward_min=1.5,
        when_to_use="Intraday pe actiuni/indici lichizi. VWAP e irelevant overnight.",
        when_to_avoid="Pe crypto (nu are sesiune clara), pe actiuni illicchide, dupa ore.",
        indicators_needed=["VWAP", "Volum", "ATR"],
    ),

    # ── 8. BREAKOUT + RETEST ────────────────────────────────────
    StrategyRule(
        name="Breakout + Retest",
        description=(
            "Nu intri la breakout direct (multi sunt falsi). "
            "Astepti RETEST-ul nivelului spart si intri la confirmare."
        ),
        category="breakout",
        timeframes=["1D", "4h", "1h"],
        entry_rules=[
            "1. Identifica rezistenta/suport clar (testat de 3+ ori)",
            "2. Asteapta breakout CLAR cu volum mare (>1.5× media)",
            "3. Asteapta RETEST: pretul revine la nivelul spart",
            "4. BUY: Rezistenta sparta devine suport, pret bounceaza de pe ea",
            "5. SELL: Suport spart devine rezistenta, pret e respins",
            "CONFIRMARE: Pattern candlestick la retest + volum scazut la pullback",
        ],
        exit_rules=[
            "Daca pretul reintra sub/peste nivelul spart → iesire (breakout fals)",
            "Trailing stop la 2× ATR",
            "Sau: la urmatorul nivel S/R",
        ],
        stop_loss_rule="Sub nivelul spart (+ buffer 0.5× ATR)",
        take_profit_rule="La urmatorul S/R sau proiectia miscarii pre-breakout",
        risk_per_trade="1-2%",
        risk_reward_min=2.5,
        when_to_use="Dupa consolidari lungi, la breakout din range sau triunghi.",
        when_to_avoid="Breakout-uri fara volum — de obicei sunt false.",
        indicators_needed=["Suport/Rezistenta", "Volum", "ATR", "Pattern-uri"],
    ),

    # ── 9. MEAN REVERSION RSI ───────────────────────────────────
    StrategyRule(
        name="Mean Reversion RSI Extrem",
        description=(
            "Cumpara cand RSI atinge extrema de supravanzare, vinde la supracumparare. "
            "Counter-trend, dar cu rate de succes ridicate in piete laterale."
        ),
        category="mean_reversion",
        timeframes=["1D", "4h"],
        entry_rules=[
            "BUY: RSI < 25 + pattern bullish (hammer, doji dupa scadere)",
            "SELL: RSI > 75 + pattern bearish (shooting star dupa crestere)",
            "CONFIRMARE: ADX < 25 (piata NU e in trend puternic)",
            "CONFIRMARE: Pretul e la/aproape de suport (BUY) sau rezistenta (SELL)",
        ],
        exit_rules=[
            "RSI revine la 50 (zona neutra)",
            "Sau: la SMA 20",
            "Sau: take profit fix de 1.5-2× ATR",
        ],
        stop_loss_rule="Sub ultimul low (BUY) sau peste ultimul high (SELL) + ATR buffer",
        take_profit_rule="La SMA 20 sau cand RSI revine la 50",
        risk_per_trade="1%",
        risk_reward_min=2.0,
        when_to_use="Piata laterala / ranging, dupa miscari exagerate.",
        when_to_avoid="In trenduri puternice (ADX > 30) — RSI poate sta extrem saptamani.",
        indicators_needed=["RSI(14)", "ADX", "SMA 20", "Suport/Rezistenta"],
    ),

    # ── 10. SWING TRADING EMA 9/21 ──────────────────────────────
    StrategyRule(
        name="Swing Trading EMA 9/21",
        description=(
            "Strategie de swing pe termen mediu. EMA 9 si 21 pe daily chart. "
            "Simpla, eficienta, pozitii tinute 3-15 zile."
        ),
        category="swing",
        timeframes=["1D"],
        entry_rules=[
            "BUY: EMA 9 trece PESTE EMA 21 pe daily",
            "SELL: EMA 9 trece SUB EMA 21 pe daily",
            "FILTRU: Pretul e peste EMA 200 (trend mare bullish) pentru long",
            "CONFIRMARE: Volum crescut la crossover",
            "BONUS: RSI > 50 la crossover bullish",
        ],
        exit_rules=[
            "Crossover invers",
            "Sau: pretul inchide sub EMA 21 (pentru long) pe 2 zile consecutive",
            "Sau: RSI > 80",
        ],
        stop_loss_rule="Sub EMA 21 + 1× ATR buffer",
        take_profit_rule="Trailing stop la EMA 21 sau 3× ATR de la entry",
        risk_per_trade="2%",
        risk_reward_min=2.0,
        when_to_use="Pe actiuni si crypto cu trend clar pe daily. Ideal pentru part-time traders.",
        when_to_avoid="Piete foarte volatile cu gap-uri mari. Piete laterale.",
        indicators_needed=["EMA 9", "EMA 21", "EMA 200", "RSI", "Volum"],
    ),

    # ── 11. STOCHASTIC + ADX FILTER ─────────────────────────────
    StrategyRule(
        name="Stochastic cu Filtru ADX",
        description=(
            "Stochastic Oscillator pentru timing, ADX ca filtru de trend. "
            "Intra la crossover Stochastic doar cand exista trend confirmat."
        ),
        category="momentum",
        timeframes=["4h", "1h"],
        entry_rules=[
            "BUY: Stoch K trece PESTE D in zona < 20 (supravanzare)",
            "SELL: Stoch K trece SUB D in zona > 80 (supracumparare)",
            "FILTRU OBLIGATORIU: ADX > 25 (trend exista)",
            "DIRECTIE: +DI > -DI pentru BUY, -DI > +DI pentru SELL",
        ],
        exit_rules=[
            "Stochastic atinge zona opusa (>80 pentru long, <20 pentru short)",
            "Sau: crossover invers",
            "Sau: ADX scade sub 20 (trend se pierde)",
        ],
        stop_loss_rule="Sub ultimul swing low/high + 1× ATR",
        take_profit_rule="La extrema Stochastic opusa sau 2× ATR",
        risk_per_trade="1-2%",
        risk_reward_min=2.0,
        when_to_use="Piata in trend moderat. ADX filtreaza semnalele false din piata laterala.",
        when_to_avoid="ADX < 20 — nu folosi, va da semnale false.",
        indicators_needed=["Stochastic(14,3,3)", "ADX(14)", "+DI", "-DI"],
    ),

    # ── 12. MULTI-TIMEFRAME CONFIRMATION ────────────────────────
    StrategyRule(
        name="Multi-Timeframe Confirmation",
        description=(
            "Confirma semnalul pe 3 timeframe-uri: mare (trend), mediu (setup), mic (entry). "
            "Reduce semnalele false drastic."
        ),
        category="trend",
        timeframes=["1D + 4h + 1h", "4h + 1h + 15m"],
        entry_rules=[
            "1. TIMEFRAME MARE (1D): Identifica trendul — pret vs EMA 200, directie cloud",
            "2. TIMEFRAME MEDIU (4h): Cauta setup — RSI la extrema, MACD crossover",
            "3. TIMEFRAME MIC (1h): Entry precis — pattern candlestick, breakout minor",
            "REGULA: Tranzactioneaza DOAR in directia trendului de pe TF mare",
            "CONFIRMARE: Toate 3 TF-uri arata aceeasi directie",
        ],
        exit_rules=[
            "TF mediu da semnal de iesire (crossover invers, RSI extrema opusa)",
            "Sau: trailing stop pe TF mic",
            "NU iesi pe semnale de pe TF mic contra trendului mare",
        ],
        stop_loss_rule="Bazat pe structura TF mediu (sub swing low/high)",
        take_profit_rule="La S/R de pe TF mare sau trailing stop pe TF mediu",
        risk_per_trade="1-2%",
        risk_reward_min=3.0,
        when_to_use="Oricand. Cea mai sigura abordare. Reduce semnalele false cu 60-70%.",
        when_to_avoid="Daca nu ai rabdare sa astepti alinierea tuturor TF-urilor.",
        indicators_needed=["EMA 200", "RSI", "MACD", "Ichimoku Cloud", "Volum", "Suport/Rezistenta"],
        notes=(
            "Aceasta e strategia recomandata pentru incepatori si avansati deopotriva. "
            "Rabdarea de a astepta alinierea tuturor timeframe-urilor e cheia."
        ),
    ),
]


def get_strategy_library() -> List[StrategyRule]:
    return STRATEGY_LIBRARY


def get_strategy_by_name(name: str) -> StrategyRule:
    for s in STRATEGY_LIBRARY:
        if s.name.lower() == name.lower():
            return s
    return None


def get_strategies_by_category(category: str) -> List[StrategyRule]:
    return [s for s in STRATEGY_LIBRARY if s.category == category]


def format_strategy_text(s: StrategyRule) -> str:
    """Formatteaza o strategie ca text readable."""
    lines = [
        f"═══ {s.name} ═══",
        f"Categorie: {s.category.upper()}",
        f"Timeframe: {', '.join(s.timeframes)}",
        "",
        f"DESCRIERE: {s.description}",
        "",
        "CONDITII ENTRY:",
    ]
    for r in s.entry_rules:
        lines.append(f"  • {r}")
    lines.append("\nCONDITII EXIT:")
    for r in s.exit_rules:
        lines.append(f"  • {r}")
    lines.append(f"\nSTOP LOSS: {s.stop_loss_rule}")
    lines.append(f"TAKE PROFIT: {s.take_profit_rule}")
    lines.append(f"RISC/TRADE: {s.risk_per_trade}")
    lines.append(f"R:R MINIM: 1:{s.risk_reward_min}")
    lines.append(f"\nFOLOSESTE CAND: {s.when_to_use}")
    lines.append(f"EVITA CAND: {s.when_to_avoid}")
    lines.append(f"\nINDICATORI: {', '.join(s.indicators_needed)}")
    if s.notes:
        lines.append(f"\nNOTE: {s.notes}")
    return "\n".join(lines)
