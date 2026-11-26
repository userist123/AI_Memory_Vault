ADB="adb -s emulator-5600"
GW=$(ip route show default | awk '{print $3}')
python3 -I -m http.server 18080 --bind 127.0.0.1 --directory /tmp >/dev/null 2>&1 &
HP=$!; sleep 1
for t in 10.0.2.2:1 10.0.2.2:5037 10.0.2.2:5600 10.0.2.2:5601 10.0.2.2:1080 10.0.2.2:8123 10.0.2.2:9001 10.0.2.2:11434 10.0.2.2:18080 $GW:135 $GW:445 $GW:3389 1.1.1.1:443; do
  h=${t%:*}; p=${t#*:}
  r=$($ADB shell "printf 'GET / HTTP/1.0\r\n\r\n' | toybox nc -w 3 -q 2 $h $p 2>/data/local/tmp/e | head -c 40 | tr -d '\r\n'; echo \" err=\$(cat /data/local/tmp/e)\"" 2>&1 | tr -d '\r'); echo "$t -> $r"
done
$ADB shell 'printf 000chost:version | toybox nc -w 3 -q 2 10.0.2.2 5037 | head -c 20; echo " <-adb host:version"' | tr -d '\r'
$ADB shell 'ping -c1 -W2 8.8.8.8 >/dev/null 2>&1; echo ping_8888_exit=$?' | tr -d '\r'
kill $HP; $ADB shell 'rm -f /data/local/tmp/e'
