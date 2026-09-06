# Cerebras Coding Agent

Agent autonom de programare bazat pe Cerebras (gpt-oss-120b, GRATUIT).
2.600+ tokens/secunda. 1M tokens/zi fara card de credit.

## Setup rapid (2 minute)

### 1. Instaleaza dependintele
```bash
pip install cerebras-cloud-sdk
```

### 2. Obtine API key GRATUIT
Mergi la: https://cloud.cerebras.ai -> API Keys -> Create Key
(Nu necesita card de credit)

### 3. Seteaza cheia
```powershell
# Windows (PowerShell)
$env:CEREBRAS_API_KEY = "csk-..."
```
```bash
# Linux / Mac
export CEREBRAS_API_KEY="csk-..."
```

### 4. Ruleaza agentul

**Mod interactiv (recomandat):**
```bash
python agent.py
```

**Mod one-shot:**
```bash
python agent.py "Creaza un script Python care citeste un CSV si genereaza un grafic"
python agent.py "Fa un web scraper pentru hacker news"
python agent.py "Construieste un REST API cu FastAPI pentru o to-do list"
```

## Ce poate face agentul

| Tool | Descriere |
|------|-----------|
| read_file | Citeste orice fisier |
| write_file | Creeaza/suprascrie fisiere |
| run_command | Executa comenzi bash/PowerShell |
| list_directory | Listeaza fisierele unui director |
| create_directory | Creeaza directoare |
| search_in_files | Grep in fisiere |

## Exemple de taskuri

```
> Creaza un trading bot simplu care calculeaza RSI si SMA pentru un simbol dat
> Fa un script care monitorizeaza un director si trimite email cand apare un fisier nou
> Construieste o aplicatie FastAPI cu autentificare JWT si baza de date SQLite
> Scrie un script PowerShell care face backup la Active Directory
> Creaza un bot Discord care raspunde la comenzi
```

## Configurare avansata

Editeaza agent.py si schimba:

```python
MODEL = "llama-4-scout-17b-16e-instruct"  # mai rapid, context mai mare
MODEL = "gpt-oss-120b"                    # mai capabil (default)

max_iterations = 20  # creste pentru taskuri foarte complexe
```

## Limite free tier Cerebras
- 1.000.000 tokens/zi
- 30 requests/minut
- Fara card de credit
- Fara expirare
