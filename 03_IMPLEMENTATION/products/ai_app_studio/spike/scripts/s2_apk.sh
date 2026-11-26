export ANDROID_USER_HOME=$HOME/.aas/android-user ANDROID_ADB_SERVER_PORT=5641
ADB="$HOME/emu/sdk/platform-tools/adb -P 5641 -s emulator-5600"
for pkg in com.android.egg com.android.dreams.basic com.android.wallpaperbackup; do
P=$($ADB shell pm path $pkg | tr -d '\r' | head -1 | cut -d: -f2); echo "$pkg path=$P"; [ -z "$P" ] && continue
$ADB pull $P /tmp/aas-test.apk >/dev/null; ls -l /tmp/aas-test.apk | awk '{print $5}'
$ADB install -r -d /tmp/aas-test.apk 2>&1 | tail -2 && break
done
rm -f /tmp/aas-test.apk
