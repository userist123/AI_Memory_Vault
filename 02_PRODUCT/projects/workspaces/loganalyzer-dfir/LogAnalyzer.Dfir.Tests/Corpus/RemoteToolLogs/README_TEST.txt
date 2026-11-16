TEST: all files in this folder are SYNTHETIC samples (ids 9990001xx, addresses 203.0.113.0/24 and 2001:db8::/32 are documentation ranges), written in the formats described in public DFIR literature. They come from no real installation of AnyDesk or TeamViewer.

teamviewer_TEST/TeamViewer15_Logfile.log (added 2026-11-16, rewritten in the real row order): SYNTHETIC, written by hand for the tests in the row format of the TeamViewer main log
("yyyy/MM/dd HH:mm:ss.fff  pid  tid  LABEL  message", local time, header block with "Start: ... (UTC+h:mm)"). Ids are 999000xxx, addresses 192.0.2.x / 198.51.100.x / 2001:db8::,
the partner name is TEST-Partener. It is NOT copied from any real log: the message wording and the order of the rows follow the markers observed in a real TeamViewer 15.55 log (2024);
"was denied" is the wording of a real failed attempt, checked against a public sample (only mentioned here, not copied). It holds: an accepted session (link-local and documentation
UDP addresses, version, Terminate then Statistics), a second accepted session after a second "Start:" header with another UTC offset, and a failed session with three attempts
(SRP secret mismatch, denied x3, CMD_ENDSESSION LoginFailed, Statistics before the "no session ... aborting login" Terminate, AbortLogin). The first two sessions have the same
partner and start (within seconds) as the two rows of teamviewer_TEST/Connections_incoming.txt, so the rule must count 3 connections for 5 rows. File transfers are not covered.


rustdesk_TEST/rustdesk_rCURRENT.log (added 2026-11-16): SYNTHETIC, written by hand for the tests in the row format of flexi_logger::opt_format used by RustDesk
("[yyyy-MM-dd HH:mm:ss.ffffff +HH:mm] LEVEL [file:line] message", the UTC offset is on EVERY row). It is NOT copied from any real log: the marker wording ("#id Connection opened from addr.",
"#id Connection closed: reason", "#id connection loop exited", "new wakelock, require display on: true", "drop wakelock") follows rows published in rustdesk/rustdesk issues (Windows and Linux);
the non-marker rows and all values are invented. Values are TEST values: ids 901-904, addresses 192.0.2.x / 198.51.100.x / 2001:db8:: (documentation ranges) and 127.0.0.1 (a local connection).
It holds: session #903 from 127.0.0.1 (loopback: a LocalConnection, not a session), #901 with a wakelock (display on, then dropped) closed by "Peer close" then "connection loop exited",
#902 with two continuation lines and the "connection loop exited" row BEFORE the "Connection closed" row, and #904 from an IPv6 address at offset +02:00 (source paths with "/"). It has no
authentication and no file transfer rows: the parser does not read them (no verified real rows exist). Expected: 4 events, 3 of them partner sessions, no gap.

rustdesk_TEST (updated 2026-11-16, file transfers): rustdesk_rCURRENT.log also holds two "new read File: <path>" rows (08:35:10 and 08:36:20, when only the partner session #901 is open, so they carry
Session Id 901). rustdesk_TEST/cm/rustdesk_rCURRENT.log (the connection-manager log, collected from log\cm) is SYNTHETIC too: a non-transfer row, two "new write File: <path>" rows
(C:\Users\TEST-user\Desktop\TEST-raport.docx, C:\Users\TEST-user\Downloads\TEST-unealta.zip) and one "new write Bytes" row (memory data, no path). The row wording follows libs/base/src/fs.rs of
RustDesk (log::info!("new write {}", data_source) / ("new read {}", data_source); data source prints "File: <path>" or "Bytes"); the source file name in the prefix is invented and must not be relied on.
Meaning: "new write File:" = a file written ON the station (received from the remote side, seen in log\cm); "new read File:" = a file read FROM the station (sent to the remote side, seen in the main log).
LIMITATION: ordinary RustDesk authentication is NOT logged (the only message is "Permanent password accepted via logon-screen fallback", rare): it is not parsed and has no sample.
Expected: main log 6 events (4 sessions + 2 FileRead), cm log 3 events (2 FileWrite + 1 Bytes), no gap.
