# usage: run.sh <tag> [extra emulator flags...]
TAG=$1; shift
export ANDROID_SDK_ROOT=$HOME/emu/sdk ANDROID_AVD_HOME=$HOME/.aas/avd
ADB="adb -s emulator-5600"
LOG=$HOME/.aas/emu.log; [ "$TAG" = main ] || LOG=$HOME/.aas/emu-$TAG.log
echo "== free before"; free -m
T0=$(date +%s.%N)
setsid nohup sudo -n runuser -u <owner-user> -g kvm -- env HOME=$HOME ANDROID_SDK_ROOT=$ANDROID_SDK_ROOT ANDROID_AVD_HOME=$ANDROID_AVD_HOME \
  $ANDROID_SDK_ROOT/emulator/emulator -avd aas-phone -port 5600 -no-window -no-audio -no-boot-anim \
  -gpu swiftshader_indirect -no-snapshot-save -no-snapshot-load -accel on "$@" > $LOG 2>&1 &
sleep 3
PID=$(pgrep -f 'qemu-system-x86_64.*-avd aas-phone' | head -1)
echo "emulator qemu PID=$PID"; echo $PID > $HOME/.aas/pid-$TAG
ok=0
for i in $(seq 1 150); do
  v=$($ADB shell getprop sys.boot_completed 2>/dev/null | tr -d '\r')
  if [ "$v" = "1" ]; then ok=1; break; fi
  sleep 2
done
T1=$(date +%s.%N)
echo "boot_completed=$ok after $(echo "$T1 - $T0" | bc) s"
[ $ok = 1 ] || { tail -30 $LOG; }
echo "== RSS (KB)"; ps -o pid,rss,vsz,cmd -p $PID | cut -c1-120
echo "== free after"; free -m
if [ "$TAG" = main ]; then
  s=$(date +%s.%N); $ADB exec-out screencap -p > $HOME/.aas/shot.png; e=$(date +%s.%N)
  echo "screencap took $(echo "$e - $s" | bc) s"; ls -l $HOME/.aas/shot.png; file $HOME/.aas/shot.png
  python3 -c "import struct;d=open('$HOME/.aas/shot.png','rb').read(24);print('PNG',struct.unpack('>II',d[16:24]))"
  for k in 1 2 3; do s=$(date +%s.%N); $ADB exec-out screencap -p > /tmp/aas-shot$k.png; e=$(date +%s.%N); echo "screencap repeat $k: $(echo "$e - $s" | bc) s"; done
  rm -f /tmp/aas-shot?.png
  cp $HOME/.aas/shot.png /mnt/e/claude-work/ai-app-studio/spike/shot.png
  s=$(date +%s.%N); $ADB shell input tap 540 1200; echo "tap exit=$? took $(echo "$(date +%s.%N) - $s" | bc) s"
fi
