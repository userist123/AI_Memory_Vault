"""
Aplicația Grafică Windows Desktop (GUI) pentru Puntea cu Registratura Electronică.
Oferă interfață vizuală nativă Windows pentru importul manifestelor din stația offline,
semnarea duală pe cartelele cu cip și exportul automat fără hârtie către registratură.
"""

import sys
import os
import json
from typing import Optional, List
import tkinter as tk
from tkinter import ttk, messagebox, filedialog

from .smartcard_auth import SmartcardAuthenticator, SmartcardError
from .registry_connector import RegistryBridgeClient, RegistryExportPackage
from .tpm_signer import TPMSigner, TPMVerificationError


class WindowsBridgeGUI(tk.Tk):
    def __init__(self, simulation_mode: bool = True, trusted_tpm_keys: Optional[List[str]] = None):
        super().__init__()
        self.title("Punte INFOSEC - Semnare Calificată & Registratură Electronică (HG 585/2002)")
        self.geometry("820x680")
        self.minsize(780, 600)
        
        self.simulation_mode = simulation_mode
        self.trusted_tpm_keys = trusted_tpm_keys
        self.bridge_client = RegistryBridgeClient(simulation_mode=simulation_mode)
        self.loaded_manifest = None
        self.manifest_file_path = None

        self._build_ui()

    def _build_ui(self):
        # Header Frame
        header = ttk.Frame(self, padding="15")
        header.pack(fill=tk.X)
        
        title_lbl = ttk.Label(
            header,
            text="Sistem Punte: Atestare Medii Sanitate & Registratură Digitală",
            font=("Segoe UI", 14, "bold"),
        )
        title_lbl.pack(anchor=tk.W)
        
        sub_lbl = ttk.Label(
            header,
            text="Semnătură Electronică Calificată (eIDAS / Legea 455/2001) | Integrare Fără Hârtie",
            font=("Segoe UI", 9, "italic"),
            foreground="#555555",
        )
        sub_lbl.pack(anchor=tk.W, pady=(2, 0))

        separator = ttk.Separator(self, orient=tk.HORIZONTAL)
        separator.pack(fill=tk.X, padx=15, pady=5)

        # Pas 1: Import Manifest
        step1_frame = ttk.LabelFrame(self, text=" Pasul 1: Import Manifest din Stația Bootabilă Offline ", padding="12")
        step1_frame.pack(fill=tk.X, padx=15, pady=8)

        self.btn_select_file = ttk.Button(step1_frame, text="Selectează Fișier Manifest (*.json)", command=self._select_manifest)
        self.btn_select_file.pack(side=tk.LEFT, padx=(0, 10))

        self.lbl_file_status = ttk.Label(step1_frame, text="Niciun fișier încărcat.", foreground="#666666")
        self.lbl_file_status.pack(side=tk.LEFT, fill=tk.X, expand=True)

        # Detalii Dispozitiv (Read-only Card)
        self.dev_info_frame = ttk.LabelFrame(self, text=" Detalii Suport & Verificare Hardware ", padding="12")
        self.dev_info_frame.pack(fill=tk.X, padx=15, pady=8)

        self.txt_dev_details = tk.Text(self.dev_info_frame, height=5, wrap=tk.WORD, font=("Consolas", 9), bg="#F8F9FA")
        self.txt_dev_details.pack(fill=tk.BOTH, expand=True)
        self.txt_dev_details.insert(tk.END, "Așteptare încărcare manifest...")
        self.txt_dev_details.config(state=tk.DISABLED)

        # Pas 2: Semnare Calificată (Cartele cu cip)
        step2_frame = ttk.LabelFrame(self, text=" Pasul 2: Atestare Comisie prin Cartele cu Cip (Control Dual) ", padding="12")
        step2_frame.pack(fill=tk.X, padx=15, pady=8)

        # Operator Row
        f_op = ttk.Frame(step2_frame)
        f_op.pack(fill=tk.X, pady=4)
        ttk.Label(f_op, text="Cartelă Operator (Slot 0):", width=25).pack(side=tk.LEFT)
        self.lbl_card_op = ttk.Label(f_op, text="Lt. Popescu Ion (OPERATOR_INFOSEC)", font=("Segoe UI", 9, "bold"))
        self.lbl_card_op.pack(side=tk.LEFT, padx=5)
        ttk.Label(f_op, text="PIN Cartelă:").pack(side=tk.LEFT, padx=(15, 5))
        self.ent_pin_op = ttk.Entry(f_op, show="*", width=8)
        self.ent_pin_op.insert(0, "1234")
        self.ent_pin_op.pack(side=tk.LEFT)

        # Witness Row
        f_wit = ttk.Frame(step2_frame)
        f_wit.pack(fill=tk.X, pady=4)
        ttk.Label(f_wit, text="Cartelă Martor (Slot 1):", width=25).pack(side=tk.LEFT)
        self.lbl_card_wit = ttk.Label(f_wit, text="Cpt. Ionescu Vasile (RESPONSABIL_SIC)", font=("Segoe UI", 9, "bold"))
        self.lbl_card_wit.pack(side=tk.LEFT, padx=5)
        ttk.Label(f_wit, text="PIN Cartelă:").pack(side=tk.LEFT, padx=(15, 5))
        self.ent_pin_wit = ttk.Entry(f_wit, show="*", width=8)
        self.ent_pin_wit.insert(0, "5678")
        self.ent_pin_wit.pack(side=tk.LEFT)

        # Pas 3: Registratură
        step3_frame = ttk.LabelFrame(self, text=" Pasul 3: Destinație & Transmitere în Registratura Electronică ", padding="12")
        step3_frame.pack(fill=tk.X, padx=15, pady=8)

        f_inv = ttk.Frame(step3_frame)
        f_inv.pack(fill=tk.X, pady=4)
        ttk.Label(f_inv, text="Nr. Inventar / Evidență SIC:", width=25).pack(side=tk.LEFT)
        self.ent_inv_num = ttk.Entry(f_inv, width=28)
        self.ent_inv_num.insert(0, "INV-SIC-2026-A118")
        self.ent_inv_num.pack(side=tk.LEFT, padx=5)

        # Action Buttons
        btn_box = ttk.Frame(self, padding="15")
        btn_box.pack(fill=tk.X, side=tk.BOTTOM)

        self.btn_submit = ttk.Button(
            btn_box,
            text="Semnează Calificat și Transmite la Registratură (Zero Hârtie)",
            command=self._process_and_export,
            state=tk.DISABLED,
        )
        self.btn_submit.pack(fill=tk.X, ipady=8)

    def _select_manifest(self):
        path = filedialog.askopenfilename(
            title="Selectați fișierul manifest exportat din stația bootabilă",
            filetypes=[("Fișiere JSON", "*.json"), ("Toate fișierele", "*.*")],
        )
        if not path:
            return

        try:
            with open(path, "r", encoding="utf-8") as f:
                data = json.load(f)

            if "device" not in data or "integrity" not in data:
                messagebox.showerror("Eroare", "Fișierul nu conține un manifest de sanitizare valid.")
                return

            # Verificare integritate și semnătură hardware TPM
            try:
                TPMSigner.verify_manifest_signature(
                    manifest_data=data,
                    trusted_public_keys=self.trusted_tpm_keys,
                )
                tpm_status = "VALIDATĂ CRIPTOGRAFIC (RSA-PSS)"
            except TPMVerificationError as v_err:
                messagebox.showerror("Eroare Criptografică TPM", f"Semnătura TPM a manifestului este invalidă:\n{str(v_err)}")
                return

            self.loaded_manifest = data
            self.manifest_file_path = path
            self.lbl_file_status.config(text=os.path.basename(path), foreground="#007ACC")

            # Afișare sumar
            dev = data["device"]
            disp = data.get("final_disposition", "N/A")
            lba_stat = data.get("lba_verification_status", "N/A")
            info = (
                f"Model: {dev.get('model_number')} | Serie (SN): {dev.get('serial_number')}\n"
                f"Capacitate: {dev.get('capacity_bytes', 0)/(1024**3):.1f} GB | Topologie: {dev.get('topology')}\n"
                f"Verdict Tehnic: {disp} | Verificare LBA: {lba_stat}\n"
                f"Pași de audit verificați: {data['integrity'].get('total_audit_steps')} | Semnătură TPM: {tpm_status}"
            )
            self.txt_dev_details.config(state=tk.NORMAL)
            self.txt_dev_details.delete("1.0", tk.END)
            self.txt_dev_details.insert(tk.END, info)
            self.txt_dev_details.config(state=tk.DISABLED)

            self.btn_submit.config(state=tk.NORMAL)

        except Exception as ex:
            messagebox.showerror("Eroare la citire", f"Nu s-a putut încărca manifestul: {str(ex)}")

    def _process_and_export(self):
        if not self.loaded_manifest:
            return

        pin_op = self.ent_pin_op.get().strip()
        pin_wit = self.ent_pin_wit.get().strip()
        inv_num = self.ent_inv_num.get().strip()

        if not pin_op or not pin_wit or not inv_num:
            messagebox.showwarning("Atenție", "Completați ambele coduri PIN și numărul de inventar SIC.")
            return

        try:
            target_file = self.bridge_client.prepare_and_sign_for_registry(
                raw_manifest=self.loaded_manifest,
                operator_pin=pin_op,
                witness_pin=pin_wit,
                sic_inventory_number=inv_num,
                output_folder="export_pentru_registratura",
            )

            msg = (
                "Pachetul nativ digital a fost semnat calificat cu succes de ambii membri ai comisiei!\n\n"
                f"Fișier generat pentru registratura electronică:\n{target_file}\n\n"
                "Statut: Nativ digital (Nu necesită tipărire pe suport de hârtie conform eIDAS)."
            )
            messagebox.showinfo("Succes - Transmis la Registratură", msg)
            self.btn_submit.config(state=tk.DISABLED)

        except SmartcardError as s_err:
            messagebox.showerror("Eroare Cartelă cu Cip", str(s_err))
        except Exception as ex:
            messagebox.showerror("Eroare la procesare", f"A apărut o problemă: {str(ex)}")


def launch_gui():
    app = WindowsBridgeGUI()
    app.mainloop()


if __name__ == "__main__":
    launch_gui()
