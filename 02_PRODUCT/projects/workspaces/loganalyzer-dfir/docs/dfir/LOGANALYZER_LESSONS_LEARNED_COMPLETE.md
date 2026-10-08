# LOGANALYZER — LESSONS LEARNED
## DFIR + Audit + Air-Gapped Networks + NIS2 + HG 585/2002 + Information Classification

### Scop

Acest document transforma in cerinte de proiectare pentru LogAnalyzer lectiile rezultate din:
- testarea reala a unei platforme DFIR/forensic;
- discutiile despre Veritas;
- cerintele pentru LogAnalyzer;
- mediile Windows/AD/GPO;
- retele air-gapped;
- protectia informatiilor clasificate;
- NIS2;
- HG 585/2002;
- analiza jurnalelor, a mediilor de stocare si a activitatii utilizatorilor.

LogAnalyzer trebuie sa fie un sistem de **evidence-driven investigation**, nu doar un viewer de Event Viewer.

---

# 1. Principiul fundamental

LogAnalyzer trebuie sa raspunda la:

```text
CE s-a intamplat?
CAND?
CINE?
DE UNDE?
CU CE CONT?
CU CE PRIVILEGII?
CU CE PROCES?
ASUPRA CARUI OBIECT?
PRIN CE SISTEM?
CE DOVADA AVEM?
CE ALTE SURSE CONFIRMA?
CE CONTRADICTII EXISTA?
CE LIPSESTE?
CAT DE SIGUR ESTE?
CE TREBUIE FACUT?
```

Pipeline:

```text
COLLECT
   ↓
NORMALIZE
   ↓
PARSE
   ↓
VALIDATE
   ↓
CORRELATE
   ↓
VERIFY
   ↓
EXPLAIN
   ↓
REPORT
   ↓
AUDIT
```

---

# 2. Lectia din Veritas: artefactul nu este automat actiune

Exemple:

```text
LNK gasit
    !=
fisier deschis demonstrat
```

```text
Amcache
    !=
executie demonstrata
```

```text
ShimCache
    !=
executie demonstrata
```

```text
Jump List
    !=
document deschis demonstrat
```

```text
USB conectat
    !=
fisiere copiate demonstrat
```

```text
CD introdus
    !=
date transferate demonstrat
```

```text
email existent
    !=
email trimis demonstrat
```

```text
fisier gasit pe NAS
    !=
fisier descarcat demonstrat
```

Fiecare parser trebuie sa documenteze:

```text
WHAT IT CAN PROVE
WHAT IT CANNOT PROVE
LIMITATIONS
CORRELATION SOURCES
CONFIDENCE
```

---

# 3. Stari standard ale dovezii

LogAnalyzer trebuie sa foloseasca un vocabular controlat:

```text
OBSERVED
CORRELATED
SUPPORTED
VERIFIED
INFERRED
UNPROVEN
CONTRADICTED
REJECTED
UNKNOWN
NOT_ASSESSED
```

Nu trebuie sa transforme automat:

```text
UNKNOWN -> NO
CORRELATED -> PROVEN
INFERRED -> VERIFIED
```

---

# 4. Coverage si confidence sunt obligatorii

Un rezultat trebuie sa afiseze:

```text
Evidence coverage
Parser coverage
Source availability
Critical sources missing
Verdict confidence
```

Exemplu:

```text
Risk signal: LOW
Evidence coverage: PARTIAL
Verdict confidence: LIMITED

Missing:
- Security.evtx 02:00-03:00
- proxy logs
- NAS audit logs
- removable-media telemetry
```

Regula:

> Lipsa dovezii nu inseamna dovada absentei.

---

# 5. Parser health

Pentru fiecare sursa:

```text
AVAILABLE
COLLECTED
PARSED
PARTIAL
UNSUPPORTED
INVALID
ERROR
BLOCKED
NOT_PRESENT
NOT_ENABLED
```

Parser errors trebuie sa fie vizibile in raport.

---

# 6. Rapoarte

Un raport foarte mare nu este automat un raport forensic bun.

LogAnalyzer trebuie sa genereze:

## 6.1 Raport de concluzie

```text
Ce s-a intamplat?
Ce este demonstrat?
Ce este posibil?
Ce nu poate fi stabilit?
Ce dovezi exista?
Ce lipseste?
Ce contradictii exista?
Ce trebuie facut?
```

## 6.2 Raport tehnic

```text
raw events
artifacts
hashes
sources
parser status
errors
timestamps
provenance
chain of custody
correlations
rules
evidence gaps
```

Principiu:

> Nu simplifica dovada. Simplifica prezentarea.

---

# 7. AI-ul este interpret, nu autoritate

AI-ul poate:

```text
correlation
timeline reasoning
summarization
hypothesis generation
contradiction detection
explanation
report drafting
```

AI-ul NU poate:

```text
invent evidence
upgrade UNKNOWN -> VERIFIED
treat correlation as proof
hide missing evidence
override evidence rules
override authority
```

Pipeline:

```text
RAW
 ↓
NORMALIZED
 ↓
VERIFIED FINDINGS
 ↓
AI REASONING
 ↓
HUMAN-READABLE REPORT
```

---

# 8. Audit Log Lifecycle

Nu confunda:

```text
WRITE
EXPORT
ARCHIVE
VERIFY
ROTATE
CLEAR
DELETE
RESTORE
PURGE
```

Un `Security log cleared` nu trebuie considerat automat atac.

Un mediu operational poate avea:

```text
export lunar
 ↓
hash
 ↓
salvare pe mediu readonly/controlat
 ↓
verificare
 ↓
clear local
 ↓
nou ciclu de logging
```

Aceasta poate fi procedura normala.

---

# 9. Clear lunar si programul de lucru

Exemplu de politica:

```text
Normal working hours:
07:00 - 17:00

After-hours:
< 07:00
sau
> 17:00
```

After-hours nu inseamna automat suspicious.

Daca politica spune ca rotatia lunara se face dupa program:

```text
18:30
export
hash
archive
verification
clear
```

rezultat:

```text
ROUTINE / EXPECTED LOG ROTATION
```

Nu:

```text
SUSPICIOUS
```

Un clear devine interesant cand deviaza de la procedura:

```text
clear
+
no export
+
no archive
+
outside expected schedule
+
incident activity
```

Rezultat:

```text
UNEXPECTED / REQUIRES REVIEW
```

Nu trebuie sarit automat la "anti-forensics".

---

# 10. HG 585/2002 — ce trebuie sa inteleaga LogAnalyzer

