# DungeonForge — Unity C# Implementation
## Markov Chain Dungeon Generation + Bellman Value Iteration

---

## Overview

This package generates procedural dungeon rooms using a **Markov chain** for room sequencing, then applies **Bellman value iteration** to compute an optimal agent policy over the resulting tile grid.

```
MarkovChainGenerator  →  room sequence  →  RoomLayoutBuilder  →  DungeonMap (tile grid)
                                                                        ↓
                                                               BellmanSolver
                                                                        ↓
                                                         V*(s) value function + π*(s) policy
                                                                        ↓
                                                              AgentController (animated)
```

---

## The Algorithms

### 1. Markov Chain Room Generation

A Markov chain models transitions between **states** (room types) using conditional probabilities:

```
P(Room_n+1 = B | Room_n = A)
```

**1st-order chain** — the next room depends only on the current room.  
**2nd-order chain** — the next room depends on the last *two* rooms, giving richer patterns (e.g. `Corridor,Room → Treasure` is more likely than after `Trap,Room`).

The transition tables are hand-designed to produce sensible dungeon progressions:

```
Entrance → Corridor (70%) → Room (45%) → Treasure/Trap/Boss...
                          → Corridor (25%)
                          → DeadEnd  (10%)
```

The chain always terminates with `Boss → Exit`.

### 2. Bellman Value Iteration (MDP Solver)

The dungeon is modelled as a **deterministic MDP** (Markov Decision Process):

- **States**: every passable cell (x, y) in the tile grid
- **Actions**: move North, South, East, West
- **Reward R(s)**: defined per tile type (e.g. Treasure = +10, Trap = −8, Exit = +20)
- **Discount γ**: future rewards are worth γ times present rewards

The Bellman optimality equation is iterated until convergence:

```
V*(s)  ←  R(s) + γ · max_{a ∈ A(s)}  V*(s')
```

Because transitions are deterministic, `P(s'|s,a) = 1` for the intended next cell, simplifying the expected value to a direct max over neighbours.

Once converged, the greedy policy is extracted:

```
π*(s) = argmax_{s' reachable from s}  V*(s')
```

An agent following π*(s) will take the highest-value path from Entrance to Exit.

---

## File Structure

```
DungeonForge/
├── DungeonData.cs          — TileType, RoomType, DungeonMap, TileRewards
├── MarkovChainGenerator.cs — 1st/2nd-order Markov chain room sequencer
├── RoomLayoutBuilder.cs    — Converts room sequence → tile grid (DungeonMap)
├── BellmanSolver.cs        — Value iteration (sync + coroutine), policy extraction, path following
├── DungeonRenderer.cs      — Unity Tilemap rendering + value/policy overlays
├── AgentController.cs      — Animated agent that follows the optimal policy
├── DungeonManager.cs       — Main orchestrator MonoBehaviour (wires everything)
└── Tests/
    └── DungeonTests.cs     — NUnit tests for all core systems
```

---

## Unity Setup

### Requirements
- Unity 2021.3 LTS or later
- 2D Tilemap Extras (install via Package Manager)
- TextMeshPro (install via Package Manager)

### Step-by-Step

#### 1. Import scripts
Copy the `DungeonForge/` folder into your project's `Assets/Scripts/` directory.

#### 2. Create the Scene hierarchy

```
[Scene]
 ├── DungeonManager          (Empty GameObject)
 │    └── DungeonManager.cs  (attach this script)
 │
 ├── Grid                    (Add → 2D Object → Tilemap → Rectangular)
 │    ├── DungeonTilemap      (child Tilemap — main dungeon layer)
 │    │    └── DungeonRenderer.cs  (attach here)
 │    └── OverlayTilemap      (child Tilemap — policy arrows layer)
 │
 ├── Agent                   (Empty GameObject or Sprite)
 │    └── AgentController.cs (attach here)
 │
 └── Canvas                  (UI Canvas)
      ├── InfoText            (TextMeshPro)
      ├── StatsText           (TextMeshPro)
      ├── GenerateButton
      ├── SolveButton
      ├── AnimateButton
      ├── StepButton
      ├── ResetAgentButton
      ├── ShowValuesToggle
      ├── ShowPolicyToggle
      ├── GammaSlider
      └── DepthSlider
```

#### 3. Create Tile assets

For each tile type you need a **Tile** asset:
1. Create a sprite (or import a tileset).
2. Right-click in Project → Create → 2D → Tiles → Tile.
3. Assign your sprite to the tile asset.

Tile types needed: `Wall`, `Floor`, `Door`, `Treasure`, `Enemy`, `Entrance`, `Exit`, `Trap`, `Corridor`.

