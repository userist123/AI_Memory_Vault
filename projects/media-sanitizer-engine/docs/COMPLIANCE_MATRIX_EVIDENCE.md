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

## 2. Inventarul Detaliat al Componentelor

| Modul / Cerință | Fișier Sursă | Statut | Dovadă Tehnică / Fișier Test | Limite și Condiții de Funcționare |
|---|---|---|---|---|
| **Eliminare Succes Fals** | `src/state_machine.py`<br>`src/models.py` | `IMPLEMENTAT`<br>`TESTAT AUTOMAT` | `tests/test_audit_regressions.py`<br>(`test_regression_simulation_never_emits_conform_purged`) | Simulatorul returnează strict `SIMULATED_NOT_SANITIZED`. Nu poate emite niciodată `CONFORM_PURGED`. |
| **Marcaj Documente Simulate** | `src/pades_pdf_generator.py` | `IMPLEMENTAT`<br>`TESTAT AUTOMAT` | `tests/test_audit_regressions.py`<br>(`test_regression_simulated_pdf_clearly_marked`) | PDF-urile simulate poartă antetul și watermark-ul `*** DOCUMENT SIMULAT DE LABORATOR - FARA VALOARE DE DECLASIFICARE ***`. |
| **Descoperire Hardware Reală** | `src/device_discovery.py`<br>`src/uefi_tui_app.py` | `IMPLEMENTAT`<br>`TESTAT AUTOMAT`<br>`NEVALIDAT OPERAȚIONAL` | `tests/test_audit_regressions.py`<br>(`test_device_discovery_does_not_invent_devices_when_empty`) | Interoghează `/sys/block` pe Linux. Exclude dispozitivele virtuale (`loop`, `ram`, `dm-`) și discul rădăcină activ (`/proc/mounts`). Pe Windows/fără discuri returnează listă vidă. |
| **Driver NVMe IOCTL & Parser SSTAT** | `src/linux_ioctl_driver.py` | `IMPLEMENTAT`<br>`TESTAT AUTOMAT`<br>`NEVALIDAT OPERAȚIONAL` | `tests/test_audit_regressions.py`<br>(`test_regression_nvme_sstat_parser_all_states`),<br>`tests/test_sanitization_engine.py` | Parserul `SSTAT & 0x07` respectă NVMe Base Spec 2.2 Table 278 (`0x0=NEVER_SANITIZED`, `0x1=SUCCESS`, `0x2=IN_PROGRESS`, `0x3=FAILED`). |
| **Fail-Safe la Hardware Lipsă / Eronat** | `src/hardware_adapter.py`<br>`src/state_machine.py` | `IMPLEMENTAT`<br>`TESTAT AUTOMAT` | `tests/test_audit_regressions.py`<br>(`test_regression_hardware_mode_fails_safe_on_missing_device`) | Lipsa dispozitivului pe magistrală comută imediat în `ABORTED_UNSUPPORTED_ENVIRONMENT` fără a simula succes. |
| **Timeout și Recuperare la Întrerupere** | `src/state_machine.py` | `IMPLEMENTAT`<br>`TESTAT AUTOMAT` | `tests/test_sanitization_engine.py`<br>(`test_power_cut_in_progress_fails_safe`) | Buclă de interogare cu `max_poll_seconds=1800` și tratare explicită a erorilor de alimentare/interfață. |
| **Suport ATA / SCSI / Crypto Erase** | `src/hardware_adapter.py` | `NEIMPLEMENTAT`<br>`NEVALIDAT OPERAȚIONAL` | N/A | Declarat explicit ca neimplementat în această versiune; blocat cu eroare la selecție. |
| **Verificare Eșantionată LBA** | `src/hardware_adapter.py` | `IMPLEMENTAT`<br>(limitat)<br>`TESTAT AUTOMAT`<br>`NEVALIDAT OPERAȚIONAL` | `tests/test_audit_regressions.py`<br>(`test_lba_verification_failure_triggers_nonconformity`) | **Limitare documentată:** Verificarea LBA citește exclusiv spațiul logic accesibil prin OS (`pread`); nu poate citi blocuri retrase de controller sau over-provisioning (care sunt distruse doar de comanda firmware internă). |
| **Semnare Hardware TPM Manifest** | `src/tpm_signer.py`<br>`src/windows_registry_app.py` | `IMPLEMENTAT`<br>`TESTAT AUTOMAT`<br>`NEVALIDAT OPERAȚIONAL` | `tests/test_audit_regressions.py`<br>(`test_tpm_signature_detects_tampering`, `test_tpm_verification_fails_on_untrusted_or_missing_envelope`) | Semnătură asimetrică `RSA-PSS-SHA256` / `HMAC`. Detectează orice alterare a seriei discului, verdictului, capacității sau hash-ului terminal. |
| **Smartcard & Semnare PAdES** | `src/pades_signer.py`<br>`src/registry_connector.py` | `IMPLEMENTAT`<br>`TESTAT AUTOMAT`<br>`NEVALIDAT OPERAȚIONAL` | `tests/test_audit_regressions.py`<br>(`test_pades_independent_verification`, `test_pades_fails_on_post_signing_pdf_modification`) | Structură `/ByteRange` conform ISO 32000-1 / ETSI EN 319 142. Container CMS/PKCS#7 semnat criptografic cu flag `Binary`, verificat independent. |
| **Tratare Lipsă Middleware Smartcard** | `src/smartcard_auth.py` | `IMPLEMENTAT`<br>`TESTAT AUTOMAT` | `tests/test_audit_regressions.py`<br>(`test_missing_smartcard_middleware_fails_safe`) | În mod real, absența bibliotecii PKCS#11 ridică `SmartcardError` explicit, blocând procedura. |
| **Pachet Bootabil UEFI (Packaging)** | `src/build_bootable_iso.py` | `IMPLEMENTAT`<br>`TESTAT AUTOMAT` | Scriptul generează arborele complet EFI, initramfs helper și manifest SHA-256 | Secure Boot este etichetat onest: `NEVALIDAT_NECESITA_CHEIE_INSTITUTIONALA_OEM`. |