HG 585/2002 include reguli privind:
- evidenta;
- accesarea;
- procesarea;
- multiplicarea;
- manipularea;
- transportul;
- transmiterea;
- inventarierea;
- pastrarea;
- arhivarea;
- distrugerea informatiilor clasificate;
- INFOSEC;
- mediile de stocare;
- accesul electronic.

HG 585 art. 291 prevede ca evidenta automata a accesului la informatiile clasificate in format electronic se tine in registre de acces si trebuie realizata prin software. Pentru nivelurile strict secret de importanta deosebita, strict secret si secret exista perioade minime de pastrare de 10 ani, respectiv cel putin 3 ani. 

Pentru LogAnalyzer, aceasta trebuie transformata intr-un modul de **Access Evidence**, nu doar intr-un parser de Event Viewer.

---

# 11. HG 585 — Access Evidence

Pentru fiecare acces relevant:

```text
USER
ACCOUNT
CLEARANCE / AUTHORIZATION CONTEXT
NEED-TO-KNOW CONTEXT (daca este disponibil)
HOST
ZONE
RESOURCE
DOCUMENT
CLASSIFICATION
ACTION
PROCESS
TIMESTAMP
SOURCE
RESULT
CORRELATION
```

Actiuni:

```text
OPEN
READ
WRITE
CREATE
MODIFY
COPY
MOVE
RENAME
DELETE
PRINT
EXPORT
TRANSMIT
UPLOAD
DOWNLOAD
SHARE
PERMISSION_CHANGE
CLASSIFICATION_CHANGE
```

Important:

```text
file exists
!=
file was read
```

```text
file accessed
!=
file content was copied
```

```text
copy event
!=
successful transmission outside the zone
```

Trebuie demonstrata secventa completa.

---

# 12. Clasificarea documentelor

Pentru documente trebuie detectate si corelate, unde este posibil:

```text
UNCLASSIFIED / PUBLIC
NATO UNCLASSIFIED
NATO RESTRICTED
NATO CONFIDENTIAL
NATO SECRET
COSMIC TOP SECRET

NATIONAL:
SECRET DE SERVICIU
SECRET
STRICT SECRET
STRICT SECRET DE IMPORTANTA DEOSEBITA
```

HG 585 stabileste echivalentele nationale cu nivelurile NATO:
- Strict secret de importanta deosebita -> NATO Top Secret;
- Strict secret -> NATO Secret;
- Secret -> NATO Confidential;
- Secret de serviciu -> NATO Restricted.

NATO publica oficial nivelurile NATO RESTRICTED, NATO CONFIDENTIAL, NATO SECRET si COSMIC TOP SECRET. NATO UNCLASSIFIED este o categorie distincta de informatii neclasificate, dar gestionata ca informatie NATO cu reguli proprii. 

---

# 13. Detectarea documentelor clasificate

LogAnalyzer trebuie sa poata identifica, cand exista date suficiente:

```text
document classification marking
file extension
document metadata
filename
path
content classification markers
DLP classification tags
IRM labels
document management labels
share classification
storage classification
```

Exemple de finding:

```text
NATO SECRET document detected
Location: \\SERVER\Share\...
User: ...
Timestamp: ...
```

sau:

```text
STRICT SECRET document observed on storage classified below expected level
```

Dar NU trebuie sa inventeze clasificarea doar din numele fisierului.

---

# 14. "Document mai mare decat nivelul permis"

Aceasta este o regula foarte importanta.

LogAnalyzer trebuie sa compare:

```text
DOCUMENT CLASSIFICATION
        vs
STORAGE CLASSIFICATION
        vs
SYSTEM/ZONE CLASSIFICATION
        vs
USER AUTHORIZATION
        vs
NEED-TO-KNOW
```

Exemplu:

```text
Document: NATO SECRET
Storage: NATO RESTRICTED
```

=> **CLASSIFICATION MISMATCH — REQUIRES REVIEW**

Alt exemplu:

```text
Document: STRICT SECRET
Storage: SECRET
```

=> **CLASSIFICATION MISMATCH**

Nu trebuie sa fie tratat doar ca simplu "fisier suspect".

---

# 15. Medii de stocare

HG 585 prevede ca mediile de stocare ale informatiilor clasificate sunt identificate si controlate corespunzator nivelului de clasificare. Pentru nivelurile superioare exista cerinte de evidenta si inventariere periodica. Mediile refolosibile isi mentin cea mai inalta clasificare folosita anterior pana la reclasificare/declasificare sau distrugere conform procedurilor.

LogAnalyzer trebuie sa poata urmari:

```text
DEVICE ID
SERIAL
TYPE
OWNER
CLASSIFICATION
ZONE
ASSIGNED USER
FIRST SEEN
LAST SEEN
CONNECTED
DISCONNECTED
MOUNTED
UNMOUNTED
FILES ACCESSED
FILES COPIED
FILES WRITTEN
FILES DELETED
TRANSFER DIRECTION
AUTHORIZATION
```

---

# 16. USB si medii amovibile

Detecteaza:

```text
USB insertion
USB removal
device enumeration
VID
PID
serial
manufacturer
model
volume serial
filesystem
drive letter
mount point
first seen
last seen
user
host
```

Coreleaza cu:

```text
file creation
file copy
file modification
archive creation
hash
process
PowerShell
Explorer
robocopy
xcopy
cmd
third-party copy tools
Defender
DLP
```

Important:

```text
USB inserted
!=
data copied
```

Dar:

```text
USB inserted
+
new file writes
+
matching hashes
+
copy process
=
SUPPORTED COPY ACTIVITY
```

---

# 17. CD/DVD/optical media

Trebuie tratate separat de USB.

Detecteaza:

```text
CD/DVD inserted
CD/DVD removed
optical drive activity
filesystem mounted
read activity
write/burn activity
disc metadata
user
process
timestamp
```

Coreleaza:

```text
ISO creation
disc burning software
IMAPI activity
file staging
archive creation
hashes
device insertion
```

Exemplu:

```text
ISO created
+
CD inserted
+
burn operation
+
classified files staged
```

=> finding cu prioritate ridicata pentru review.

Nu:

```text
CD inserted = data exfiltration
```

---

# 18. Introducerea de medii neautorizate

LogAnalyzer trebuie sa aiba un control:

```text
AUTHORIZED MEDIA
vs
OBSERVED MEDIA
```

Exemplu:

```text
Device serial: ABC123
Observed: YES
Registered: NO
Authorized: UNKNOWN
```

Rezultat:

```text
UNAUTHORIZED / UNREGISTERED MEDIA — REQUIRES REVIEW
```

Daca politica spune ca device-ul este interzis:

