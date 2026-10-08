# Platformă Autonomă de Sanitizare a Mediilor cu Informații Clasificate (TOE-SSE-v1)

Proiect acreditatabil conform metodologiei **INFOSEC 14 (ORNISS)**, **HG nr. 585/2002**, **NIST SP 800-88 Rev. 2** și **IEEE 2883-2022**.

> ⚠️ **STATUT ACTUAL:** În curs de remediere după audit static. Rămâne în modul DRAFT până la validarea pe banc hardware dedicat.
> Consultați [Dosarul de Dovezi și Matricea de Conformitate](docs/COMPLIANCE_MATRIX_EVIDENCE.md) pentru statutul exact al fiecărei componente.

---

## 1. Arhitectura Sistemului (Hibridă / Zero-Hârtie)

```text
[ MEDIU BOOTABIL UEFI OFFLINE (TOE) ]
   ├── Kernel minimal imutabil (Air-gapped, fără rețea, fără shell)
   ├── Motor de execuție C/Python (NVMe Sanitize, ATA Sanitize)
   ├── Protecție anti-ștergere eronată (confirmare sufix serie hardware)
   ├── Verificare eșantionată LBA post-ștergere
   └── Semnare TPM hardware a manifestului tehnic
          │
          ▼ (Export stick USB dedicat)
   [ manifest_<SN>.json ]
          │
          ▼
[ APLICAȚIA WINDOWS DE BIROU - PUNTE REGISTRATURĂ (NON-TOE) ]
   ├── Citire manifest și validare semnătură TPM
   ├── Autentificare Operator prin Cartelă cu Cip (Slot 0 + PIN)
   ├── Autentificare Martor / Resp. SIC prin Cartelă cu Cip (Slot 1 + PIN)
   ├── Aplicare semnături electronice calificate (PAdES / eIDAS)
   └── Export pachet nativ digital direct în Registratura Electronică a SIC
```

---

## 2. Structura Modulelor

| Fișier | Rol și Descriere |
|---|---|
| [`docs/SECURITY_TARGET_TOE.md`](docs/SECURITY_TARGET_TOE.md) | Ținta de securitate (TOE Boundary, Amenințări, Cerințe SFRs Common Criteria). |
| [`src/models.py`](src/models.py) | Modele de date, niveluri de clasificare (RO/NATO/UE) și atribute hardware. |
| [`src/policy_engine.py`](src/policy_engine.py) | Motorul de politici (blochează punțile USB opace, impune distrugerea mecanică). |
| [`src/hardware_adapter.py`](src/hardware_adapter.py) | Abstracția comenzilor firmware de nivel jos și simulator de erori/pene de curent. |
| [`src/state_machine.py`](src/state_machine.py) | Mașina de stări cu protecție anti-mismatch și control dual. |
| [`src/evidence_chain.py`](src/evidence_chain.py) | Lanț de audit append-only SHA-256 chained. |
| [`src/smartcard_auth.py`](src/smartcard_auth.py) | Modul de integrare cu cartele cu cip pentru semnătură electronică calificată. |
| [`src/registry_connector.py`](src/registry_connector.py) | Conectorul de export nativ digital către aplicația existentă de registratură. |
| [`src/uefi_tui_app.py`](src/uefi_tui_app.py) | Consola text (TUI) interactivă pentru stația bootabilă UEFI. |
| [`src/windows_bridge_gui.py`](src/windows_bridge_gui.py) | Aplicația Desktop Windows (GUI) pentru import stick, semnare carduri și export. |
| [`src/build_bootable_iso.py`](src/build_bootable_iso.py) | Generatorul imaginii de distribuție bootabile UEFI cu hash de proveniență. |

---

## 3. Rulare și Utilizare

### Rularea Suitei de Teste (Pytest)
```powershell
python -m pytest projects/media-sanitizer-engine/tests -v
```

### Lansarea Consolei Bootabile Offline (TUI)
```powershell
python -m projects.media-sanitizer-engine.src.cli
```

### Lansarea Aplicației Grafice Windows (Punte Registratură)
```powershell
python -m projects.media-sanitizer-engine.src.windows_bridge_gui
```

### Construirea Pachetului de Distribuție Bootabil
```powershell
python -m projects.media-sanitizer-engine.src.build_bootable_iso
```
