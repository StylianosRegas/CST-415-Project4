// ============================================================
// DungeonRenderer.cs
//
// Renders the DungeonMap onto a Unity Tilemap.
//
// KEY FIXES vs. original:
// ─────────────────────────────────────────────────────────
// 1. POLICY ARROWS are now DEBUG-ONLY.
//    They live on a separate OverlayTilemap that is HIDDEN by
//    default.  Call ShowPolicyOverlay(true) to reveal them in
//    debug mode.  A clear tooltip in the Inspector explains
//    what they represent.
//
// 2. CAMERA no longer zooms to show the whole dungeon.
//    FocusCameraOnEntrance() positions the camera over the
//    entrance at a comfortable zoom (~1 room visible).
//    PlayerController then takes over and follows the player
//    at the zoom level set by CameraViewTiles.
//
// 3. Tile rendering uses SetTiles (batch API) instead of
//    per-tile SetTile calls — much faster for large maps.
// ============================================================
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace DungeonForge
{
    [RequireComponent(typeof(Tilemap))]
    public class DungeonRenderer : MonoBehaviour
    {
        // -------------------------------------------------------
        // Inspector — Tile Assets
        // -------------------------------------------------------
        [Header("Tile Assets  (drag your tile sprites here)")]
        public TileBase WallTile;
        public TileBase FloorTile;
        public TileBase DoorTile;
        public TileBase TreasureTile;
        public TileBase EnemyTile;
        public TileBase EntranceTile;
        public TileBase ExitTile;
        public TileBase TrapTile;
        public TileBase CorridorTile;

        // -------------------------------------------------------
        // Policy Arrow Overlay  (DEBUG only)
        // -------------------------------------------------------
        [Header("─── DEBUG: Policy Arrow Overlay ───────────────────")]
        [Tooltip(
            "WHAT ARE THESE ARROWS?\n\n" +
            "After running Bellman Value Iteration (the AI solver), each floor cell\n" +
            "gets an arrow showing the direction the algorithm recommends moving to\n" +
            "maximise long-term reward (reach the Exit while collecting treasure).\n\n" +
            "This is a DEBUGGING TOOL — the arrows visualise the AI's computed\n" +
            "policy π*(s) = argmax V*(s').  They do NOT control the player.\n" +
            "The player is always controlled by WASD / arrow keys.\n\n" +
            "Assign a second child Tilemap here, then call ShowPolicyOverlay(true)\n" +
            "to reveal them, or leave hidden during normal gameplay.")]
        public Tilemap OverlayTilemap;
        public TileBase ArrowUp;
        public TileBase ArrowDown;
        public TileBase ArrowLeft;
        public TileBase ArrowRight;

        // -------------------------------------------------------
        // Value Heatmap Colours
        // -------------------------------------------------------
        [Header("Value Heatmap Gradient  (used by debug overlay)")]
        public Color LowValueColour = new Color(0.75f, 0.08f, 0.08f);   // red
        public Color MidValueColour = new Color(0.08f, 0.08f, 0.18f);   // dark
        public Color HighValueColour = new Color(0.08f, 0.85f, 0.25f);   // green
        public float cameraSize = 8f;

        // -------------------------------------------------------
        // Private state
        // -------------------------------------------------------
        private Tilemap _tilemap;
        private DungeonMap _currentMap;
        private Dictionary<TileType, TileBase> _assetLookup;

        // -------------------------------------------------------
        // Unity lifecycle
        // -------------------------------------------------------
        private void Awake()
        {
            _tilemap = GetComponent<Tilemap>();
            BuildLookup();

            // Policy overlay hidden by default — it's a debug tool
            if (OverlayTilemap != null)
                OverlayTilemap.gameObject.SetActive(false);
        }

        // -------------------------------------------------------
        // Map rendering
        // -------------------------------------------------------

        /// <summary>
        /// Clear and re-render the entire dungeon tilemap.
        /// Call this after generating a new dungeon.
        /// </summary>
        public void RenderMap(DungeonMap map)
        {
            _currentMap = map;
            _tilemap.ClearAllTiles();

            // Batch-build arrays for a single SetTiles call (much faster)
            int count = map.Width * map.Height;
            var positions = new Vector3Int[count];
            var tiles = new TileBase[count];
            int idx = 0;

            for (int x = 0; x < map.Width; x++)
                for (int y = 0; y < map.Height; y++)
                {
                    positions[idx] = new Vector3Int(x, y, 0);
                    tiles[idx] = GetAsset(map.Tiles[x, y]);
                    idx++;
                }

            _tilemap.SetTiles(positions, tiles);

            // Position camera at entrance at comfortable zoom
            FocusCameraOnEntrance(map);
        }

        // -------------------------------------------------------
        // Live tile refresh  (after player interaction)
        // -------------------------------------------------------

        /// <summary>
        /// Redraw a single tile.  Call this after the player collects
        /// treasure, defeats an enemy, or springs a trap so the tile
        /// visually updates immediately.
        /// </summary>
        public void RefreshTile(DungeonMap map, int x, int y)
        {
            if (!map.InBounds(x, y)) return;
            _tilemap.SetTile(new Vector3Int(x, y, 0), GetAsset(map.Tiles[x, y]));
        }

        // -------------------------------------------------------
        // Value heatmap overlay
        // -------------------------------------------------------

        /// <summary>
        /// Tint each passable tile with a colour representing V*(s).
        /// Red = low value, dark = neutral, green = high value.
        /// Safe to call every Bellman iteration for live animation.
        /// </summary>
        public void RenderValueOverlay(DungeonMap map, BellmanResult result)
        {
            float range = result.MaxValue - result.MinValue;
            if (Mathf.Approximately(range, 0f)) range = 1f;

            for (int x = 0; x < map.Width; x++)
                for (int y = 0; y < map.Height; y++)
                {
                    var pos = new Vector3Int(x, y, 0);
                    _tilemap.SetTileFlags(pos, TileFlags.None);

                    if (!TileRewards.IsPassable(map.Tiles[x, y]))
                    {
                        _tilemap.SetColor(pos, Color.white);
                        continue;
                    }

                    float t = (result.Values[x, y] - result.MinValue) / range;
                    Color col = t < 0.5f
                        ? Color.Lerp(LowValueColour, MidValueColour, t * 2f)
                        : Color.Lerp(MidValueColour, HighValueColour, (t - 0.5f) * 2f);

                    _tilemap.SetColor(pos, col);
                }
        }

        /// <summary>Reset all tile tints to white (removes value overlay).</summary>
        public void ClearValueOverlay(DungeonMap map)
        {
            for (int x = 0; x < map.Width; x++)
                for (int y = 0; y < map.Height; y++)
                {
                    var pos = new Vector3Int(x, y, 0);
                    _tilemap.SetTileFlags(pos, TileFlags.None);
                    _tilemap.SetColor(pos, Color.white);
                }
        }

        // -------------------------------------------------------
        // Policy arrow overlay  (DEBUG ONLY)
        // -------------------------------------------------------

        /// <summary>
        /// Populate the overlay tilemap with directional arrows representing
        /// the Bellman greedy policy π*(s).
        ///
        /// Each arrow points in the direction the AI solver recommends
        /// moving from that cell to maximise discounted future reward.
        ///
        /// The overlay is HIDDEN by default.  Call ShowPolicyOverlay(true)
        /// to reveal it as a debug aid.  It does not affect the player.
        /// </summary>
        public void RenderPolicyOverlay(DungeonMap map, BellmanResult result)
        {
            if (OverlayTilemap == null)
            {
                Debug.LogWarning("[DungeonRenderer] OverlayTilemap is not assigned. " +
                                 "Create a second child Tilemap and assign it in the Inspector " +
                                 "to see policy arrows.");
                return;
            }

            OverlayTilemap.ClearAllTiles();

            for (int x = 0; x < map.Width; x++)
                for (int y = 0; y < map.Height; y++)
                {
                    if (!TileRewards.IsPassable(map.Tiles[x, y])) continue;

                    Vector2Int target = result.Policy[x, y];
                    if (target.x < 0) continue;   // isolated / no policy

                    TileBase arrow = GetArrowTile(target - new Vector2Int(x, y));
                    if (arrow != null)
                        OverlayTilemap.SetTile(new Vector3Int(x, y, 0), arrow);
                }
        }

        /// <summary>
        /// Show or hide the policy arrow overlay.
        ///   true  → reveal (debug mode)
        ///   false → hide  (normal gameplay)
        /// </summary>
        public void ShowPolicyOverlay(bool visible)
        {
            if (OverlayTilemap != null)
                OverlayTilemap.gameObject.SetActive(visible);
        }

        /// <summary>Wipe the policy overlay tiles and hide the layer.</summary>
        public void ClearPolicyOverlay()
        {
            OverlayTilemap?.ClearAllTiles();
            ShowPolicyOverlay(false);
        }

        // -------------------------------------------------------
        // Camera — entrance-focused, NOT full-dungeon zoom
        // -------------------------------------------------------

        /// <summary>
        /// Position the camera over the entrance at a sensible zoom level.
        ///
        /// NOTE: This is called once on map generation.  After that,
        /// PlayerController.FollowCamera() takes over and keeps the camera
        /// centred on the player at the zoom level set by CameraViewTiles.
        /// </summary>
        public void FocusCameraOnEntrance(DungeonMap map)
        {
            if (Camera.main == null) return;

            // Snap to entrance cell centre
            Camera.main.transform.position = new Vector3(
                map.EntranceCell.x + 0.5f,
                map.EntranceCell.y + 0.5f,
                Camera.main.transform.position.z);

            // Show roughly one room at a comfortable size.
            // PlayerController will adjust this as the player moves.
            Camera.main.orthographicSize = cameraSize;
        }

        // -------------------------------------------------------
        // Helpers
        // -------------------------------------------------------
        private void BuildLookup()
        {
            _assetLookup = new Dictionary<TileType, TileBase>
            {
                { TileType.Wall,      WallTile     },
                { TileType.Floor,     FloorTile    },
                { TileType.Door,      DoorTile     },
                { TileType.Treasure,  TreasureTile },
                { TileType.Enemy,     EnemyTile    },
                { TileType.Entrance,  EntranceTile },
                { TileType.Exit,      ExitTile     },
                { TileType.Trap,      TrapTile     },
                { TileType.Corridor,  CorridorTile },
            };
        }

        private TileBase GetAsset(TileType t) =>
            _assetLookup.TryGetValue(t, out var tile) ? tile : WallTile;

        private TileBase GetArrowTile(Vector2Int dir)
        {
            if (dir == Vector2Int.up) return ArrowUp;
            if (dir == Vector2Int.down) return ArrowDown;
            if (dir == Vector2Int.left) return ArrowLeft;
            if (dir == Vector2Int.right) return ArrowRight;
            return null;
        }
    }
}