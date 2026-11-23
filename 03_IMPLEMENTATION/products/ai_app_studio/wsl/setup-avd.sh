#!/bin/bash -p
# setup-avd.sh -- run as the owner user by install.ps1 after /opt/aas/bin is installed.
# Creates the studio AVDs (aas-phone, aas-tablet) under $HOME/.aas/avd from the pinned config.ini copies in
# /opt/aas/bin. Existing AVDs are kept; a config.ini that differs from the pin is backed up, then replaced.
set -euo pipefail
umask 022
PATH=/usr/sbin:/usr/bin:/sbin:/bin
[ "$(id -u)" -ne 0 ] || { echo "run as the owner user, not root" >&2; exit 65; }
RH=$(getent passwd "$(id -un)" | cut -d: -f6)
A="$RH/.aas/avd"
SYS="$RH/emu/sdk/system-images/android-33/google_apis/x86_64"
[ -f "$SYS/system.img" ] || { echo "system image missing" >&2; exit 29; }
mkdir -p "$A"
[ -z "$(find "$RH/.aas" -type l -print -quit)" ] || { echo "symlink under studio state" >&2; exit 23; }
for avd in aas-phone aas-tablet; do
  pin="/opt/aas/bin/$avd.config.ini"
  [ -f "$pin" ] || { echo "missing $pin" >&2; exit 28; }
  mkdir -p "$A/$avd.avd"
  printf 'avd.ini.encoding=UTF-8\npath=%s\npath.rel=avd/%s.avd\ntarget=android-33\n' "$A/$avd.avd" "$avd" > "$A/$avd.ini.new"
  if ! cmp -s "$A/$avd.ini.new" "$A/$avd.ini" 2>/dev/null; then
    [ -f "$A/$avd.ini" ] && cp -p "$A/$avd.ini" "$A/$avd.ini.bak"
    mv "$A/$avd.ini.new" "$A/$avd.ini"
  else
    rm -f "$A/$avd.ini.new"
  fi
  if ! cmp -s "$pin" "$A/$avd.avd/config.ini" 2>/dev/null; then
    [ -f "$A/$avd.avd/config.ini" ] && cp -p "$A/$avd.avd/config.ini" "$A/$avd.avd/config.ini.bak"
    cp "$pin" "$A/$avd.avd/config.ini"
    echo "$avd: config written"
  else
    echo "$avd: ok"
  fi
done
