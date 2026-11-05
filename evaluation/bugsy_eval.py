#!/usr/bin/env python3
"""Offline, event-level evaluation for BUGSY's synchronized Unity CSV logs.

No synthetic data is generated. Rows missing a shared monotonic timestamp or
run/trial/episode identity are retained as unmatched/unknown, never guessed.
"""
import argparse
import csv
import json
import math
import statistics
from collections import defaultdict
from pathlib import Path


def read_csv(path):
    with open(path, newline="", encoding="utf-8-sig") as stream:
        return list(csv.DictReader(stream))


def value(row, *names, default=""):
    for name in names:
        if name in row and row[name] not in (None, ""):
            return row[name]
    lowered = {str(k).lower(): v for k, v in row.items()}
    for name in names:
        v = lowered.get(name.lower())
        if v not in (None, ""):
            return v
    return default


def number(row, *names):
    try:
        return float(value(row, *names))
    except (TypeError, ValueError):
        return None


def identity(row):
    return (str(value(row, "RunId", "Run")), str(value(row, "TrialId", "Trial")),
            str(value(row, "EpisodeId", "Episode")))


def normalize_ground_truth(rows):
    result = []
    for i, row in enumerate(rows):
        fault = str(value(row, "EventType", "FaultType", "Type"))
        if not fault:
            continue
        result.append({"RunId": identity(row)[0], "TrialId": identity(row)[1],
                       "EpisodeId": identity(row)[2],
                       "EventId": str(value(row, "EventId")),
                       "EventType": fault,
                       "Time": number(row, "RealtimeSinceStartup", "InjectionTimestampMonotonic", "InjectionTimeMonotonicSeconds"),
                       "GameTime": number(row, "GameTime"), "Position": tuple(number(row, n) for n in ("PositionAfterX", "PositionAfterY", "PositionAfterZ")),
                       "raw": row})
    return result


def normalize_alerts(rows):
    result = []
    for i, row in enumerate(rows):
        category = str(value(row, "Category", "Type"))
        if not category or category.lower() == "collision":
            continue
        result.append({"RunId": identity(row)[0], "TrialId": identity(row)[1],
                       "EpisodeId": identity(row)[2],
                       "AlertId": str(value(row, "AlertId")),
                       "Category": category,
                       "Rule": str(value(row, "DetectionRule", "Rule", default=category)),
                       "Time": number(row, "DetectionRealtimeSinceStartup", "RealtimeSinceStartup", "MonotonicSeconds"),
                       # Legacy Timestamp is Time.time/game-time, not the shared
                       # monotonic clock; it is retained only as GameTime below.
                       "GameTime": number(row, "GameTime", "Timestamp"),
                       "MeasuredValue": number(row, "MeasuredValue"),
                       "Threshold": number(row, "Threshold"),
                       "Position": tuple(number(row, n) for n in ("PosX", "PosY", "PosZ")),
                       "raw": row})
    return result


def compatible(fault, category):
    fault = fault.lower()
    category = category.lower()
    if fault in ("teleport", "positiondiscontinuity"):
        return category == "positiondiscontinuity"
    if fault in ("velocityspike", "velocity"):
        return category in ("physics", "totalvelocity", "horizontalvelocity", "verticalvelocity")
    if fault == "wallpenetration":
        return category == "wallpenetration"
    return fault == category


