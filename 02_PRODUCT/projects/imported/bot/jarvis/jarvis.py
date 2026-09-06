#!/usr/bin/env python3
# -*- coding: utf-8 -*-

"""
J.A.R.V.I.S. 3.0 - Asistent vocal complet cu inteligență locală (Ollama)
și acțiuni avansate pe sistem, plus integrări externe (vreme, știri, wiki, glume).
Rulează o singură dată - se instalează automat.
"""

import os
import sys
import subprocess
import time
import json
import re
import threading
import webbrowser
import platform
import warnings
from datetime import datetime
from pathlib import Path

# ========== AUTO INSTALARE DEPENDINȚE ==========
def install_package(pkg):
    subprocess.check_call([sys.executable, "-m", "pip", "install", "--quiet", pkg])

def ensure_deps():
    required = [
        "openai-whisper", "sounddevice", "scipy", "requests",
        "pyttsx3", "pyautogui", "pyperclip", "psutil", "wikipedia"
    ]
    for pkg in required:
        try:
            __import__(pkg.replace("-", "_"))
        except ImportError:
            print(f"📦 Instalez {pkg}...")
            install_package(pkg)

ensure_deps()

# ========== IMPORTURI ==========
import whisper
import sounddevice as sd
import numpy as np
import requests
import pyttsx3
import pyautogui
import pyperclip
import psutil
import wikipedia

# Suprimă avertismentele Whisper legate de FP16
warnings.filterwarnings("ignore", message="FP16 is not supported")

# ========== CONFIGURARE OLLAMA ==========
def gaseste_ollama():
    for port in [11434, 8080]:
        try:
            r = requests.get(f"http://localhost:{port}/api/tags", timeout=2)
            if r.status_code == 200:
                models = r.json().get("models", [])
                if models:
                    model_name = models[0]["name"]
                    print(f"✅ Ollama pe port {port}, model: {model_name}")
                    return port, model_name
        except:
            continue
    print("❌ Ollama nu rulează. Pornește-l cu 'ollama serve' într-un alt terminal.")
    return None, None

OLLAMA_PORT, MODEL_NAME = gaseste_ollama()
if OLLAMA_PORT is None:
    sys.exit(1)

OLLAMA_URL = f"http://localhost:{OLLAMA_PORT}/api/generate"

# ========== INIȚIALIZARE MOTOARE ==========
print("⏳ Încarc modelul Whisper (recunoaștere vocală)...")
stt = whisper.load_model("base")
print("✅ Whisper gata.")

engine = pyttsx3.init()
engine.setProperty('rate', 160)   # viteză naturală
engine.setProperty('volume', 0.9)

def spune(text):
    if not text:
        return
    print(f"🗣️ JARVIS: {text}")
    engine.say(text)
    engine.runAndWait()

# ========== FUNCȚII DE SISTEM ȘI ACȚIUNI ==========
def deschide_aplicatie(nume):
    nume = nume.lower()
    app_map = {
        "notepad": "notepad.exe", "calculator": "calc.exe", "chrome": "chrome.exe",
        "firefox": "firefox.exe", "edge": "msedge.exe", "explorer": "explorer.exe",
        "task manager": "taskmgr.exe", "cmd": "cmd.exe", "powershell": "powershell.exe",
        "spotify": "spotify.exe", "vscode": "code.exe", "paint": "mspaint.exe",
        "whatsapp": "whatsapp.exe", "telegram": "telegram.exe", "discord": "discord.exe",
    }
    for cheie, cale in app_map.items():
        if cheie in nume:
            try:
                subprocess.Popen(cale)
                return f"Am deschis {cheie}."
            except:
                return f"Nu am putut deschide {cheie}."
    try:
        subprocess.Popen(nume)
        return f"Am deschis {nume}."
    except:
        return f"Nu știu cum să deschid '{nume}'."

