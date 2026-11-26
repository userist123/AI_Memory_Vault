# Emulator spike (WSL2 Ubuntu-S9AI-D, 2026-11-22)

AVD `aas-phone` created by hand in `~/.aas/avd` (no cmdline-tools/avdmanager in `~/emu/sdk`). Image android-33 google_apis x86_64 rev 17. Flags: `-port 5600 -no-window -no-audio -no-boot-anim -gpu swiftshader_indirect -no-snapshot-save -no-snapshot-load -accel on`. Scripts: `spike/scripts/*.sh`.

## 0. Environment findings
- User `<owner-user>` is NOT in group `kvm` (`/etc/group: kvm:x:993:`), `/dev/kvm` is `crw-rw---- root kvm`.
- Workaround without persistent change: `sudo -n runuser -u <owner-user> -g kvm -- emulator ...` (passwordless sudo; `sudo -g` alone asks for a password).
- A process started by `nohup ... &` dies when the `wsl.exe` call ends (first emulator got graceful-shutdown). `setsid nohup ...` survives.

## 1. accel-check
- Plain user: exit 11, `ProbeKVM: This user doesn't have permissions to use KVM (/dev/kvm).`
- Via runuser -g kvm: `accel: 0  KVM (version 12) is installed and usable.`

## 2. Boot time (to sys.boot_completed=1, 2 s poll)
- Fresh userdata (first boot): 29.4 s (emulator log: `Boot completed in 28123 ms`).
- Later cold boots (-no-snapshot-load): 18.8 s, 17.4 s, ~18 s.

## 3. Memory
- qemu-system-x86_64-headless RSS after boot: 2,696,136 KB / 2,646,848 KB (~2.6 GB), VSZ ~4.6 GB.
- `free -m` used: 4794 -> 7488 MB (run 1), 4980 -> 7382 MB (run 2); available 11048 -> 8355 MB.

## 4. Screenshot (`adb exec-out screencap -p`)
- 1.27 s first, repeats 0.92-1.29 s. PNG 1080 x 2340 RGBA, 0.96-1.38 MB. Copied to `spike/shot.png`.
- Conclusion: ~1 fps via screencap; not live-preview grade (needs scrcpy/grpc stream).

## 5. Input
- `adb shell input tap 540 1200`: exit 0, 0.16 s.

## 6. P9a guest -> host (from `adb shell`, uid 2000; toybox 0.8.6 has no `nc -z`, used `printf ... | toybox nc -w 3 -q 2 HOST PORT`; refused = `nc: connect: Connection refused`)
WSL listeners (`ss -ltn`): 127.0.0.1:{1080,5037,9001,11434,5600,5601(emu),44613(emu)}, 0.0.0.0:8080, *:8123, 127.0.0.53/54:53, 10.255.255.254:53.

| target | default slirp | restrict=on |
|---|---|---|
| 10.0.2.2:1 (control closed) | refused | refused |
| 10.0.2.2:5037 owner adb server | REACHABLE, `000chost:version` -> `OKAY00040029` | refused |
| 10.0.2.2:5641 studio adb server | REACHABLE, `OKAY00040029` | n/t |
| 10.0.2.2:5600 emu console | REACHABLE, `Android Console: Authentication required` | refused |
| 10.0.2.2:5601 emu adb | REACHABLE | refused |
| 10.0.2.2:5554/5555/8554/17865 | refused (nothing listening) | - |
| 10.0.2.2:44613 (emu, 127.0.0.1) | refused (seen after relaunch) | - |
| 10.0.2.2:1080 | REACHABLE | refused |
| 10.0.2.2:8080 | REACHABLE | n/t |
| 10.0.2.2:8123 | REACHABLE, HTTP 200 | refused |
| 10.0.2.2:9001 | REACHABLE, HTTP 200 nginx | refused |
| 10.0.2.2:11434 | REACHABLE, HTTP 200 | refused |
| 10.0.2.2:18080 (test python http) | REACHABLE, HTTP 200 | refused |
| Windows/WSL gw 172.27.96.1:135 | REACHABLE | Network is unreachable |
| 172.27.96.1:445, :3389 | Timeout | Network is unreachable |
| 1.1.1.1:443 / ping 8.8.8.8 | OK (rc 0, 35 ms) once network up; timeout 10 s after boot | Network is unreachable |

- `getprop ro.adb.secure` = 0.
- `unshare -rn true` as <owner-user>: exit 0 (unprivileged user+net namespace available).
- Conclusion: with default networking any app can reach every WSL loopback service including the adb server (full adb host protocol -> control of all attached devices) and the emulator console; `restrict=on` on both user-mode nets blocks all of it, adb over the emulator pipe still works.

## 7. Network options (`emulator -help`)
No `-no-network`. Present: `-net-tap`, `-wifi-tap`, `-netspeed`, `-netdelay`, `-netfast`, `-http-proxy`, `-dns-server`, `-tcpdump`, `-wifi-user-mode-options`, `-network-user-mode-options`, `-qemu args...`. Relaunched with `-wifi-user-mode-options restrict=on -network-user-mode-options restrict=on`: results in table column 2.

