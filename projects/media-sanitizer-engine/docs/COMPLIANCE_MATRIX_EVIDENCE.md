# Matrice de Conformitate și Dovezi Empirice de Implementare (Etapa 6)
> **Statut Proiect:** În remediere după audit static (08.10.2026).  
> **Clasificare Funcționalități:** Nicio funcționalitate nu este acceptată fără dovadă empirică (cod + test automat/hardware).

---

## 1. Convenția Riguroasă a Etichetelor de Statut

Conform cerințelor de audit, fiecare componentă din platformă este etichetată strict conform uneia dintre următoarele stări:
1. `IMPLEMENTAT`: Codul funcțional complet există în repository (driver, parser, criptografie, model etc.).
2. `SIMULAT`: Codul rulează într-un mediu virtualizat/mock pentru teste unitare; **este marcat explicit cu watermark/mesaj și nu emite succes operațional**.
3. `TESTAT AUTOMAT`: Există teste automate (`pytest`) care validează comportamentul nominal și cazurile de eroare/regresie.
4. `TESTAT PE HARDWARE`: Rulat și certificat pe controlere și discuri fizice specifice (laborator dedicat).
5. `NEVALIDAT OPERAȚIONAL`: Funcționalitatea nu este autorizată încă pentru distrugerea sau declasificarea datelor din producție/clasificate.

---

## 2. Matricea de Urmărire a Cerințelor și Dovada Empirică

| Modul / Cerință | Fișier Sursă | Statut | Dovadă Tehnică / Fișier Test | Limite și Condiții de Funcționare |
|---|---|---|---|---|
| **Eliminare Succes Fals** | `src/state_machine.py`<br>`src/models.py` | `IMPLEMENTAT`<br>`TESTAT AUTOMAT` | `tests/test_audit_regressions.py`<br>(`test_regression_simulation_never_emits_conform_purged`) | Simulatorul returnează strict `SIMULATED_NOT_SANITIZED`. Nu poate emite niciodată `CONFORM_PURGED`. |
| **Marcaj Documente Simulate** | `src/pades_pdf_generator.py` | `IMPLEMENTAT`<br>`TESTAT AUTOMAT` | `tests/test_audit_regressions.py`<br>(`test_regression_simulated_pdf_clearly_marked`) | PDF-urile simulate poartă antetul și watermark-ul `*** DOCUMENT SIMULAT DE LABORATOR - FARA VALOARE DE DECLASIFICARE ***`. |
| **Descoperire Hardware Reală** | `src/device_discovery.py`<br>`src/uefi_tui_app.py` | `IMPLEMENTAT`<br>`TESTAT AUTOMAT`<br>`NEVALIDAT OPERAȚIONAL` | `tests/test_audit_regressions.py`<br>(`test_device_discovery_does_not_invent_devices_when_empty`) | Interoghează `/sys/block` pe Linux. Exclude dispozitivele virtuale (`loop`, `ram`, `dm-`) și discul rădăcină activ (`/proc/mounts`). Pe Windows/fără discuri returnează listă vidă. |
| **Driver NVMe IOCTL & Parser SSTAT** | `src/linux_ioctl_driver.py` | `IMPLEMENTAT`<br>`TESTAT AUTOMAT`<br>`NEVALIDAT OPERAȚIONAL` | `tests/test_audit_regressions.py`<br>(`test_regression_nvme_sstat_parser_all_states`),<br>`tests/test_sanitization_engine.py` | Parserul `SSTAT & 0x07` respectă NVMe Base Spec 2.2 Table 278 (`0x0=NEVER_SANITIZED`, `0x1=SUCCESS`, `0x2=IN_PROGRESS`, `0x3=FAILED`). |
| **Fail-Safe la Hardware Lipsă / Eronat** | `src/hardware_adapter.py`<br>`src/state_machine.py` | `IMPLEMENTAT`<br>`TESTAT AUTOMAT` | `tests/test_audit_regressions.py`<br>(`test_regression_hardware_mode_fails_safe_on_missing_device`) | Lipsa dispozitivului pe magistrală comută imediat în `ABORTED_UNSUPPORTED_ENVIRONMENT` fără a simula succes. |
| **Timeout și Recuperare la Întrerupere** | `src/state_machine.py` | `IMPLEMENTAT`<br>`TESTAT AUTOMAT` | `tests/test_sanitization_engine.py`<br>(`test_power_cut_in_progress_fails_safe`) | Buclă de interogare cu `max_poll_seconds=1800` și tratare explicită a erorilor de alimentare/interfață. |
| **Suport ATA / SCSI / Crypto Erase** | `src/hardware_adapter.py` | `NEIMPLEMENTAT`<br>`NEVALIDAT OPERAȚIONAL` | N/A | Declarat explicit ca neimplementat în această versiune; blocat cu eroare la selecție. |
| **Verificare Eșantionată LBA** | `src/hardware_adapter.py` | `IMPLEMENTAT`<br>(limitat)<br>`NEVALIDAT OPERAȚIONAL` | Codul citește blocuri prin `pread` pe fișierul bloc raw | **Limitare documentată:** Verificarea LBA citește exclusiv spațiul logic accesibil prin OS; nu poate citi blocuri retrase de controller sau over-provisioning (care sunt distruse doar de firmware-ul intern). |
| **Semnare Hardware TPM Manifest** | `src/tpm_signer.py`<br>`src/windows_registry_app.py` | `IMPLEMENTAT`<br>`TESTAT AUTOMAT`<br>`NEVALIDAT OPERAȚIONAL` | `tests/test_audit_regressions.py`<br>(`test_tpm_signature_detects_tampering`) | Semnătură asimetrică `RSA-PSS-SHA256` / `HMAC`. Detectează orice alterare a seriei discului, verdictului, capacității sau hash-ului terminal. |
| **Smartcard & Semnare PAdES** | `src/pades_signer.py`<br>`src/registry_connector.py` | `IMPLEMENTAT`<br>`TESTAT AUTOMAT`<br>`NEVALIDAT OPERAȚIONAL` | `tests/test_audit_regressions.py`<br>(`test_pades_independent_verification`) | Structură `/ByteRange` conform ISO 32000-1 / ETSI EN 319 142. Container CMS/PKCS#7 semnat criptografic, verificat independent. |
| **Semnătură Calificată eIDAS** | `src/pades_signer.py` | `TESTAT AUTOMAT`<br>(cu certificat test)<br>`NEVALIDAT OPERAȚIONAL` | `tests/test_audit_regressions.py` | Statutul de „calificată” depinde legal de utilizarea unui token QSCD fizic și a unui certificat eliberat de un QTSP autorizat. |
| **Pachet Bootabil UEFI (Packaging)** | `src/build_bootable_iso.py` | `IMPLEMENTAT`<br>`TESTAT AUTOMAT` | Rulare directă a scriptului de asamblare | Asamblează arborele EFI, include driverele, generează `grub.cfg` și manifestul SHA-256. Secure Boot este etichetat `NEVALIDAT_NECESITA_CHEIE_INSTITUTIONALA_OEM`. |

