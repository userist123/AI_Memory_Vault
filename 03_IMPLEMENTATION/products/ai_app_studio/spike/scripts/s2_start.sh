export ANDROID_SDK_ROOT=$HOME/emu/sdk ANDROID_AVD_HOME=$HOME/.aas/avd ANDROID_USER_HOME=$HOME/.aas/android-user ANDROID_EMULATOR_HOME=$HOME/.aas/android-user ANDROID_ADB_SERVER_PORT=5641
A=$HOME/emu/sdk/platform-tools/adb
$A -P 5641 start-server >/dev/null 2>&1
setsid nohup sudo -n runuser -u <owner-user> -g kvm -- env HOME=$HOME ANDROID_SDK_ROOT=$ANDROID_SDK_ROOT ANDROID_AVD_HOME=$ANDROID_AVD_HOME ANDROID_USER_HOME=$ANDROID_USER_HOME ANDROID_EMULATOR_HOME=$ANDROID_EMULATOR_HOME ANDROID_ADB_SERVER_PORT=5641 \
  $ANDROID_SDK_ROOT/emulator/emulator -avd aas-phone -port 5600 -no-window -no-audio -no-boot-anim -gpu swiftshader_indirect -no-snapshot-save -no-snapshot-load -accel on \
  -network-user-mode-options restrict=on -wifi-user-mode-options restrict=on > $HOME/.aas/emu-s2.log 2>&1 &
ADB="$A -P 5641 -s emulator-5600"
for i in $(seq 1 150); do [ "$($ADB shell getprop sys.boot_completed 2>/dev/null | tr -d '\r')" = 1 ] && break; sleep 2; done
echo "booted after ~$((i*2))s"; sleep 5
pgrep -f "qemu-system.*aas-phone" | head -1 > $HOME/.aas/pid-s2; cat $HOME/.aas/pid-s2
