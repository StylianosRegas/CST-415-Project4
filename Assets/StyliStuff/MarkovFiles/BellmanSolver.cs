// ============================================================
// BellmanSolver.cs
//
// Implements synchronous Value Iteration using the Bellman
// optimality equation for a deterministic grid MDP:
//
//   V*(s) = R(s) + γ · max_{a ∈ A(s)}  V*(s')
//
// where:
//   s      = current cell (state)
//   R(s)   = immediate reward at state s
//   γ      = discount factor  (0 < γ < 1)
//   A(s)   = actions available from s (4-directional moves)
//   s'     = next state after taking action a
//
// Because the dungeon is deterministic (no stochastic transitions),
// P(s'|s,a) = 1 for the chosen action and 0 otherwise, which
// simplifies the Bellman sum to a simple max over neighbours.
//
// The solver also extracts the greedy policy π(s) = argmax_a V*(s').
// ============================================================
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace DungeonForge
{
    // -------------------------------------------------------
    // Result container
    // -------------------------------------------------------
    public class BellmanResult
    {
        public float[,]   Values;          // V*(s) for every cell
        public Vector2Int[,] Policy;       // best action (as target cell) per cell
        public int        IterationsRun;
        public float      FinalDelta;
        public bool       Converged;
        public float      MaxValue;
        public float      MinValue;
    }

    // -------------------------------------------------------
    // Solver
    // -------------------------------------------------------
    public class BellmanSolver
    {
        // 4-directional movement (N S W E)
        private static readonly Vector2Int[] s_actions =
        {
            Vector2Int.up,
            Vector2Int.down,
            Vector2Int.left,
            Vector2Int.right
        };

        // -------------------------------------------------------
        // Synchronous solve  —  runs all iterations at once
        // -------------------------------------------------------
        /// <summary>
        /// Run full value iteration synchronously.
        /// </summary>
        /// <param name="map">The dungeon map (tile grid + metadata)</param>
        /// <param name="gamma">Discount factor γ ∈ (0,1)</param>
        /// <param name="maxIterations">Hard iteration cap</param>
        /// <param name="epsilon">Convergence threshold (max |ΔV| per sweep)</param>
        public BellmanResult Solve(DungeonMap map,
                                   float gamma         = 0.85f,
                                   int   maxIterations = 100,
                                   float epsilon       = 0.01f)
        {
            int W = map.Width;
            int H = map.Height;

            float[,] V    = InitialiseValues(map);
            float[,] newV = new float[W, H];

            int   iterations = 0;
            float delta      = float.MaxValue;

            // ---- Main iteration loop ----
            while (iterations < maxIterations && delta > epsilon)
            {
                delta = 0f;
                CopyValues(V, newV, W, H);

                for (int x = 0; x < W; x++)
                {
                    for (int y = 0; y < H; y++)
                    {
                        if (!TileRewards.IsPassable(map.Tiles[x, y]))
                            continue;

                        float immediateReward = TileRewards.Get(map.Tiles[x, y]);
                        float bestNeighbour   = BestNeighbourValue(V, map, x, y);

                        // Bellman update:  V(s) ← R(s) + γ · max_a V(s')
                        float updated = immediateReward + gamma * bestNeighbour;

                        delta          = Mathf.Max(delta, Mathf.Abs(updated - V[x, y]));
                        newV[x, y]     = updated;
                    }
                }

                // Swap buffers
                var tmp = V;
                V    = newV;
                newV = tmp;

                iterations++;
            }

            // ---- Extract policy ----
            var policy = ExtractPolicy(V, map);

            // ---- Compute stats ----
            float minV =  float.MaxValue;
            float maxV = -float.MaxValue;
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++)
                    if (TileRewards.IsPassable(map.Tiles[x, y]))
                    {
                        minV = Mathf.Min(minV, V[x, y]);
                        maxV = Mathf.Max(maxV, V[x, y]);
                    }

            return new BellmanResult
            {
                Values        = V,
                Policy        = policy,
                IterationsRun = iterations,
                FinalDelta    = delta,
                Converged     = delta <= epsilon,
                MaxValue      = maxV,
                MinValue      = minV
            };
        }

        // -------------------------------------------------------
        // Coroutine solve  —  one iteration per frame (visualise!)
        // -------------------------------------------------------
        /// <summary>
        /// Run value iteration as a Unity coroutine so you can
        /// watch the value function converge in real time.
        /// Calls onIterationComplete after each sweep.
        /// </summary>
        public IEnumerator SolveCoroutine(DungeonMap          map,
                                          float               gamma,
                                          int                 maxIterations,
                                          float               epsilon,
                                          Action<float[,], int, float> onIterationComplete,
                                          Action<BellmanResult>        onFinished)
        {
            int W = map.Width;
            int H = map.Height;

            float[,] V    = InitialiseValues(map);
            float[,] newV = new float[W, H];

            int   iterations = 0;
            float delta      = float.MaxValue;

            while (iterations < maxIterations && delta > epsilon)
            {
                delta = 0f;
                CopyValues(V, newV, W, H);

                for (int x = 0; x < W; x++)
                    for (int y = 0; y < H; y++)
                    {
                        if (!TileRewards.IsPassable(map.Tiles[x, y])) continue;

                        float r       = TileRewards.Get(map.Tiles[x, y]);
                        float bestNbr = BestNeighbourValue(V, map, x, y);
                        float updated = r + gamma * bestNbr;

                        delta      = Mathf.Max(delta, Mathf.Abs(updated - V[x, y]));
                        newV[x, y] = updated;
                    }

                var tmp = V; V = newV; newV = tmp;
                iterations++;

                onIterationComplete?.Invoke(V, iterations, delta);
                yield return null;   // ← one frame per iteration
            }

            var policy = ExtractPolicy(V, map);
            float minV =  float.MaxValue;
            float maxV = -float.MaxValue;
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++)
                    if (TileRewards.IsPassable(map.Tiles[x, y]))
                    { minV = Mathf.Min(minV, V[x,y]); maxV = Mathf.Max(maxV, V[x,y]); }

            onFinished?.Invoke(new BellmanResult
            {
                Values        = V,
                Policy        = policy,
                IterationsRun = iterations,
                FinalDelta    = delta,
                Converged     = delta <= epsilon,
                MaxValue      = maxV,
                MinValue      = minV
            });
        }

        // -------------------------------------------------------
        // Greedy policy extraction
        //   π(s) = argmax_{s' reachable from s} V(s')
        // -------------------------------------------------------
        private Vector2Int[,] ExtractPolicy(float[,] V, DungeonMap map)
        {
            int W = map.Width;
            int H = map.Height;
            var policy = new Vector2Int[W, H];

            for (int x = 0; x < W; x++)
            {
                for (int y = 0; y < H; y++)
                {
                    if (!TileRewards.IsPassable(map.Tiles[x, y]))
                    {
                        policy[x, y] = new Vector2Int(-1, -1); // no policy for walls
                        continue;
                    }

                    float bestVal = float.NegativeInfinity;
                    var   bestCell = new Vector2Int(x, y);

                    foreach (var dir in s_actions)
                    {
                        int nx = x + dir.x;
                        int ny = y + dir.y;
                        if (!map.InBounds(nx, ny)) continue;
                        if (!TileRewards.IsPassable(map.Tiles[nx, ny])) continue;

                        if (V[nx, ny] > bestVal)
                        {
                            bestVal  = V[nx, ny];
                            bestCell = new Vector2Int(nx, ny);
                        }
                    }

                    policy[x, y] = bestCell;
                }
            }
            return policy;
        }

        // -------------------------------------------------------
        // Helpers
        // -------------------------------------------------------

        /// <summary>Seed the value grid with immediate tile rewards.</summary>
        private float[,] InitialiseValues(DungeonMap map)
        {
            var V = new float[map.Width, map.Height];
            for (int x = 0; x < map.Width; x++)
                for (int y = 0; y < map.Height; y++)
                {
                    float r = TileRewards.Get(map.Tiles[x, y]);
                    V[x, y] = float.IsNegativeInfinity(r) ? 0f : r;
                }
            return V;
        }

        /// <summary>
        /// Returns the highest V(s') among all passable neighbours of (x, y).
        /// </summary>
        private float BestNeighbourValue(float[,] V, DungeonMap map, int x, int y)
        {
            float best = float.NegativeInfinity;
            foreach (var dir in s_actions)
            {
                int nx = x + dir.x;
                int ny = y + dir.y;
                if (!map.InBounds(nx, ny)) continue;
                if (!TileRewards.IsPassable(map.Tiles[nx, ny])) continue;
                if (V[nx, ny] > best) best = V[nx, ny];
            }
            return best == float.NegativeInfinity ? 0f : best;
        }

        private void CopyValues(float[,] src, float[,] dst, int W, int H)
        {
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++)
                    dst[x, y] = src[x, y];
        }

        // -------------------------------------------------------
        // Path extraction from policy  (for agent navigation)
        // -------------------------------------------------------
        /// <summary>
        /// Follow the greedy policy from 'start' until we reach 'goal'
        /// or exceed maxSteps.  Returns the list of cells to visit.
        /// </summary>
        public List<Vector2Int> ExtractPath(Vector2Int[,] policy,
                                            DungeonMap     map,
                                            Vector2Int     start,
                                            Vector2Int     goal,
                                            int            maxSteps = 500)
        {
            var path    = new List<Vector2Int> { start };
            var visited = new HashSet<Vector2Int> { start };
            var current = start;

            for (int step = 0; step < maxSteps; step++)
            {
                if (current == goal) break;

                var next = policy[current.x, current.y];
                if (next.x < 0 || visited.Contains(next)) break;

                path.Add(next);
                visited.Add(next);
                current = next;
            }

            return path;
        }
    }
}
