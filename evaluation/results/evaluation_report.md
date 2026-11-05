# BUGSY experiment evaluation report

## Experimental setup and data sources

Read-only analysis of `/Users/niveditaramesh/Library/Application Support/DefaultCompany/BUGSY_Parkour` and `/Users/niveditaramesh/Downloads/Project/BUGSY_Parkour/evaluation`. No Unity project code, experimental input log, detector setting, injector, PPO agent, or threshold was modified. Matching tolerance was **0.5 seconds**, following the evaluation README example. Alerts and injections were matched by exact RunId, TrialId, EpisodeId and shared monotonic realtime fields; the existing evaluator's one-to-one matching/deduplication logic was inspected and run on the full raw dataset. For the requested primary associations this report uses Teleport → PositionDiscontinuity and VelocitySpike → Physics/TotalVelocity or HorizontalVelocity. VerticalVelocity is not treated as primary evidence for an injected velocity spike, and downstream consequences are not independent injected faults. Ordinary Collision rows are excluded.

Ground truth contains 13 injection records in 12 unique RunIds: 5 Teleport and 8 VelocitySpike. Detector structured events contain 3069 alert rows across 6 runs. There is no `bugsy_detector_samples.csv` and no labeled normal-interval file. Legacy `bugsy_anomalies*.csv` records have no shared monotonic timestamps or complete shared identities, so they were inspected as historical diagnostics but not used for event matching. Performance CSV contains 12 per-run aggregate records.

### Trial selection caveat

Ground-truth rows have no scenario/test label. The log therefore cannot prove which rows the user intended as the latest 3+3 set. To provide a reproducible provisional cohort, selected the **latest three single-fault ground-truth rows per type in file append order**, excluding the one mixed-fault session that contains both Teleport and VelocitySpike from the isolated cohort. Every record, including excluded/older/mixed records, remains in the manifest and all-record metrics. The selected cohort's RunIds are: `0c6eb50465a94b309a0b160c8e8f2b29, 4c681f1f501d49329009bc98010b0d97, 51067c44e00c4c4c8d390de632755375, 5f9ead6d68554d6d9c8f1a8b84459061, 6cd25c5598ae45bb99b99534e35a6f6d, b2ec0c5cc8b14472bffdfc3419502e7c`. All ground-truth RunIds: `0c6eb50465a94b309a0b160c8e8f2b29, 4c681f1f501d49329009bc98010b0d97, 51067c44e00c4c4c8d390de632755375, 5f9ead6d68554d6d9c8f1a8b84459061, 6cd25c5598ae45bb99b99534e35a6f6d, 7f78a309269041208879dbdae63343ce, 836f724ccb5c4163ae9fef390a919a6c, abb8b892d1774b4283596c1aa96db4fc, b2ec0c5cc8b14472bffdfc3419502e7c, c35791eab86245eca4e1ea75a7d46243, cbdbe76e5f2f490db55663c47b93be82, ef4652ca134f4a5e942b7d6d7c8598eb`. Treat the 3x3 membership as inferred pending an external trial notebook or operator labels.

## Provisional three-trial results

| Injection type | Trials with ground truth | Detected | Missed | Detection latency (s), matched events | Mean (s) | Sample SD (s) |
|---|---:|---:|---:|---|---:|---:|
| Teleport | 3 | 3 | 0 | 0.008042, 0.011462, 0.048722 | 0.022742 | 0.022564 |
| VelocitySpike | 3 | 2 | 1 | 0.012621, 0.002237 | 0.007429 | 0.007342 |

A velocity trial with both Physics and HorizontalVelocity alerts is counted once; Physics is the earliest primary alert and the horizontal row is recorded as a secondary simultaneous alert. No unmatched alert is counted as FP because no normal intervals label negative exposure. Consequently precision, F1 and FPR are **N/A**. Event recall within the provisional cohort is 5/6 = 0.833333; this is injection-event recall, not overall accuracy.

## All-record injection associations

Across all recorded ground truth, primary compatible detections: Teleport 4/5; VelocitySpike 2/8. The seven records without a compatible alert within 0.5 seconds remain false negatives for event recall where identity/timestamp data are valid. The mixed RunId `7f78a309269041208879dbdae63343ce` contains both a Teleport and VelocitySpike record but only a PositionDiscontinuity alert; this is teleport evidence and a velocity miss, not a velocity detection. RunId `51067c44e00c4c4c8d390de632755375` has thousands of later Physics/VerticalVelocity alerts; they were not assigned to its Teleport because the primary association is PositionDiscontinuity and event matching is time-bounded. Those later rows are not counted as independent velocity-injection detections.

