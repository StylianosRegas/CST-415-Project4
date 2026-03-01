// ============================================================
// DungeonTests.cs
//
// NUnit tests for the Markov generator and Bellman solver.
// Place inside an Editor/ folder or an Assembly Definition
// that includes the Unity Test Framework reference.
//
// Run via:  Unity menu → Window → General → Test Runner
// ============================================================
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using DungeonForge;

namespace DungeonForge.Tests
{
    // -------------------------------------------------------
    // Markov Chain Tests
    // -------------------------------------------------------
    [TestFixture]
    public class MarkovChainGeneratorTests
    {
        private MarkovChainGenerator _gen;

        [SetUp]
        public void SetUp() => _gen = new MarkovChainGenerator();

        [Test]
        public void Generate_AlwaysStartsWithEntrance()
        {
            var seq = _gen.Generate(10, 1);
            Assert.AreEqual(RoomType.Entrance, seq[0]);
        }

        [Test]
        public void Generate_AlwaysEndsWithExit()
        {
            for (int i = 0; i < 20; i++)
            {
                var seq = _gen.Generate(10, 1);
                Assert.AreEqual(RoomType.Exit, seq[seq.Count - 1],
                    $"Last room should be Exit but was {seq[seq.Count - 1]}");
            }
        }

        [Test]
        public void Generate_BossAppearsBeforeExit()
        {
            for (int i = 0; i < 20; i++)
            {
                var seq = _gen.Generate(10, 1);
                int bossIdx = seq.FindLastIndex(r => r == RoomType.Boss);
                int exitIdx = seq.FindLastIndex(r => r == RoomType.Exit);
                Assert.Greater(exitIdx, bossIdx,
                    "Exit must come after Boss in the sequence.");
            }
        }

        [Test]
        public void Generate_RespectsDepthLimit()
        {
            for (int depth = 5; depth <= 20; depth += 5)
            {
                var seq = _gen.Generate(depth, 1);
                // Sequence may be slightly longer due to Boss+Exit appended
                Assert.LessOrEqual(seq.Count, depth + 4,
                    $"Sequence length {seq.Count} too large for depth {depth}.");
            }
        }

        [Test]
        public void Generate_SecondOrderProducesValidSequence()
        {
            var seq = _gen.Generate(12, 2);
            Assert.IsNotEmpty(seq);
            Assert.AreEqual(RoomType.Entrance, seq[0]);
            Assert.AreEqual(RoomType.Exit,     seq[seq.Count - 1]);
        }

        [Test]
        public void TransitionProbability_SumsToOne_ForEachState()
        {
            var roomTypes = System.Enum.GetValues(typeof(RoomType));
            // We only test states that have defined transitions (not Exit)
            var testStates = new[] {
                RoomType.Entrance, RoomType.Corridor, RoomType.Room,
                RoomType.Shop, RoomType.Trap, RoomType.Treasure, RoomType.DeadEnd
            };

            foreach (var state in testStates)
            {
                float total = 0f;
                foreach (RoomType next in roomTypes)
                    total += _gen.GetTransitionProbability(state, next);

                Assert.AreEqual(1f, total, 0.01f,
                    $"Probabilities from {state} don't sum to 1 (got {total:F4}).");

             
            }
        }
    }

    // -------------------------------------------------------
    // Bellman Solver Tests
    // -------------------------------------------------------
    [TestFixture]
    public class BellmanSolverTests
    {
        private BellmanSolver _solver;
        private DungeonMap    _miniMap;

        [SetUp]
        public void SetUp()
        {
            _solver = new BellmanSolver();

            // Build a minimal 5×5 map:
            //  W W W W W
            //  W . . . W
            //  W . T . W
   //  W E . X W
            //  W W W W W
            // E = Entrance, T = Treasure, X = Exit, . = Floor, W = Wall

            _miniMap = new DungeonMap(5, 5);

            // Interior floor
            for (int x = 1; x <= 3; x++)
                for (int y = 1; y <= 3; y++)
                    _miniMap.Tiles[x, y] = TileType.Floor;

            _miniMap.Tiles[1, 1] = TileType.Entrance;
            _miniMap.Tiles[2, 2] = TileType.Treasure;
            _miniMap.Tiles[3, 1] = TileType.Exit;

            _miniMap.EntranceCell = new Vector2Int(1, 1);
            _miniMap.ExitCell     = new Vector2Int(3, 1);
        }

        [Test]
        public void Solve_ProducesValuesForAllPassableCells()
        {
            var result = _solver.Solve(_miniMap, 0.9f, 100, 0.001f);

            for (int x = 1; x <= 3; x++)
                for (int y = 1; y <= 3; y++)
                    Assert.IsTrue(result.Values[x, y] != 0f || _miniMap.Tiles[x,y] == TileType.Entrance,
                        $"Cell ({x},{y}) should have a non-zero value after solving.");
        }