```text
POLICY VIOLATION
```

Nu trebuie confundat cu dovada ca datele au fost copiate.

---

# 19. Media sanitization

Detecteaza:

```text
secure erase
format
diskpart clean
cipher /w
manufacturer erase tools
repartition
filesystem recreation
device retirement
```

Coreleaza cu:

```text
device inventory
classification
last known user
last known data
authorization
retirement record
```

Finding:

```text
Classified storage device sanitized
Authorization evidence: ...
Previous classification: ...
Sanitization evidence: ...
```

---

# 20. Air-gapped network — regula de baza

Air-gapped nu inseamna:

```text
nu exista risc de transfer
```

Trebuie analizate canalele prin care informatia poate iesi sau intra:

```text
USB
CD/DVD
portable HDD/SSD
laptop
phone
camera
Bluetooth
Wi-Fi adapter
network adapter
temporary network connection
maintenance laptop
KVM
console
serial
shared printer
scanner
fax
removable backup
manual transfer
```

LogAnalyzer trebuie sa construiasca o harta:

```text
ZONE A
  |
  | approved transfer?
  |
ZONE B
  |
  | removable media?
  |
OFFLINE / EXTERNAL
```

---

# 21. Detectarea conectarii la retea intr-un sistem air-gapped

Foarte important:

```text
NIC enabled
NIC disabled
link up
link down
DHCP request
DHCP lease
IP assigned
gateway assigned
DNS configured
route created
ARP activity
network profile change
firewall profile change
VPN
Wi-Fi association
Bluetooth PAN
```

Finding:

```text
Air-gapped endpoint
+
network interface became active
+
IP assigned
+
route created
```

=> **NETWORK CONNECTIVITY OBSERVED**

Nu trebuie presupus automat ca date au fost transferate.

---

# 22. NIC / Wi-Fi / Bluetooth

Detecteaza:

```text
adapter installation
adapter enable/disable
driver installation
device arrival
Wi-Fi association
SSID
BSSID
Bluetooth pairing
Bluetooth device connection
network profile creation
```

Coreleaza cu:

```text
user
device
time
process
file activity
network traffic
```

---

# 23. Retele: ce trebuie sa poata analiza

Pentru o retea air-gapped sau izolata:

```text
hosts
switches
routers
firewalls
DNS
DHCP
proxy
VPN
IDS/IPS
EDR
SIEM
RADIUS
802.1X
NAC
```

LogAnalyzer trebuie sa accepte importuri din acestea, nu sa presupuna ca toate exista pe endpoint.

---

# 24. Email

Intr-un mediu cu email intern/offline sau email conectat controlat, trebuie analizate:

```text
mailbox login
mailbox access
message sent
message received
message deleted
message moved
attachment added
attachment downloaded
attachment uploaded
forward
reply
external recipient
internal recipient
mailbox permission change
delegated access
shared mailbox access
rule creation
rule modification
auto-forward
transport rule
admin access
```

Coreleaza cu:

```text
document access
file creation
archive creation
USB activity
network activity
classification
user
```

Exemplu:

```text
Classified document opened
+
attachment created
+
email sent
+
recipient outside authorized group
```

=> **HIGH-PRIORITY REVIEW**

Nu declara automat exfiltrare fara dovada completa a transferului.

---

# 25. Email rules — foarte important

Detecteaza:

```text
Inbox rule created
Forwarding rule created
External forwarding
Auto-forward
Mailbox delegation
Permission change
Transport rule change
Admin impersonation
```

Acestea sunt foarte importante pentru investigarea persistentei si exfiltrarii.

---

# 26. Servere de fisiere / SMB

Pentru file server:

```text
authentication
share access
share creation
share deletion
share permission changes
NTFS permission changes
file open
file read
file write
file create
file rename
file delete
file copy
bulk access
bulk modification
```

Coreleaza:

```text
4624
5140
5145
4663
4670
process creation
network source
user
```

Finding:

```text
User X accessed 4,821 files on classified share in 17 minutes.
```

Dar trebuie sa distingem:

```text
access
read
copy
delete
```

---

# 27. NAS

Pentru NAS, LogAnalyzer trebuie sa accepte, unde exista:

```text
SMB audit
NFS audit
NAS audit logs
authentication
share access
file access
file modification
snapshot
snapshot deletion
admin login
configuration changes
ACL changes
account changes
USB attachment
backup/export
replication
sync
```

Foarte important:

```text
NAS file exists
!=
user copied file
```

Corelarea cu endpoint-ul si reteaua este esentiala.

---

# 28. Servicii web interne

Intr-un mediu air-gapped pot exista:

```text
intranet
document management
web portals
internal applications
ticketing
management platforms
API services
webmail
administrative portals
```

LogAnalyzer trebuie sa poata importa:

```text
HTTP/HTTPS access logs
authentication
authorization
session
uploads
downloads
file names
API calls
admin actions
configuration changes
errors
client IP
user
user-agent
timestamp
```

---

# 29. Web upload/download

Un finding relevant:

```text
classified_document.pdf
+
web upload
+
user X
+
internal/external destination
```

sau:

```text
web download
+
new executable
+
execution
```

Corelare obligatorie cu:

```text
browser
process
network
file hash
user
destination
```

---

# 30. Platforme web si aplicatii interne

LogAnalyzer trebuie sa poata trata actiuni de business:

```text
login
logout
document view
document download
document upload
document share
permission change
role change
admin action
workflow approval
classification change
export
bulk download
bulk delete
```

Asta permite o investigatie de nivel mai inalt:

```text
Windows activity
+
application activity
+
file server activity
+
network activity
```

---

# 31. Politici Windows / GPO

Aceasta este una dintre cele mai importante directii pentru mediile AD.

LogAnalyzer trebuie sa detecteze:

```text
GPO created
GPO deleted
GPO modified
GPO linked
GPO unlinked
GPO security filtering changed
GPO permissions changed
GPO WMI filter changed
GPO startup script changed
GPO logon script changed
GPO scheduled task changed
GPO firewall rule changed
GPO Defender policy changed
GPO audit policy changed
GPO removable storage policy changed
GPO USB restrictions changed
GPO PowerShell policy changed
GPO Windows Update policy changed
GPO account policy changed
GPO password policy changed
GPO local administrator policy changed
```

---

# 32. Ce s-a intamplat cand politica a fost scoasa?

LogAnalyzer trebuie sa poata construi:

```text
POLICY BEFORE
       ↓
CHANGE
       ↓
POLICY AFTER
       ↓
APPLICATION
       ↓
BEHAVIOR CHANGE
```

