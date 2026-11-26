ADB="adb -s emulator-5600"
echo "== WSL listeners"; ss -ltnp 2>/dev/null | awk 'NR>1{print $4, $6}' | cut -c1-80
python3 -I -m http.server 18080 --bind 127.0.0.1 --directory /tmp >/dev/null 2>&1 &
HP=$!; sleep 1
for p in 1 5037 5554 5555 5600 5601 8554 17865 1080 8123 9001 11434 18080; do
  r=$($ADB shell "s=\$(date +%s); printf 'GET / HTTP/1.0\r\n\r\n' | toybox nc -w 3 -q 2 10.0.2.2 $p 2>/data/local/tmp/e | head -c 60 | tr -d '\r\n'; echo \" rc=\$? err=\$(cat /data/local/tmp/e) t=\$((\$(date +%s)-s))s\"" 2>&1 | tr -d '\r'); echo "10.0.2.2:$p -> $r"
done
kill $HP
$ADB shell 'rm -f /data/local/tmp/e'
