ADB="adb -s emulator-5600"
$ADB shell 'dumpsys connectivity | grep -m3 -E "Active default network|NetworkAgentInfo"' | tr -d '\r' | cut -c1-200
for p in 5037 5554 5555 5600 5601 8554 17865 1080 8123 9001 11434; do
  r=$($ADB shell "echo | toybox nc -w 2 -q 1 10.0.2.2 $p >/dev/null 2>/tmp/e; echo \$?; cat /tmp/e 2>/dev/null; true" 2>&1 | tr -d '\r' | tr '\n' ' '); echo "10.0.2.2:$p -> $r"
done
$ADB shell 'echo | toybox nc -w 2 -q 1 10.0.2.2 5037; echo rc=$?' 2>&1 | tr -d '\r' | head -3
$ADB shell 'ping -c1 -W2 8.8.8.8; echo ping_exit=$?' | tr -d '\r' | tail -3
$ADB shell 'echo | toybox nc -w 3 -q 1 1.1.1.1 443 >/dev/null; echo nc_1111_443=$?' 2>&1 | tr -d '\r'
$ADB shell 'echo | toybox nc -w 3 -q 1 10.0.2.2 1 >/dev/null; echo closedport_rc=$?' 2>&1 | tr -d '\r'
$ADB shell 'ip rule | head -30' | tr -d '\r' | tail -8
$ADB shell 'id' | tr -d '\r'
