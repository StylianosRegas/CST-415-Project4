// ============================================================
// RoomLayoutBuilder.cs
// Converts a Markov-generated room sequence into an actual
// 2-D tile grid (DungeonMap), placing rooms in a snake layout
// connected by corridor tiles.
// ============================================================
using System.Collections.Generic;
using UnityEngine;

namespace DungeonForge
{
    public class RoomLayoutBuilder
    {
        // Configuration
        public int   RoomSize       { get; set; } = 13;   // must be odd
        public int   CorridorLength { get; set; } = 3;
        public float EnemyRate      { get; set; } = 0.30f;
        public float TreasureRate   { get; set; } = 0.15f;

        // Direction offsets (x, y) in tile space
        private static readonly Vector2Int[] s_dirs = {
            Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right
        };

        // -------------------------------------------------------
        // Build  —  entry point
        // -------------------------------------------------------
        public DungeonMap Build(List<RoomType> sequence)
        {
            int cols       = Mathf.CeilToInt(Mathf.Sqrt(sequence.Count));
            int cellStride = RoomSize + CorridorLength;
            int gridW      = cols * cellStride;
            int gridH      = (Mathf.CeilToInt((float)sequence.Count / cols)) * cellStride;

            var map = new DungeonMap(gridW, gridH);

            for (int i = 0; i < sequence.Count; i++)
            {
                int col = i % cols;
                int row = i / cols;
                int ox  = col * cellStride;
                int oy  = row * cellStride;

                var room = new DungeonRoom
                {
                    Type         = sequence[i],
                    GridX        = col,
                    GridY        = row,
                    WorldOffsetX = ox,
                    WorldOffsetY = oy,
                    Size         = RoomSize
                };

                CarveRoom(map, room);
                map.Rooms.Add(room);

                // Carve corridor to next room
                if (i + 1 < sequence.Count)
                    CarveCorridorToNext(map, room, i, cols, cellStride);

                // Record special cells
                int mid = RoomSize / 2;
                if (sequence[i] == RoomType.Entrance)
                    map.EntranceCell = new Vector2Int(ox + mid, oy + mid);
                if (sequence[i] == RoomType.Exit || sequence[i] == RoomType.Boss)
                    map.ExitCell = new Vector2Int(ox + mid, oy + mid);
            }

            // Link room neighbours
            BuildNeighbourLinks(map);

            return map;
        }

        // -------------------------------------------------------
        // Carve a single room into the tile grid
        // -------------------------------------------------------
        private void CarveRoom(DungeonMap map, DungeonRoom room)
        {
            int s   = room.Size;
            int mid = s / 2;
            int ox  = room.WorldOffsetX;
            int oy  = room.WorldOffsetY;

            // Interior floor
            for (int x = 1; x < s - 1; x++)
                for (int y = 1; y < s - 1; y++)
                    SetTile(map, ox + x, oy + y, TileType.Floor);

            // Doors at the centre of each edge
            SetTile(map, ox + mid,     oy,         TileType.Door);
            SetTile(map, ox + mid,     oy + s - 1, TileType.Door);
            SetTile(map, ox,           oy + mid,   TileType.Door);
            SetTile(map, ox + s - 1,  oy + mid,   TileType.Door);

            // Populate interior based on room type
            PlaceSpecialTiles(map, room);
        }