Exemplu:

```text
18:02
USB blocking policy removed

18:04
GPO replicated

18:07
endpoint applied new policy

18:12
USB device inserted

18:13
file copied to USB
```

Acesta este un finding mult mai puternic decat:

```text
USB detected
```

---

# 33. GPO timeline

Pentru fiecare schimbare:

```text
GPO
GUID
VERSION
DOMAIN
DC
USER
ADMIN ACCOUNT
TIMESTAMP
OLD VALUE
NEW VALUE
LINK
OU
SECURITY FILTER
WMI FILTER
REPLICATION
CLIENT APPLICATION
```

Trebuie sa existe diff:

```text
BEFORE
vs
AFTER
```

---

# 34. Politici scoase temporar

Foarte important:

```text
policy disabled
+
policy re-enabled later
```

nu trebuie ignorat.

LogAnalyzer trebuie sa intrebe:

```text
Cat timp a fost dezactivata?
Ce sisteme au primit schimbarea?
Ce activitati au aparut in interval?
A fost folosita fereastra?
A existat justificare/change ticket?
```

Finding:

```text
SECURITY CONTROL GAP

Control:
USB restriction

Disabled:
18:02

Restored:
19:15

During gap:
USB inserted
+ file writes
```

---

# 35. Audit policy changes

Detecteaza:

```text
audit policy changed
advanced audit policy changed
logging disabled
PowerShell logging disabled
command-line logging disabled
object access auditing changed
process creation auditing changed
log size changed
retention changed
```

Coreleaza cu:

```text
GPO
local policy
registry
secedit
auditpol
```

---

# 36. Defender / security tooling

Detecteaza:

```text
Defender disabled
real-time protection changed
exclusions added
exclusions removed
tamper protection changes
signature changes
service stopped
EDR stopped
agent uninstalled
agent disconnected
firewall disabled
firewall profile changed
```

Coreleaza cu:

```text
process creation
PowerShell
GPO
network
file changes
user
```

---

# 37. Conturi si privilegii

Detecteaza:

```text
user created
user enabled
user disabled
user deleted
password changed
account unlocked
account locked
group membership changed
local admin membership changed
domain admin membership changed
service account changes
scheduled task account changes
privilege assignment
```

Important:

```text
account created
!=
account used
```

Trebuie corelate.

---

# 38. Service / persistence

Detecteaza:

```text
service installed
service modified
service started
service stopped
service deleted
driver installed
scheduled task created
startup item changed
Run/RunOnce changes
WMI persistence
```

Coreleaza cu:

```text
user
process
file
registry
network
```

---

# 39. RDP / remote administration

Detecteaza:

```text
RDP logon
RDP failure
remote interactive logon
WinRM
PowerShell remoting
PsExec
SMB admin share
Remote Service
Remote Registry
SSH
remote management tools
```

Coreleaza:

```text
source IP
account
target host
process
time
privilege
subsequent activity
```

---

# 40. Print / scan / physical document flow

In medii clasificate trebuie luate in calcul:

```text
print job
printer
user
document
pages
time
printer location
copy count
scan
scanner
scan destination
email scan
network scan
USB scan
```

Daca exista suficiente date:

```text
classified document
→ print
→ scan
→ PDF
→ USB
```

trebuie prezentat ca un chain de activitate, nu ca patru evenimente independente.

---

# 41. Printers si multifunction devices

LogAnalyzer ar trebui sa poata importa:

```text
print server logs
printer audit
MFP audit
scan logs
copy logs
fax logs
USB print/scan logs
admin configuration
```

---

# 42. Backup si replicare

Detecteaza:

```text
backup started
backup completed
backup failed
backup exported
restore
restore failed
snapshot created
snapshot deleted
replication started
replication stopped
replication target changed
backup media attached
```

Important:

```text
backup
!=
safe backup
```

Trebuie verificat:

```text
destination
integrity
retention
encryption
access
classification
authorization
```

---

# 43. Snapshot deletion

Un caz important:

```text
snapshot deleted
+
file mass modification
+
ransomware-like behavior
```

poate fi critic.

Dar:

```text
snapshot deleted
```

singur nu inseamna atac.

---

# 44. Bulk activity

LogAnalyzer trebuie sa detecteze deviatii de volum:

```text
1 file
10 files
1,000 files
10,000 files
```

Actiuni:

```text
bulk read
bulk copy
bulk rename
bulk delete
bulk modify
bulk archive
bulk download
bulk upload
bulk print
```

Dar baseline-ul trebuie sa fie configurabil.

---

# 45. Data staging

Detecteaza:

```text
large archive creation
ZIP/RAR/7z
ISO creation
large temporary directory
mass file copy
mass rename
file aggregation
staging folder
```

Coreleaza cu:

```text
USB
CD/DVD
email
web upload
network connection
NAS
```

---

# 46. Network transfer

Pentru fiecare flux disponibil:

```text
SOURCE
DESTINATION
USER
PROCESS
PROTOCOL
PORT
BYTES
TIMESTAMP
DIRECTION
RESULT
```

Clasificare:

```text
INTERNAL
AUTHORIZED
UNEXPECTED
UNAUTHORIZED
UNKNOWN
```

Nu:

```text
external IP = malicious
```

---

# 47. DNS

Detecteaza:

```text
new domain
rare domain
internal DNS change
DNS server change
failed resolution
suspicious volume
unexpected DNS server
```

In air-gapped:

```text
DNS request observed
```

poate fi deja un semnal de conectivitate care trebuie explicat.

---

# 48. DHCP / IP

Detecteaza:

```text
new lease
unexpected lease
new IP
new gateway
new DNS
duplicate IP
unexpected DHCP server
```

Coreleaza cu:

```text
NIC enable
Wi-Fi
network profile
user
time
```

---

# 49. Firewall

Detecteaza:

```text
rule created
rule modified
rule deleted
profile changed
firewall disabled
port opened
port closed
application exception
remote address changed
```

Pentru fiecare:

```text
OLD RULE
NEW RULE
WHO
WHEN
WHY
APPLIED TO
TRAFFIC OBSERVED AFTER CHANGE
```

---

# 50. Air-gap violation model

LogAnalyzer trebuie sa aiba o categorie:

# AIR-GAP INTEGRITY

Subcategorii:

```text
NETWORK INTERFACE
USB
OPTICAL MEDIA
PORTABLE STORAGE
WIRELESS
BLUETOOTH
PHONE
MAINTENANCE DEVICE
LAPTOP
REMOTE MANAGEMENT
NETWORK CABLE
SWITCH PORT
VPN
PROXY
EMAIL
WEB
FILE TRANSFER
BACKUP
PRINT/SCAN
```

