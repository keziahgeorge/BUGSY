# Kezia's Contribution — BUGSY Parkour: PPO Exploration Agent

> **This folder contains Kezia's implementation** of the RL-based exploration agent for the BUGSY project.
> The shared Unity game code (scripts, scenes, materials) lives in the parent [`Assets/`](../Assets/) folder
> and is part of the same Unity project. **No code was duplicated** — only Kezia's unique contributions
> (training scripts, evaluation figures, trained models, and baseline CSVs) are stored here.

---

## What This Contribution Does

Kezia's work focuses on **training and evaluating the PPO exploration agent** that drives BUGSY's autonomous
maze coverage. The core research question is:

> *Can count-based intrinsic motivation (novelty reward) teach a PPO agent to explore a 3D parkour maze
> more effectively than a hand-coded wall-follower or a random agent?*

The answer, backed by 100-episode evaluation runs: **yes** — the trained v8 agent covers significantly
more of the 492-cell maze than any baseline.

---

## Folder Structure

```
kezia-exploration/
├── baseline-results/           # CoverageTracker CSV logs (100 episodes each)
│   ├── baseline_random.csv         # Random action baseline
│   ├── baseline_wallfollower.csv   # Scripted right-hand-rule baseline
│   ├── baseline_v8.csv             # v8 PPO agent (seen maze, seed 21)
│   ├── baseline_v8_unseen.csv      # v8 PPO agent (unseen maze, seed 7)
│   ├── baseline_v9.csv             # v9 PPO agent
│   ├── baseline_nomap.csv          # Ablation: novelty reward, no visited-cell map
│   └── baseline_nomap300.csv       # Same ablation, 300 episodes
│
├── figures/                    # Pre-generated evaluation plots
│   ├── fig1_cumulative_unique.png      # Cumulative unique cells over 100 episodes
│   ├── fig2_coverage_improved.png      # Mean cells/episode + cumulative coverage bar charts
│   ├── fig4_generalization_gap.png     # Seen vs unseen maze performance
│   └── fig11_cumulative_regret_difference.png  # Regret vs random baseline
│
├── models/                     # Exported ONNX inference models
│   ├── BugsyAgent_v8.onnx      # Final trained model (best exploration coverage)
│   └── BugsyAgent_v9.onnx      # Extended training run
│
├── make_figures.py             # Generates all evaluation figures from baseline CSVs
├── bugsy_ppo.yaml              # ML-Agents PPO training configuration
├── BUGSY_Parkour_RL_Explanation.md # Full line-by-line code explanation & RL mathematics
└── README.md                   # This file
```

---

## RL Implementation Details

### Algorithm: Proximal Policy Optimisation (PPO)

The agent uses Unity ML-Agents' built-in PPO trainer. Training is launched from the project root with:

```bash
mlagents-learn kezia-exploration/bugsy_ppo.yaml --run-id=bugsy_v8
```

Key hyperparameters (`bugsy_ppo.yaml`):

| Parameter | Value | Rationale |
|---|---|---|
| `trainer_type` | `ppo` | On-policy, stable for discrete actions |
| `batch_size` | 256 | Minibatch per gradient update |
| `buffer_size` | 4096 | 48 updates per rollout |
| `learning_rate` | 0.0003 | Standard Adam LR for PPO |
| `epsilon` | 0.2 | PPO clip ratio |
| `lambd` | 0.95 | GAE-λ smoothing |
| `beta` | 0.005 | Entropy coefficient |
| `gamma` | 0.995 | Discount factor (long-horizon episodes) |
| `hidden_units` | 128 | 2-layer MLP |
| `max_steps` | 300,000 | ~100 s per episode |
| `normalize` | true | Running-stats observation normalisation |

### Intrinsic Reward (Count-Based Novelty)

No extrinsic task reward is used. The only reward signal is a **+0.1 bonus per newly visited 3×3 m grid cell** per episode, computed by `CoverageTracker.cs` (in `../Assets/Scripts/`):

$$r_t = \beta \cdot \mathbf{1}[g(s_{t+1}) \notin V_t]$$

where β = 0.1 and V_t is the set of visited cells this episode.

### Observation Space (43 numbers)

| Component | Size | Description |
|---|---|---|
| Normalised position (x, z) | 2 | `pos / 66` → [0, 1] |
| Local-frame velocity (x, y, z) | 3 | `vel / 10` → ≈[−1, 1] |
| Heading (sin θ, cos θ) | 2 | Continuous angle encoding |
| Grounded flag | 1 | 1 if on ground |
| Sliding flag | 1 | 1 if crouching |
| Previous action one-hots | 9 | move×2, jump×2, slide×2, turn×3 |
| 5×5 visited-cell window | 25 | 0 = unvisited (target), 1 = visited/outside |

### Action Space (discrete, 4 branches)