def match_events(ground_truth, alerts, tolerance, normal_intervals=None,
                 negative_window=None, dedup_gap=0.25):
    if tolerance is None or tolerance < 0:
        raise ValueError("A non-negative --tolerance-seconds value is required")
    gt = list(ground_truth)
    al = list(alerts)
    edges = []
    ambiguous_gt = set()
    duplicate_alerts = set()
    for gi, event in enumerate(gt):
        if event["Time"] is None or not all(event[k] for k in ("RunId", "TrialId", "EpisodeId")):
            continue
        candidates = []
        for ai, alert in enumerate(al):
            if identity(event) != identity(alert) or alert["Time"] is None:
                continue
            delta = alert["Time"] - event["Time"]
            if 0 <= delta <= tolerance and compatible(event["EventType"], alert["Category"]):
                candidates.append((abs(delta), gi, ai))
        candidates.sort(key=lambda item: (item[0],
                         {"physics": 0, "totalvelocity": 0, "positiondiscontinuity": 0,
                          "horizontalvelocity": 1, "verticalvelocity": 2}.get(al[item[2]]["Category"].lower(), 3),
                         item[2]))
        if candidates:
            # Simultaneous compatible rules (e.g., Physics + HorizontalVelocity)
            # are one alert episode for an injected velocity spike. Prefer the
            # general total-speed rule when both are logged at the same instant.
            best = candidates[0]
            near = [item for item in candidates if abs(al[item[2]]["Time"] - al[best[2]]["Time"]) <= dedup_gap]
            if len(near) > 1:
                duplicate_alerts.update(item[2] for item in near[1:])
            edges.append(best)
    alert_candidates = defaultdict(list)
    for _, gi, ai in edges:
        alert_candidates[ai].append(gi)
    for candidate_events in alert_candidates.values():
        if len(candidate_events) > 1:
            ambiguous_gt.update(candidate_events)
    used_gt, used_alert, pairs = set(), set(), []
    for delta, gi, ai in sorted(edges):
        if gi in ambiguous_gt or gi in used_gt or ai in used_alert:
            continue
        used_gt.add(gi); used_alert.add(ai)
        pairs.append((gi, ai, delta))

    # Repeated compatible alerts near an already matched injection are duplicate
    # rows for that event, not additional detections or false positives.
    for gi, _, _ in pairs:
        event = gt[gi]
        for ai, alert in enumerate(al):
            if ai in used_alert or identity(event) != identity(alert) or alert["Time"] is None:
                continue
            if compatible(event["EventType"], alert["Category"]) and abs(alert["Time"] - event["Time"]) <= max(tolerance, dedup_gap):
                duplicate_alerts.add(ai)

    intervals = normal_intervals or []
    def in_normal(alert):
        if alert["Time"] is None:
            return False
        for interval in intervals:
            if identity(alert) == identity(interval) and interval["Start"] <= alert["Time"] <= interval["End"]:
                return True
        return False

    # Collapse repeated alerts of one rule into alert episodes before counting FPs.
    unmatched = [i for i in range(len(al)) if i not in used_alert and i not in duplicate_alerts]
    unmatched.sort(key=lambda i: (identity(al[i]), al[i]["Category"], al[i]["Time"] if al[i]["Time"] is not None else -1))
    alert_episodes = []
    for ai in unmatched:
        alert = al[ai]
        if alert["Time"] is None:
            alert_episodes.append({"indices": [ai], "alert": alert})
            continue
        if (alert_episodes and alert_episodes[-1]["alert"]["Time"] is not None
                and identity(alert_episodes[-1]["alert"]) == identity(alert)
                and alert_episodes[-1]["alert"]["Category"] == alert["Category"]
                and alert["Time"] - alert_episodes[-1]["alert"]["Time"] <= dedup_gap):
            alert_episodes[-1]["indices"].append(ai)
        else:
            alert_episodes.append({"indices": [ai], "alert": alert})

    fp_episodes = [ep for ep in alert_episodes if in_normal(ep["alert"])]
    unclassified_alerts = [ep for ep in alert_episodes if not in_normal(ep["alert"])]
    fn_indices = [i for i in range(len(gt)) if i not in used_gt and i not in ambiguous_gt]
    # A ground-truth event lacking timestamps/IDs is unmatched, not silently dropped.
    total_windows = None
    occupied_windows = set()
    if intervals and negative_window and negative_window > 0:
        for index, interval in enumerate(intervals):
            count = max(1, int(math.ceil((interval["End"] - interval["Start"]) / negative_window)))
            total_windows = (total_windows or 0) + count
            for ep in fp_episodes:
                alert = ep["alert"]
                if identity(alert) == identity(interval) and interval["Start"] <= alert["Time"] <= interval["End"]:
                    slot = min(count - 1, int((alert["Time"] - interval["Start"]) / negative_window))
                    occupied_windows.add((index, slot))
    metrics = {"TP": len(pairs), "FN": len(fn_indices), "Ambiguous": len(ambiguous_gt),
               "UnmatchedAlertsInNormalIntervals": len(fp_episodes),
               "UnclassifiedUnmatchedAlertEpisodes": len(unclassified_alerts),
               "FP": len(fp_episodes) if intervals else None,
               "TN": max(0, total_windows - len(occupied_windows)) if total_windows is not None else None,
               "Precision": None, "Recall": None, "F1": None, "FPR": None}
    if metrics["FP"] is not None:
        metrics["Precision"] = ratio(metrics["TP"], metrics["TP"] + metrics["FP"])
    metrics["Recall"] = ratio(metrics["TP"], metrics["TP"] + metrics["FN"])
    if metrics["Precision"] is not None and metrics["Recall"] is not None:
        metrics["F1"] = ratio(2 * metrics["Precision"] * metrics["Recall"], metrics["Precision"] + metrics["Recall"])
    if metrics["FP"] is not None and metrics["TN"] is not None:
        metrics["FPR"] = ratio(metrics["FP"], metrics["FP"] + metrics["TN"])

    results = []
    pair_by_gt = {gi: (ai, delta) for gi, ai, delta in pairs}
    for gi, event in enumerate(gt):
        pair = pair_by_gt.get(gi)
        if pair:
            ai, latency = pair
            alert = al[ai]
            status = "TP"
        elif gi in ambiguous_gt:
            ai, latency, alert, status = None, None, None, "ambiguous"
        else:
            ai, latency, alert = None, None, None
            status = "FN" if event["Time"] is not None and identity(event)[0] and identity(event)[1] and identity(event)[2] else "unmatchable_missing_identity_or_timestamp"
        results.append({"event": event, "alert": alert, "status": status, "latency": latency})
    return {"metrics": metrics, "matches": results, "pairs": pairs,
            "false_positive_episodes": fp_episodes, "unclassified_alert_episodes": unclassified_alerts,
            "duplicate_alert_indices": duplicate_alerts, "alerts": al, "ground_truth": gt}


