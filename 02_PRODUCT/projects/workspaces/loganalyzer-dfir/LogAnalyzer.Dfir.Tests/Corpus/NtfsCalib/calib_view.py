"""Independent view of the calibration volume: $SI / $FN per scenario from raw $MFT bytes + the USN rows of the same file."""
import csv, datetime, json, struct, sys, io

D = sys.argv[1]
mft = open(f"{D}/mft.bin", "rb").read()
truth = json.load(open(f"{D}/truth.json", encoding="utf-8-sig"))
RS = 1024

def ft(v):
    if v <= 0: return "0"
    t = datetime.datetime(1601, 1, 1) + datetime.timedelta(microseconds=v // 10)
    return t.strftime("%Y-%m-%d %H:%M:%S") + f".{v % 10_000_000:07d}"

def record(n):
    r = bytearray(mft[n * RS:(n + 1) * RS])
    uo, uc = struct.unpack_from("<HH", r, 4)
    for i in range(1, uc):
        r[i * 512 - 2:i * 512] = r[uo + 2 * i:uo + 2 * i + 2]
    seq = struct.unpack_from("<H", r, 16)[0]
    p = struct.unpack_from("<H", r, 20)[0]
    si, fns = None, []
    while p + 8 <= RS:
        t, ln = struct.unpack_from("<II", r, p)
        if t == 0xFFFFFFFF or ln == 0: break
        if r[p + 8] == 0:
            vl, vo = struct.unpack_from("<IH", r, p + 16)
            v = r[p + vo:p + vo + vl]
            if t == 0x10: si = struct.unpack_from("<4q", v, 0)
            if t == 0x30:
                times = struct.unpack_from("<4q", v, 8)
                nl, ns = v[64], v[65]
                fns.append((ns, v[66:66 + 2 * nl].decode("utf-16-le"), times))
        p += ln
    return seq, si, fns

usn = list(csv.reader(io.open(f"{D}/usn.csv", encoding="utf-8-sig")))
hdr_i = next(i for i, row in enumerate(usn) if row and row[0] == "Usn")
h = [c.strip() for c in usn[hdr_i]]
rows = [dict(zip(h, [c.strip() for c in r])) for r in usn[hdr_i + 1:] if len(r) >= 10]

for s in truth["scenarios"]:
    fid = int(s["fileId"], 16)
    num, seq_t = fid & 0xFFFFFFFFFFFF, (fid >> 48) & 0xFFFF
    seq, si, fns = record(num)
    print(f"== {s['name']} [{s['expected']}] {s['path']}  rec={num} seq={seq}/{seq_t}  {s['note']}")
    labels = ["B", "M", "C(mft)", "A"]
    print("   SI: " + "  ".join(f"{l}={ft(x)}" for l, x in zip(labels, si)))
    for ns, name, t in fns:
        print(f"   FN ns={ns} {name}: " + "  ".join(f"{l}={ft(x)}" for l, x in zip(labels, t)))
    for r in rows:
        try: rid = int(r["File ID"], 16) & 0xFFFFFFFFFFFFFFFF
        except ValueError: continue
        if rid & 0xFFFFFFFFFFFF == num:
            print(f"   USN {r['Usn']:>6} {r['Time stamp']}  {r['File name']:<28} {r['Reason']}")