All-record recall is 6/13 = 0.461538. Precision, F1, TN and FPR are unavailable without labeled fault-free intervals and a negative-window definition. No claims are made that unmatched detector alerts are false positives.

## Repeatability

The provisional cohort has three independent RunId/TrialId sessions for each type by the selection rule above. Teleport triggered PositionDiscontinuity in 3/3 selected trials. Velocity injection triggered a primary velocity rule in 2/3 selected trials; simultaneous Physics and HorizontalVelocity rows in a trial are one detected injection. This is observed repeatability in the inferred cohort only; the event records lack explicit intended-trial labels, so it should not be described as confirmed protocol repeatability until the operator identifies the six runs.

## Rule-level observations

| Rule | Alert rows | Unique alert episodes (0.25 s gap) | Repeated rows | Matching injection events | Observation |
|---|---:|---:|---:|---:|---|
| TotalVelocity | 1558 | 3 | 1555 | 2 | VelocitySpike |
| HorizontalVelocity | 2 | 2 | 0 | 2 | VelocitySpike |
| VerticalVelocity | 1505 | 1 | 1504 | 0 | No dedicated repeated injector / not evidenced |
| PositionDiscontinuity | 4 | 4 | 0 | 4 | Teleport |
| WallPenetration | 0 | 0 | 0 | 0 | No dedicated repeated injector / not evidenced |
| Softlock | 0 | 0 | 0 | 0 | No dedicated repeated injector / not evidenced |

Physics/TotalVelocity and HorizontalVelocity both responded in the same two velocity-injection sessions, but are not two independent detections per event. PositionDiscontinuity responded in four teleport sessions with structured evidence. There are no structured WallPenetration or Softlock alerts in the collected event file; this does not establish that their checks were exercised. VerticalVelocity rows occur in the long RunId above but do not match a dedicated vertical fault injection. No sample trace was available for recomputing threshold-level checks.

## Computational efficiency

The performance file contains 99692 detector evaluations across 12 per-run aggregate records. Weighted mean processing time = sum of recorded total milliseconds / sum of evaluations = **0.022714 ms/evaluation**. Across per-run summaries, mean-of-trial medians = 0.037083 ms; median-of-trial medians = 0.010500 ms. The largest recorded per-trial P95 is 2.669500 ms, and the largest recorded per-trial maximum is 70.544200 ms.

The CSV stores aggregate per-trial mean/median/P95/max, not raw timing samples. Therefore a pooled all-step median and pooled P95 **cannot be reconstructed**. These are detector `FixedUpdate` processing times, not total game frame time or CPU utilization. No comparable detector-disabled baseline exists, so overhead comparison is unavailable.

## Missing data and limits

- No `bugsy_detector_samples.csv` or labeled normal intervals were present.
- The legacy CSVs lack reliable shared RunId/TrialId/monotonic-time matching fields and were excluded from quantitative event association.
- There are 13 injections (5 teleport, 8 velocity), not just six. The intended three-plus-three cohort is inferred from append order as documented above; some rows have no detector event rows for their RunId.
- The ground-truth and detector logs do not identify which sessions were PPO navigation versus standalone injection tests; the large alert burst is retained but not attributed to an independent injected velocity fault.
- The 0.5 s matching tolerance is explicit and consistent with the README example, but conclusions should be checked against the actual intended protocol.
- No false-positive rate, precision or F1 is defensible without labeled negative exposure.

## Conclusion

In the provisional latest-three single-fault cohort, recorded event-level detection was 3/3 Teleports and 2/3 velocity spikes. Mean latency was 0.022742 s for matched Teleports and 0.007429 s for matched velocity spikes. These results are genuine calculations from the persistent Unity CSVs, but cohort intent is not independently confirmed by available metadata. The manifest and per-event files preserve the full historical record and make the selection auditable.

## Generated figures

- `detection_success_teleport.png`
- `detection_success_velocity.png`
- `detection_latency_by_type.png`
- `mean_latency_comparison.png`
- `alerts_by_detection_rule.png`
- `computational_processing_time_distribution.png`