For policy arrows: `ArrowUp`, `ArrowDown`, `ArrowLeft`, `ArrowRight`.

#### 4. Wire the Inspector

On `DungeonManager`:
- Drag `DungeonRenderer` (on the DungeonTilemap object) into the **Renderer** slot.
- Drag `AgentController` (on the Agent object) into the **Agent** slot.
- Drag all UI elements into their respective slots.
- Tune **Markov Settings** and **Bellman Settings**.

On `DungeonRenderer`:
- Assign all tile assets to the corresponding slots.
- Assign `OverlayTilemap` to the overlay tilemap child.
- Assign arrow tile assets.

On `AgentController`:
- Assign the `SpriteRenderer` on the Agent GameObject.

#### 5. Run

Press **Play**. The dungeon generates automatically.

| Key | Action |
|-----|--------|
| `G` | Generate new dungeon |
| `S` | Run Bellman value iteration |
| `A` | Animate agent on optimal path |
| `R` | Reset agent to entrance |
| `Space` | Single-step agent |

---

## Tuning Guide

### Markov Parameters

| Parameter | Effect |
|-----------|--------|
| `DungeonDepth` | Target number of rooms (5–25) |
| `RoomSize` | Width/height of each room in tiles (7–19, odd) |
| `ChainOrder` | 1 = memoryless, 2 = context-aware (richer patterns) |
| `EnemyRate` | Fraction of floor tiles that become enemies |
| `TreasureRate` | Fraction of floor tiles that become treasure |

### Bellman Parameters

| Parameter | Effect |
|-----------|--------|
| `Gamma (γ)` | Discount factor. Higher → agent cares more about distant rewards. 0.99 = far-sighted, 0.5 = short-sighted |
| `MaxIterations` | Hard cap on sweeps. 50–200 is usually enough |
| `Epsilon (ε)` | Convergence threshold. Smaller = more accurate but slower |
| `AnimateIteration` | If true, runs one iteration per frame so you can watch V(s) propagate |

### Reward Table (in `TileRewards`)

```csharp
Floor     = -0.1f   // small step cost encourages shorter paths
Corridor  = -0.2f   // slightly more expensive to discourage long corridors
Door      = -0.1f
Entrance  =  0f
Treasure  = +10f
Enemy     = -5f
Trap      = -8f
Exit      = +20f    // highest reward — agent will seek this
```

Tweak these values to change agent behaviour. Setting `Enemy = -50f` makes the agent strongly avoid enemies. Setting `Treasure = 0f` makes the agent ignore loot and head straight for the exit.

---

## Extending the System

### Adding stochastic transitions
To model "slippery" floors, change `BestNeighbourValue` to a weighted sum:

```csharp
// Instead of max, use expected value:
// V(s) = R(s) + γ · Σ P(s'|s,a) · V(s')
float expectedValue = 0f;
foreach (var dir in s_actions)
{
    float prob = dir == chosenAction ? 0.8f : 0.0667f; // 80% intended, 20% random
    expectedValue += prob * V[nx, ny];
}
```

### Adding multi-floor dungeons
Generate a new `DungeonMap` per floor. Use `Exit` cells to link floors. Run a separate `BellmanSolver` per floor or across the concatenated grid.

### Training the Markov chain from data
Replace the hard-coded `_trans1st` dictionary with counts trained from hand-crafted dungeon sequences, then normalise:

```csharp
// Count transitions from your example dungeons
counts["Entrance"]["Corridor"] += 1;
// Normalise
foreach (var from in counts)
{
    float total = counts[from.Key].Values.Sum();
    probabilities[from.Key] = counts[from.Key]
        .ToDictionary(k => k.Key, k => k.Value / total);
}
```

---

## Running the Tests

1. Open **Window → General → Test Runner**.
2. Select **PlayMode** or **EditMode** tab.
3. Click **Run All**.

All tests in `DungeonTests.cs` should pass green. Key tests:
- Markov sequence always starts `Entrance`, ends `Exit`, `Boss` appears before `Exit`.
- Transition probabilities sum to 1.
- Bellman converges, Exit has highest value, policy points toward higher value.
- Path extraction reaches the Exit cell.

---

## Bellman Equation Reference

```
V*(s) = R(s) + γ · max_{a ∈ A(s)} V*(s')

where:
  s         current cell (state)
  R(s)      immediate reward of tile at s
  γ ∈(0,1)  discount factor
  A(s)      available actions (N/S/E/W moves to passable neighbours)
  s'        next cell after taking action a (deterministic)

Convergence criterion:
  max_{s} |V_{k+1}(s) − V_k(s)| < ε

Greedy policy extraction:
  π*(s) = argmax_{s' reachable} V*(s')
```
