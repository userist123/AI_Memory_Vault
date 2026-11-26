export ANDROID_USER_HOME=$HOME/.aas/android-user ANDROID_ADB_SERVER_PORT=5641
ADB="$HOME/emu/sdk/platform-tools/adb -P 5641 -s emulator-5600"
(sleep 2; for i in 1 2 3 4 5 6; do $ADB shell input swipe 540 1800 540 600 300; sleep 0.7; $ADB shell input swipe 540 600 540 1800 300; sleep 0.7; done) >/dev/null 2>&1 &
$ADB exec-out screenrecord --output-format=h264 --size 540x1170 --bit-rate 4000000 --time-limit 10 -
