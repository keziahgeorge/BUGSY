# BUGSY trial selection review — corrected velocity injector

## Revised cohort status

The requested three Teleport trials are retained. Two VelocitySpike runs are verified as post-fix trials. **There is no third post-fix VelocitySpike ground-truth event in the available logs**, so a corrected-implementation 3-trial repeatability result is not yet supportable. No pre-fix run is substituted. The full historical event list remains below. The existing `evaluation_report.md` and all prior metrics were not changed.

## Evidence for the velocity-fix cutoff

The corrected execution-order files have saved modification times (UTC): `PlayerController.cs` 2026-11-03T06:44:56.529983+00:00, `BugsyAnomalyDetector.cs` 2026-11-03T06:44:56.623110+00:00, and `BugsyVelocityBug.cs` 2026-11-03T06:46:02.937645+00:00. The injector is the latest of these. Its current source orders the components as PlayerController `-200`, velocity injector `+50`, detector `+100`, and applies/records the spike in `FixedUpdate` after PlayerController. The comparison uses the Unity ground-truth `TimestampUtc` values and local filesystem source mtimes; these clocks agree with the Unity Editor log timestamps. Runtime diagnostic lines for the two post-fix trials independently confirm the new injector/detector handshake.

| RunId | Injection UTC | Compared with injector save | Classification |
|---|---|---:|---|
| `cbdbe76e5f2f490db55663c47b93be82` | 2026-11-03T06:12:56.6470520Z | -1986.291 s | Pre-fix |
| `b2ec0c5cc8b14472bffdfc3419502e7c` | 2026-11-03T06:37:08.6302260Z | -534.307 s | Pre-fix |
| `0c6eb50465a94b309a0b160c8e8f2b29` | 2026-11-03T06:49:17.3672310Z | +194.430 s | Post-fix |
| `4c681f1f501d49329009bc98010b0d97` | 2026-11-03T07:04:09.1100130Z | +1086.172 s | Post-fix |

`cbdbe76e5f2f490db55663c47b93be82` was **not** conducted after the fix: its ground-truth UTC timestamp is 33 min 6.290 s before `BugsyVelocityBug.cs` was saved. It is a pre-fix event and is excluded from corrected repeatability, just like the explicitly identified b2 run (8 min 54.307 s before the save).

## Post-fix velocity verification

| RunId | TrialId | EpisodeId | EventId | Ground truth | Actual detector alerts | Detector observation | Result |
|---|---|---:|---|---|---|---|---|
| `0c6eb50465a94b309a0b160c8e8f2b29` | `b773e5eda90d4fe98ee682e655ea2849` | 1 | `5771a022bb8e40e1943662799a8e2034` | Successful VelocitySpike row; before (1.438522, 0.000000, 4.788596), after (-11.508180, 0.000000, -38.308770) | 5cd3a9dd4e594240ad04fca67e521b10 (Physics/TotalVelocity, t=10.987995s, Δ=0.012621s); 4eb17b07d58e43d984b465b38487702c (HorizontalVelocity/HorizontalVelocity, t=10.988359s, Δ=0.012985s) | Unity Editor.log records total=40 and horizontal=40; exceeded total threshold 20 and horizontal threshold 12. | Detected once (Physics primary; HorizontalVelocity corroborating) |
| `4c681f1f501d49329009bc98010b0d97` | `b105e191800740299c0649c4cd96f1f6` | 1 | `a3a03aaf44ee49548ecb5df1b89f1ac7` | Successful VelocitySpike row; before (-3.970607, 0.000000, 6.945090), after (-34.725450, 0.000000, -19.853030) | 109e5609662b4ac7bfb823db10c76b51 (Physics/TotalVelocity, t=10.364525s, Δ=0.002237s); 0cc61a5c160240e2a215f9bb06fd31b9 (HorizontalVelocity/HorizontalVelocity, t=10.364904s, Δ=0.002616s) | Unity Editor.log records total=40 and horizontal=40; exceeded total threshold 20 and horizontal threshold 12. | Detected once (Physics primary; HorizontalVelocity corroborating) |

For both runs, the ground-truth and structured alert rows match exact RunId, TrialId, and EpisodeId (`1`). Each had two expected alert categories: `Physics` / rule `TotalVelocity`, then `HorizontalVelocity`. The Physics alert is the earliest primary detection and each injected event counts once, not twice. Detector CSV primary latencies are 0.012621 s (0c) and 0.002237 s (4c). The Editor.log independently records the injected velocity and the next diagnostic sample with the same IDs and observed vector; both total and horizontal thresholds were exceeded. This supports that the corrected injection was applied and visible to the detector for both trials.