def cauta_web(query):
    webbrowser.open(f"https://www.google.com/search?q={query}")
    return f"Caut pe web: {query}"

def ia_screenshot():
    timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    folder = Path.home() / "Pictures" / "JARVIS_Screenshots"
    folder.mkdir(exist_ok=True)
    path = folder / f"screenshot_{timestamp}.png"
    pyautogui.screenshot(str(path))
    return f"Screenshot salvat la {path}"

def volum(action):
    if platform.system() == "Windows":
        if action == "up":
            pyautogui.press("volumeup", presses=5)
            return "Volum crescut."
        elif action == "down":
            pyautogui.press("volumedown", presses=5)
            return "Volum scăzut."
        elif action == "mute":
            pyautogui.press("volumemute")
            return "Sunet oprit/pornit."
    else:
        return "Control volum doar pe Windows."

def copy_to_clipboard(text):
    pyperclip.copy(text)
    return "Text copiat în clipboard."

def paste_from_clipboard():
    return pyperclip.paste()

def inchide_proces(nume):
    for proc in psutil.process_iter(['name']):
        if nume.lower() in proc.info['name'].lower():
            proc.kill()
            return f"Am închis procesul {nume}."
    return f"Nu am găsit procesul {nume}."

def scrie_notita(text):
    folder = Path.home() / "Documents" / "JARVIS_Notes"
    folder.mkdir(exist_ok=True)
    with open(folder / "notite.txt", "a", encoding="utf-8") as f:
        f.write(f"{datetime.now().strftime('%Y-%m-%d %H:%M:%S')}: {text}\n")
    return "Notița a fost salvată."

def citeste_notite():
    folder = Path.home() / "Documents" / "JARVIS_Notes"
    file = folder / "notite.txt"
    if file.exists():
        with open(file, "r", encoding="utf-8") as f:
            content = f.read()
        if content:
            return f"Ultimele notițe:\n{content[-500:]}"
        else:
            return "Nu ai nicio notiță."
    return "Nu ai nicio notiță."

def get_ora():
    return datetime.now().strftime("%H:%M")

def get_data():
    return datetime.now().strftime("%d.%m.%Y")

def calculeaza(expresie):
    try:
        if not re.match(r'^[\d\s\+\-\*\/\(\)\.]+$', expresie):
            return "Expresie invalidă. Folosește doar numere și operatori (+, -, *, /)."
        rezultat = eval(expresie)
        return f"Rezultatul este {rezultat}"
    except:
        return "Nu am putut calcula expresia."

def vremea(oras="București"):
    """Folosește OpenWeatherMap (necesită cheie API). Dacă nu există cheie, returnează mesaj."""
    api_key = "YOUR_OPENWEATHER_API_KEY"  # Înlocuiește cu cheia ta reală
    if api_key == "YOUR_OPENWEATHER_API_KEY":
        return "Funcția vreme necesită o cheie API de la OpenWeatherMap. Înlocuiește variabila api_key din cod."
    url = f"http://api.openweathermap.org/data/2.5/weather?q={oras}&appid={api_key}&units=metric&lang=ro"
    try:
        r = requests.get(url, timeout=10)
        if r.status_code == 200:
            data = r.json()
            temp = data['main']['temp']
            desc = data['weather'][0]['description']
            return f"În {oras} sunt {temp} grade Celsius și {desc}."
        else:
            return "Nu am putut obține vremea. Verifică numele orașului sau conexiunea."
    except:
        return "Eroare la conectare cu serviciul de vreme."

def cauta_wikipedia(query):
    try:
        wikipedia.set_lang("ro")
        rezumat = wikipedia.summary(query, sentences=2)
        return f"Wikipedia spune: {rezumat}"
    except wikipedia.exceptions.DisambiguationError as e:
        return f"Termenul este ambiguu. Încearcă: {e.options[:3]}"
    except wikipedia.exceptions.PageError:
        return "Nu am găsit o pagină pentru această căutare."
    except:
        return "Eroare la căutarea pe Wikipedia."