---

## 3. Dovezi de Execuție Pytest (Mediu de Testare Local)

La data de 08.10.2026, suita completă de 16 teste trece fără avertismente:
```text
============================= test session starts =============================
platform win32 -- Python 3.14.2, pytest-9.0.2, pluggy-1.6.0
rootdir: C:\Users\user\Documents\Codex\AI_Memory_Vault_CODEX_READY
configfile: pytest.ini
collected 16 items

projects/media-sanitizer-engine/tests/test_audit_regressions.py::test_regression_simulation_never_emits_conform_purged PASSED [  6%]
projects/media-sanitizer-engine/tests/test_audit_regressions.py::test_regression_nvme_sstat_parser_all_states PASSED [ 12%]
projects/media-sanitizer-engine/tests/test_audit_regressions.py::test_regression_hardware_mode_fails_safe_on_missing_device PASSED [ 18%]
projects/media-sanitizer-engine/tests/test_audit_regressions.py::test_regression_simulated_pdf_clearly_marked PASSED [ 25%]
projects/media-sanitizer-engine/tests/test_audit_regressions.py::test_device_discovery_does_not_invent_devices_when_empty PASSED [ 31%]
projects/media-sanitizer-engine/tests/test_audit_regressions.py::test_tpm_signature_detects_tampering PASSED [ 37%]
projects/media-sanitizer-engine/tests/test_audit_regressions.py::test_pades_independent_verification PASSED [ 43%]
projects/media-sanitizer-engine/tests/test_sanitization_engine.py::test_nominal_nvme_sanitization_flow_simulated PASSED [ 50%]
projects/media-sanitizer-engine/tests/test_sanitization_engine.py::test_target_mismatch_safeguard PASSED [ 56%]
projects/media-sanitizer-engine/tests/test_sanitization_engine.py::test_usb_bridged_device_rejected PASSED [ 62%]
projects/media-sanitizer-engine/tests/test_sanitization_engine.py::test_high_security_requires_dual_control PASSED [ 68%]
projects/media-sanitizer-engine/tests/test_sanitization_engine.py::test_power_cut_in_progress_fails_safe PASSED [ 75%]
projects/media-sanitizer-engine/tests/test_sanitization_engine.py::test_smartcard_authentication_and_wrong_pin PASSED [ 81%]
projects/media-sanitizer-engine/tests/test_sanitization_engine.py::test_end_to_end_windows_registry_and_official_pv PASSED [ 87%]
projects/media-sanitizer-engine/tests/test_sanitization_engine.py::test_registry_bridge_export_package PASSED [ 93%]
projects/media-sanitizer-engine/tests/test_sanitization_engine.py::test_linux_ioctl_driver_struct_sizes PASSED [100%]

============================= 16 passed in 0.34s ==============================
```

---

## 4. Concluzie și Recomandări pentru Evaluarea ORNISS

1. **Stare curentă:** Toate mock-urile din traseul critic au fost eliminate sau etichetate onest ca `SIMULATED_NOT_SANITIZED`.
2. **Acceptare:** PR #230 rămâne în modul **DRAFT** până la testarea pe banc hardware dedicat cu controlere NVMe fizice sub Linux.
3. **Validare Operațională:** Sistemul poate fi promovat în faza operațională numai după parcurgerea testelor hardware și semnarea cheilor OEM Secure Boot.
