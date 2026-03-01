// ============================================================
// DungeonData.cs
// Core enums, structs, and tile definitions for the dungeon system.
// ============================================================
using System.Collections.Generic;
using UnityEngine;

namespace DungeonForge
{
    // ----------------------------------------------------------
    // Tile Types
    // ----------------------------------------------------------
    public enum TileType
    {
        Wall      = 0,
        Floor     = 1,
        Door      = 2,
        Treasure  = 3,
        Enemy     = 4,
        Entrance  = 5,
        Exit      = 6,
        Trap      = 7,
        Corridor  = 8
    }

    // ----------------------------------------------------------
    // Room Category (used by the Markov Chain)
    // ----------------------------------------------------------
    public enum RoomType
    {
        Entrance,
        Corridor,
        Room,
        Shop,
        Trap,
        Treasure,
        Boss,
        DeadEnd,
        Exit
    }

    // ----------------------------------------------------------
    // A single room in the dungeon layout
    // ----------------------------------------------------------
    [System.Serializable]
    public class DungeonRoom
    {
        public RoomType  Type;
        public int       GridX;          // position in room-grid space
        public int       GridY;
        public int       WorldOffsetX;   // top-left tile in world space
        public int       WorldOffsetY;
        public int       Size;           // width == height (square rooms)
        public List<DungeonRoom> Neighbours = new List<DungeonRoom>();
    }

    // ----------------------------------------------------------
    // Reward Table  (used by Bellman solver)
    // ----------------------------------------------------------
    public static class TileRewards
    {
        private static readonly Dictionary<TileType, float> _table = new Dictionary<TileType, float>
        {
            { TileType.Wall,      float.NegativeInfinity }, // impassable
            { TileType.Floor,    -0.1f  },
            { TileType.Corridor, -0.2f  },
            { TileType.Door,     -0.1f  },
            { TileType.Entrance,  0f    },
            { TileType.Treasure,  10f   },
            { TileType.Enemy,    -5f    },
            { TileType.Trap,     -8f    },
            { TileType.Exit,      20f   },
        };

        public static float Get(TileType t) =>
            _table.TryGetValue(t, out float v) ? v : -0.1f;

        public static bool IsPassable(TileType t) =>
            t != TileType.Wall && _table.TryGetValue(t, out float v) &&
            !float.IsNegativeInfinity(v);
    }

    // ----------------------------------------------------------
    // Full dungeon snapshot passed between systems
    // ----------------------------------------------------------
    public class DungeonMap
    {
        public TileType[,] Tiles;
        public int         Width;
        public int         Height;
        public List<DungeonRoom> Rooms = new List<DungeonRoom>();
        public Vector2Int  EntranceCell;
        public Vector2Int  ExitCell;

        public DungeonMap(int w, int h)
        {
            Width  = w;
            Height = h;
            Tiles  = new TileType[w, h];
            // Fill with walls by default
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                    Tiles[x, y] = TileType.Wall;
        }

        public bool InBounds(int x, int y) =>
            x >= 0 && x < Width && y >= 0 && y < Height;
    }
}
