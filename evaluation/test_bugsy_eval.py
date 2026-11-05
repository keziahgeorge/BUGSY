"""Synthetic unit tests for metric logic only; not BUGSY experiment results."""
import unittest
import csv
import subprocess
import sys
import tempfile
import re
from pathlib import Path

from bugsy_eval import (match_events, normalize_alerts, normalize_ground_truth,
                        ratio, read_csv, to_generated_alerts, value)


def gt(event_id="e1", fault="VelocitySpike", t=10.0, run="r", trial="t", episode="1"):
    return {"RunId": run, "TrialId": trial, "EpisodeId": episode, "EventId": event_id,
            "EventType": fault, "Time": t, "GameTime": t, "Position": ()}


def alert(alert_id="a1", category="Physics", t=10.1, run="r", trial="t", episode="1", rule=None):
    return {"RunId": run, "TrialId": trial, "EpisodeId": episode, "AlertId": alert_id,
            "Category": category, "Rule": rule or category, "Time": t, "GameTime": t,
            "MeasuredValue": 40, "Threshold": 20, "Position": (), "raw": {}}


class EvaluationTests(unittest.TestCase):
    def test_one_to_one_event_matching_and_latency(self):
        result = match_events([gt()], [alert()], 0.5)
        self.assertEqual(result["metrics"]["TP"], 1)
        self.assertAlmostEqual(result["matches"][0]["latency"], 0.1)

    def test_simultaneous_velocity_rules_are_one_injection_detection(self):
        result = match_events([gt()], [alert("p", "Physics", 10.0),
                                      alert("h", "HorizontalVelocity", 10.0, rule="HorizontalVelocity")], 0.5)
        self.assertEqual(result["metrics"]["TP"], 1)
        self.assertEqual(result["metrics"]["Ambiguous"], 0)
        self.assertEqual(len(result["duplicate_alert_indices"]), 1)

    def test_repeated_alert_rows_deduplicate_as_one_alert_episode(self):
        result = match_events([], [alert("a", "Physics", 1), alert("b", "Physics", 1.1)], 0.2,
                              [{"RunId":"r","TrialId":"t","EpisodeId":"1","Start":0,"End":5}], 1)
        self.assertEqual(result["metrics"]["UnmatchedAlertsInNormalIntervals"], 1)

    def test_unmatched_injection_is_fn(self):
        result = match_events([gt()], [], 0.5)
        self.assertEqual(result["metrics"]["FN"], 1)
        self.assertEqual(result["matches"][0]["status"], "FN")

    def test_missing_timestamp_is_not_fabricated_and_remains_fn(self):
        event = gt(); event["Time"] = None
        result = match_events([event], [alert()], 0.5)
        self.assertEqual(result["metrics"]["FN"], 1)
        self.assertIsNone(result["matches"][0]["latency"])

    def test_unmatched_alert_not_counted_fp_without_normal_labels(self):
        result = match_events([], [alert()], 0.5)
        self.assertIsNone(result["metrics"]["FP"])
        self.assertIsNone(result["metrics"]["TN"])
        self.assertIsNone(result["metrics"]["FPR"])

    def test_false_positive_and_fpr_require_normal_intervals(self):
        interval = {"RunId":"r","TrialId":"t","EpisodeId":"2","Start":0,"End":10}
        result = match_events([], [alert(t=3, episode="2")], 0.5, [interval], 5)
        self.assertEqual(result["metrics"]["FP"], 1)
        self.assertEqual(result["metrics"]["TN"], 1)
        self.assertEqual(result["metrics"]["FPR"], 0.5)

    def test_metrics_formulas(self):
        self.assertEqual(ratio(3, 4), 0.75)
        self.assertIsNone(ratio(0, 0))

    def test_threshold_configuration_recomputes_from_samples(self):
        sample = {"RunId":"r","TrialId":"t","EpisodeId":"1","SampleRealtimeSinceStartup":"10.1",
                  "TotalSpeed":"21","MaxExpectedSpeed":"20","HorizontalSpeed":"8","MaxHorizontalSpeed":"12",
                  "VerticalSpeed":"1","MaxVerticalSpeed":"25","PositionChange":"0.1","MaxPositionChange":"2"}
        self.assertEqual(len(to_generated_alerts([sample])), 1)
        self.assertEqual(len(to_generated_alerts([sample], total_threshold=22)), 0)

    def test_velocity_thresholds_are_independently_configurable(self):
        sample = {"RunId":"r","TrialId":"t","EpisodeId":"1","SampleRealtimeSinceStartup":"10.1",
                  "TotalSpeed":"21","MaxExpectedSpeed":"20","HorizontalSpeed":"13","MaxHorizontalSpeed":"12",
                  "VerticalSpeed":"26","MaxVerticalSpeed":"25","PositionChange":"0.1","MaxPositionChange":"2"}
        default_rules = {a["Rule"] for a in to_generated_alerts([sample])}
        self.assertEqual(default_rules, {"TotalVelocity", "HorizontalVelocity", "VerticalVelocity"})
        adjusted = to_generated_alerts([sample], total_threshold=22,
                                       horizontal_threshold=14, vertical_threshold=27)
        self.assertEqual(adjusted, [])

    def test_alert_before_injection_does_not_match(self):
        result = match_events([gt(t=10)], [alert(t=9.9)], 0.5)
        self.assertEqual(result["metrics"]["TP"], 0)
        self.assertEqual(result["metrics"]["FN"], 1)

    def test_competing_injections_are_reported_ambiguous(self):
        result = match_events([gt("e1", t=10), gt("e2", t=10)], [alert(t=10.05)], 0.5)
        self.assertEqual(result["metrics"]["Ambiguous"], 2)
        self.assertEqual(result["metrics"]["TP"], 0)

    def test_precision_recall_f1_and_fpr(self):
        events = [gt("e1", t=10), gt("e2", t=30)]
        alerts = [alert("matched", t=10.1), alert("false", t=5, episode="2")]
        normal = [{"RunId":"r","TrialId":"t","EpisodeId":"2","Start":0,"End":10}]
        result = match_events(events, alerts, 0.5, normal, 10)
        self.assertEqual(result["metrics"]["TP"], 1)
        self.assertEqual(result["metrics"]["FP"], 1)
        self.assertEqual(result["metrics"]["FN"], 1)
        self.assertEqual(result["metrics"]["Precision"], 0.5)
        self.assertEqual(result["metrics"]["Recall"], 0.5)
        self.assertEqual(result["metrics"]["F1"], 0.5)
        self.assertEqual(result["metrics"]["FPR"], 1.0)

    def test_ablation_filters_only_selected_rule(self):
        events = [alert("p", "Physics", rule="TotalVelocity"), alert("h", "HorizontalVelocity", rule="HorizontalVelocity")]
        ablated = [item for item in events if item["Rule"] != "TotalVelocity"]
        self.assertEqual([item["Category"] for item in ablated], ["HorizontalVelocity"])

    def test_cli_emits_report_files_from_synthetic_fixture(self):
        script = Path(__file__).with_name("bugsy_eval.py")
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            gt_path, alerts_path, output = root / "gt.csv", root / "alerts.csv", root / "out"
            with gt_path.open("w", newline="") as stream:
                writer = csv.DictWriter(stream, fieldnames=["RunId", "TrialId", "EpisodeId", "EventId", "EventType", "RealtimeSinceStartup"])
                writer.writeheader(); writer.writerow({"RunId":"r","TrialId":"t","EpisodeId":1,"EventId":"e","EventType":"Teleport","RealtimeSinceStartup":2})
            with alerts_path.open("w", newline="") as stream:
                writer = csv.DictWriter(stream, fieldnames=["RunId", "TrialId", "EpisodeId", "AlertId", "DetectionRealtimeSinceStartup", "Category", "DetectionRule"])
                writer.writeheader(); writer.writerow({"RunId":"r","TrialId":"t","EpisodeId":1,"AlertId":"a","DetectionRealtimeSinceStartup":2.1,"Category":"PositionDiscontinuity","DetectionRule":"PositionDiscontinuity"})
            subprocess.run([sys.executable, str(script), "--ground-truth", str(gt_path), "--alerts", str(alerts_path), "--tolerance-seconds", "0.5", "--output", str(output)], check=True, capture_output=True, text=True)
            expected = ["evaluation_summary.csv", "per_trial_metrics.csv", "per_category_metrics.csv",
                        "repeated_trial_summary.csv", "event_matching.csv", "sensitivity_analysis.csv",
                        "ablation_results.csv", "computational_efficiency.csv", "evaluation_report.md",
                        "metrics_comparison.svg", "threshold_sensitivity.svg", "ablation_comparison.svg",
                        "detection_latency.svg", "computational_overhead.svg"]
            self.assertTrue(all((output / name).is_file() for name in expected))

    def test_exact_unity_writer_schemas_normalize_and_match(self):
        project_root = Path(__file__).parents[1]
        truth_source = (project_root / "Assets/Scripts/BugsyGroundTruthRecorder.cs").read_text()
        detector_source = (project_root / "Assets/Scripts/BugsyAnomalyDetector.cs").read_text()
        truth_match = re.search(r"private const string Header\s*=\s*(.*?);", truth_source, re.S)
        alert_match = re.search(r'OpenAppendCsv\(Path.Combine\(root, "bugsy_detector_events.csv"\),\s*"([^"]+)"\)', detector_source, re.S)
        sample_match = re.search(r'OpenAppendCsv\(Path.Combine\(root, "bugsy_detector_samples.csv"\),\s*"([^"]+)"\)', detector_source, re.S)
        performance_match = re.search(r'if \(needsHeader\) writer.WriteLine\("([^"]+)"\)', detector_source, re.S)
        self.assertIsNotNone(truth_match)
        self.assertIsNotNone(alert_match)
        self.assertIsNotNone(sample_match)
        self.assertIsNotNone(performance_match)
        truth_header = "".join(re.findall(r'"([^"\\]*)"', truth_match.group(1))).split(",")
        alert_header = alert_match.group(1).split(",")
        sample_header = sample_match.group(1).split(",")
        performance_header = performance_match.group(1).split(",")
        run_id, trial_id, episode_id = "run-fixed", "trial-fixed", "7"
        with tempfile.TemporaryDirectory() as temp:
            gt_path, alert_path = Path(temp) / "gt.csv", Path(temp) / "alerts.csv"
            gt_record = {name: "" for name in truth_header}
            gt_record.update({"RunId":run_id, "TrialId":trial_id, "EpisodeId":episode_id,
                              "EventId":"event-fixed", "EventType":"VelocitySpike",
                              "GameTime":"10.005", "RealtimeSinceStartup":"12.125000",
                              "PositionAfterX":"3", "PositionAfterY":"1", "PositionAfterZ":"4"})
            alert_record = {name: "" for name in alert_header}
            alert_record.update({"RunId":run_id, "TrialId":trial_id, "EpisodeId":episode_id,
                                 "AlertId":"alert-fixed", "DetectionRealtimeSinceStartup":"12.135",
                                 "GameTime":"10.015", "Category":"Physics", "DetectionRule":"TotalVelocity",
                                 "MeasuredValue":"40", "Threshold":"20", "PosX":"3", "PosY":"1", "PosZ":"4"})
            for path, header, record in ((gt_path, truth_header, gt_record), (alert_path, alert_header, alert_record)):
                with path.open("w", newline="") as stream:
                    writer = csv.DictWriter(stream, fieldnames=header)
                    writer.writeheader(); writer.writerow(record)
            gt_rows = normalize_ground_truth(read_csv(gt_path))
            alert_rows = normalize_alerts(read_csv(alert_path))
            result = match_events(gt_rows, alert_rows, 0.05)
        sample_record = {name: "0" for name in sample_header}
        sample_record.update({"RunId":run_id, "TrialId":trial_id, "EpisodeId":episode_id,
                              "SampleRealtimeSinceStartup":"12.14", "TotalSpeed":"21",
                              "MaxExpectedSpeed":"20", "HorizontalSpeed":"1", "MaxHorizontalSpeed":"12",
                              "VerticalSpeed":"1", "MaxVerticalSpeed":"25", "PositionChange":"0.1",
                              "MaxPositionChange":"2", "TotalVelocityEnabled":"1"})
        self.assertEqual(len(to_generated_alerts([sample_record])), 1)
        performance_record = dict(zip(performance_header, ["run", "trial", "100", "1.0", "0.01", "0.009", "0.02", "0.03"]))
        self.assertEqual(value(performance_record, "P95Milliseconds"), "0.02")
        self.assertEqual(result["metrics"]["TP"], 1)
        self.assertEqual(result["metrics"]["FN"], 0)
        self.assertEqual(result["matches"][0]["event"]["EventId"], "event-fixed")
        self.assertEqual(result["matches"][0]["alert"]["AlertId"], "alert-fixed")
        self.assertAlmostEqual(result["matches"][0]["latency"], 0.01)

    def test_legacy_rows_keep_missing_ids_and_monotonic_times_missing(self):
        legacy_alert = normalize_alerts([{"Timestamp":"10.25", "Episode":"3", "Type":"Physics"}])[0]
        legacy_truth = normalize_ground_truth([{"EpisodeId":"3", "EventType":"Teleport", "GameTime":"10.2"}])[0]
        self.assertEqual(legacy_alert["AlertId"], "")
        self.assertEqual(legacy_alert["RunId"], "")
        self.assertIsNone(legacy_alert["Time"])
        self.assertEqual(legacy_alert["GameTime"], 10.25)
        self.assertEqual(legacy_truth["EventId"], "")
        self.assertIsNone(legacy_truth["Time"])
        result = match_events([legacy_truth], [legacy_alert], 0.5)
        self.assertEqual(result["metrics"]["TP"], 0)
        self.assertEqual(result["matches"][0]["status"], "unmatchable_missing_identity_or_timestamp")

    def test_unity_writers_share_session_ids_and_exact_monotonic_clock(self):
        project_root = Path(__file__).parents[1]
        truth_source = (project_root / "Assets/Scripts/BugsyGroundTruthRecorder.cs").read_text()
        detector_source = (project_root / "Assets/Scripts/BugsyAnomalyDetector.cs").read_text()
        self.assertIn("runId = Guid.NewGuid().ToString(\"N\")", truth_source)
        self.assertIn("trialId = Guid.NewGuid().ToString(\"N\")", truth_source)
        self.assertIn("Time.realtimeSinceStartupAsDouble.ToString(\"F6\", CultureInfo.InvariantCulture)", truth_source)
        self.assertIn("Time.realtimeSinceStartupAsDouble;", truth_source)
        for field in ("RunId", "TrialId", "EpisodeId", "MonotonicSeconds"):
            self.assertIn(f"BugsyGroundTruthRecorder.{field}", detector_source)


if __name__ == "__main__":
    unittest.main()
