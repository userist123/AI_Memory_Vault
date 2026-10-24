> ⚠️ **AVERTISMENT DE SECURITATE — STATUS: ÎN REMEDIERE DUPĂ AUDIT STATIC**
> **NEVALIDAT PENTRU UTILIZARE OPERAȚIONALĂ PE MEDII CU INFORMAȚII CLASIFICATE.**
>
> În urma auditului static din 08.10.2026, au fost identificate neconformități critice legate de:
> - Emiterea verdictului `CONFORM_PURGED` din simulator fără verificare hardware reală;
> - Dispozitive hardcodate în interfața TUI;
> - Decuplarea driverului NVMe și interpretarea eronată a stărilor NVM Express `SSTAT`;
> - Semnături TPM și Smartcard simulate ca șiruri de caractere / hash-uri simple, fără apeluri criptografice reale;
> - Structură PAdES incompletă, fără container CMS/PKCS#7 valid;
> - Script ISO care genera doar structura de foldere, fără kernel, initramfs sau imagine binară.
>
> **PRINCIPIU DE ACCEPTARE:** Funcționalitățile sunt acceptate individual numai pe baza dovezilor de implementare și testare empirică. Nicio operațiune nu va fi marcată drept realizată dacă nu există codul și testul asociat.
>
> ### Matricea Curentă de Urmărire a Remadierilor:
> - [ ] **Etapa 1: Eliminarea succesului fals** (Simulatorul emite strict `SIMULATED_NOT_SANITIZED`; documente marcate DEMO; blocare flux la componente neimplementate; teste de regresie pentru audit).
> - [ ] **Etapa 2: Sanitizare hardware reală** (Descoperire reală sysfs/udev; parser NVMe aliniat Base Spec 2.2 cu stările `0x0`, `0x1`, `0x2`, `0x3`; timeout-uri și recuperare; eliminare declarații premature ATA/SCSI/Crypto Erase).
> - [ ] **Etapa 3: Imagine Bootabilă UEFI Reală** (Construire pachet binar complet: kernel minimal, initramfs cu runtime Python/ctypes, bootloader EFI, excludere etichetă Secure Boot până la semnare efectivă).
> - [ ] **Etapa 4: Integrare TPM și Verificare Manifest** (Integrare TSS2 / `/dev/tpm0` cu cheie asimetrică reală, verificare public-key a manifestului, eliminare verificare prefix text).
> - [ ] **Etapa 5: Smartcard și PAdES Criptografic** (Integrare PKCS#11 reală fără credențiale hardcodate; semnare container CMS/PKCS#7 peste `/ByteRange` pe document PDF real; validare cu instrument independent).
> - [ ] **Etapa 6: Dosar de Acceptare și Dovezi** (Statut explicit: IMPLEMENTAT / SIMULAT / TESTAT AUTOMAT / TESTAT PE HARDWARE / NEVALIDAT OPERAȚIONAL).
