#!/usr/bin/env python3
"""One-time downloader for Delaware Code title PDFs.

IMPORTANT: Each title PDF is downloaded at most once. If the local file already
exists and is non-empty, this script will NOT contact delcode.delaware.gov for
that title again.
"""

from __future__ import annotations

import json
import sys
import time
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "data" / "pdfs"
MANIFEST = ROOT / "data" / "download_manifest.json"
BASE = "https://delcode.delaware.gov"


def main() -> int:
    OUT.mkdir(parents=True, exist_ok=True)
    files = []
    downloaded = 0
    skipped = 0

    for n in range(1, 32):
        name = f"title{n}.pdf"
        dest = OUT / name
        url = f"{BASE}/title{n}/{name}"
        if dest.exists() and dest.stat().st_size > 0:
            print(f"SKIP (already archived): {name} ({dest.stat().st_size:,} bytes)")
            files.append(
                {
                    "title": n,
                    "file": name,
                    "url": url,
                    "bytes": dest.stat().st_size,
                    "status": "skipped_existing",
                }
            )
            skipped += 1
            continue

        print(f"DOWNLOADING ({n}/31): {url}")
        req = urllib.request.Request(
            url,
            headers={
                "User-Agent": "statuteViewer-local-archive/1.0 (one-time offline archive)"
            },
        )
        with urllib.request.urlopen(req, timeout=180) as resp:
            data = resp.read()
            ctype = resp.headers.get("Content-Type", "")
        if b"%PDF" not in data[:8] and "pdf" not in ctype.lower():
            print(f"ERROR: not a PDF for title {n}", file=sys.stderr)
            return 1
        dest.write_bytes(data)
        print(f"  saved {len(data):,} bytes")
        files.append(
            {
                "title": n,
                "file": name,
                "url": url,
                "bytes": len(data),
                "status": "downloaded",
            }
        )
        downloaded += 1
        time.sleep(0.5)

    manifest = {
        "source": BASE,
        "downloaded_at_utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        "policy": (
            "Each title PDF is downloaded exactly once and committed to the repo. "
            "Never re-fetch a title when the local file already exists."
        ),
        "downloaded_this_run": downloaded,
        "skipped_existing": skipped,
        "files": files,
    }
    MANIFEST.write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    print(f"Done. downloaded={downloaded} skipped={skipped}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
