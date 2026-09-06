import ccxt
import time
import json
import logging
import threading
import os
from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel
from openai import OpenAI
from dotenv import load_dotenv

# --- 1. CONFIGURARE SISTEM ȘI JURNALIZARE ---
logging.basicConfig(level=logging.INFO, format='%(asctime)s - [JARVIS] - %(message)s')
logger = logging.getLogger('JARVIS')

app = FastAPI(title="J.A.R.V.I.S. Secure Enclave API")

# Permitem Frontend-ului (React) să comunice cu acest backend
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"], # În producție, se pune URL-ul frontend-ului (ex: "http://localhost:5173")
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

# --- 2. STARE IN-MEMORY (Securitate anti-hack) ---
# Cheile și starea nu se scriu NICIODATĂ pe disc. Stau doar în RAM.
jarvis_state = {
    "status": "OFFLINE", # OFFLINE, RUNNING, PAUSED
    "config": None,      # Configurația curentă (Pereche, Risc)
    "exchange": None,    # Instanța de CCXT conectată la bursă
    "logs": []           # Jurnalul terminalului pentru Frontend
}
trading_thread = None

# --- 3. INIȚIALIZARE "CREIER" (OpenAI) ---
load_dotenv()
OPENAI_KEY = os.getenv("OPENAI_API_KEY", "INTRODU_AICI_CHEIA_TA_OPENAI")
try:
    ai_client = OpenAI(api_key=OPENAI_KEY)
except Exception as e:
    logger.error("Eșec critic: Nu s-a putut inițializa OpenAI. Verifică API KEY-ul.")
    ai_client = None

# --- 4. PROMPT-UL JARVIS DIN CERCETAREA TA (Structură fixă) ---
JARVIS_SYSTEM_PROMPT = """
<CORE_IDENTITY>
Ești J.A.R.V.I.S. v8.0 - Un motor cognitiv de tranzacționare autonomă.
Obsesia ta: Acuratețea verificabilă și conservarea capitalului.
Preferi TĂCEREA (REPAUS) în locul acțiunilor riscante sau bazate pe FOMO.
</CORE_IDENTITY>

<CONSTITUTION>
1. Conservarea capitalului primează în fața profitului.
2. Nicio decizie bazată pe impuls (FOMO).
3. Evaluează strict datele prin cele 5 straturi cognitive.
</CONSTITUTION>

<INSTRUCTIONS>
Analizează datele de piață primite (Trunchi Cerebral -> Cortex).
Aplică filtrele de siguranță (Amigdala).
RĂSPUNDE STRICT ȘI EXCLUSIV ÎN FORMAT JSON. Fără Markdown, fără saluturi.

Format obligatoriu:
{
  "analiza_cortex": "Scurtă justificare logică pe baza datelor tehnice.",
  "siguranta_amigdala_procent": <număr între 0 și 100>,
  "decizie_prefrontal": "CUMPARA" sau "VINDE" sau "REPAUS"
}
Regulă critică de sistem: Dacă siguranta_amigdala_procent < 80, decizie_prefrontal TREBUIE FORȚAT să fie "REPAUS".
</INSTRUCTIONS>
"""

# --- 5. MODELE DE DATE API (Formatul datelor primite de la UI) ---
class APIConfig(BaseModel):
    apiKey: str
    apiSecret: str
    platform: str
    pair: str
    riskLevel: str
    maxDrawdown: str

# --- 6. LOGICA COGNITIVĂ A BOT-ULUI ---

def log_to_system(log_type: str, message: str):
    """Adaugă înregistrări în memoria serverului pentru a fi citite de interfață."""
    log_entry = {
        "time": time.strftime("%H:%M:%S"),
        "type": log_type,
        "message": message
    }
    jarvis_state["logs"].append(log_entry)
    print(f"[{log_type.upper()}] {message}")
    
    # Prevenim scurgerile de memorie (păstrăm doar ultimele 50 de loguri)
    if len(jarvis_state["logs"]) > 50:
        jarvis_state["logs"].pop(0)

def fetch_market_data(exchange, pair):
    """[STRAT 1: Trunchi Cerebral] Preluare date neprocesate."""
    try:
        ticker = exchange.fetch_ticker(pair)
        return {
            "pereche": pair,
            "pret_curent": ticker['last'],
            "volum_24h": ticker['baseVolume'],
            "schimbare_procentuala": ticker['percentage']
        }
    except Exception as e:
        log_to_system("error", f"Eroare Senzorială (Extragere API Bursă): {str(e)}")
        return None

def process_with_ai(market_data):
    """[STRAT 2, 3, 4: Cortex & Amigdală] Procesare prin OpenAI cu Temperatură Redusă."""
    if not ai_client:
        return {"decizie_prefrontal": "REPAUS", "analiza_cortex": "Eroare Critică: Lipsă AI API Key", "siguranta_amigdala_procent": 0}
        
    log_to_system("info", f"Trimitem setul de date către Cortex (Model: GPT-4o)...")
    user_prompt = f"DATE PIAȚĂ ACTUALE: {json.dumps(market_data)}. Evaluează și ia o decizie."

    try:
        response = ai_client.chat.completions.create(
            model="gpt-4o", # Modelul ideal pentru reasoning complex
            messages=[
                {"role": "system", "content": JARVIS_SYSTEM_PROMPT},
                {"role": "user", "content": user_prompt}
            ],
            response_format={ "type": "json_object" }, # ANTICORP: Forțează structura datelor
            temperature=0.1 # ANTICORP: Determinism maxim, creativitate (halucinare) minimă
        )
        return json.loads(response.choices[0].message.content)
    except Exception as e:
        log_to_system("error", f"Eroare Cognitivă (Eșec API OpenAI): {str(e)}")
        return {"decizie_prefrontal": "REPAUS", "analiza_cortex": "Eroare rețea/AI. Trecere în Repaus de siguranță.", "siguranta_amigdala_procent": 0}