def gluma():
    """Glumă simplă din listă locală (evită API extern)."""
    glume = [
        "De ce nu se ceartă doi electroni? Pentru că își pierd sarcina.",
        "Ce face un programator când plouă? Închide geamul, altfel intra variabilele.",
        "De ce nu poți avea încredere într-un atom? Pentru că ei alcătuiesc totul.",
        "Care e animalul preferat al programatorilor? Șarpele (python)."
    ]
    import random
    return random.choice(glume)

def stiri():
    """Știri prin NewsAPI (necesită cheie). Dacă nu, returnează mesaj."""
    api_key = "YOUR_NEWSAPI_KEY"
    if api_key == "YOUR_NEWSAPI_KEY":
        return "Funcția știri necesită o cheie API de la NewsAPI.org. Înlocuiește variabila api_key din cod."
    url = f"https://newsapi.org/v2/top-headlines?country=ro&apiKey={api_key}"
    try:
        r = requests.get(url, timeout=10)
        if r.status_code == 200:
            articole = r.json().get("articles", [])[:3]
            if articole:
                titluri = "\n".join([f"- {a['title']}" for a in articole])
                return f"Ultimele știri din România:\n{titluri}"
            else:
                return "Nu am găsit știri recente."
        else:
            return "Serviciul de știri nu răspunde."
    except:
        return "Eroare la conectare cu serviciul de știri."

def controleaza_sistem(actiune):
    act = actiune.lower()
    if "oprește" in act or "shutdown" in act:
        os.system("shutdown /s /t 10")
        return "Sistemul se va opri în 10 secunde. Spune 'anulează oprirea' dacă ai greșit."
    elif "repornire" in act or "restart" in act:
        os.system("shutdown /r /t 10")
        return "Sistemul va reporni în 10 secunde."
    elif "blochează" in act or "lock" in act:
        os.system("rundll32.exe user32.dll,LockWorkStation")
        return "Ecran blocat."
    elif "anulează oprirea" in act:
        os.system("shutdown /a")
        return "Am anulat oprirea sau repornirea."
    else:
        return "Nu am înțeles acțiunea. Spune: oprește calculatorul, repornește, blochează ecranul."

# ========== DICȚIONAR DE COMENZI RAPIDE ==========
comenzi_rapide = [
    (r"deschide (.*)", lambda m: deschide_aplicatie(m)),
    (r"caută pe web (.*)", lambda m: cauta_web(m)),
    (r"fă un screenshot|ia un screenshot", lambda _: ia_screenshot()),
    (r"crește volumul|volum plus", lambda _: volum("up")),
    (r"scade volumul|volum minus", lambda _: volum("down")),
    (r"oprește sunetul|mute", lambda _: volum("mute")),
    (r"copiază (.*)", lambda m: copy_to_clipboard(m)),
    (r"lipește|paste", lambda _: paste_from_clipboard()),
    (r"închide (.*)", lambda m: inchide_proces(m)),
    (r"scrie o notiță (.*)", lambda m: scrie_notita(m)),
    (r"citește notițe", lambda _: citeste_notite()),
    (r"ce oră este|cât e ceasul", lambda _: get_ora()),
    (r"ce dată este|azi ce dată", lambda _: get_data()),
    (r"calculează (.*)", lambda m: calculeaza(m)),
    (r"vremea(?: în| din| de la)? (.*)", lambda m: vremea(m.strip())),
    (r"vremea$", lambda _: vremea("București")),
    (r"caută pe wikipedia (.*)|wiki (.*)", lambda m: cauta_wikipedia(m[0] or m[1])),
    (r"spune o glumă|glumă|o glumă", lambda _: gluma()),
    (r"știri|ultimele știri", lambda _: stiri()),
    (r"(oprește|repornire|restart|blochează|anulează oprirea) (calculatorul|sistemul|ecranul)?", 
     lambda m: controleaza_sistem(m[0])),
]

