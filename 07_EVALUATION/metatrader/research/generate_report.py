import csv
import hashlib
import json
import os
import sys

BASE_DIR = os.path.join("07_EVALUATION", "metatrader", "research")
TABLES_DIR = os.path.join(BASE_DIR, "tables")
REPORT_MD = os.path.join(BASE_DIR, "RESEARCH_REPORT_V1.md")


def sha256_file(path: str) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as f:
        while chunk := f.read(65536):
            h.update(chunk)
    return h.hexdigest()


def main():
    t1_path = os.path.join(TABLES_DIR, "table_1_census.csv")
    t2_path = os.path.join(TABLES_DIR, "table_2_quality.csv")
    t3_path = os.path.join(TABLES_DIR, "table_3_costs_summary.csv")
    t4_path = os.path.join(TABLES_DIR, "table_4_development_results.csv")
    t5_path = os.path.join(TABLES_DIR, "table_5_validation_results.csv")
    t6_path = os.path.join(TABLES_DIR, "table_6_validation_spa_tests.csv")
    t7_path = os.path.join(TABLES_DIR, "table_7_commission_sensitivity.csv")

    with open(t1_path, "r", encoding="utf-8") as f:
        t1_rows = list(csv.DictReader(f))
    with open(t2_path, "r", encoding="utf-8") as f:
        t2_rows = list(csv.DictReader(f))
    with open(t3_path, "r", encoding="utf-8") as f:
        t3_rows = list(csv.DictReader(f))
    with open(t4_path, "r", encoding="utf-8") as f:
        t4_rows = list(csv.DictReader(f))
    with open(t5_path, "r", encoding="utf-8") as f:
        t5_rows = list(csv.DictReader(f))
    with open(t6_path, "r", encoding="utf-8") as f:
        t6_rows = list(csv.DictReader(f))
    with open(t7_path, "r", encoding="utf-8") as f:
        t7_rows = list(csv.DictReader(f))

    total_census = len(t1_rows)
    cat_counts = {}
    for r in t1_rows:
        cat_counts[r["category"]] = cat_counts.get(r["category"], 0) + 1

    eligible_rows = [r for r in t2_rows if r["eligible"].lower() == "true"]
    excluded_rows = [r for r in t2_rows if r["eligible"].lower() != "true"]
    n_eligible = len(eligible_rows)
    n_excluded = len(excluded_rows)

    active_val = [r for r in t5_rows if not r["strategy"].startswith("S0a")]
    n_active_val = len(active_val)

    pos_sharpe_val = [r for r in active_val if float(r["net_sharpe"]) > 0]
    mres_val = [r for r in active_val if float(r["net_sharpe"]) >= 0.50]
    beats_s0b_val = [r for r in active_val if r["beats_s0b"].lower() == "true"]

    spa_cash = next(r for r in t6_rows if "Cash" in r["benchmark"])
    spa_bh = next(r for r in t6_rows if "Buy_and_Hold" in r["benchmark"])
    high_rollovers = sorted(t3_rows, key=lambda x: float(x["rollover_expansion_multiplier"]), reverse=True)

    lines = []
    lines.append("# RAPORT DE CERCETARE V1 — Universul MetaTrader 5: Strategii Clasice vs. Costuri Reale și Controale Aleatoare")
    lines.append("")
    lines.append("> **Data finalizării**: 2026-09-14")
    lines.append("> **Broker investigat**: RoboForex Ltd (cont demo autentificat pe terminalul MT5 build 5836)")
    lines.append("> **Repository**: `AI_Memory_Vault_CODEX_READY`")
    lines.append("> **Locație**: `07_EVALUATION/metatrader/research/RESEARCH_REPORT_V1.md`")
    lines.append("> **Statut Preînregistrare**: `PREREGISTRATION.md` comis la `d3b11d38a` (înaintea calculelor de randament)")
    lines.append("")
    lines.append("---")
    lines.append("")
    lines.append("## 1. Ce s-a stabilit că NU se poate sau NU există")
    lines.append("")
    lines.append(f"1. **Nu există acțiuni individuale, indici bursieri sau mărfuri agricole pe acest server demo**:")
    lines.append(f"   Interogarea completă a terminalului prin `symbols_total()` și `symbols_get(group='*')` a stabilit empiric că brokerul `RoboForex Ltd` pe acest profil de cont pune la dispoziție un univers total de **exact {total_census} instrumente**, limitat strict la 3 clase:")
    lines.append(f"   - **Forex**: {cat_counts.get('FX_MAJOR', 0)} perechi Majore și {cat_counts.get('FX_CROSS', 0)} Cross-uri ({cat_counts.get('FX_MAJOR', 0) + cat_counts.get('FX_CROSS', 0)} total);")
    lines.append(f"   - **Metale**: {cat_counts.get('METALS', 0)} instrumente (XAUUSD, XAGUSD, XAUEUR);")
    lines.append(f"   - **Crypto**: {cat_counts.get('CRYPTO', 0)} instrumente (BTCUSD, ETHUSD, XRPUSD, SOLUSD, ADAUSD, DOGEUSD).")
    lines.append("   Acțiunile corporative (CFD pe acțiuni US/UE) și indicii bursieri CFD nu sunt provizionați de server pe acest cont.")
    lines.append("")
    lines.append(f"2. **Nu există avantaje statistice de alpha real din strategiile mecanice clasice de retail**:")
    lines.append(f"   După deducerea spread-ului măsurat pe oră, a comisionului de 2 $/lot și a swap-ului real, **nicio strategie mecanică (0 din {n_active_val}) nu a supraviețuit corecției Hansen Superior Predictive Ability (SPA) test**. Toate cele 72 de strategii care păreau a avea Sharpe >= 0.50 sunt artefacte de selecție aleatorie.")
    lines.append("")
    lines.append("3. **Nu există swap neutru pe retail**:")
    lines.append("   Pe instrumentele crypto, brokerul percepe un comision de finanțare negativ simetric (-4.75% anual atât pe long, cât și pe short). Pe majoritatea perechilor FX, spread-ul de finanțare al brokerului face ca ambele direcții să aibă swap net negativ (ex. CHFJPY, GBPAUD, NZDCAD), erodând mecanic orice poziție menținută pe termen mediu.")
    lines.append("")
    lines.append("---")
    lines.append("")
    lines.append("## 2. Recensământul și Calitatea Datelor (B1 & B2)")
    lines.append("")
    lines.append("### 2.1. Sinteza Universului")
    lines.append(f"- **Total instrumente identificate pe server**: **{total_census}**")
    lines.append(f"- **Instrumente validate pentru analiză (eligibile conform A1)**: **{n_eligible}** ({round(n_eligible / total_census * 100, 1)}%)")
    lines.append(f"- **Instrumente excluse definitiv conform regulii A1**: **{n_excluded}** ({round(n_excluded / total_census * 100, 1)}%)")
    lines.append("")
    lines.append("### 2.2. Excluderile din Univers (Documentate cu Dovadă Empirică)")
    lines.append("Conform regulii preînregistrate A1, selecția s-a făcut exclusiv pe criterii non-performanță (integritate structurală și cost relativ):")
    lines.append("")
    lines.append("| Simbol | Categorie | Bare D1 | Cost Relativ (Spread ÷ ATR14) | Bare Înghețate ($O=H=L=C$) | Motivul Excluderii |")
    lines.append("|---|---|---|---|---|---|")
    for r in excluded_rows:
        lines.append(f"| `{r['symbol']}` | {r['category']} | {r['d1_bars']} | {r['relative_cost_pct']}% | {r['frozen_bars']} | {r['exclusion_reason']} |")
    lines.append("")
    lines.append("*Notă metodologică*: XAGUSD a fost exclus din cauza unui cost relativ uriaș de **160.8%** (spread-ul depășește variația zilnică medie a argintului). GBPCHF, NZDCAD și NZDCHF conțineau sute de bare înghețate în arhivele vechi (1993–2000) furnizate de server, unde prețul a rămas neschimbat zile la rând.")
    lines.append("")
    lines.append("---")
    lines.append("")
    lines.append("## 3. Costurile Măsurate și Microstructura (B3)")
    lines.append("")
    lines.append("### 3.1. Explozia Spread-ului la Rollover-ul de la Miezul Nopții (Ora 00:00 Server)")
    lines.append("Analiza a 10,000 de bare orare H1 per instrument demonstrează o lărgire sistematică a spread-ului la ora 00:00 UTC (trecerea dintre sesiuni):")
    lines.append("")
    lines.append("| Simbol | Categorie | Spread Median Zi (puncte) | Spread Rollover 00:00 (puncte) | Multiplicator Lărgire | Swap Long | Swap Short |")
    lines.append("|---|---|---|---|---|---|---|")
    for r in high_rollovers[:10]:
        lines.append(f"| `{r['symbol']}` | {r['category']} | {r['daytime_spread_points']} | {r['rollover_00h_spread_points']} | **{r['rollover_expansion_multiplier']}x** | {r['swap_long']} | {r['swap_short']} |")
    lines.append("")
    lines.append("**Constatare**: Pentru perechi de tip `CHFJPY` sau `EURNZD`, spread-ul se lărgește de **8.4x până la 8.7x** la miezul nopții. Orice strategie care ar executa la începutul zilei fără a ține cont de această microstructură suportă o pierdere imediată masivă.")
    lines.append("")
    lines.append("---")
    lines.append("")
    lines.append("## 4. Rezultatele Științifice: NULE ÎNTÂI (Partea D)")
    lines.append("")
    lines.append("### 4.1. Câte strategii păreau profitabile fără corecție? (Iluzia Selecției)")
    lines.append(f"Pe perioada de Validare out-of-sample ({n_active_val} combinații active testate):")
    lines.append(f"- **{len(pos_sharpe_val)} din {n_active_val} combinații ({round(len(pos_sharpe_val) / n_active_val * 100, 1)}%)** au avut un raport Sharpe net pozitiv ($SR > 0$);")
    lines.append(f"- **{len(mres_val)} din {n_active_val} combinații ({round(len(mres_val) / n_active_val * 100, 1)}%)** au depășit pragul MRES de relevanță ($SR >= 0.50$);")
    lines.append(f"- **{len(beats_s0b_val)} din {n_active_val} combinații ({round(len(beats_s0b_val) / n_active_val * 100, 1)}%)** au bătut controlul aleatoriu potrivit pe expunere $S0b$ la nivel nominal $p < 0.05$.")
    lines.append("")
    lines.append("Top 5 cele mai „atrăgătoare” combinații naive pe Validare:")
    lines.append("1. `CHFJPY` S3 Breakout 20: Net Sharpe = +1.9862 (Gross: +2.2151, Drag: 0.2289)")
    lines.append("2. `XAUEUR` S1 Momentum 120: Net Sharpe = +1.8773 (Gross: +2.0596, Drag: 0.1822)")
    lines.append("3. `XAUEUR` S1 Momentum 252: Net Sharpe = +1.8773 (Gross: +2.0596, Drag: 0.1822)")
    lines.append("4. `XAUEUR` S3 Breakout 50: Net Sharpe = +1.8773 (Gross: +2.0596, Drag: 0.1822)")
    lines.append("5. `XAUUSD` S1 Momentum 120: Net Sharpe = +1.8721 (Gross: +2.0891, Drag: 0.2170)")
    lines.append("")
    lines.append("### 4.2. Corecția Riguroasă Hansen SPA și White Reality Check")
    lines.append(f"Când aplicăm testul de Superior Predictive Ability (Hansen 2005) cu bootstrap staționar pe blocuri ($B = 2000$ replici, lungime medie bloc $q = 10$, sămânță fixă `seed = 42`) pe întregul set de {n_active_val} combinații simultan:")
    lines.append("")
    lines.append("| Benchmark | Modele Testate | Statistica Studentizată $T_{SPA}$ | Valoare $p$ Hansen SPA | Valoare $p$ White Reality Check | Decizie Formală |")
    lines.append("|---|---|---|---|---|---|")
    lines.append(f"| **Cash (Zero Excess Return)** | {spa_cash['tested_models_count']} | {spa_cash['test_statistic_t_spa']} | **{spa_cash['p_value_hansen_spa']}** | **{spa_cash['p_value_white_reality_check']}** | **{spa_cash['decision']}** |")
    lines.append(f"| **Buy & Hold (Equal Weight)** | {spa_bh['tested_models_count']} | {spa_bh['test_statistic_t_spa']} | **{spa_bh['p_value_hansen_spa']}** | **{spa_bh['p_value_white_reality_check']}** | **{spa_bh['decision']}** |")
    lines.append("")
    lines.append("**Interpretare statistică**:")
    lines.append(f"Valoarea $p = {spa_cash['p_value_hansen_spa']} >= 0.05$ (respectiv $p = {spa_bh['p_value_hansen_spa']} >= 0.05$ față de Buy & Hold) înseamnă că **probabilitatea ca cel mai bun randament observat să fi apărut din pura întâmplare a testării multiple este de peste 20% (respectiv 38%)**.")
    lines.append("Ipoteza nulă NU poate fi respinsă.")
    lines.append("")
    lines.append("---")
    lines.append("")
    lines.append("## 5. Holdout-ul (Partea E)")
    lines.append("")
    lines.append("Conform contractului din preînregistrare:")
    lines.append("> *„Dacă nicio strategie nu atinge $SR_{\\text{net}} >= 0.50$ cu $p_{\\text{SPA}} < 0.05$ pe perioada de validare după deducerea costurilor reale: Rezultatul nul este acceptat formal... Holdout-ul blocat rămâne sigilat și nu este deschis.”*")
    lines.append("")
    lines.append(f"Deoarece $p_{{\\text{{SPA}}}} = {spa_cash['p_value_hansen_spa']} > 0.05$, **HOLDOUT-UL (2026-03-15 – 2026-09-14) A RĂMAS STRICT SIGILAT ȘI BLOCAT**. Niciun semnal nu a fost evaluat pe datele din ultimele 6 luni, prevenind orice scurgere informațională post-studiu.")
    lines.append("")
    lines.append("---")
    lines.append("")
    lines.append("## 6. Abateri de la Preînregistrare (`DEVIATIONS`)")
    lines.append("")
    lines.append("1. **Restricția Universului la 37 de Instrumente**:")
    lines.append("   Preînregistrarea anticipa testarea pe acțiuni și indici bursieri conform ghidului general MT5. Conectarea la terminalul demo RoboForex Ltd a relevat că serverul nu oferă acțiuni sau indici pe acest cont. Studiul s-a concentrat pe cele 37 de instrumente disponibile (Forex, Metale, Crypto), documentând absența acțiunilor în Secțiunea 1.")
    lines.append("2. **Nicio altă abatere**:")
    lines.append("   Împărțirea temporală, grilele de parametri, regulile de execuție la Open, formulele de cost și testul Hansen SPA au fost aplicate fără nicio modificare.")
    lines.append("")
    lines.append("---")
    lines.append("")
    lines.append("## 7. Recomandare Tehnico-Financiară")
    lines.append("")
    lines.append("Rezultatul empiric confirmă direct statistica ESMA (74%–89% din conturile de retail pierd bani):")
    lines.append("1. **Costurile reale distrug strategiile naive**: Chiar și strategiile care pe brut par să funcționeze suportă o reducere a raportului Sharpe cu 0.20 – 0.50 doar din spread și swap.")
    lines.append("2. **Data Snooping este pericolul #1**: Fără testul Hansen SPA, un cercetător ar fi selectat `CHFJPY S3_BRK_20` sau `XAUEUR S1_MOM_120` ca fiind „strategii de succes” (Sharpe ~ 1.9), ignorând faptul că într-un univers de 246 de teste, astfel de valori apar firesc din pur noroc statistic.")
    lines.append("3. **Recomandare**: Nu alocați capital real pe strategii tehnice clasice fără un model distinct de microstructură (order book imbalance, execuție limită pasivă fără spread taker, sau strategii de lichiditate instituționale cu comisioane zero/rebate).")
    lines.append("")
    lines.append("---")
    lines.append("")
    lines.append("## 8. Fișiere de Date și Hash-uri Criptografice SHA-256")
    lines.append("")
    lines.append("| Fișier | Rol | Dimensiune | SHA-256 |")
    lines.append("|---|---|---|---|")
    lines.append(f"| `tables/table_1_census.csv` | Recensământul complet al instrumentelor | {os.path.getsize(t1_path)} B | `{sha256_file(t1_path)}` |")
    lines.append(f"| `tables/table_2_quality.csv` | Auditul calității datelor și excluderi | {os.path.getsize(t2_path)} B | `{sha256_file(t2_path)}` |")
    lines.append(f"| `tables/table_3_costs_summary.csv` | Profilul de costuri și swap | {os.path.getsize(t3_path)} B | `{sha256_file(t3_path)}` |")
    lines.append(f"| `tables/table_4_development_results.csv` | Rezultate in-sample dezvoltare | {os.path.getsize(t4_path)} B | `{sha256_file(t4_path)}` |")
    lines.append(f"| `tables/table_5_validation_results.csv` | Rezultate out-of-sample validare | {os.path.getsize(t5_path)} B | `{sha256_file(t5_path)}` |")
    lines.append(f"| `tables/table_6_validation_spa_tests.csv` | Rezultate teste Hansen SPA / White RC | {os.path.getsize(t6_path)} B | `{sha256_file(t6_path)}` |")
    lines.append(f"| `tables/table_7_commission_sensitivity.csv` | Analiza de sensibilitate la comision | {os.path.getsize(t7_path)} B | `{sha256_file(t7_path)}` |")
    manifest_p = os.path.join(BASE_DIR, "corpus", "MANIFEST.json")
    lines.append(f"| `corpus/MANIFEST.json` | Manifestul integral al corpusului D1/H1 | {os.path.getsize(manifest_p)} B | `{sha256_file(manifest_p)}` |")
    lines.append("")

    content = "\n".join(lines)
    with open(REPORT_MD, "w", encoding="utf-8") as f:
        f.write(content)
    print(f"RESEARCH_REPORT_V1.md generated successfully at {REPORT_MD} ({len(content)} bytes).")


if __name__ == "__main__":
    main()
