# BUGSY evaluation pipeline

The pipeline evaluates recorded Unity data; it does not run Unity, ML-Agents, or training. The detector remains a rule-based detector. Fault labels are read only by the offline evaluator and never enter detector decisions.

## Unity capture

1. In `MazeBugged`, keep the anomaly detector enabled. For ordinary event evaluation, leave `Record Evaluation Samples` off. For threshold sensitivity, enable it before Play Mode; this writes one raw measurement row per physics step and adds disk I/O.
2. The six individual rule switches default on. Change one at a time only for an ablation trial, and restore all six for the full-detector run. Preserve the scene's current thresholds unless explicitly running a documented sensitivity experiment.
3. Run one Play Mode session per trial. Successful teleport and velocity injectors write to append-only `bugsy_ground_truth.csv`. Structured alerts write to append-only `bugsy_detector_events.csv`; sample traces, if enabled, go to `bugsy_detector_samples.csv`. The legacy diagnostic log now uses `bugsy_anomalies_<RunId>.csv`, preserving its original column schema without overwriting another run. The output path is `Application.persistentDataPath` and is printed by Unity at startup.
4. `RunId` and `TrialId` are generated once for a runtime session, and `EpisodeId` is shared with the recorder. Both injection and alert times use `Time.realtimeSinceStartupAsDouble`, a monotonic Unity realtime clock. Current injectors are instantaneous; no end time applies. Existing `RealtimeSinceStartup` ground-truth values already use that same clock.
5. For a fault-free baseline, keep both bug components disabled and both GameManager toggles false. Enable `Record Evaluation Samples`; its rows provide the run/trial/episode IDs and monotonic timestamps. Select a known fault-free span and write a row into a separate interval CSV with columns `RunId,TrialId,EpisodeId,StartMonotonicSeconds,EndMonotonicSeconds`. Do not label intervals containing an injection as normal. Sample logging adds I/O, so use a separate run with sample logging off for efficiency measurements.

## Run analysis

From the project root:

```sh
python3 evaluation/bugsy_eval.py \
  --ground-truth "/path/to/bugsy_ground_truth.csv" \
  --alerts "/path/to/bugsy_detector_events.csv" \
  --tolerance-seconds 0.5 \
  --output evaluation/output
```

The tolerance is required and must be chosen and reported for the experiment. Add `--normal-intervals path.csv --negative-window-seconds 1` only when valid labeled normal intervals exist. FP/TN/FPR remain N/A without them. To evaluate threshold sensitivity, add `--samples path.csv --velocity-thresholds 16,20,24 --horizontal-velocity-thresholds 8,12,16 --vertical-velocity-thresholds 20,25,30 --position-thresholds 1,2,3`; include the actual baseline thresholds (20 total speed, 12 horizontal, 25 vertical, 2 position change). Alternative values are example sweep points to configure, not recommended replacements. To show the planned repetition gap, add `--expected-trials-per-scenario N` with your chosen target. To include detector timing summaries, pass `--performance path/bugsy_detector_performance.csv`.

Outputs are written under the selected output directory: overall summary, per-category and per-trial metrics, repeated-trial mean/standard deviation, event matching, threshold sensitivity, ablation table, computational efficiency, Markdown report, and SVG charts. Missing evidence is reported as N/A. Run-specific legacy diagnostic logs and old Console text cannot be reliably time-matched because they lack the shared monotonic timestamp/session identity; use `bugsy_detector_events.csv` for synchronized matching. The evaluator does not invent missing IDs or timestamps.

## Ablation and efficiency limits

The script provides offline ablation by removing one rule's recorded alert rows. This is a quick rule-contribution view, not a causal replacement for repeated Unity runs with that Inspector rule disabled. Use multiple independent trials per configuration and label each trial/run before making research claims.

Threshold sensitivity requires `bugsy_detector_samples.csv` collected with Record Evaluation Samples enabled. It separately re-evaluates total, horizontal, vertical velocity, and position-change thresholds over recorded samples; it cannot recreate wall-penetration or softlock checks from samples alone.

`bugsy_detector_performance.csv` reports detector FixedUpdate count and stopwatch duration distribution. It is not whole-game CPU utilization. A detector-disabled comparison requires a separate paired baseline run under comparable conditions; current output explicitly leaves baseline and overhead N/A. Sample tracing and verbose Console logging can affect runtime cost, so do not use tracing runs as performance runs.

## Tests

```sh
python3 -m unittest discover -s evaluation -p 'test_*.py' -v
```

Unit-test fixtures are synthetic and are not mixed into Unity experiment results.