        [Test]
        public void Solve_ExitHasHighestValue()
        {
            var result = _solver.Solve(_miniMap, 0.9f, 200, 0.0001f);
            float exitVal = result.Values[3, 1];

            for (int x = 1; x <= 3; x++)
                for (int y = 1; y <= 3; y++)
                    Assert.LessOrEqual(result.Values[x,y], exitVal + 0.01f,
                        $"Exit value {exitVal:F2} should dominate cell ({x},{y}) = {result.Values[x,y]:F2}.");
        }

        [Test]
        public void Solve_TreasureHasPositiveValue()
        {
            var result = _solver.Solve(_miniMap, 0.9f, 100, 0.001f);
            Assert.Greater(result.Values[2, 2], 0f,
                "Treasure cell should have a positive value.");
        }

        [Test]
        public void Solve_Converges_WithinMaxIterations()
        {
            var result = _solver.Solve(_miniMap, 0.9f, 200, 0.001f);
            Assert.IsTrue(result.Converged,
                $"Should have converged (final Δ={result.FinalDelta:F6}).");
        }

        [Test]
        public void Solve_WallsHaveZeroValue()
        {
            var result = _solver.Solve(_miniMap, 0.9f, 100, 0.001f);
            for (int x = 0; x < 5; x++)
                Assert.AreEqual(0f, result.Values[x, 0],
                    $"Wall cell ({x},0) should be 0.");
        }

        [Test]
        public void Solve_PolicyPointsTowardHigherValue()
        {
            var result = _solver.Solve(_miniMap, 0.9f, 200, 0.0001f);

            // The policy at Entrance (1,1) should point toward Exit (3,1)
            // i.e. the target cell should have a higher value than Entrance
            var target = result.Policy[1, 1];
            Assert.Greater(result.Values[target.x, target.y], result.Values[1, 1],
                "Policy from Entrance should point to a cell with higher value.");
        }

        [Test]
        public void ExtractPath_ReachesExit()
        {
            var result = _solver.Solve(_miniMap, 0.9f, 200, 0.0001f);
            var path   = _solver.ExtractPath(result.Policy, _miniMap,
                                              _miniMap.EntranceCell,
                                              _miniMap.ExitCell);

            Assert.IsNotEmpty(path);
            Assert.AreEqual(_miniMap.ExitCell, path[path.Count - 1],
                "Extracted path should end at the Exit cell.");
        }

        [Test]
        public void Solve_HighGamma_GivesLargerValues()
        {
            var r09  = _solver.Solve(_miniMap, 0.9f,  200, 0.0001f);
            var r05  = _solver.Solve(_miniMap, 0.5f,  200, 0.0001f);

            // With higher discount, future rewards are worth more → larger max V
            Assert.Greater(r09.MaxValue, r05.MaxValue,
                "Higher gamma should result in larger max V(s).");
        }
    }

    // -------------------------------------------------------
    // Layout Builder Tests
    // -------------------------------------------------------
    [TestFixture]
    public class RoomLayoutBuilderTests
    {
        [Test]
        public void Build_MapHasCorrectDimensions()
        {
            var builder  = new RoomLayoutBuilder { RoomSize = 9 };
            var sequence = new List<RoomType>
                { RoomType.Entrance, RoomType.Corridor, RoomType.Room,
                  RoomType.Boss,     RoomType.Exit };

            var map = builder.Build(sequence);
            Assert.Greater(map.Width,  0);
            Assert.Greater(map.Height, 0);
        }

        [Test]
        public void Build_EntranceTileExists()
        {
            var builder  = new RoomLayoutBuilder { RoomSize = 9 };
            var sequence = new List<RoomType>
                { RoomType.Entrance, RoomType.Corridor, RoomType.Exit };

            var map = builder.Build(sequence);

            bool found = false;
            for (int x = 0; x < map.Width && !found; x++)
                for (int y = 0; y < map.Height && !found; y++)
                    if (map.Tiles[x, y] == TileType.Entrance)
                        found = true;

            Assert.IsTrue(found, "DungeonMap should contain at least one Entrance tile.");
        }

        [Test]
        public void Build_ExitTileExists()
        {
            var builder  = new RoomLayoutBuilder { RoomSize = 9 };
            var sequence = new List<RoomType>
                { RoomType.Entrance, RoomType.Boss, RoomType.Exit };

            var map = builder.Build(sequence);

            bool found = false;
            for (int x = 0; x < map.Width && !found; x++)
                for (int y = 0; y < map.Height && !found; y++)
                    if (map.Tiles[x, y] == TileType.Exit)
                        found = true;

            Assert.IsTrue(found, "DungeonMap should contain at least one Exit tile.");
        }
    }
}
