#!/usr/bin/env python3
"""
Regenerates SH2EE.Core/Resources/component-files.csv — the map of which installed
file came from which component, used to repackage an existing installation into
offline archives without re-downloading ~4 GB from the SH2:EE servers.

Run this whenever upstream bumps a component version, otherwise the map goes stale
and repackaging will warn (and skip files it can't account for):

    python3 tools/refresh-component-map.py SH2EE.Core/Resources/component-files.csv

It reads each component's file list and per-file CRC-32 straight out of the ZIP
central directory using HTTP Range requests, so it transfers a few hundred KB
rather than the full archives. The whole run takes well under a minute.
"""
import csv, io, struct, sys, urllib.request

CSV = "https://files.townofsilenthill.com/SH2EE/_sh2ee.csv"


def fetch(url, start=None, end=None):
    req = urllib.request.Request(url)
    if start is not None:
        req.add_header("Range", f"bytes={start}-{end}")
    with urllib.request.urlopen(req, timeout=60) as r:
        return r.read(), r.headers


def size_of(url):
    req = urllib.request.Request(url, method="HEAD")
    with urllib.request.urlopen(req, timeout=60) as r:
        return int(r.headers["Content-Length"])


def central_directory(url):
    total = size_of(url)

    # EOCD is within the last 64 KiB + 22 bytes (max comment length).
    tail_len = min(total, 65536 + 22)
    tail, _ = fetch(url, total - tail_len, total - 1)

    pos = tail.rfind(b"PK\x05\x06")
    if pos < 0:
        raise RuntimeError("no EOCD found")
    count, cd_size, cd_off = struct.unpack("<HLL", tail[pos + 10:pos + 20])

    # ZIP64 when the 32-bit fields are saturated.
    if cd_off == 0xFFFFFFFF or cd_size == 0xFFFFFFFF or count == 0xFFFF:
        loc = tail.rfind(b"PK\x06\x07")
        if loc < 0:
            raise RuntimeError("ZIP64 locator missing")
        eocd64_off = struct.unpack("<Q", tail[loc + 8:loc + 16])[0]
        blob, _ = fetch(url, eocd64_off, eocd64_off + 55)
        count, cd_size, cd_off = struct.unpack("<QQQ", blob[32:56])

    cd, _ = fetch(url, cd_off, cd_off + cd_size - 1)

    entries, off = [], 0
    while off < len(cd) and cd[off:off + 4] == b"PK\x01\x02":
        (crc, csize, usize, nlen, elen, clen) = struct.unpack("<LLLHHH", cd[off + 16:off + 34])
        name = cd[off + 46:off + 46 + nlen].decode("utf-8", "replace")
        entries.append((name, crc, usize))
        off += 46 + nlen + elen + clen
    return total, entries


rows = list(csv.reader(io.StringIO(fetch(CSV)[0].decode())))
grand_files = 0
grand_bytes = 0
out = []

for row in rows:
    if not row or row[0].startswith("#") or row[0] in ("id", "setup_tool"):
        continue
    cid, name, ver, url = row[0], row[1], row[2], row[3]
    try:
        total, entries = central_directory(url)
    except Exception as e:
        print(f"  {cid:22} FAILED: {e}")
        continue
    files = [e for e in entries if not e[0].endswith("/")]
    grand_files += len(files)
    grand_bytes += total
    archive = url.rsplit("/", 1)[-1]
    print(f"  {cid:22} {total/1048576:8.1f} MB archive   {len(files):5} files")
    for n, crc, usize in files:
        out.append((cid, name, ver, archive, n, f"{crc:08X}", usize))

print(f"\n  TOTAL {grand_bytes/1048576:.0f} MB of archives — {grand_files} files mapped")

with open(sys.argv[1], "w", newline="") as f:
    w = csv.writer(f)
    w.writerow(["componentId", "componentName", "componentVersion", "archiveFileName", "path", "crc32", "size"])
    w.writerows(out)
print(f"  written: {sys.argv[1]}")
