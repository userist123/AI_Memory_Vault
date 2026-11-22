export ANDROID_USER_HOME=$HOME/.aas/android-user ANDROID_ADB_SERVER_PORT=5641
ADB="$HOME/emu/sdk/platform-tools/adb -P 5641 -s emulator-5600"
$ADB shell 'which nslookup getent ping nc; toybox --help 2>&1 | head -0; for c in nslookup ping getent host dig; do toybox $c --help >/dev/null 2>&1 && echo "toybox has $c"; done'
echo "--- ping canary"; $ADB shell 'ping -c1 -W3 canary-aas-test.example 2>&1; echo rc=$?'
echo "--- nslookup canary"; $ADB shell 'toybox nslookup canary-aas-test.example 2>&1 | head; echo rc=$?'
echo "--- ping 8.8.8.8"; $ADB shell 'ping -c1 -W3 8.8.8.8 2>&1; echo rc=$?'
$ADB shell 'getprop net.dns1; getprop net.eth0.dns1; ip addr show 2>&1| grep inet; ip route 2>&1 | head -3'
echo "--- pm"; $ADB shell pm list packages | head -5
echo "--- am start"; $ADB shell am start -a android.intent.action.VIEW -d about:blank 2>&1 | head -3
echo "--- install"; find / -name '*.apk' -size -3M 2>/dev/null | grep -v -E "^/proc" | head -3
