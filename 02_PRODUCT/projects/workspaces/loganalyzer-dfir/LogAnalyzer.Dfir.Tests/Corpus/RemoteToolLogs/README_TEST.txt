TEST: all files in this folder are SYNTHETIC samples (ids 9990001xx, addresses 203.0.113.0/24 and 2001:db8::/32 are documentation ranges), written in the formats described in public DFIR literature. They come from no real installation of AnyDesk or TeamViewer.

teamviewer_TEST/TeamViewer15_Logfile.log (added 2026-11-16, rewritten in the real row order): SYNTHETIC, written by hand for the tests in the row format of the TeamViewer main log
("yyyy/MM/dd HH:mm:ss.fff  pid  tid  LABEL  message", local time, header block with "Start: ... (UTC+h:mm)"). Ids are 999000xxx, addresses 192.0.2.x / 198.51.100.x / 2001:db8::,
the partner name is TEST-Partener. It is NOT copied from any real log: the message wording and the order of the rows follow the markers observed in a real TeamViewer 15.55 log (2024);
"was denied" is the wording of a real failed attempt, checked against a public sample (only mentioned here, not copied). It holds: an accepted session (link-local and documentation
UDP addresses, version, Terminate then Statistics), a second accepted session after a second "Start:" header with another UTC offset, and a failed session with three attempts
(SRP secret mismatch, denied x3, CMD_ENDSESSION LoginFailed, Statistics before the "no session ... aborting login" Terminate, AbortLogin). The first two sessions have the same
partner and start (within seconds) as the two rows of teamviewer_TEST/Connections_incoming.txt, so the rule must count 3 connections for 5 rows. File transfers are not covered.