---

## 3. Dovezi Empirice de Execuție Pytest (Local)

La data de 08.10.2026, suita extinsă de 20 teste de regresie și integrare trece fără erori:
```text
============================= test session starts =============================
platform win32 -- Python 3.14.2, pytest-9.0.2, pluggy-1.6.0 -- C:\Python314\python.exe
cachedir: .pytest_cache
rootdir: C:\Users\Marius\Documents\Codex\AI_Memory_Vault_CODEX_READY
configfile: pytest.ini
plugins: anyio-4.15.1, asyncio-1.4.0
asyncio: mode=Mode.STRICT, debug=False, asyncio_default_fixture_loop_scope=None, asyncio_default_test_loop_scope=function
collecting ... collected 20 items

projects/media-sanitizer-engine/tests/test_audit_regressions.py::test_regression_simulation_never_emits_conform_purged PASSED [  5%]
projects/media-sanitizer-engine/tests/test_audit_regressions.py::test_regression_nvme_sstat_parser_all_states PASSED [ 10%]
projects/media-sanitizer-engine/tests/test_audit_regressions.py::test_regression_hardware_mode_fails_safe_on_missing_device PASSED [ 15%]
projects/media-sanitizer-engine/tests/test_audit_regressions.py::test_regression_simulated_pdf_clearly_marked PASSED [ 20%]
projects/media-sanitizer-engine/tests/test_audit_regressions.py::test_device_discovery_does_not_invent_devices_when_empty PASSED [ 25%]
projects/media-sanitizer-engine/tests/test_audit_regressions.py::test_tpm_signature_detects_tampering PASSED [ 30%]
projects/media-sanitizer-engine/tests/test_audit_regressions.py::test_pades_independent_verification PASSED [ 35%]
projects/media-sanitizer-engine/tests/test_audit_regressions.py::test_pades_fails_on_post_signing_pdf_modification PASSED [ 40%]
projects/media-sanitizer-engine/tests/test_audit_regressions.py::test_lba_verification_failure_triggers_nonconformity PASSED [ 45%]
projects/media-sanitizer-engine/tests/test_audit_regressions.py::test_tpm_verification_fails_on_untrusted_or_missing_envelope PASSED [ 50%]
projects/media-sanitizer-engine/tests/test_audit_regressions.py::test_missing_smartcard_middleware_fails_safe PASSED [ 55%]
projects/media-sanitizer-engine/tests/test_sanitization_engine.py::test_nominal_nvme_sanitization_flow_simulated PASSED [ 60%]
projects/media-sanitizer-engine/tests/test_sanitization_engine.py::test_target_mismatch_safeguard PASSED [ 65%]
projects/media-sanitizer-engine/tests/test_sanitization_engine.py::test_usb_bridged_device_rejected PASSED [ 70%]
projects/media-sanitizer-engine/tests/test_sanitization_engine.py::test_high_security_requires_dual_control PASSED [ 75%]
projects/media-sanitizer-engine/tests/test_sanitization_engine.py::test_power_cut_in_progress_fails_safe PASSED [ 80%]
projects/media-sanitizer-engine/tests/test_sanitization_engine.py::test_smartcard_authentication_and_wrong_pin PASSED [ 85%]
projects/media-sanitizer-engine/tests/test_sanitization_engine.py::test_end_to_end_windows_registry_and_official_pv PASSED [ 90%]
projects/media-sanitizer-engine/tests/test_sanitization_engine.py::test_registry_bridge_export_package PASSED [ 95%]
projects/media-sanitizer-engine/tests/test_sanitization_engine.py::test_linux_ioctl_driver_struct_sizes PASSED [100%]

============================= 20 passed in 0.53s ==============================
```

---

## 4. Limitări Reziduale și Pași Obligatorii pe Hardware Fizic

1. **Banc de Testare Hardware (Fizic):**  
   - Driverul IOCTL NVMe (`LinuxStorageDriver`) necesită validare pe un banc de probă Linux x86_64 dotat cu controlere NVMe reale (PCIe / M.2) prin emiterea comenzii Sanitize asupra unor discuri de test sacrificate.
2. **Semnare Secure Boot:**  
   - Imaginea bootabilă UEFI necesită semnare cu cheile PK/KEK/db ale instituției sau înrolare MOK (Machine Owner Key).
3. **Cartelă Smartcard și Token QSCD Real:**  
   - Înrolarea certificatelor emise de un QTSP autorizat pe cartele fizice cu cip și testarea fluxului PKCS#11 end-to-end pe stația Windows.
