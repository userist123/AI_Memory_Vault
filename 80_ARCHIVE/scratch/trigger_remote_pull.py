import urllib.request
import json
import time

remote_url = "https://rand-sperm-knowing-sega.trycloudflare.com/api/pull"
model = "qwen2.5-coder:32b"

print("="*60)
print("[*] Declansare Descarcare Model pe GPU Kaggle (2x T4 - 29.1 GB VRAM)...")
print(f"Model: {model}")
print(f"URL:   {remote_url}")
print("="*60)

req = urllib.request.Request(
    remote_url,
    data=json.dumps({"name": model, "stream": True}).encode('utf-8'),
    headers={"Content-Type": "application/json"}
)

try:
    with urllib.request.urlopen(req, timeout=600) as response:
        last_pct = -1
        for line in response:
            if not line:
                continue
            data = json.loads(line.decode('utf-8'))
            if "completed" in data and "total" in data:
                pct = int((data["completed"] / data["total"]) * 100)
                if pct != last_pct and pct % 5 == 0:
                    last_pct = pct
                    mb_done = data["completed"] / (1024 * 1024)
                    mb_total = data["total"] / (1024 * 1024)
                    print(f"[*] Descarcare: {pct}% ({mb_done:.1f} MB / {mb_total:.1f} MB)")
            elif "status" in data:
                print(f"[*] Status: {data['status']}")
    print("\n" + "="*60)
    print("✅ MODELUL QWEN2.5-CODER:32B A FOST INSTALAT CU SUCCES PE KAGGLE GPU!")
    print("="*60)
except Exception as e:
    print(f"❌ Eroare la descarcare: {e}")