Finding example:

```text
AIR-GAP INTEGRITY EVENT

Host:
PC-042

Event:
Wi-Fi adapter enabled

Time:
06:42

User:
X

IP:
10.x.x.x

Duration:
17 min

Subsequent activity:
file access observed

Confidence:
SUPPORTED
```

---

# 51. Zone-to-zone transfer

Pentru sisteme clasificate, LogAnalyzer trebuie sa poata reprezenta:

```text
ZONE
CLASSIFICATION
SYSTEM
USER
TRANSFER CHANNEL
TRANSFER OBJECT
AUTHORIZATION
TIME
RESULT
```

Exemplu:

```text
NATO SECRET zone
       ↓
transfer medium
       ↓
NATO RESTRICTED zone
```

Finding:

```text
CLASSIFICATION FLOW VIOLATION — REQUIRES REVIEW
```

Dar numai daca exista dovezi suficiente privind clasificarea si transferul.

---

# 52. Transferuri intre SPAD / RTD-SIC

HG 585 prevede ca atunci cand informatiile sunt transferate intre SPAD sau RTD-SIC, acestea trebuie protejate atat in timpul transferului, cat si in sistemul beneficiar, corespunzator nivelului de clasificare. 

LogAnalyzer trebuie sa poata modela:

```text
SOURCE SYSTEM
SOURCE ZONE
CLASSIFICATION
TRANSFER CHANNEL
DESTINATION SYSTEM
DESTINATION ZONE
USER
AUTHORIZATION
OBJECT
TIME
RESULT
```

---

# 53. Mediile de stocare — control si inventar

LogAnalyzer trebuie sa urmareasca:

```text
media ID
serial
classification
owner
custodian
location
zone
issue
return
transfer
destruction
reclassification
last seen
```

Pentru mediile clasificate, HG 585 cere identificare si control corespunzator nivelului; pentru nivelurile superioare exista cerinte suplimentare de evidenta si inventariere. 

---

# 54. Storage classification mismatch

Regula:

```text
DOCUMENT LEVEL
        >
STORAGE LEVEL
```

=> **CRITICAL REVIEW**

Exemplu:

```text
Document: NATO SECRET
NAS share: NATO RESTRICTED
```

sau:

```text
Document: STRICT SECRET
USB: SECRET
```

Trebuie generat:

```text
CLASSIFICATION MISMATCH
```

cu toate dovezile.

---

# 55. Unauthorized media

Un mediu nou trebuie comparat cu:

```text
approved inventory
serial list
asset database
authorized user
authorized zone
classification
time window
```

Stari:

```text
AUTHORIZED
REGISTERED
UNREGISTERED
UNAUTHORIZED
UNKNOWN
```

Nu confunda:

```text
UNREGISTERED
```

cu:

```text
PROVEN MALICIOUS
```

---

# 56. Document lifecycle

LogAnalyzer trebuie sa poata construi:

```text
CREATE
CLASSIFY
STORE
ACCESS
MODIFY
COPY
PRINT
TRANSMIT
ARCHIVE
RECLASSIFY
DECLASSIFY
DELETE
DESTROY
```

Pentru fiecare etapa:

```text
WHO
WHEN
WHERE
HOW
AUTHORIZATION
EVIDENCE
```

---

# 57. Clasificare modificata

Detecteaza:

```text
classification added
classification removed
classification lowered
classification raised
marking changed
document metadata changed
DLP label changed
IRM label changed
```

Coreleaza cu:

```text
user
admin
application
GPO
document management
file hash
```

Exemplu:

```text
NATO SECRET
→
NATO RESTRICTED
```

Finding:

```text
CLASSIFICATION CHANGE OBSERVED
REQUIRES AUTHORIZATION VALIDATION
```

Nu presupune automat ca downgrade-ul este neautorizat.

---

# 58. Documente sterse

Trebuie diferentiat:

```text
delete
recycle bin
secure delete
retention purge
archive
document version removal
snapshot removal
```

Coreleaza cu:

```text
classification
user
authorization
retention policy
archive
backup
```

---

# 59. Evidence integrity

Pentru fiecare colectare:

```text
source path
source size
source timestamps
acquisition timestamp
SHA-256
collection status
parser version
parser status
transformation history
```

Daca exista copie:

```text
ORIGINAL HASH
COPY HASH
```

Trebuie explicat ce demonstreaza fiecare.

---

# 60. Chain of custody

Pentru fiecare operatie:

```text
WHO
WHAT
WHEN
WHERE
WHY
HASH
ACTION
RESULT
```

Exemplu:

```text
10:02 evidence acquired
10:03 SHA-256 calculated
10:04 copied to workspace
10:05 parser executed
10:06 finding generated
10:08 verification executed
```

---

# 61. Ce trebuie sa faca LogAnalyzer cand o sursa este lipsa

Nu:

```text
source missing
=
nothing happened
```

Ci:

```text
SOURCE UNAVAILABLE

Impact:
This source could contain evidence relevant to:
- authentication
- file access
- policy change

Verdict impact:
Confidence reduced
```

---

# 62. GPO / AD investigation model

Pentru mediile AD:

```text
DOMAIN
DC
OU
GPO
LINK
USER
GROUP
COMPUTER
POLICY
VERSION
TIMESTAMP
```

Corelare:

```text
GPO changed
 ↓
replication
 ↓
client receives policy
 ↓
client behavior changes
```

---

# 63. "Ce s-a intamplat cand politica a fost scoasa?"

Acesta trebuie sa fie un finding de prim rang.

Exemplu:

```text
18:02
USB restriction removed

18:04
GPO replicated

18:07
endpoint applied new policy

18:12
USB inserted

18:13
file copy observed
```

Concluzie:

```text
CONTROL GAP FOLLOWED BY MEDIA ACTIVITY
```

Acesta este mult mai valoros decat patru evenimente independente.

---

# 64. Politici de securitate

LogAnalyzer trebuie sa urmareasca:

```text
Audit Policy
PowerShell Policy
Defender Policy
Firewall Policy
USB Policy
Device Installation Policy
Removable Storage Policy
Network Policy
Password Policy
Account Lockout Policy
RDP Policy
WinRM Policy
Windows Update Policy
Application Control
AppLocker
WDAC
Screen Lock
Encryption
BitLocker
```

---

# 65. Schimbari de politica

Pentru fiecare:

```text
OLD
NEW
WHO
WHEN
SOURCE
GPO
OU
TARGET
APPLICATION
DURATION
RELATED EVENTS
```