## PPO callbacks in the 4c run

The project Editor.log has `BugsyAgent:OnActionReceived` / `BUGSY ACTION` callbacks during the 4c editor capture. MazeBugged serializes Behavior Parameters with `m_BehaviorType: 2`; the installed ML-Agents package enum maps 2 to `InferenceOnly`. The scene assigns model GUID `038cd0460598841dbab276ab645ab49f`, which resolves to `Assets/BugsyAgent.onnx`. Thus these callbacks are consistent with autonomous inference, and their presence is not a reason to reject 4c. The callbacks themselves are not tagged with RunId; the scene configuration and contemporaneous log support inference, but do not label whether the user intended this session as an injection-only or combined PPO experiment.

## Candidate corrected-implementation cohort

| Type / slot | RunId | Evidence and status |
|---|---|---|
| Teleport T1 | `51067c44e00c4c4c8d390de632755375` | User-retained Teleport; single GT event; PositionDiscontinuity detected at 0.008042s; performance row present. |
| Teleport T2 | `6cd25c5598ae45bb99b99534e35a6f6d` | User-retained Teleport; single GT event; PositionDiscontinuity detected at 0.011462s; performance row present. |
| Teleport T3 | `5f9ead6d68554d6d9c8f1a8b84459061` | User-retained Teleport; single GT event; PositionDiscontinuity detected at 0.048722s; performance row present. |
| Velocity V1 (post-fix) | `0c6eb50465a94b309a0b160c8e8f2b29` | User-retained post-fix VelocitySpike; injection verified and both expected velocity categories recorded; one detection, latency 0.012621s. |
| Velocity V2 (post-fix) | `4c681f1f501d49329009bc98010b0d97` | User-retained post-fix VelocitySpike; injection verified and both expected velocity categories recorded; one detection, latency 0.002237s. PPO callbacks align with InferenceOnly model setup; included unless user specifies a separate-control-only requirement. |

**Third post-fix velocity trial:** none is available to select. The only post-fix VelocitySpike GT events in the persistent log are 0c and 4c. `cbd` is pre-fix and has no structured detector alerts, so it cannot replace a third corrected trial. A new Play Mode trial using the corrected code is required for three-trial repeatability. No fabricated placeholder event has been added.

## Historical velocity experiments (kept separate)

All six other VelocitySpike GT events are pre-fix by their UTC timestamps. They remain in the all-event table and confirmation CSV but are excluded from corrected-implementation repeatability:

- `abb8b892d1774b4283596c1aa96db4fc`, `ef4652ca134f4a5e942b7d6d7c8598eb`, `c35791eab86245eca4e1ea75a7d46243`: no structured detector alert or performance row.
- `7f78a309269041208879dbdae63343ce`: mixed Teleport + VelocitySpike run; only the Teleport PositionDiscontinuity matched. Not isolated and pre-fix.
- `cbdbe76e5f2f490db55663c47b93be82`: pre-fix; successful ground-truth row and performance summary exist, but no structured detector alert row.
- `b2ec0c5cc8b14472bffdfc3419502e7c`: pre-fix per the user; successful GT row but no structured detector alert row. Explicitly excluded from corrected repeatability.

## Complete chronological event review

Sessions are ordered by `TimestampUtc` because monotonic `RealtimeSinceStartup` restarts each Play Mode session. Alert matching uses the same RunId/TrialId/EpisodeId and the 0.5 s monotonic tolerance used in the existing analysis. `Missed` below means no primary-compatible structured alert in that window; for pre-fix velocity trials this is historical evidence only, not a corrected-detector miss.