## 8. scrcpy
`which scrcpy`: not found.

## 9. Versions
Android emulator 31.3.9.0 (build_id 8748233). adb `/usr/bin/adb` 1.0.41 / 34.0.4-debian; `~/emu/sdk/platform-tools/adb` used in final run (port 5641).

## Spike 2 (2026-11-22)
Scripts: `spike/scripts/s2_*.sh`. ss -ltn before: 127.0.0.1:{1080,9001,11434}, 127.0.0.5x:53, 10.255.255.254:53, *:8123 (no 5037/5641). After (adb -P 5641 kill-server, all emulators killed): see final report; nothing of mine left.

### (a) restrict=on (both nets)
- Guest has no nslookup/getent; `ping canary-aas-test.example` -> `unknown host` (rc 2); `ping 8.8.8.8` -> `Network is unreachable` (no route/DNS reply possible). Guest still has eth0 10.0.2.15 and wlan0 10.0.2.16 but no default route. No tcpdump, so host-side capture not done; guest-side: nothing resolvable or routable.
- `pm list packages` works; `am start -a VIEW -d about:blank` starts. No APK in SDK; pulled `/system/app/EasterEgg/EasterEgg.apk` (5.2 MB) and `adb install -r -d` -> `Success` (adb over the emulator pipe works under restrict=on). Overlay APK reinstall failed only because it is a static overlay (scanner error), transport fine.
- Boot (headless): ~20 s.

### (b) unshare -rn
- Order runuser -> unshare (`sudo -n runuser -u <owner-user> -g kvm -- env HOME=$HOME unshare -rn bash inner.sh`): WORKS. Inside: uid 0 mapped, `/dev/kvm` opens OK, `emulator -accel-check` = usable, `ip link set lo up` OK, `adb -P 5641 start-server` OK (listens 127.0.0.1:5641 inside ns only), emulator boots (~20 s) and `adb devices` shows emulator-5600 device.
- ONE gotcha: with only lo, qemu fails `-chardev socket,host=::1,...modem: address resolution failed for ::1` (AI_ADDRCONFIG needs a non-loopback IPv6 address) -> emulator never registers with adb. Fix: `ip link add d0 type dummy; ip link set d0 up; ip -6 addr add fd00::1/64 dev d0 nodad` before launching; then OK.
- Order unshare -> runuser: FAILS (`sudo: /etc/sudo.conf is owned by uid 65534`, setuid unusable in userns); plain `unshare -rn` as <owner-user>: `/dev/kvm` Permission denied.
- Inside the ns there is no route to WSL loopback services at all (separate loopback); host adb 5641/5037 not visible.

### (c) screenrecord
- `wsl.exe ... adb exec-out screenrecord --output-format=h264 --size 540x1170 --bit-rate 4000000 --time-limit 10 -` > rec.h264 (needs MSYS_NO_PATHCONV=1 from Git Bash): 272,531 bytes, ffprobe: h264 540x1170, 254 frames in ~10 s (~25 fps, while swiping). Screenrecord only emits frames on change. Latency tap->frame: not measured. No tcpdump/scrcpy.

### (d) B9
- WSL `python3 -m http.server 5799 --bind 127.0.0.1`: from `docker run --rm alpine wget http://host.docker.internal:5799/` -> got directory listing (reachable). `host.docker.internal:8080` -> HTML returned (reachable; WSL 0.0.0.0:8080 / other listener). `Test-NetConnection 127.0.0.1 -Port 5799` -> TcpTestSucceeded True (WSL loopback listeners are forwarded to Windows). Note alpine image was not present; pulled `alpine:latest` (public). No containers left (--rm).

### (e) keys
- Under ANDROID_USER_HOME only `~/.aas/android-user/adbkey` (owner <owner-user>:kvm, written by emulator) + emulator ini/modem files; no adbkey.pub there. `~/.android/adbkey` atime 23:33:56 and `.pub` 23:34:59 (predecessor run) unchanged through all of spike 2; mtime 2026-11-18 unchanged.

### (f) windowed emulator via WSLg
- Env: DISPLAY=:0, WAYLAND_DISPLAY=wayland-0, XDG_RUNTIME_DIR=/run/user/1000/; passed through runuser env. Window appears: `xwininfo -root -tree` -> "Android Emulator - aas-phone:5600" 531x1151 (+ toolbar "Emulator"); Windows-side window is in msrdc with empty MainWindowTitle (Get-Process title filter finds nothing), but it is visible on desktop (desktop.png). wmctrl absent.
- Boot: swiftshader_indirect 21 s, host 23 s. Both start. `-gpu host` renders launcher correctly; the swiftshader screenshot showed a white screen (taken mid-swipe; not re-checked).
- A modal "Emulator Running in Nested Virtualization" dialog appears each launch (must be dismissed/"never show again" stored in ~/.aas/android-user).
- `settings put system show_touches 1` / `pointer_location 1` OK; pointer_location overlay (P/X/Y/Pressure header + crosshair at X:300 Y:700) visibly drawn in the window during taps/swipe. desktop.png = last capture (host GPU run, idle: overlay header visible, no active touch).