Rezultatul trebuie sa permita:

```text
BEFORE
CHANGE
AFTER
```

---

# 66. Control gap

Un control poate fi temporar dezactivat.

LogAnalyzer trebuie sa masoare:

```text
CONTROL
DISABLED
START
END
AFFECTED SYSTEMS
ACTIVITY DURING GAP
CONTROL RESTORED
```

Exemplu:

```text
USB blocking
disabled: 18:02
restored: 19:15

During gap:
USB inserted
+
classified file accessed
+
copy activity observed
```

Finding:

```text
SECURITY CONTROL GAP WITH CORRELATED ACTIVITY
```

---

# 67. Email + classified information

Corelare recomandata:

```text
classified document accessed
        ↓
file staged
        ↓
attachment created
        ↓
email sent
        ↓
recipient
        ↓
network transfer
```

Daca destinatarul este in afara zonei autorizate:

```text
REQUIRES REVIEW
```

Nu se declara automat exfiltrare fara dovada transferului si a autorizarii.

---

# 68. Web + classified information

Corelare:

```text
document accessed
 ↓
browser opened
 ↓
upload
 ↓
destination
```

sau:

```text
download
 ↓
file created
 ↓
execution
```

LogAnalyzer trebuie sa explice exact ce etapa este demonstrata.

---

# 69. File server / NAS + removable media

Un scenariu important:

```text
NAS access
 ↓
large file reads
 ↓
local staging
 ↓
USB insertion
 ↓
file writes to USB
```

Acesta trebuie ridicat automat ca o secventa corelata.

Nu doar:

```text
NAS activity: 1
USB activity: 1
```

---

# 70. Email + web + USB + NAS

LogAnalyzer trebuie sa construiasca un **Evidence Graph**:

```text
USER
 ├── accessed → NAS
 ├── created → archive
 ├── connected → USB
 ├── wrote → USB
 ├── opened → browser
 └── sent → email
```

Apoi:

```text
Evidence Graph
       ↓
Correlation
       ↓
Verification
       ↓
Verdict
```

---

# 71. Baseline

LogAnalyzer trebuie sa invete comportamentul normal:

```text
normal log rotation
normal USB use
normal admin activity
normal GPO changes
normal backup
normal email volume
normal file access
normal after-hours maintenance
```

Apoi detecteaza abaterea:

```text
NORMAL
vs
OBSERVED
```

Important:

> Anomaly != Incident.

---

# 72. After-hours

After-hours trebuie folosit pentru:

```text
prioritization
baseline
anomaly detection
correlation
```

Nu:

```text
after-hours = malicious
```

Exemplu:

```text
03:00
scheduled backup
authorized account
known server
known destination
```

=> normal.

Dar:

```text
03:00
unknown account
USB
classified document
new archive
```

=> high-priority review.

---

# 73. Bulk anomalies

Detecteaza:

```text
bulk read
bulk copy
bulk modify
bulk delete
bulk rename
bulk print
bulk download
bulk upload
bulk archive
```

Comparativ cu baseline.

---

# 74. Backup si arhivare

Trebuie verificat:

```text
backup created
backup completed
backup failed
archive created
archive hash
archive destination
readonly storage
retention
restore test
restore
media classification
authorization
```

Nu:

```text
backup exists = evidence safe
```

---

# 75. NIS2 — ce trebuie transformat in capability

NIS2 cere masuri adecvate de gestionare a riscurilor, inclusiv politici de securitate, incident handling, continuitate/backup, supply-chain security, securitate pentru achizitie/dezvoltare/mentenanta, evaluarea eficacitatii, training, criptografie, control acces si asset management. 

Pentru LogAnalyzer, acestea se transforma in:

```text
ASSET INVENTORY
LOGGING
MONITORING
DETECTION
ACCESS CONTROL
INCIDENT TIMELINE
INCIDENT EVIDENCE
BACKUP
CONFIGURATION CHANGES
SECURITY CONTROL CHANGES
SUPPLY-CHAIN EVIDENCE
REPORTING
POST-INCIDENT REVIEW
```

---

# 76. NIS2 — logging

Regulamentul UE 2024/2690 cere, pentru entitatile carora li se aplica, proceduri si instrumente pentru monitorizarea si logarea activitatilor din retele si sisteme, pentru detectarea evenimentelor ce pot fi incidente; monitorizarea trebuie automatizata unde este fezabil, iar logurile trebuie mentinute, documentate si revizuite. 

LogAnalyzer trebuie sa poata demonstra:

```text
WHAT IS LOGGED
WHERE
WHEN
RETENTION
WHO REVIEWS
WHAT ALERTS
WHAT WAS VERIFIED
WHAT WAS MISSING
```

---

# 77. Incident lifecycle

LogAnalyzer trebuie sa poata urmari:

```text
DETECT
 ↓
CLASSIFY
 ↓
ANALYZE
 ↓
CONTAIN
 ↓
ERADICATE
 ↓
RECOVER
 ↓
VERIFY
 ↓
REPORT
 ↓
LESSONS LEARNED
```

Pentru fiecare etapa:

```text
timestamp
actor
action
evidence
result
approval
```

---

# 78. Security control effectiveness

Nu este suficient:

```text
USB policy exists
```

Trebuie verificat:

```text
policy exists
+
policy applied
+
endpoint enforced
+
device blocked
+
attempt logged
```

La fel pentru:

```text
Defender
Firewall
Audit Policy
GPO
BitLocker
AppLocker
WDAC
RDP
PowerShell
```

---

# 79. Regula "configured" vs "effective"

Foarte important:

```text
CONFIGURED
!=
APPLIED
!=
ENFORCED
!=
OBSERVED
```

Exemplu:

```text
GPO says USB blocked
```

nu inseamna automat:

```text
USB was blocked
```

Trebuie verificat comportamentul efectiv.

---

# 80. Config drift

Detecteaza:

```text
expected configuration
vs
actual configuration
```

Exemple:

```text
GPO expected: USB blocked
Actual: USB allowed

Firewall expected: enabled
Actual: disabled

PowerShell logging expected: enabled
Actual: disabled
```

Rezultat:

```text
CONFIGURATION DRIFT
```

---

# 81. Unauthorized software

Detecteaza:

```text
new software
portable executable
unsigned executable
driver
browser
remote administration tool
file transfer tool
archive tool
disk imaging tool
USB utility
network tool
```

Coreleaza:

```text
installer
user
admin privilege
execution
network
USB
policy
```

---

# 82. Software + removable media

Un scenariu important:

```text
new portable tool
+
USB insertion
+
large archive creation
+
file writes
```

