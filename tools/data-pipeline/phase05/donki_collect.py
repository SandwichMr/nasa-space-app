#!/usr/bin/env python3
"""Phase 5 (D3): bounded collector of NASA CCMC DONKI responses for ONE historical window (developer/host tool).

It never runs inside the game, never at session time and never on clients. No API key is used or accepted: the published
DONKI-API OpenAPI document declares no security scheme. Every request is logged (URL, HTTP status, bytes, SHA-256 of the body,
UTC request time) in requests.jsonl next to the raw bodies, which are stored unmodified.

Network access requires the explicit --allow-network flag. Windows are limited to 60 days (DONKI rule) and the query list is
fixed and small.

Usage:
  python3 donki_collect.py --start 2017-09-10 --end 2017-09-11 --out raw/2017-09-10 --allow-network
"""
import argparse
import datetime
import hashlib
import json
import os
import sys
import urllib.error
import urllib.request

BASE = "https://ccmc.gsfc.nasa.gov/DONKI-API"
USER_AGENT = "nasa-space-app-phase05-collector/1.0"
MAX_BYTES = 5_000_000
MAX_WINDOW_DAYS = 60


def queries(start, end):
    """Fixed, bounded query list: flares, SEPs and FLR/SEP notifications of the window."""
    return [
        ("flr.json", "%s/get/FLR?startDate=%s&endDate=%s" % (BASE, start, end)),
        ("sep.json", "%s/get/SEP?startDate=%s&endDate=%s" % (BASE, start, end)),
        ("notif_flr.json", "%s/get/notifications?startDate=%s&endDate=%s&type=FLR" % (BASE, start, end)),
        ("notif_sep.json", "%s/get/notifications?startDate=%s&endDate=%s&type=SEP" % (BASE, start, end)),
    ]


def check_window(start, end):
    a = datetime.date.fromisoformat(start)
    b = datetime.date.fromisoformat(end)
    if b < a:
        raise ValueError("end before start")
    if (b - a).days > MAX_WINDOW_DAYS:
        raise ValueError("DONKI windows are limited to 60 days")


def fetch(url):
    request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT, "Accept": "application/json"})
    requested = datetime.datetime.now(datetime.timezone.utc).isoformat()
    try:
        with urllib.request.urlopen(request, timeout=40) as response:
            return response.status, response.read(MAX_BYTES), requested
    except urllib.error.HTTPError as error:
        return error.code, error.read(200_000), requested


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--start", required=True)
    parser.add_argument("--end", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--allow-network", action="store_true", help="required: this tool makes real HTTP requests")
    args = parser.parse_args(argv)
    check_window(args.start, args.end)
    if not args.allow_network:
        print("refusing to contact DONKI without --allow-network", file=sys.stderr)
        return 2
    os.makedirs(args.out, exist_ok=True)
    log_path = os.path.join(args.out, "requests.jsonl")
    with open(log_path, "a", encoding="utf-8") as log:
        for name, url in queries(args.start, args.end):
            status, body, requested = fetch(url)
            open(os.path.join(args.out, name), "wb").write(body)
            entry = {"file": name, "url": url, "status": status, "bytes": len(body),
                     "sha256": hashlib.sha256(body).hexdigest(), "requested_utc": requested}
            log.write(json.dumps(entry, sort_keys=True) + "\n")
            print(json.dumps(entry, sort_keys=True))
    return 0


if __name__ == "__main__":
    sys.exit(main())
