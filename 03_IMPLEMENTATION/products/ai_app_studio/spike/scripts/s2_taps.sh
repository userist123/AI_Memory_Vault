export ANDROID_USER_HOME=$HOME/.aas/android-user ANDROID_ADB_SERVER_PORT=5641
ADB="$HOME/emu/sdk/platform-tools/adb -P 5641 -s emulator-5600"
$ADB shell input tap 540 1200; sleep 0.5; $ADB shell input swipe 200 1800 800 900 1500 & sleep 0.6
$ADB shell input tap 300 700 ; wait