        // -------------------------------------------------------
        // Place type-specific tiles inside the room
        // -------------------------------------------------------
        private void PlaceSpecialTiles(DungeonMap map, DungeonRoom room)
        {
            int s   = room.Size;
            int mid = s / 2;
            int ox  = room.WorldOffsetX;
            int oy  = room.WorldOffsetY;

            // Gather all interior floor positions
            var interior = new List<Vector2Int>();
            for (int x = 1; x < s - 1; x++)
                for (int y = 1; y < s - 1; y++)
                    interior.Add(new Vector2Int(ox + x, oy + y));

            Shuffle(interior);
            int cursor = 0;

            // ---- Type-specific placements ----
            switch (room.Type)
            {
                case RoomType.Entrance:
                    SetTile(map, ox + mid, oy + mid, TileType.Entrance);
                    cursor++;
                    break;

                case RoomType.Exit:
                    SetTile(map, ox + mid, oy + mid, TileType.Exit);
                    cursor++;
                    break;

                case RoomType.Boss:
                    SetTile(map, ox + mid, oy + mid, TileType.Exit);
                    // 3 guaranteed enemies flanking the boss
                    for (int i = 0; i < 3 && cursor < interior.Count; i++, cursor++)
                        SetTile(map, interior[cursor].x, interior[cursor].y, TileType.Enemy);
                    break;

                case RoomType.Treasure:
                    for (int i = 0; i < 4 && cursor < interior.Count; i++, cursor++)
                        SetTile(map, interior[cursor].x, interior[cursor].y, TileType.Treasure);
                    break;

                case RoomType.Shop:
                    for (int i = 0; i < 2 && cursor < interior.Count; i++, cursor++)
                        SetTile(map, interior[cursor].x, interior[cursor].y, TileType.Treasure);
                    break;

                case RoomType.Trap:
                    int trapCount = Mathf.Max(2, Mathf.FloorToInt(s * 0.15f));
                    for (int i = 0; i < trapCount && cursor < interior.Count; i++, cursor++)
                        SetTile(map, interior[cursor].x, interior[cursor].y, TileType.Trap);
                    break;
            }

            // ---- Scatter enemies and treasure in remaining cells ----
            for (int i = cursor; i < interior.Count; i++)
            {
                float roll = Random.value;
                if      (roll < EnemyRate)                      SetTile(map, interior[i].x, interior[i].y, TileType.Enemy);
                else if (roll < EnemyRate + TreasureRate)       SetTile(map, interior[i].x, interior[i].y, TileType.Treasure);
                // else leave as Floor
            }
        }

        // -------------------------------------------------------
        // Carve a straight corridor to the next room
        // -------------------------------------------------------
        private void CarveCorridorToNext(DungeonMap map, DungeonRoom room,
                                         int index, int cols, int cellStride)
        {
            int nextIndex = index + 1;
            int nextCol   = nextIndex % cols;
            int nextRow   = nextIndex / cols;
            int mid       = room.Size / 2;

            if (nextRow == room.GridY)
            {
                // Rooms are in the same row → corridor goes right
                int startX = room.WorldOffsetX + room.Size;
                int endX   = nextCol * cellStride;
                int y      = room.WorldOffsetY + mid;
                for (int x = startX; x < endX; x++)
                    SetTile(map, x, y, TileType.Corridor);
            }
            else
            {
                // Rooms are in different rows → corridor goes down
                int startY = room.WorldOffsetY + room.Size;
                int endY   = nextRow * cellStride;
                int x      = room.WorldOffsetX + mid;
                for (int y = startY; y < endY; y++)
                    SetTile(map, x, y, TileType.Corridor);
            }
        }

        // -------------------------------------------------------
        // Build spatial neighbour links between rooms
        // -------------------------------------------------------
        private void BuildNeighbourLinks(DungeonMap map)
        {
            for (int i = 0; i < map.Rooms.Count; i++)
            {
                for (int j = i + 1; j < map.Rooms.Count; j++)
                {
                    if (AreAdjacent(map.Rooms[i], map.Rooms[j]))
                    {
                        map.Rooms[i].Neighbours.Add(map.Rooms[j]);
                        map.Rooms[j].Neighbours.Add(map.Rooms[i]);
                    }
                }
            }
        }

        private bool AreAdjacent(DungeonRoom a, DungeonRoom b)
        {
            int dx = Mathf.Abs(a.GridX - b.GridX);
            int dy = Mathf.Abs(a.GridY - b.GridY);
            return (dx == 1 && dy == 0) || (dx == 0 && dy == 1);
        }

        // -------------------------------------------------------
        // Helpers
        // -------------------------------------------------------
        private void SetTile(DungeonMap map, int x, int y, TileType t)
        {
            if (map.InBounds(x, y))
                map.Tiles[x, y] = t;
        }

        private void Shuffle<T>(List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
