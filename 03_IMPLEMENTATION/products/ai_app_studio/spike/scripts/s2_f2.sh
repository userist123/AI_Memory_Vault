export ANDROID_USER_HOME=$HOME/.aas/android-user ANDROID_ADB_SERVER_PORT=5641
ADB="$HOME/emu/sdk/platform-tools/adb -P 5641 -s emulator-5600"
DISPLAY=:0 xwininfo -root -tree 2>&1 | grep -i -E "emulator|android|aas|qemu" | head -4
$ADB shell settings put system show_touches 1; $ADB shell settings put system pointer_location 1
echo "show_touches=$($ADB shell settings get system show_touches) pointer_location=$($ADB shell settings get system pointer_location)"
$ADB shell input keyevent KEYCODE_WAKEUP
