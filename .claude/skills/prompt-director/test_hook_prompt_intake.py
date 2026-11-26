"""Tests for hook_prompt_intake.py. Run: python -I test_hook_prompt_intake.py (from any directory)."""
import importlib.util
import json
import subprocess
import sys
import unittest
from pathlib import Path

HOOK = Path(__file__).with_name("hook_prompt_intake.py")
spec = importlib.util.spec_from_file_location("hook_prompt_intake", HOOK)
mod = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mod)

TASKS = [
    "Fa-le cum trebuie, cauta pe internet, întreabă si chatgpt si gemini când ai nelămuriri si fa skillurile competente",
    "Sa pornească in momentul in care ii dau de făcut ceva sau când ii cer ceva nu de fiecare data",
    "Fă-mi un site pentru restaurantul de pe colț",
    "Fa-mi un raport din logurile de ieri",
    "Repară eroarea din testul de login te rog",
    "Poți să cauți pe internet prețul la RTX 5090?",
    "Ce culori să pun pe site? fă-mi o paletă",
    "Vreau sa muti proiectul pe discul E",
    "Instalează Blender pe discul E și verifică dacă pornește",
    "Write a script that renames the photos by date",
    "<pasted_content id=\"x\">\nlog line 1\nlog line 2\n</pasted_content id=\"x\">\nanalizează logul de mai sus și spune-mi ce a picat",
    # review round 1: cedilla diacritics, missing verbs, short instruction around a paste
    "Porneşte serverul de test pe portul 8080",
    "Şterge folderul temp de pe discul E",
    "Curăţă cache-ul din proiectul vechi",
    "Aş vrea un raport cu erorile de azi",
    "Poţi să cauţi documentaţia pentru API?",
    "Deschide aplicația și oprește serviciul vechi",
    "Crează un fisier nou cu notițele de azi",
    "Arată-mi logurile de la ultimul build",
    "Salvează configurația curentă pe discul E",
    "Redenumește pozele după data din EXIF",
    "Explică-mi de ce pică testul de login",
    "Spune-mi ce a picat în compilare",
    "Deploy the site to vercel right now",
    "Refactor the parser module into two files",
    "verifică asta\n<pasted_content id=\"p\">\nTraceback ...\n</pasted_content id=\"p\">\nok",
    "Nu merge login-ul, repară-l până diseară",
    # outside research (Gemini/Codex): polite interrogative requests and mixed question + request
    "Poți verifica serverul și ce eroare dă?",
    "Ai putea să muți fișierele pe discul E?",
    "Puteți să trimiteți raportul diseară?",
]
NOT_TASKS = [
    "Am skill care face prompturi instalat si este prins in skillul cost-router?",
    "Cand ii dau agentului o sarcina se transforma in prompt?",
    "Ce face scriptul asta cand pornește calculatorul?",
    "Cum funcționează cost-router?",
    "Care e diferența dintre Sonnet și Haiku?",
    "da",
    "continuă",
    "ok mersi mult de tot",
    "/clip https://twitch.tv/x",
    "<agent-message from=\"a92\">\n[Subagent hand-back] The text below is the final report. Fix the bug and run the tests.",
    "<task-notification>\nAgent finished: implement the parser and run tests</task-notification>",
    "<system-reminder>\nAnother Claude session sent a message: please build the app\n</system-reminder>",
    "[Subagent hand-back] build the page and deploy it",
    # review round 1: questions without '?', yes/no questions, clitic question words, stop orders
    "Ce face scriptul asta cand pornește calculatorul",
    "Ce face scriptul asta cand pornește?!",
    "Ce face scriptul asta cand pornește? 🙂",
    "Scriptul pornește singur la boot?",
    "Agentul verifică testele înainte de push?",
    "Oare scriptul verifică semnăturile?",
    "Și cum verifică el semnăturile?",
    "Did the build run correctly last night?",
    "Ce-mi poți spune despre build-ul de ieri?",
    "Vreau să știu cum funcționează cost-router",
    "Nu fă nimic încă acum",
    "Nu, nu vreau asta",
    "Nu mai verifica, e ok",
    "Stai, nu șterge nimic deocamdată",
    "Da, continuă cu pasul următor",
    "<command-name>/clip</command-name> fă clipuri",
    "!git status și verifică ramura",
    "Poți fi sigur că testele au trecut toate?",
]


class IntakeTests(unittest.TestCase):
    def test_tasks_fire(self):
        for p in TASKS:
            with self.subTest(p=p[:60]):
                self.assertTrue(mod.is_task(p))

    def test_non_tasks_silent(self):
        for p in NOT_TASKS:
            with self.subTest(p=p[:60]):
                self.assertFalse(mod.is_task(p))

    def test_process_output_contract(self):
        def run(prompt):
            r = subprocess.run([sys.executable, "-I", str(HOOK)], input=json.dumps({"prompt": prompt}),
                               capture_output=True, text=True, encoding="utf-8", timeout=10)
            self.assertEqual(r.returncode, 0)
            return r.stdout.strip()
        out = json.loads(run(TASKS[2]))
        self.assertEqual(out["hookSpecificOutput"]["hookEventName"], "UserPromptSubmit")
        self.assertIn("prompt-director (intake)", out["hookSpecificOutput"]["additionalContext"])
        self.assertEqual(run(NOT_TASKS[0]), "")

    def test_bad_input_never_fails(self):
        for raw in ("not json", '{"prompt": 123}', '{"prompt": ["x"]}', "[]"):
            with self.subTest(raw=raw):
                r = subprocess.run([sys.executable, "-I", str(HOOK)], input=raw, capture_output=True, text=True, timeout=10)
                self.assertEqual(r.returncode, 0)
                self.assertEqual(r.stdout.strip(), "")

    def test_bom_input_still_classified(self):
        raw = ("﻿" + json.dumps({"prompt": TASKS[2]})).encode("utf-8")
        r = subprocess.run([sys.executable, "-I", str(HOOK)], input=raw, capture_output=True, timeout=10)
        self.assertEqual(r.returncode, 0)
        self.assertIn(b"prompt-director (intake)", r.stdout)

    def test_hostile_paste_is_fast(self):
        import time
        prompt = "<pasted_content id=\"a\">\n" * 200000 + "fă-mi un raport"
        t = time.perf_counter()
        mod.is_task(prompt)
        self.assertLess(time.perf_counter() - t, 1.0)


if __name__ == "__main__":
    unittest.main(verbosity=1)
