#!/usr/bin/env python3
"""Phase 5 (D3): normalize frozen raw DONKI responses into the game's historical episode (schema v1).

raw responses (verified against requests.jsonl hashes) + didactic timeline -> episode.json + manifest.json

Offline only: never touches the network. Deterministic: same inputs give byte-identical outputs.
Rules (docs/SCIENTIFIC_RULES.md, docs/phase-05/DATA_PROVENANCE.md):
  * FLR, SEP and notifications stay separate records; nothing is converted into dose or damage.
  * issuedUtc = messageIssueTime (notifications) or submissionTime (catalog records): first DEMONSTRABLE availability.
  * Unknown values are null, never zero.
  * The end of the shelter period is a didactic decision; it never quotes a source as an all-clear.

Usage:
  python3 normalize_episode.py --raw raw/2017-09-10 --timeline timeline_2017-09-10.json --out <dir>
"""
import argparse
import hashlib
import json
import os
import re
import sys

SCHEMA_VERSION = 1
PROVIDER = "NASA GSFC CCMC/SWRC DONKI"
MAX_TEXT = 280
UTC_RE = re.compile(r"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d{1,3})?)?Z$")


class NormalizeError(Exception):
    pass


def sha256_bytes(data):
    return hashlib.sha256(data).hexdigest()


def load_verified(raw_dir):
    """Loads every raw file named in requests.jsonl and checks its SHA-256 and size against the request log."""
    log_path = os.path.join(raw_dir, "requests.jsonl")
    if not os.path.isfile(log_path):
        raise NormalizeError("missing requests.jsonl in " + raw_dir)
    files, log = {}, []
    with open(log_path, encoding="utf-8") as handle:
        for line in handle:
            line = line.strip()
            if not line:
                continue
            entry = json.loads(line)
            data = open(os.path.join(raw_dir, entry["file"]), "rb").read()
            if sha256_bytes(data) != entry["sha256"] or len(data) != entry["bytes"]:
                raise NormalizeError("hash or size mismatch for " + entry["file"])
            if entry.get("status") != 200:
                raise NormalizeError("non-200 response logged for " + entry["file"])
            files[entry["file"]] = json.loads(data.decode("utf-8"))
            log.append(entry)
    return files, log


def utc_minutes(value):
    """DONKI times are 'YYYY-MM-DDTHH:MMZ' or with seconds; returns the value unchanged when valid, None when missing."""
    if value in (None, ""):
        return None
    if not UTC_RE.match(value):
        raise NormalizeError("unexpected UTC format: " + repr(value))
    return value


def retrieved_utc(log):
    """Latest request time of the log, as ISO-8601 with seconds and Z."""
    stamps = sorted(entry["requested_utc"] for entry in log)
    stamp = stamps[-1]
    match = re.match(r"^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})", stamp)
    if not match:
        raise NormalizeError("bad requested_utc " + stamp)
    return match.group(1) + "Z"


def summary_of(body):
    """First paragraph of the '## Summary:' section, whitespace collapsed, bounded."""
    match = re.search(r"## Summary:\s*(.*?)(\n\s*\n|## Notes:)", body, re.S)
    text = match.group(1) if match else body
    text = " ".join(text.split())
    return text[:MAX_TEXT]


def find(items, key, value):
    for item in items:
        if item.get(key) == value:
            return item
    raise NormalizeError("record %s=%s not found in raw data" % (key, value))


def build_record(spec, files, retrieved):
    data = files.get(spec["file"])
    if data is None:
        raise NormalizeError("raw file not logged: " + spec["file"])
    kind = spec["type"]
    if kind == "notification":
        n = find(data, "messageID", spec["id"])
        record = {
            "provider": PROVIDER, "kind": "Notification", "sourceId": n["messageID"], "version": None,
            "uri": n["messageURL"], "eventTimeUtc": None, "issuedUtc": utc_minutes(n["messageIssueTime"]),
            "summary": summary_of(n["messageBody"]),
        }
    elif kind == "sep":
        s = find(data, "sepID", spec["id"])
        instruments = ", ".join(i["displayName"] for i in s.get("instruments") or [])
        record = {
            "provider": PROVIDER, "kind": "SolarEnergeticParticles", "sourceId": s["sepID"], "version": s.get("versionId"),
            "uri": s["link"], "eventTimeUtc": utc_minutes(s["eventTime"]), "issuedUtc": utc_minutes(s.get("submissionTime")),
            "summary": ("SEP onset " + s["eventTime"] + " measured by " + instruments)[:MAX_TEXT],
        }
    elif kind == "flr":
        f = find(data, "flrID", spec["id"])
        instruments = ", ".join(i["displayName"] for i in f.get("instruments") or [])
        record = {
            "provider": PROVIDER, "kind": "Flare", "sourceId": f["flrID"], "version": f.get("versionId"),
            "uri": f["link"], "eventTimeUtc": utc_minutes(f["peakTime"]), "issuedUtc": utc_minutes(f.get("submissionTime")),
            "summary": ("Flare class %s, peak %s, location %s, active region %s, %s"
                        % (f["classType"], f["peakTime"], f.get("sourceLocation"), f.get("activeRegionNum"), instruments))[:MAX_TEXT],
        }
    else:
        raise NormalizeError("unknown record type " + kind)
    record["retrievedUtc"] = retrieved
    record["measurements"] = [dict(m) for m in spec.get("measurements", [])]
    for m in record["measurements"]:
        if m["classification"] not in ("Observation", "Reference", "Calculation", "Assumption"):
            raise NormalizeError("bad classification " + m["classification"])
    return record


