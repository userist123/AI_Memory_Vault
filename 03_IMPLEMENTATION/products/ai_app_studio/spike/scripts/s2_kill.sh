export ANDROID_USER_HOME=$HOME/.aas/android-user ANDROID_ADB_SERVER_PORT=5641
A="$HOME/emu/sdk/platform-tools/adb -P 5641"
$A -s emulator-5600 emu kill >/dev/null 2>&1; sleep 15
pgrep -f "qemu-system.*aas-phone" || echo emulator_gone