# ========== INTEGRARE OLLAMA (pentru întrebări generale) ==========
def foloseste_llm(prompt):
    print("🧠 Gândesc...")
    sys_prompt = f"""Ești JARVIS, un asistent vocal inteligent, prietenos și precis. 
Răspunde în limba română, scurt și la obiect (maxim 2 propoziții). 
Poți oferi informații, explicații, sfaturi. Dacă utilizatorul cere ceva ce nu poți face, spune politicos. 
Nu inventa fapte. Dacă nu știi, spune că nu știi.
Acum răspunde la: {prompt}"""
    try:
        r = requests.post(OLLAMA_URL, json={
            "model": MODEL_NAME,
            "prompt": sys_prompt,
            "stream": False,
            "options": {"num_predict": 150, "temperature": 0.7}
        }, timeout=30)
        return r.json().get("response", "Nu am primit răspuns.")
    except Exception as e:
        return f"Eroare conexiune AI: {e}"

# ========== PROCESARE COMANDĂ ==========
def proceseaza_comanda(text):
    text_lower = text.lower().strip()
    # 1. Încearcă comenzile rapide
    for pattern, func in comenzi_rapide:
        match = re.search(pattern, text_lower)
        if match:
            args = match.groups()
            # elimină None din grupe
            args = [a for a in args if a is not None]
            try:
                if args:
                    rez = func(args[0]) if len(args) == 1 else func(args)
                else:
                    rez = func()
                return rez
            except Exception as e:
                return f"Eroare la execuție: {e}"
    # 2. Dacă nu e comandă rapidă, întreabă LLM-ul
    return foloseste_llm(text)

# ========== FUNCȚII VOCALE ==========
def inregistreaza(durata=4, rata=16000):
    print("\n🎤 Ascult...", end="", flush=True)
    audio = sd.rec(int(durata * rata), samplerate=rata, channels=1, dtype=np.int16)
    sd.wait()
    print(" gata.")
    return audio.squeeze().astype(np.float32) / 32768.0

def asculta():
    audio = inregistreaza()
    rezultat = stt.transcribe(audio, language="ro", fp16=False)
    text = rezultat["text"].strip()
    if text:
        print(f"📝 Ai spus: {text}")
    else:
        print("🔇 Nu am auzit nimic.")
    return text

# ========== BUCUL PRINCIPAL ==========
if __name__ == "__main__":
    print("\n" + "="*50)
    print("🤖 J.A.R.V.I.S. 3.0 - Asistent vocal complet")
    print("="*50)
    print("💡 Exemple de comenzi:")
    print("   • Deschide Chrome, fă un screenshot, crește volumul")
    print("   • Vremea în Timișoara, caută pe Wikipedia Inteligența Artificială")
    print("   • Scrie o notiță cumpărături, citește notițe, ce oră este")
    print("   • Oprește calculatorul (în 10 secunde), anulează oprirea")
    print("   • Spune o glumă, știri, calculează 25*4")
    print("   • Întrebări generale: Cine a scris Moromeții? Ce este un black hole?")
    print("💡 Spune 'oprește-te', 'exit' sau 'la revedere' pentru a închide.\n")
    
    spune("Jarvis 3.0 activ. Sunt gata să vă ajut.")

    while True:
        try:
            comanda = asculta()
            if not comanda:
                continue
            if any(c in comanda.lower() for c in ["oprește-te", "exit", "închide", "la revedere", "quit"]):
                spune("La revedere, domnule!")
                break
            raspuns = proceseaza_comanda(comanda)
            spune(raspuns)
        except KeyboardInterrupt:
            print("\n👋 Oprire manuală.")
            spune("La revedere!")
            break
        except Exception as e:
            print(f"❌ Eroare: {e}")
            time.sleep(1)