def build_messages(timeline, records):
    ids = {r["sourceId"]: r for r in records}
    messages, previous_offset = [], -1.0
    for spec in timeline["messages"]:
        for source in spec.get("sources", []):
            if source not in ids:
                raise NormalizeError("message %s quotes unknown record %s" % (spec["id"], source))
        if spec["offset"] < previous_offset:
            raise NormalizeError("messages out of order at " + spec["id"])
        if spec["kind"] == "InstructionEnd" and spec["basis"] == "SourceNotification":
            raise NormalizeError("the end of the shelter period is never quoted as a source all-clear")
        issued = None
        if spec.get("sourceIssued"):
            issued = ids[spec["sourceIssued"]]["issuedUtc"]
        messages.append({
            "id": spec["id"], "kind": spec["kind"], "basis": spec["basis"], "offsetSeconds": float(spec["offset"]),
            "validForSeconds": float(spec["validFor"]),
            "shelterByOffsetSeconds": float(spec["shelterBy"]) if "shelterBy" in spec else None,
            "supersedes": spec.get("supersedes"), "textKey": spec["textKey"], "sourceRecordIds": list(spec.get("sources", [])),
            "sourceIssuedUtc": issued,
        })
        previous_offset = spec["offset"]
    return messages


def normalize(raw_dir, timeline_path):
    files, log = load_verified(raw_dir)
    timeline = json.load(open(timeline_path, encoding="utf-8"))
    retrieved = retrieved_utc(log)
    records = [build_record(spec, files, retrieved) for spec in timeline["records"]]
    if len({r["sourceId"] for r in records}) != len(records):
        raise NormalizeError("duplicate source records")
    episode = {
        "schemaVersion": SCHEMA_VERSION, "key": timeline["key"], "mode": "Historical", "label": timeline["label"],
        "provenance": timeline["provenance"], "timelineNote": timeline["timelineNote"],
        "dataset": {"datasetId": timeline["datasetId"], "schemaVersion": SCHEMA_VERSION, "retrievedUtc": retrieved,
                    "license": timeline["license"]},
        "records": records, "messages": build_messages(timeline, records),
        "didacticOnsetSeconds": float(timeline["didacticOnsetSeconds"]),
        "didacticEndSeconds": float(timeline["didacticEndSeconds"]),
    }
    episode_bytes = (json.dumps(episode, ensure_ascii=False, indent=1, sort_keys=True) + "\n").encode("utf-8")
    manifest = {
        "schemaVersion": SCHEMA_VERSION, "datasetId": timeline["datasetId"], "episodeFile": "episode.json",
        "sha256": sha256_bytes(episode_bytes), "retrievedUtc": retrieved, "license": timeline["license"],
        "source": "https://ccmc.gsfc.nasa.gov/DONKI-API (no API key; OpenAPI at https://ccmc.gsfc.nasa.gov/DONKI/api/openapi/)",
        "requests": [{"url": e["url"], "status": e["status"], "bytes": e["bytes"], "sha256": e["sha256"],
                      "requestedUtc": e["requested_utc"]} for e in log],
        "generator": "tools/data-pipeline/phase05/normalize_episode.py",
    }
    manifest_bytes = (json.dumps(manifest, ensure_ascii=False, indent=1, sort_keys=True) + "\n").encode("utf-8")
    return episode_bytes, manifest_bytes


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--raw", required=True)
    parser.add_argument("--timeline", required=True)
    parser.add_argument("--out", required=True)
    args = parser.parse_args(argv)
    try:
        episode_bytes, manifest_bytes = normalize(args.raw, args.timeline)
    except (NormalizeError, KeyError, ValueError) as error:
        print("normalize failed: %s" % error, file=sys.stderr)
        return 2
    os.makedirs(args.out, exist_ok=True)
    open(os.path.join(args.out, "episode.json"), "wb").write(episode_bytes)
    open(os.path.join(args.out, "manifest.json"), "wb").write(manifest_bytes)
    print(json.dumps({"episode_sha256": sha256_bytes(episode_bytes), "out": args.out}))
    return 0


if __name__ == "__main__":
    sys.exit(main())