=> high-priority correlation.

Nu un verdict automat de exfiltrare.

---

# 83. Remote tools

Detecteaza:

```text
AnyDesk
TeamViewer
RDP
VNC
RustDesk
WinRM
PsExec
SSH
remote PowerShell
management agents
```

In mediile unde acestea nu sunt autorizate:

```text
UNAUTHORIZED REMOTE ACCESS TOOL
```

---

# 84. Time manipulation

Detecteaza:

```text
system time change
timezone change
NTP change
time service stopped
manual time adjustment
clock drift
```

Coreleaza cu:

```text
log gaps
authentication
file access
GPO changes
incident timeline
```

Un timestamp nesigur trebuie sa reduca confidence.

---

# 85. Log tampering

Detecteaza:

```text
log clear
log deletion
log service stop
audit policy disabled
log size reduced
retention changed
log forwarding disabled
SIEM agent stopped
EDR agent stopped
time changed
```

Dar clasifica in functie de context:

```text
ROUTINE
AUTHORIZED
UNEXPECTED
UNEXPLAINED
SUSPICIOUS
```

---

# 86. Evidence gap detection

LogAnalyzer trebuie sa detecteze automat:

```text
timestamp gap
missing log
missing source
parser gap
disabled logging
unavailable system
clock drift
archival gap
```

Exemplu:

```text
Security.evtx:
01:00
01:01
01:02
GAP
04:15
```

Rezultat:

```text
AUDIT EVIDENCE GAP
```

---

# 87. Independent verification

Veritas poate fi folosit ca strat independent:

```text
LogAnalyzer
    ↓
investigates
    ↓
findings
    ↓
Veritas
    ↓
challenges / verifies
```

Principiu:

> LogAnalyzer investigheaza. Veritas contesta si verifica.

Nu este nevoie ca Veritas sa devina al doilea LogAnalyzer.

---

# 88. Verdict model

Un verdict bun trebuie sa arate:

```text
FINDING
EVIDENCE
CORRELATION
CONTRADICTIONS
MISSING SOURCES
COVERAGE
CONFIDENCE
VERDICT
```

Exemplu:

```text
Finding:
Classified file copied to removable media.

Evidence:
USB inserted.
File write observed.
Matching hash observed.

Correlation:
User X logged in.
USB serial registered to workstation.

Contradictions:
None.

Missing:
No DLP transfer confirmation.

Confidence:
HIGH

Verdict:
SUPPORTED COPY ACTIVITY.
External disclosure: NOT ESTABLISHED.
```

---

# 89. Ce trebuie sa apara in interfata pentru utilizator

Meniul principal:

```text
HOME
ANALYZE
WHAT HAPPENED?
IS IT SAFE?
INVESTIGATE
VERIFY
AI
ACTIONS
REPORTS
MEMORY
ADVANCED
```

Pentru utilizator:

```text
Windows logs
Program activity
File activity
Device activity
Network activity
Policy changes
Security controls
Classified information
Removable media
Email
Web
File servers
NAS
```

Nu doar:

```text
EVTX
BAM
SRUM
Amcache
ShimCache
```

Acestea pot ramane in Advanced.

---

# 90. Modul "What happened?"

Exemplu:

```text
07:02
User X authenticated.

07:04
GPO changed USB restriction.

07:07
Endpoint applied new policy.

07:12
USB device inserted.

07:13
Large archive created.

07:14
Files read from NAS.

07:15
Files written to USB.

07:18
USB removed.

Conclusion:
The evidence supports a file-copy sequence.
External disclosure is not established.
```

---

# 91. Modul "What is unknown?"

Obligatoriu:

```text
UNKNOWN / NOT DETERMINED

- NAS audit unavailable
- email server logs unavailable
- no DLP evidence
- source archive not found
- time synchronization unknown
```

Acesta este un rezultat forensic important, nu un esec al produsului.

---

# 92. Modul "Why?"

Pentru fiecare finding:

```text
Finding
 ↓
Evidence
 ↓
Correlation
 ↓
Reasoning
 ↓
Confidence
```

Utilizatorul trebuie sa poata apasa:

```text
Why?
```

si sa vada exact de ce sistemul a ajuns la concluzie.

---

# 93. Modul "Show me the evidence"

Orice afirmatie importanta trebuie sa poata fi desfacuta:

```text
CLAIM
 ↓
SOURCE
 ↓
EVENT
 ↓
RAW DATA
 ↓
HASH / PROVENANCE
```

---

# 94. Evidence graph

Model recomandat:

```text
USER
 ↓
HOST
 ↓
PROCESS
 ↓
FILE
 ↓
CLASSIFICATION
 ↓
STORAGE
 ↓
DEVICE
 ↓
NETWORK
 ↓
SERVICE
 ↓
EVENT
```

Relatii:

```text
accessed
created
modified
copied
deleted
executed
connected
authenticated
changed
transmitted
printed
mounted
unmounted
```

---

# 95. Memory Vault integration

Doar informatia verificata trebuie propusa pentru Memory Vault:

```text
Finding
 ↓
Verification
 ↓
Evidence validation
 ↓
Vault proposal
 ↓
Authority review
 ↓
ACTIVE
```

AI-ul nu trebuie sa poata introduce direct o concluzie neverificata in Memory Vault.

---

# 96. Privacy / minimization

Testarea Veritas a ridicat si problema datelor sensibile.

LogAnalyzer trebuie sa aiba:

```text
purpose limitation
data minimization
case scope
collection scope
redaction
access control
retention
audit
```

Nu trebuie colectat automat orice informatie doar pentru ca tehnic poate fi colectata.

---

# 97. Multi-source correlation

Un finding important trebuie sa poata avea:

```text
Endpoint
+
AD
+
GPO
+
File Server
+
NAS
+
Email
+
Web
+
Firewall
+
EDR
+
Removable Media
```

LogAnalyzer trebuie sa pastreze provenance pentru fiecare sursa.

---

# 98. Nu toate sursele sunt obligatorii

Fiecare caz trebuie sa aiba:

```text
AVAILABLE
NOT_AVAILABLE
NOT_ENABLED
NOT_APPLICABLE
NOT_COLLECTED
```

Astfel nu apare falsa impresie:

```text
source not present
=
event did not happen
```

---

# 99. Prioritizarea findings

Recomandare:

```text
CRITICAL
HIGH
MEDIUM
LOW
INFO
```

Dar severity trebuie separata de confidence.

Exemplu:

```text
Severity: HIGH
Confidence: LOW
```

inseamna:

> daca ipoteza se confirma, impactul poate fi mare, dar dovezile sunt inca incomplete.

