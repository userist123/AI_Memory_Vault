echo "id: $(id)"; echo "ip link: $(ip -o link 2>&1 | tr '\n' ' ')"
python3 - <<'P'
try:
    f=open('/dev/kvm','rb+',buffering=0); print('open /dev/kvm: OK')
except Exception as e: print('open /dev/kvm FAIL:',e)
P
export ANDROID_SDK_ROOT=$HOME/emu/sdk ANDROID_AVD_HOME=$HOME/.aas/avd ANDROID_USER_HOME=$HOME/.aas/android-user ANDROID_EMULATOR_HOME=$HOME/.aas/android-user ANDROID_ADB_SERVER_PORT=5641
ip link add d0 type dummy 2>&1; ip link set d0 up 2>&1; ip -6 addr add fd00::1/64 dev d0 nodad 2>&1; ip link set lo up; echo "ipv6 disable_ipv6=$(cat /proc/sys/net/ipv6/conf/lo/disable_ipv6 2>&1)"; ip addr add ::1/128 dev lo nodad 2>&1; ip -o addr show lo | tr "
" " "; echo
A=$HOME/emu/sdk/platform-tools/adb
$A -P 5641 start-server 2>&1 | tail -2; echo "adb start rc=$?"; ss -ltn | awk 'NR>1{print $4}' | tr '\n' ' '; echo
$ANDROID_SDK_ROOT/emulator/emulator -accel-check 2>&1 | tail -2
setsid nohup $ANDROID_SDK_ROOT/emulator/emulator -avd aas-phone -port 5600 -no-window -no-audio -no-boot-anim -gpu swiftshader_indirect -no-snapshot-save -no-snapshot-load -accel on -network-user-mode-options restrict=on -wifi-user-mode-options restrict=on > $HOME/.aas/emu-ns.log 2>&1 &
ADB="$A -P 5641 -s emulator-5600"
for i in $(seq 1 45); do [ "$($ADB shell getprop sys.boot_completed 2>/dev/null | tr -d '\r')" = 1 ] && { echo "booted ~$((i*2))s"; break; }; sleep 2; done
$ADB devices 2>&1; tail -5 $HOME/.aas/emu-ns.log | cut -c1-200
$ADB -s emulator-5600 emu kill >/dev/null 2>&1; sleep 10; $A -P 5641 kill-server
pkill -f "qemu-system.*aas-phone"; true
