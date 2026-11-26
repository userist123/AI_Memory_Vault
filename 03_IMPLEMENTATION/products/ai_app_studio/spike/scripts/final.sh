export ANDROID_SDK_ROOT=$HOME/emu/sdk ANDROID_AVD_HOME=$HOME/.aas/avd ANDROID_USER_HOME=$HOME/.aas/android-user ANDROID_EMULATOR_HOME=$HOME/.aas/android-user ANDROID_ADB_SERVER_PORT=5641
mkdir -p $ANDROID_USER_HOME
A=$HOME/emu/sdk/platform-tools/adb
echo "owner adb before: $(pgrep -a -x adb)"
unshare -rn true; echo "unshare -rn exit=$?"
echo "== listeners"; ss -ltn | awk 'NR>1{print $4}' | tr '\n' ' '; echo
$A -P 5641 start-server >/dev/null 2>&1
setsid nohup sudo -n runuser -u <owner-user> -g kvm -- env HOME=$HOME ANDROID_SDK_ROOT=$ANDROID_SDK_ROOT ANDROID_AVD_HOME=$ANDROID_AVD_HOME ANDROID_USER_HOME=$ANDROID_USER_HOME ANDROID_EMULATOR_HOME=$ANDROID_EMULATOR_HOME ANDROID_ADB_SERVER_PORT=5641 \
  $ANDROID_SDK_ROOT/emulator/emulator -avd aas-phone -port 5600 -no-window -no-audio -no-boot-anim -gpu swiftshader_indirect -no-snapshot-save -no-snapshot-load -accel on > $HOME/.aas/emu-final.log 2>&1 &
ADB="$A -P 5641 -s emulator-5600"
for i in $(seq 1 150); do [ "$($ADB shell getprop sys.boot_completed 2>/dev/null | tr -d '\r')" = 1 ] && break; sleep 2; done
echo "booted after ~$((i*2))s"; sleep 5
echo "ro.adb.secure=$($ADB shell getprop ro.adb.secure | tr -d '\r')"
for p in 5037 5641 5600 5601 44613 8080 1080 8123 9001 11434; do
  r=$($ADB shell "printf 'GET / HTTP/1.0\r\n\r\n' | toybox nc -w 2 -q 2 10.0.2.2 $p 2>/data/local/tmp/e | head -c 30 | tr -d '\r\n'; echo \" err=\$(cat /data/local/tmp/e)\"" | tr -d '\r'); echo "10.0.2.2:$p -> $r"
done
$ADB shell "printf '000chost:version' | toybox nc -w 2 10.0.2.2 5037 | head -c 12; echo ' <-5037 host:version'" | tr -d '\r'
$ADB shell "printf '000chost:version' | toybox nc -w 2 10.0.2.2 5641 | head -c 12; echo ' <-5641 host:version'" | tr -d '\r'
grep -i -E "adb public key|android-user|\.android" $HOME/.aas/emu-final.log | cut -c1-90 | head -3
$ADB shell rm -f /data/local/tmp/e
$ADB emu kill; sleep 25
pgrep -f "^$HOME/emu/sdk/emulator/qemu.*aas-phone" || echo emulator_gone
$A -P 5641 kill-server; sleep 1
echo "owner adb after: $(pgrep -a -x adb)"
ls $ANDROID_USER_HOME
