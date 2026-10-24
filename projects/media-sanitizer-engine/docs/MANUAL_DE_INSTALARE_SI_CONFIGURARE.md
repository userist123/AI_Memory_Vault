# MANUAL DE INSTALARE ȘI CONFIGURARE (SEC-MAN-01)
## Platforma de Sanitizare a Mediilor Clasificate (TOE-SSE-v1)

### 1. CERINȚE DE SISTEM MINIME

#### Stația Bootabilă Offline (TOE):
- Arhitectură: x86_64 cu suport UEFI Secure Boot activat.
- Memorie RAM: Minim 4 GB DDR4/DDR5.
- Conectivitate stocare: Porturi native M.2 NVMe (PCIe Gen3/4/5) și porturi interne SATA III.
- Modul TPM: TPM 2.0 integrat pe placa de bază (pentru sigilarea cheii de platformă a manifestelor).
- Rețea: **Deconectată fizic (fără cablu Ethernet, modul Wi-Fi/Bluetooth dezactivat din BIOS/UEFI)**.

#### Stația de Birou Windows (Punte Registratură - Non-TOE):
- Sistem de operare: Windows 10 / 11 Enterprise sau Windows Server.
- Cititoare de carduri: Minim 1 cititor USB Smartcard compatibil PC/SC (ISO 7816).
- Software adițional: Adobe Acrobat Reader (pentru vizualizare PAdES) și clientul Aplicației de Registratură Electronică.

---

### 2. CONSTRUIREA ȘI SCRIEREA MEDIULUI BOOTABIL (USB STICK)

1. Pe stația de build securizată, se rulează scriptul de împachetare:
   ```powershell
   python -m projects.media-sanitizer-engine.src.build_bootable_iso
   ```
2. Se verifică hash-ul SHA-256 afișat în terminal cu cel menționat în Raportul Tehnic de Evaluare ORNISS.
3. Se scrie imaginea pe un mediu USB utilizând modul `dd` sau utilitarul instituțional omologat:
   ```bash
   dd if=build_bootable/image.iso of=/dev/sdX bs=4M status=progress conv=fsync
   ```

---

### 3. CONFIGURAREA CITITOARELOR DE CARDURI CU CIP (SMARTCARDS)
- Driverul Smartcard trebuie să asigure comunicarea prin stiva standard Windows `Microsoft Base Smart Card Crypto Provider`.
- Se verifică funcționarea cititoarelor prin testul automat:
  ```powershell
  python -m pytest projects/media-sanitizer-engine/tests -k test_smartcard
  ```

---

### 4. PROCEDURA DE TESTARE A INTEGRITĂȚII
Înainte de darea în exploatare la nivelul structurii de securitate, se execută suita completă de teste de conformitate:
```powershell
python -m pytest projects/media-sanitizer-engine/tests -v
```
Toate cele 9 teste trebuie să raporteze starea `PASSED`.