def ratio(n, d):
    return n / d if d else None


def alert_episode_count(alerts, max_gap):
    ordered = sorted(alerts, key=lambda a: (identity(a), a["Category"], a["Rule"],
                                            a["Time"] if a["Time"] is not None else -1))
    count, previous = 0, None
    for alert in ordered:
        if alert["Time"] is None:
            count += 1; previous = None; continue
        same = (previous is not None and identity(previous) == identity(alert)
                and previous["Category"] == alert["Category"] and previous["Rule"] == alert["Rule"]
                and alert["Time"] - previous["Time"] <= max_gap)
        if not same: count += 1
        previous = alert
    return count


def load_intervals(rows):
    out = []
    for row in rows:
        start = number(row, "StartMonotonicSeconds", "Start")
        end = number(row, "EndMonotonicSeconds", "End")
        if start is None or end is None or end <= start:
            raise ValueError("Normal interval needs StartMonotonicSeconds < EndMonotonicSeconds")
        out.append({"RunId": identity(row)[0], "TrialId": identity(row)[1],
                    "EpisodeId": identity(row)[2], "Start": start, "End": end})
    return out


def write_csv(path, rows, columns):
    with open(path, "w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=columns, extrasaction="ignore")
        writer.writeheader()
        for row in rows:
            writer.writerow({k: "N/A" if row.get(k) is None else row.get(k, "") for k in columns})


def grouped_metrics(evaluation, key_fields):
    groups = defaultdict(lambda: {"gt": [], "alerts": []})
    for event in evaluation["ground_truth"]:
        groups[tuple(event[k] for k in key_fields)]["gt"].append(event)
    for alert in evaluation["alerts"]:
        groups[tuple(alert[k] for k in key_fields)]["alerts"].append(alert)
    output = []
    for key, data in groups.items():
        part = match_events(data["gt"], data["alerts"], evaluation["tolerance"],
                            evaluation["intervals"], evaluation["window"], evaluation["dedup"])
        row = dict(zip(key_fields, key)); row.update(part["metrics"]); output.append(row)
    return output


def to_generated_alerts(samples, total_threshold=None, position_threshold=None,
                        horizontal_threshold=None, vertical_threshold=None):
    alerts = []
    for i, row in enumerate(samples):
        t = number(row, "SampleRealtimeSinceStartup")
        if t is None: continue
        enabled_total = str(value(row, "TotalVelocityEnabled", default="1")) not in ("0", "False", "false")
        enabled_position = str(value(row, "PositionDiscontinuityEnabled", default="1")) not in ("0", "False", "false")
        checks = []
        threshold = total_threshold if total_threshold is not None else number(row, "MaxExpectedSpeed")
        if enabled_total and threshold is not None and (number(row, "TotalSpeed") or 0) > threshold:
            checks.append(("Physics", "TotalVelocity", number(row, "TotalSpeed"), threshold))
        hthr = horizontal_threshold if horizontal_threshold is not None else number(row, "MaxHorizontalSpeed")
        if hthr is not None and (number(row, "HorizontalSpeed") or 0) > hthr and str(value(row, "HorizontalVelocityEnabled", default="1")) not in ("0", "False", "false"):
            checks.append(("HorizontalVelocity", "HorizontalVelocity", number(row, "HorizontalSpeed"), hthr))
        vthr = vertical_threshold if vertical_threshold is not None else number(row, "MaxVerticalSpeed")
        if vthr is not None and (number(row, "VerticalSpeed") or 0) > vthr and str(value(row, "VerticalVelocityEnabled", default="1")) not in ("0", "False", "false"):
            checks.append(("VerticalVelocity", "VerticalVelocity", number(row, "VerticalSpeed"), vthr))
        pthr = position_threshold if position_threshold is not None else number(row, "MaxPositionChange")
        if enabled_position and pthr is not None and (number(row, "PositionChange") or 0) > pthr:
            checks.append(("PositionDiscontinuity", "PositionDiscontinuity", number(row, "PositionChange"), pthr))
        for cat, rule, measured, threshold_value in checks:
            alerts.append({"RunId": identity(row)[0], "TrialId": identity(row)[1], "EpisodeId": identity(row)[2],
                           "AlertId": f"sample-{i}-{rule}", "Category": cat, "Rule": rule,
                           "Time": t, "GameTime": number(row, "GameTime"), "MeasuredValue": measured,
                           "Threshold": threshold_value, "Position": (), "raw": row})
    return alerts


def build_svg(path, title, labels, values, y_label="Value"):
    width, height = 760, 420
    if not labels or not values:
        body = '<text x="380" y="220" text-anchor="middle" font-size="22">N/A — more Unity experiments required</text>'
    else:
        maxv = max([v for v in values if v is not None] + [1])
        barw = min(70, 600 / max(len(values), 1))
        parts = [f'<text x="380" y="34" text-anchor="middle" font-size="20">{title}</text>',
                 '<line x1="80" y1="350" x2="720" y2="350" stroke="black"/>']
        for i, (label, val) in enumerate(zip(labels, values)):
            x = 100 + i * (600 / max(len(values), 1))
            if val is None:
                parts.append(f'<text x="{x+barw/2:.0f}" y="320" text-anchor="middle" font-size="13">N/A</text>')
            else:
                bh = 270 * max(0, val) / maxv
                parts.append(f'<rect x="{x:.0f}" y="{350-bh:.1f}" width="{barw:.0f}" height="{bh:.1f}" fill="#3978ad"/>')
                parts.append(f'<text x="{x+barw/2:.0f}" y="{340-bh:.1f}" text-anchor="middle" font-size="12">{val:.3g}</text>')
            parts.append(f'<text x="{x+barw/2:.0f}" y="375" text-anchor="middle" font-size="11">{label}</text>')
        body = "".join(parts)
    Path(path).write_text(f'<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}"><text x="20" y="405" font-size="12">{y_label}</text>{body}</svg>', encoding="utf-8")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--ground-truth", required=True)
    parser.add_argument("--alerts", required=True, help="Use bugsy_detector_events.csv; legacy anomaly CSV rows lack synchronized identifiers/timestamps")
    parser.add_argument("--tolerance-seconds", type=float, required=True, help="Explicit maximum injection-to-alert time delta")
    parser.add_argument("--normal-intervals", help="CSV columns: RunId,TrialId,EpisodeId,StartMonotonicSeconds,EndMonotonicSeconds")
    parser.add_argument("--negative-window-seconds", type=float)
    parser.add_argument("--samples", help="Optional bugsy_detector_samples.csv for threshold sensitivity")
    parser.add_argument("--performance", help="Optional bugsy_detector_performance.csv")
    parser.add_argument("--output", default="evaluation/output")
    parser.add_argument("--dedup-gap-seconds", type=float, default=0.25)
    parser.add_argument("--velocity-thresholds", default="", help="Comma-separated total-speed thresholds; defaults to observed baseline values")
    parser.add_argument("--horizontal-velocity-thresholds", default="", help="Comma-separated horizontal-speed thresholds; defaults to observed baseline values")
    parser.add_argument("--vertical-velocity-thresholds", default="", help="Comma-separated vertical-speed thresholds; defaults to observed baseline values")
    parser.add_argument("--position-thresholds", default="", help="Comma-separated position-change thresholds; defaults to observed baseline values")
    parser.add_argument("--expected-trials-per-scenario", type=int, help="Optional planned trial count used to report missing repetitions")
    args = parser.parse_args()
    out = Path(args.output); out.mkdir(parents=True, exist_ok=True)
    gt = normalize_ground_truth(read_csv(args.ground_truth))
    alerts = normalize_alerts(read_csv(args.alerts))
    intervals = load_intervals(read_csv(args.normal_intervals)) if args.normal_intervals else []
    overall = match_events(gt, alerts, args.tolerance_seconds, intervals,
                           args.negative_window_seconds, args.dedup_gap_seconds)
    overall["tolerance"], overall["intervals"], overall["window"], overall["dedup"] = args.tolerance_seconds, intervals, args.negative_window_seconds, args.dedup_gap_seconds

    metrics = overall["metrics"]
    write_csv(out / "evaluation_summary.csv", [{"Scope": "overall", **metrics, "DetectionLatencyMeanSeconds": statistics.mean([p[2] for p in overall["pairs"]]) if overall["pairs"] else None}],
              ["Scope", "TP", "FP", "TN", "FN", "Ambiguous", "Precision", "Recall", "F1", "FPR", "DetectionLatencyMeanSeconds", "UnclassifiedUnmatchedAlertEpisodes"])
    trials = grouped_metrics(overall, ("RunId", "TrialId"))
    scenario_by_trial = defaultdict(set)
    for event in gt:
        scenario_by_trial[(event["RunId"], event["TrialId"])].add(event["EventType"])
    for row in trials:
        row["Scenario"] = "+".join(sorted(scenario_by_trial[(row["RunId"], row["TrialId"])])) or "NoGroundTruthEvent"
    write_csv(out / "per_trial_metrics.csv", trials, ["RunId", "TrialId", "Scenario", "TP", "FP", "TN", "FN", "Ambiguous", "Precision", "Recall", "F1", "FPR", "UnclassifiedUnmatchedAlertEpisodes"])
    repeated = []
    by_scenario = defaultdict(list)
    for row in trials: by_scenario[row["Scenario"]].append(row)
    for scenario, group in sorted(by_scenario.items()):
        successful = sum(1 for row in group if row["TP"] > 0 and row["FN"] == 0)
        unsuccessful = len(group) - successful
        for metric in ("Precision", "Recall", "F1", "FPR"):
            values = [row[metric] for row in group if isinstance(row.get(metric), (int, float))]
            repeated.append({"Scenario": scenario, "Metric": metric, "ValidTrialMetricCount": len(values),
                             "Mean": statistics.mean(values) if values else None,
                             "StdDev": statistics.stdev(values) if len(values) > 1 else None,
                             "ObservedTrials": len(group), "SuccessfulTrials": successful,
                             "UnsuccessfulTrials": unsuccessful,
                             "ExpectedTrials": args.expected_trials_per_scenario,
                             "MissingTrials": max(0, args.expected_trials_per_scenario-len(group)) if args.expected_trials_per_scenario is not None else None})
    write_csv(out / "repeated_trial_summary.csv", repeated, ["Scenario", "Metric", "ValidTrialMetricCount", "Mean", "StdDev", "ObservedTrials", "SuccessfulTrials", "UnsuccessfulTrials", "ExpectedTrials", "MissingTrials"])
    categories = []
    for fault_type in sorted({event["EventType"] for event in gt}):
        category_gt = [event for event in gt if event["EventType"] == fault_type]
        category_alerts = [alert for alert in alerts if compatible(fault_type, alert["Category"])]
        ev = match_events(category_gt, category_alerts, args.tolerance_seconds, intervals,
                          args.negative_window_seconds, args.dedup_gap_seconds)
        categories.append({"AnomalyCategory": fault_type, **ev["metrics"]})
    write_csv(out / "per_category_metrics.csv", categories, ["AnomalyCategory", "TP", "FP", "TN", "FN", "Ambiguous", "Precision", "Recall", "F1", "FPR", "UnclassifiedUnmatchedAlertEpisodes"])
    match_rows = []
    for item in overall["matches"]:
        event, alert = item["event"], item["alert"]
        match_rows.append({"RunId": event["RunId"], "TrialId": event["TrialId"], "EpisodeId": event["EpisodeId"], "EventId": event["EventId"], "EventType": event["EventType"], "InjectionMonotonicSeconds": event["Time"], "Status": item["status"], "AlertId": alert["AlertId"] if alert else "", "AlertCategory": alert["Category"] if alert else "", "DetectionMonotonicSeconds": alert["Time"] if alert else None, "DetectionLatencySeconds": item["latency"]})
    write_csv(out / "event_matching.csv", match_rows, ["RunId", "TrialId", "EpisodeId", "EventId", "EventType", "InjectionMonotonicSeconds", "Status", "AlertId", "AlertCategory", "DetectionMonotonicSeconds", "DetectionLatencySeconds"])

    sensitivity = []
    sample_rows = read_csv(args.samples) if args.samples else []
    if sample_rows:
        vvalues = [float(x) for x in args.velocity_thresholds.split(",") if x.strip()] if args.velocity_thresholds else sorted({number(r, "MaxExpectedSpeed") for r in sample_rows if number(r, "MaxExpectedSpeed") is not None})
        hvalues = [float(x) for x in args.horizontal_velocity_thresholds.split(",") if x.strip()] if args.horizontal_velocity_thresholds else sorted({number(r, "MaxHorizontalSpeed") for r in sample_rows if number(r, "MaxHorizontalSpeed") is not None})
        vertical_values = [float(x) for x in args.vertical_velocity_thresholds.split(",") if x.strip()] if args.vertical_velocity_thresholds else sorted({number(r, "MaxVerticalSpeed") for r in sample_rows if number(r, "MaxVerticalSpeed") is not None})
        pvalues = [float(x) for x in args.position_thresholds.split(",") if x.strip()] if args.position_thresholds else sorted({number(r, "MaxPositionChange") for r in sample_rows if number(r, "MaxPositionChange") is not None})
        for rule, values in (("TotalVelocity", vvalues), ("HorizontalVelocity", hvalues),
                             ("VerticalVelocity", vertical_values), ("PositionDiscontinuity", pvalues)):
            for threshold in values:
                generated = to_generated_alerts(sample_rows, total_threshold=threshold if rule == "TotalVelocity" else None,
                                                position_threshold=threshold if rule == "PositionDiscontinuity" else None,
                                                horizontal_threshold=threshold if rule == "HorizontalVelocity" else None,
                                                vertical_threshold=threshold if rule == "VerticalVelocity" else None)
                generated = [a for a in generated if a["Rule"] == rule]
                target_faults = ("Teleport", "PositionDiscontinuity") if rule == "PositionDiscontinuity" else ("VelocitySpike", "Velocity")
                target_gt = [event for event in gt if event["EventType"] in target_faults]
                ev = match_events(target_gt, generated, args.tolerance_seconds, intervals, args.negative_window_seconds, args.dedup_gap_seconds)
                sensitivity.append({"Rule": rule, "Threshold": threshold, "AlertRows": len(generated),
                                    "UniqueAlertEpisodes": alert_episode_count(generated, args.dedup_gap_seconds), **ev["metrics"]})
    write_csv(out / "sensitivity_analysis.csv", sensitivity, ["Rule", "Threshold", "AlertRows", "UniqueAlertEpisodes", "TP", "FP", "TN", "FN", "Ambiguous", "Precision", "Recall", "F1", "FPR"])

    ablations = []
    rules = ["TotalVelocity", "HorizontalVelocity", "VerticalVelocity", "PositionDiscontinuity", "WallPenetration", "Softlock"]
    for removed in ["None"] + rules:
        subset = alerts if removed == "None" else [a for a in alerts if a["Rule"] != removed and a["Category"] != removed]
        ev = match_events(gt, subset, args.tolerance_seconds, intervals, args.negative_window_seconds, args.dedup_gap_seconds)
        ablations.append({"Configuration": "Full detector" if removed == "None" else f"Exclude {removed}", "DisabledRule": "" if removed == "None" else removed, **ev["metrics"], "AlertRows": len(subset), "UniqueAlertEpisodes": alert_episode_count(subset, args.dedup_gap_seconds)})
    write_csv(out / "ablation_results.csv", ablations, ["Configuration", "DisabledRule", "AlertRows", "UniqueAlertEpisodes", "TP", "FP", "TN", "FN", "Ambiguous", "Precision", "Recall", "F1", "FPR"])

    perf_rows = read_csv(args.performance) if args.performance else []
    perf_output = []
    for row in perf_rows:
        perf_output.append({"RunId": value(row, "RunId"), "TrialId": value(row, "TrialId"), "EvaluationCount": value(row, "EvaluationCount"), "TotalMilliseconds": value(row, "TotalMilliseconds"), "MeanMilliseconds": value(row, "MeanMilliseconds"), "MedianMilliseconds": value(row, "MedianMilliseconds"), "P95Milliseconds": value(row, "P95Milliseconds"), "MaxMilliseconds": value(row, "MaxMilliseconds"), "BaselineMeanMilliseconds": "N/A", "EstimatedOverheadPercent": "N/A"})
    write_csv(out / "computational_efficiency.csv", perf_output, ["RunId", "TrialId", "EvaluationCount", "TotalMilliseconds", "MeanMilliseconds", "MedianMilliseconds", "P95Milliseconds", "MaxMilliseconds", "BaselineMeanMilliseconds", "EstimatedOverheadPercent"])

    write_svg = build_svg
    write_svg(out / "metrics_comparison.svg", "Precision / Recall / F1 (overall)", ["Precision", "Recall", "F1"], [metrics["Precision"], metrics["Recall"], metrics["F1"]])
    write_svg(out / "threshold_sensitivity.svg", "Threshold sensitivity (F1)", [f"{r['Rule']}:{r['Threshold']}" for r in sensitivity], [r["F1"] for r in sensitivity])
    write_svg(out / "ablation_comparison.svg", "Ablation study (F1)", [r["Configuration"] for r in ablations], [r["F1"] for r in ablations])
    latencies = [p[2] for p in overall["pairs"]]
    write_svg(out / "detection_latency.svg", "Matched-event detection latency", [str(i+1) for i in range(len(latencies))], latencies, "Seconds")
    means = [number(r, "MeanMilliseconds") for r in perf_rows]
    write_svg(out / "computational_overhead.svg", "Detector measured time per physics evaluation", [value(r, "TrialId", default=str(i+1))[:8] for i, r in enumerate(perf_rows)], means, "Milliseconds (baseline comparison N/A)")

    report = ["# BUGSY anomaly detector evaluation", "", f"Matching tolerance: {args.tolerance_seconds:g} seconds (explicit CLI value).", "",
              f"- Ground-truth injections: {len(gt)}", f"- Matched (TP): {metrics['TP']}", f"- Unmatched injection events (FN): {metrics['FN']}",
              f"- Ambiguous injection events: {metrics['Ambiguous']}", f"- FP: {metrics['FP'] if metrics['FP'] is not None else 'N/A (no labeled normal intervals supplied)'}",
              f"- TN/FPR: {metrics['TN'] if metrics['TN'] is not None else 'N/A (no normal intervals and negative-window size supplied)'}",
              f"- Precision / Recall / F1: {fmt(metrics['Precision'])} / {fmt(metrics['Recall'])} / {fmt(metrics['F1'])}",
              f"- Unclassified unmatched alert episodes: {metrics['UnclassifiedUnmatchedAlertEpisodes']}",
              f"- Numeric detection latency available for {len(latencies)} matched events.", "",
              "## Interpretation limits", "",
              "Metrics use one-to-one event-level matching by exact RunId/TrialId/EpisodeId, compatible category, and the configured monotonic-time tolerance. Repeated same-event alerts are deduplicated. Unmatched alerts outside labeled normal intervals remain unclassified and are not counted as FP. TN/FPR require normal intervals plus a negative-window size.", "",
              "Threshold sensitivity requires `bugsy_detector_samples.csv` collected with Record Evaluation Samples enabled. It re-evaluates total-speed and position-change thresholds over recorded samples; it cannot recreate wall-penetration or softlock checks from samples alone. Ablation rows post-filter captured alerts and should be treated as offline rule-contribution analysis, not a substitute for repeated Unity runs with that rule disabled.", "",
              "Performance numbers measure detector FixedUpdate processing time with a high-resolution stopwatch. Baseline and overhead comparison remain N/A until comparable detector-disabled runs are supplied. No Unity experiment is executed by this script.", ""]
    (out / "evaluation_report.md").write_text("\n".join(report), encoding="utf-8")


def fmt(x):
    return "N/A" if x is None else f"{x:.4f}"


if __name__ == "__main__":
    main()
