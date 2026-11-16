"""Offline tests of the phase 5 DONKI pipeline (stdlib unittest; no network).

Run: python3 -m unittest discover -s tools/data-pipeline/phase05/tests -v
"""
import hashlib
import json
import os
import shutil
import sys
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
sys.path.insert(0, ROOT)

import donki_collect  # noqa: E402
import normalize_episode as ne  # noqa: E402

RAW = os.path.join(ROOT, "raw", "2017-09-10")
TIMELINE = os.path.join(ROOT, "timeline_2017-09-10.json")
REPO = os.path.dirname(os.path.dirname(os.path.dirname(ROOT)))
FROZEN = os.path.join(REPO, "Assets", "StreamingAssets", "ScientificData", "Phase05", "donki-2017-09-10")


class NormalizeTests(unittest.TestCase):
    def setUp(self):
        self.episode_bytes, self.manifest_bytes = ne.normalize(RAW, TIMELINE)
        self.episode = json.loads(self.episode_bytes)
        self.manifest = json.loads(self.manifest_bytes)

    def test_deterministic_output(self):
        again, _ = ne.normalize(RAW, TIMELINE)
        self.assertEqual(self.episode_bytes, again)

    def test_manifest_hash_matches_episode(self):
        self.assertEqual(self.manifest["sha256"], hashlib.sha256(self.episode_bytes).hexdigest())
        self.assertEqual(len(self.manifest["requests"]), 5)

    def test_phenomena_stay_separate_and_unknown_is_null(self):
        kinds = sorted({r["kind"] for r in self.episode["records"]})
        self.assertEqual(kinds, ["Flare", "Notification", "SolarEnergeticParticles"])
        notification = next(r for r in self.episode["records"] if r["sourceId"] == "20170910-AL-001")
        self.assertIsNone(notification["eventTimeUtc"], "notifications carry no event time of their own")
        self.assertIsNone(notification["version"])

    def test_issued_times_are_demonstrable_availability(self):
        flare = next(r for r in self.episode["records"] if r["kind"] == "Flare")
        self.assertEqual(flare["issuedUtc"], "2017-09-10T23:46Z", "catalog submission time, not the peak")
        self.assertEqual(flare["eventTimeUtc"], "2017-09-10T16:06Z")
        watch = self.episode["messages"][0]
        self.assertEqual(watch["sourceIssuedUtc"], "2017-09-10T16:07Z")

    def test_flare_class_is_not_quoted_before_it_was_known(self):
        # The X8.2 catalog record (submitted 23:46Z) is quoted only by the 12 September update.
        quoting = [m["id"] for m in self.episode["messages"] if "2017-09-10T15:35:00-FLR-001" in m["sourceRecordIds"]]
        self.assertEqual(quoting, ["hist-update-ongoing"])

    def test_end_is_a_didactic_decision_without_sources(self):
        end = self.episode["messages"][-1]
        self.assertEqual(end["kind"], "InstructionEnd")
        self.assertEqual(end["basis"], "DidacticDecision")
        self.assertEqual(end["sourceRecordIds"], [])

    def test_no_dose_or_clinical_fields(self):
        text = self.episode_bytes.decode("utf-8").lower()
        for word in ("dose", "sievert", "msv", "gray", "clinical", "all-clear", "all clear"):
            self.assertNotIn(word, text)

    def test_tampered_raw_file_is_rejected(self):
        work = tempfile.mkdtemp()
        try:
            for name in os.listdir(RAW):
                shutil.copy(os.path.join(RAW, name), work)
            with open(os.path.join(work, "sep.json"), "ab") as handle:
                handle.write(b" ")
            with self.assertRaises(ne.NormalizeError):
                ne.normalize(work, TIMELINE)
        finally:
            shutil.rmtree(work)

    def test_unknown_quoted_record_is_rejected(self):
        work = tempfile.mkdtemp()
        try:
            timeline = json.load(open(TIMELINE, encoding="utf-8"))
            timeline["messages"][0]["sources"] = ["NOT-A-RECORD"]
            path = os.path.join(work, "t.json")
            json.dump(timeline, open(path, "w", encoding="utf-8"))
            with self.assertRaises(ne.NormalizeError):
                ne.normalize(RAW, path)
        finally:
            shutil.rmtree(work)

    def test_frozen_dataset_is_the_normalized_output(self):
        episode = open(os.path.join(FROZEN, "episode.json"), "rb").read()
        manifest = json.load(open(os.path.join(FROZEN, "manifest.json"), encoding="utf-8"))
        self.assertEqual(episode, self.episode_bytes, "re-run the normalizer: the frozen episode differs")
        self.assertEqual(manifest["sha256"], hashlib.sha256(episode).hexdigest())


class CollectorTests(unittest.TestCase):
    def test_refuses_without_explicit_network_flag(self):
        out = tempfile.mkdtemp()
        try:
            self.assertEqual(donki_collect.main(["--start", "2017-09-10", "--end", "2017-09-11", "--out", out]), 2)
            self.assertEqual(os.listdir(out), [])
        finally:
            shutil.rmtree(out)

    def test_window_is_bounded_and_queries_carry_no_key(self):
        with self.assertRaises(ValueError):
            donki_collect.check_window("2017-01-01", "2017-06-01")
        for _, url in donki_collect.queries("2017-09-10", "2017-09-11"):
            self.assertTrue(url.startswith("https://ccmc.gsfc.nasa.gov/DONKI-API/get/"))
            self.assertNotIn("api_key", url.lower())


if __name__ == "__main__":
    unittest.main()
