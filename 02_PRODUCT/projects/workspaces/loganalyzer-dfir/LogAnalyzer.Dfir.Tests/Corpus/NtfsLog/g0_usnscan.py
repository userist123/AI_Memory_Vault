"""G0 gate, direct method (2026-11-14): scan a real $LogFile for USN_RECORD_V2 images (NTFS logs its writes to $UsnJrnl:$J, so the
records' own timestamps date the log). Independent of the C# code: a strict byte pattern, every 8 bytes, after undoing the fixup of each
4 KiB page. Prints the time span of the USN records found and how they spread over the file."""
import datetime, struct, sys
data = bytearray(open(sys.argv[1], "rb").read())
PG = 4096
def ft(v): return datetime.datetime(1601, 1, 1) + datetime.timedelta(microseconds=v // 10)
lo, hi = int((datetime.datetime(2024, 1, 1) - datetime.datetime(1601, 1, 1)).total_seconds() * 1e7), int((datetime.datetime(2027, 1, 1) - datetime.datetime(1601, 1, 1)).total_seconds() * 1e7)
pages = {"RSTR": 0, "RCRD": 0, "other": 0}
for off in range(0, len(data), PG):
    sig = bytes(data[off:off + 4])
    k = sig.decode("latin1") if sig in (b"RSTR", b"RCRD") else "other"
    pages[k] += 1
    if k == "other": continue
    uo, uc = struct.unpack_from("<HH", data, off + 4)
    if uc != PG // 512 + 1 or uo + 2 * uc > 512: continue
    for i in range(1, uc):
        data[off + i * 512 - 2:off + i * 512] = data[off + uo + 2 * i:off + uo + 2 * i + 2]
print("pages:", pages)
hits = []
for o in range(0, len(data) - 64, 8):
    rl, maj, mnr = struct.unpack_from("<IHH", data, o)
    if not (60 <= rl <= 600 and rl % 8 == 0 and maj == 2 and mnr == 0): continue
    fnl, fno = struct.unpack_from("<HH", data, o + 56)
    if fno != 60 or fnl == 0 or fnl % 2 or 60 + fnl > rl: continue
    ts = struct.unpack_from("<q", data, o + 32)[0]
    if not (lo < ts < hi): continue
    try: name = data[o + 60:o + 60 + fnl].decode("utf-16-le")
    except UnicodeDecodeError: continue
    if any(ord(c) < 32 for c in name): continue
    hits.append((ft(ts), o, name))
print(f"USN_RECORD_V2 images found: {len(hits)}")
if hits:
    ts = sorted(h[0] for h in hits)
    print(f"oldest {ts[0]:%Y-%m-%d %H:%M:%S}, newest {ts[-1]:%Y-%m-%d %H:%M:%S}, span {(ts[-1] - ts[0]).total_seconds() / 3600:.2f} h")
    for q in (1, 5, 50, 95):
        print(f"  {q}th percentile: {ts[min(len(ts) - 1, len(ts) * q // 100)]:%Y-%m-%d %H:%M:%S}")
    buckets = {}
    for t, o, n in hits: buckets[o * 16 // len(data)] = buckets.get(o * 16 // len(data), 0) + 1
    print("  spread over the file (16 parts): " + " ".join(str(buckets.get(i, 0)) for i in range(16)))