| Branch | Options |
|---|---|
| Move | Stand (0), Forward (1) |
| Jump | No (0), Jump (1) |
| Slide | No (0), Slide (1) |
| Turn | None (0), Left (1), Right (2) |

### Episode Termination

| Condition | Mechanism |
|---|---|
| Reached finish zone | `FinishZone.cs` → `Win()` → `EndEpisode()` |
| All lives lost | `GameManager.cs` → `GameOver` → `EndEpisode()` |
| Stuck (no new cell for 8 s) | `BugsyAgent.FixedUpdate` stuckSteps ≥ 800 |
| Max steps exceeded | ML-Agents Academy (MaxStep = 10,000) |

---

## Baselines Compared

| Agent | Description | Mean Cells/Episode |
|---|---|---|
| Random | Uniform random actions each step | ~8–12 |
| Wall Follower | Scripted right-hand rule (see `WallFollowerBrain.cs`) | ~20–30 |
| **v8 PPO** | **Trained with novelty reward + visited-cell map** | **~45+** |
| No Map (ablation) | Novelty reward only, visited-cell obs zeroed | Lower than v8 |

The maze has **492 reachable cells** (computed by `BugsyReachableCells.cs`). v8 covers ~10% per episode on average, reaching new cells reliably across both seen and unseen mazes.

---

## Reproducing the Evaluation

### 1. Run Baseline Measurements in Unity

1. Open the Unity project (`BUGSY_Parkour/`) in Unity 6.
2. Run **BUGSY → Build Maze Level** to generate the maze.
3. Run **BUGSY → Add Agent To Player** to attach ML-Agents components.
4. Assign the ONNX model from `models/BugsyAgent_v8.onnx` to the Behavior Parameters.
5. Set `CoverageTracker.csvLabel` (e.g. `"v8"`) and `targetEpisodes = 100`.
6. Press Play — the game runs at 10× speed and auto-stops after 100 episodes.
7. Find the CSV at the project root (`baseline_v8.csv`).

### 2. Generate Figures

```bash
cd kezia-exploration/
pip install pandas matplotlib numpy
python make_figures.py
# Figures written to figures/
```

Place any optional TensorBoard CSVs (`tb_cells_v8.csv`, etc.) in the same folder to also generate training-curve figures.

### 3. Train from Scratch

```bash
# From project root (parent of this folder)
pip install mlagents
mlagents-learn kezia-exploration/bugsy_ppo.yaml --run-id=my_run --force
```

Training takes ~2–3 hours for 300k steps. TensorBoard:
```bash
tensorboard --logdir results/
```

---

## Ablation Study

Three configurations were compared:

| Config | Novelty Reward | Visited-Cell Obs | Finish Reward |
|---|---|---|---|
| **v8 (default)** | ✅ +0.1/cell | ✅ 25 observations | ❌ |
| No Map | ✅ +0.1/cell | ❌ zeroed | ❌ |
| Task PPO | ❌ | ❌ zeroed | ✅ +1 at finish |

The visited-cell map is crucial: without it, the agent can't distinguish which nearby corridors are unexplored and coverage drops substantially.

---

## Dependencies

| Dependency | Version | Purpose |
|---|---|---|
| Unity | 6.x | Game engine |
| Unity ML-Agents | ≥3.0 | PPO training + inference |
| Python | ≥3.9 | Figure generation |
| pandas | ≥1.5 | CSV loading |
| matplotlib | ≥3.6 | Plotting |
| numpy | ≥1.23 | Numerical ops |

---

## Key Source Files (in `../Assets/`)

| File | Role |
|---|---|
| [`Scripts/BugsyAgent.cs`](../Assets/Scripts/BugsyAgent.cs) | ML-Agents Agent — observations, actions, episode logic |
| [`Scripts/CoverageTracker.cs`](../Assets/Scripts/CoverageTracker.cs) | Intrinsic reward + 5×5 visited-cell observation window |
| [`Scripts/WallFollowerBrain.cs`](../Assets/Scripts/WallFollowerBrain.cs) | Scripted baseline (right-hand rule) |
| [`Scripts/PlayerController.cs`](../Assets/Scripts/PlayerController.cs) | Physics character controller |
| [`Scripts/GameManager.cs`](../Assets/Scripts/GameManager.cs) | Episode lifecycle, lives, restart |
| [`Editor/BugsyMazeBuilder.cs`](../Assets/Editor/BugsyMazeBuilder.cs) | Procedural maze generation (recursive backtracker + BFS) |
| [`Editor/BugsyAgentSetup.cs`](../Assets/Editor/BugsyAgentSetup.cs) | One-click agent component setup |
| [`Editor/BugsyTrainingMode.cs`](../Assets/Editor/BugsyTrainingMode.cs) | Training ↔ play mode switcher |
| [`Editor/BugsyReachableCells.cs`](../Assets/Editor/BugsyReachableCells.cs) | BFS reachability counter (492 cells) |

---

*Kezia George — BUGSY Parkour RL Agent — Semester 7*