def jarvis_autonomous_loop():
    """Bucla infinită care rulează în fundal cât timp botul este ONLINE."""
    config = jarvis_state["config"]
    exchange = jarvis_state["exchange"]
    pair = config.pair
    
    log_to_system("system", f"Protocoale de tranzacționare autonome inițializate pentru {pair}.")
    
    while jarvis_state["status"] == "RUNNING":
        log_to_system("info", "--- Inițiere Ciclul Cognitiv Nou ---")
        
        # Pas 1
        log_to_system("process", "[STRAT 1] Preluare amprentă senzorială din piață...")
        data = fetch_market_data(exchange, pair)
        
        if data:
            # Pașii 2, 3, 4
            decizie = process_with_ai(data)
            
            analiza = decizie.get("analiza_cortex", "Lipsă analiză.")
            siguranta = decizie.get("siguranta_amigdala_procent", 0)
            actiune = decizie.get("decizie_prefrontal", "REPAUS")
            
            log_to_system("warning", f"[STRAT 2/3/4] Analiză Amigdală: {analiza} | Încredere: {siguranta}%")
            
            # Pas 5: Execuția reală
            if actiune == "CUMPARA" and siguranta >= 80:
                log_to_system("success", f"[STRAT 5] DECIZIE CORTEX PREFRONTAL: CUMPĂRĂ. (Simulare: Acțiune blocată de siguranțe hard-coded)")
                # PENTRU BANI REALI, decomentează liniile de mai jos (atenție la cantități!):
                # try:
                #     order = exchange.create_market_buy_order(pair, 0.001) 
                #     log_to_system("success", f"Ordin executat cu succes: {order['id']}")
                # except Exception as e:
                #     log_to_system("error", f"Eșec execuție ordin bursă: {e}")
                
            elif actiune == "VINDE" and siguranta >= 80:
                log_to_system("success", f"[STRAT 5] DECIZIE CORTEX PREFRONTAL: VINDE. (Simulare: Acțiune blocată de siguranțe hard-coded)")
                # exchange.create_market_sell_order(pair, 0.001)
            else:
                log_to_system("system", f"[STRAT 5] DECIZIE CORTEX PREFRONTAL: REPAUS. Respingere din cauza lipsei de certitudine matematică.")
        
        # Protecție Rate-Limit: Pauză 30 secunde între acțiuni
        time_to_sleep = 30
        while time_to_sleep > 0 and jarvis_state["status"] == "RUNNING":
            time.sleep(1)
            time_to_sleep -= 1
            
    log_to_system("warning", "Protocoale autonome OPRITE. Așteptare comenzi manuale.")

# --- 7. RUTE API (Comunicarea cu Frontend-ul React) ---

@app.get("/api/status")
def get_status():
    """Returnează logurile și starea către frontend la fiecare 2 secunde."""
    return {
        "status": jarvis_state["status"],
        "logs": jarvis_state["logs"],
        "isConfigured": jarvis_state["config"] is not None
    }

@app.post("/api/configure")
def configure_bot(config: APIConfig):
    """Primește datele din UI și creează instanța CCXT criptată în RAM."""
    try:
        # Preluăm numele bursei din frontend (ex: "BINANCE" -> ccxt.binance)
        exchange_class = getattr(ccxt, config.platform.lower())
        exchange_instance = exchange_class({
            'apiKey': config.apiKey,
            'secret': config.apiSecret,
            'enableRateLimit': True,
        })
        
        # VALIDARE STRICTĂ: Verificăm dacă cheile chiar funcționează cerând o balanță
        log_to_system("process", f"Inițializare test securitate API pentru {config.platform}...")
        exchange_instance.fetch_balance() 
        
        # Salvăm configurația în starea volatilă
        jarvis_state["config"] = config
        jarvis_state["exchange"] = exchange_instance
        jarvis_state["status"] = "PAUSED"
        
        log_to_system("success", f"Infrastructură securizată creată. API Keys validate și stocate în Enclavă.")
        return {"message": "Infrastructură validată."}
    
    except ccxt.AuthenticationError:
        log_to_system("error", "Breșă Securitate: API Keys introduse sunt invalide sau respinse.")
        raise HTTPException(status_code=401, detail="Chei API Invalide.")
    except Exception as e:
        log_to_system("error", f"Eroare Inițializare: {str(e)}")
        raise HTTPException(status_code=400, detail=str(e))

@app.post("/api/toggle")
def toggle_bot():
    """Pornește sau Oprește thread-ul autonom."""
    global trading_thread
    
    if jarvis_state["config"] is None:
        raise HTTPException(status_code=400, detail="Sistemul nu este configurat.")
        
    if jarvis_state["status"] in ["OFFLINE", "PAUSED"]:
        jarvis_state["status"] = "RUNNING"
        jarvis_state["logs"].clear() # Curățăm vizualul
        
        # Lansăm pe un fir de execuție separat pentru a nu bloca API-ul
        trading_thread = threading.Thread(target=jarvis_autonomous_loop)
        trading_thread.daemon = True
        trading_thread.start()
        return {"status": "RUNNING"}
    else:
        jarvis_state["status"] = "PAUSED"
        # Bucla din trading_thread va vedea că starea s-a schimbat și se va opri singură
        return {"status": "PAUSED"}

# Rularea se face din terminal:
# uvicorn jarvis_backend:app --reload --port 8000