| # | UTC | RunId | TrialId | Ep. | EventId | Type | Realtime (s) | Position before → after | Velocity before → after | Primary-compatible alerts | Result / latency | Velocity-fix epoch | Other injected fault in run? |
|---:|---|---|---|---:|---|---|---:|---|---|---|---|---|---|
| 1 | 2026-11-02T16:55:26.9673460Z | `836f724ccb5c4163ae9fef390a919a6c` | `0770fc913f2d402d9fd6131a04e8b7e2` | 1 | `cc61ce754ac74ad3858fd4dd4ad68d0e` | Teleport | 11.821196 | (26.274310, 1.000000, 59.843310) → (18.974850, 1.000000, 63.117130) | (0.000000, 0.000000, 0.000000) → (0.000000, 0.000000, 0.000000) | None within 0.5 s | Missed / N/A | Not applicable to Teleport injector | No other GT injection recorded |
| 2 | 2026-11-02T16:57:20.0415900Z | `abb8b892d1774b4283596c1aa96db4fc` | `72e1ff01e0c4453cafaa33ee0da35423` | 1 | `a3143836b8df48358833347e6e39e569` | VelocitySpike | 10.821553 | (30.000000, 1.000000, 60.000000) → (30.000000, 1.000000, 60.000000) | (0.000000, 0.000000, 0.000000) → (-34.755720, 0.000000, 19.799990) | None within 0.5 s | Missed / N/A | Pre-fix | No other GT injection recorded |
| 3 | 2026-11-02T16:59:06.7873570Z | `ef4652ca134f4a5e942b7d6d7c8598eb` | `640336bff6164ce6b1f9ac442cd1be2b` | 1 | `bb0084a9f34948788072ae656b3744bc` | VelocitySpike | 10.157643 | (7.081407, 1.175107, 47.217470) → (7.081407, 1.175107, 47.217470) | (7.982932, -0.981000, -0.522299) → (-39.914660, 0.000000, 2.611494) | None within 0.5 s | Missed / N/A | Pre-fix | No other GT injection recorded |
| 4 | 2026-11-02T16:59:41.6609770Z | `c35791eab86245eca4e1ea75a7d46243` | `639fc422af184fa48837c120d2843acf` | 1 | `4e5696c736f743a5abe333e6d8be1651` | VelocitySpike | 10.141526 | (18.659800, 1.000000, 58.800630) → (18.659800, 1.000000, 58.800630) | (0.000000, 0.000000, 0.000000) → (-20.705760, 0.000000, 34.223850) | None within 0.5 s | Missed / N/A | Pre-fix | No other GT injection recorded |
| 5 | 2026-11-03T05:59:57.2541670Z | `7f78a309269041208879dbdae63343ce` | `1ce373764a4742a1bd4e223d8755e7ea` | 1 | `e1e45cfc3260465aa5232eb152d629a4` | Teleport | 10.226227 | (31.137140, 1.355521, 60.385310) → (23.555680, 1.355521, 57.831610) | (0.000000, 6.607601, 0.000000) → (0.000000, 0.000000, 0.000000) | 18c18caca3a3402c963f87b4a2ab1eca (PositionDiscontinuity/PositionDiscontinuity, t=10.238272s, Δ=0.012045s) | Detected / 0.012045s | Not applicable to Teleport injector | Yes (mixed) |
| 6 | 2026-11-03T05:59:57.2597480Z | `7f78a309269041208879dbdae63343ce` | `1ce373764a4742a1bd4e223d8755e7ea` | 1 | `4de9072c674b4c8f911b80e8a184498e` | VelocitySpike | 10.231852 | (23.555680, 1.355521, 57.831610) → (23.555680, 1.355521, 57.831610) | (0.000000, 0.000000, 0.000000) → (-37.907320, 0.000000, -12.768520) | None within 0.5 s | Missed / N/A | Pre-fix | Yes (mixed) |
| 7 | 2026-11-03T06:12:56.6470520Z | `cbdbe76e5f2f490db55663c47b93be82` | `01e8a1320be24a20baa059dfb4408b01` | 1 | `b51204dbb9e44b2988a27c5a52b599d8` | VelocitySpike | 10.250766 | (27.410200, 3.136531, 61.726550) → (27.410200, 3.136531, 61.726550) | (0.000000, 2.291210, 0.000000) → (-23.861970, 0.000000, -32.103060) | None within 0.5 s | Missed / N/A | Pre-fix | No other GT injection recorded |
| 8 | 2026-11-03T06:14:45.4650150Z | `51067c44e00c4c4c8d390de632755375` | `6fc1ddc5ebc64e81b9f4b8260fe861b9` | 1 | `bbb820bb79534452a0d14beb7e84e2a4` | Teleport | 10.145203 | (29.905090, 0.990005, 65.694510) → (21.910390, 0.990005, 65.403270) | (0.000000, -0.000014, 0.000000) → (0.000000, 0.000000, 0.000000) | 95da3b566b65440bad0a104edb33f737 (PositionDiscontinuity/PositionDiscontinuity, t=10.153245s, Δ=0.008042s) | Detected / 0.008042s | Not applicable to Teleport injector | No other GT injection recorded |
| 9 | 2026-11-03T06:37:08.6302260Z | `b2ec0c5cc8b14472bffdfc3419502e7c` | `5b2c60bcbb3f4c40954951f261c4dbe9` | 1 | `0f28bbb518f8473db9787bf69def11a7` | VelocitySpike | 10.135813 | (29.706080, 0.490005, 63.990340) → (29.706080, 0.490005, 63.990340) | (0.000000, 0.000000, 0.000000) → (-7.700487, 0.000000, -39.251790) | None within 0.5 s | Missed / N/A | Pre-fix | No other GT injection recorded |
| 10 | 2026-11-03T06:49:17.3672310Z | `0c6eb50465a94b309a0b160c8e8f2b29` | `b773e5eda90d4fe98ee682e655ea2849` | 1 | `5771a022bb8e40e1943662799a8e2034` | VelocitySpike | 10.975374 | (32.230000, 0.490005, 68.198650) → (32.230000, 0.490005, 68.198650) | (1.438522, 0.000000, 4.788596) → (-11.508180, 0.000000, -38.308770) | 5cd3a9dd4e594240ad04fca67e521b10 (Physics/TotalVelocity, t=10.987995s, Δ=0.012621s); 4eb17b07d58e43d984b465b38487702c (HorizontalVelocity/HorizontalVelocity, t=10.988359s, Δ=0.012985s) | Detected / 0.012621s | Post-fix | No other GT injection recorded |
| 11 | 2026-11-03T06:58:35.7323740Z | `6cd25c5598ae45bb99b99534e35a6f6d` | `5f02e3bfc1424bf49f5ba866b093276f` | 1 | `dc9a7b2daedd41bd92cc85cd6b3a20ae` | Teleport | 10.296013 | (24.304990, 2.715284, 60.255300) → (16.986250, 2.715284, 57.024830) | (7.318748, -3.727798, 3.230469) → (0.000000, 0.000000, 0.000000) | a666eba360154a5fa7163419f158cabc (PositionDiscontinuity/PositionDiscontinuity, t=10.307475s, Δ=0.011462s) | Detected / 0.011462s | Not applicable to Teleport injector | No other GT injection recorded |
| 12 | 2026-11-03T07:02:49.0657870Z | `5f9ead6d68554d6d9c8f1a8b84459061` | `b4cec881863e4648a67acc0090624e15` | 1 | `e5c93ae3b9e04adbbb1795a62e1f061d` | Teleport | 10.948932 | (28.629950, 1.993248, 66.545320) → (32.299200, 1.993248, 59.436410) | (-3.669252, 5.038006, 7.108909) → (0.000000, 0.000000, 0.000000) | 42bf3572504047548b8ff27d65245ffe (PositionDiscontinuity/PositionDiscontinuity, t=10.997654s, Δ=0.048722s) | Detected / 0.048722s | Not applicable to Teleport injector | No other GT injection recorded |
| 13 | 2026-11-03T07:04:09.1100130Z | `4c681f1f501d49329009bc98010b0d97` | `b105e191800740299c0649c4cd96f1f6` | 1 | `a3a03aaf44ee49548ecb5df1b89f1ac7` | VelocitySpike | 10.362288 | (30.575790, 0.490005, 66.745930) → (30.575790, 0.490005, 66.745930) | (-3.970607, 0.000000, 6.945090) → (-34.725450, 0.000000, -19.853030) | 109e5609662b4ac7bfb823db10c76b51 (Physics/TotalVelocity, t=10.364525s, Δ=0.002237s); 0cc61a5c160240e2a215f9bb06fd31b9 (HorizontalVelocity/HorizontalVelocity, t=10.364904s, Δ=0.002616s) | Detected / 0.002237s | Post-fix | No other GT injection recorded |

## Record anomalies and limitations

- 13/13 ground-truth rows have all serialized fields populated; EventIds are unique. 3,069/3,069 detector AlertIds are unique. There is one mixed-fault RunId, 7f, with two distinct injection events (not duplicates).
- `bugsy_detector_events.csv` contains 3,069 rows across 6 RunIds; `bugsy_detector_performance.csv` has 12 run/trial summaries. No `bugsy_detector_samples.csv` exists.
- The successful GT recorder entry establishes injection was recorded after assignment verification; the post-fix Unity injection and detector diagnostics additionally show actual before/after and observed velocity values.
- The b2 and cbd runs have no structured detector alert rows, but are not corrected-code trials. Four older pre-fix velocity sessions also lack detector performance records, so absence of alerts there is incomplete evidence.
- Later Physics and VerticalVelocity alerts in the 510 Teleport run are not treated as velocity-injection detections. Same-time Physics and HorizontalVelocity alerts in 0c/4c are one detection per injection.
- No final aggregate metrics or evaluation report were changed. The currently available corrected repeatability sample is two velocity trials (2/2 detected) and three Teleport trials; a third post-fix velocity experiment is still needed.
