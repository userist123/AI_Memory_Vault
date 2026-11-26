GPU=$1
export ANDROID_SDK_ROOT=$HOME/emu/sdk ANDROID_AVD_HOME=$HOME/.aas/avd ANDROID_USER_HOME=$HOME/.aas/android-user ANDROID_EMULATOR_HOME=$HOME/.aas/android-user ANDROID_ADB_SERVER_PORT=5641
A=$HOME/emu/sdk/platform-tools/adb; ADB="$A -P 5641 -s emulator-5600"
echo "DISPLAY=$DISPLAY WAYLAND=$WAYLAND_DISPLAY XDG=$XDG_RUNTIME_DIR"; ls /tmp/.X11-unix 2>&1 | head -2; which wmctrl xdotool
T0=$(date +%s)
setsid nohup sudo -n runuser -u <owner-user> -g kvm -- env HOME=$HOME DISPLAY=${DISPLAY:-:0} WAYLAND_DISPLAY=${WAYLAND_DISPLAY:-wayland-0} XDG_RUNTIME_DIR=${XDG_RUNTIME_DIR:-/mnt/wslg/runtime-dir} ANDROID_SDK_ROOT=$ANDROID_SDK_ROOT ANDROID_AVD_HOME=$ANDROID_AVD_HOME ANDROID_USER_HOME=$ANDROID_USER_HOME ANDROID_EMULATOR_HOME=$ANDROID_EMULATOR_HOME ANDROID_ADB_SERVER_PORT=5641 \
  $ANDROID_SDK_ROOT/emulator/emulator -avd aas-phone -port 5600 -no-audio -no-boot-anim -gpu $GPU -no-snapshot-save -no-snapshot-load -accel on \
  -network-user-mode-options restrict=on -wifi-user-mode-options restrict=on > $HOME/.aas/emu-f-$GPU.log 2>&1 &
for i in $(seq 1 90); do [ "$($ADB shell getprop sys.boot_completed 2>/dev/null | tr -d '\r')" = 1 ] && break; sleep 2; done
echo "gpu=$GPU boot=$(( $(date +%s)-T0 ))s i=$i"
sleep 5
grep -i -E "qt|xcb|display|gpu|renderer|error|fail" $HOME/.aas/emu-f-$GPU.log | cut -c1-160 | head -8