---

# 100. Reguli anti-overclaim

LogAnalyzer trebuie sa aiba reguli hard:

```text
NEVER:
artifact -> automatic execution

NEVER:
presence -> user action

NEVER:
correlation -> proof

NEVER:
missing evidence -> clean system

NEVER:
low risk -> no incident

NEVER:
no detection -> no compromise

NEVER:
after-hours -> suspicious

NEVER:
USB insertion -> data theft

NEVER:
CD insertion -> exfiltration

NEVER:
email exists -> transmission

NEVER:
file access -> content read

NEVER:
policy configured -> policy effective

NEVER:
AI conclusion -> verified fact
```

---

# 101. Ce ar trebui sa fie MVP-ul LogAnalyzer

## Tier 1 — obligatoriu

```text
Security.evtx
System.evtx
PowerShell
Task Scheduler
Defender
Firewall
AD/account activity
Process creation
Service installation
File/object access
Log lifecycle
GPO/policy changes
Removable media
USB
CD/DVD
Network interfaces
RDP
SMB
Provenance
Hashes
Parser health
Coverage
Confidence
Unknowns
Timeline
Evidence graph
```

## Tier 2

```text
WMI
SRUM
BAM/DAM
Amcache
ShimCache
Prefetch
Jump Lists
LNK
Browser
DNS
DHCP
NAS
EDR
SIEM
Email
Web applications
```

## Tier 3

```text
Physical access systems
Printer/MFP
Scanner
DLP
NAC
RADIUS/802.1X
VPN
Proxy
Firewall appliances
Document management
classified-information registers
security-office records
```

---

# 102. Formula finala

LogAnalyzer nu trebuie sa fie:

```text
un program care gaseste cat mai multe evenimente.
```

Trebuie sa fie:

```text
un sistem care poate demonstra,
cu dovezi trasabile,
ce s-a intamplat,
ce nu poate fi demonstrat,
ce a lipsit,
de ce concluzia este corecta
si cat de sigura este.
```

Formula:

```text
COLLECT
   ↓
NORMALIZE
   ↓
PARSE
   ↓
VALIDATE
   ↓
CORRELATE
   ↓
VERIFY
   ↓
EXPLAIN
   ↓
REPORT
   ↓
AUDIT
```

---

# 103. Modelul special pentru un mediu air-gapped

```text
AIR-GAP
  │
  ├── NETWORK INTERFACES
  ├── WI-FI
  ├── BLUETOOTH
  ├── USB
  ├── CD/DVD
  ├── PORTABLE HDD/SSD
  ├── PHONE
  ├── MAINTENANCE LAPTOP
  ├── PRINT/SCAN
  ├── FILE SERVER
  ├── NAS
  ├── EMAIL
  ├── WEB
  ├── BACKUP
  └── MANUAL TRANSFER
```

Pentru fiecare:

```text
AUTHORIZED?
OBSERVED?
WHEN?
WHO?
WHAT OBJECT?
CLASSIFICATION?
TRANSFER?
DESTINATION?
EVIDENCE?
```

---

# 104. Modelul special pentru HG 585

```text
PERSON
 ↓
AUTHORIZATION
 ↓
NEED-TO-KNOW
 ↓
SYSTEM / ZONE
 ↓
CLASSIFICATION
 ↓
DOCUMENT / MEDIA
 ↓
ACCESS
 ↓
PROCESS / ACTION
 ↓
TRANSFER
 ↓
ARCHIVE
 ↓
RETENTION
 ↓
DESTRUCTION
```

LogAnalyzer trebuie sa poata demonstra fiecare etapa pentru care exista surse tehnice.

---

# 105. Modelul special pentru NIS2

```text
ASSET
 ↓
RISK
 ↓
CONTROL
 ↓
LOGGING
 ↓
MONITORING
 ↓
DETECTION
 ↓
INCIDENT
 ↓
RESPONSE
 ↓
RECOVERY
 ↓
VERIFICATION
 ↓
REPORT
 ↓
LESSON LEARNED
```

---

# 106. Surse oficiale de referinta

## Romania — HG 585/2002

Portal Legislativ, Standardul national de protectie a informatiilor clasificate:
- echivalente nationale/NATO;
- definitia documentului clasificat;
- INFOSEC;
- sisteme SPAD si RTD-SIC;
- transferul informatiilor;
- medii de stocare;
- evidenta automata a accesului;
- controlul mediilor;
- declasificarea/distrugerea mediilor.

HG 585/2002 prevede, intre altele, ca informatiile electronice pot fi transmise prin retele de comunicatii, ca mediile de stocare sunt protejate la nivelul corespunzator si ca accesul electronic la informatii clasificate este tinut in registre de acces realizate prin software. citeturn2view0turn2view2turn2view3

## NIS2

Directiva (UE) 2022/2555 include politici de analiza a riscului, incident handling, continuitate/backup, supply-chain security, access control si asset management in cadrul masurilor de gestionare a riscului. citeturn0search1

Regulamentul de punere in aplicare (UE) 2024/2690 detaliaza pentru entitatile carora li se aplica cerinte privind monitorizarea si logging-ul, inclusiv mentinerea, documentarea si revizuirea logurilor. citeturn0search2

## NATO

NATO descrie nivelurile NATO RESTRICTED, NATO CONFIDENTIAL, NATO SECRET si COSMIC TOP SECRET si precizeaza principiul protectiei proportionale cu nivelul de clasificare. citeturn0search3turn0search6

---

# 107. Concluzie de produs

Cea mai importanta lectie pentru LogAnalyzer este:

> **Nu construi un program care spune "am gasit un eveniment". Construieste un sistem care poate explica lantul complet al activitatii si poate demonstra unde exista sau nu exista suficiente dovezi.**

Pentru mediul vizat:

```text
Windows
+
AD/GPO
+
Air-gapped network
+
File Servers
+
NAS
+
Email
+
Web platforms
+
Removable media
+
Classified documents
+
Security policies
+
Audit logs
+
Network telemetry
+
Physical/operational records
```

trebuie sa ajunga intr-un singur Evidence Graph.

Rezultatul final trebuie sa fie:

```text
WHAT HAPPENED
WHAT IS PROVEN
WHAT IS SUPPORTED
WHAT IS ONLY POSSIBLE
WHAT IS UNKNOWN
WHAT IS MISSING
WHAT CONTRADICTS IT
WHY THE VERDICT EXISTS
WHAT SHOULD HAPPEN NEXT
```

Asta este diferenta dintre un colector de artefacte si o platforma DFIR/audit adevarata.
