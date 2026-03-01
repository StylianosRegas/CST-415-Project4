// ============================================================
// PlayerController.cs
//
// Human-controlled player.  Moves tile-by-tile on WASD / arrows.
//
// WHAT THIS SCRIPT DOES
// ─────────────────────
// • Reads keyboard input every frame (Update)
// • Validates the target tile against the DungeonMap
// • Blocks on walls, fires events for treasure/enemy/trap/exit
// • Smoothly lerps the sprite between tiles (MoveCoroutine)
// • Follows the camera at a fixed zoom level
//
// WHAT THIS SCRIPT DOES NOT DO
// ─────────────────────────────
// • It does NOT know about GameMode.  DungeonManager calls
//   SetInputEnabled(false/true) when the mode changes if
//   player movement should be suppressed.  By default input
//   is always on — the design intent is that the player CAN
//   still move in Debug mode (to explore while watching the
//   value overlay update).
//
// • It does NOT know about Bellman or policy arrows.  Those
//   are entirely in DungeonManager / DungeonRenderer.
//
// ATTACHMENT
// ──────────
// PlayerSetup.cs creates the Player GameObject and adds this
// component automatically.  You do not need to add it manually.
// If you prefer to set up the Player yourself in the scene,
// add this script to a GameObject that also has SpriteRenderer.
// ============================================================
using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace DungeonForge
{
    public class PlayerController : MonoBehaviour
    {
        // -------------------------------------------------------
        // Inspector
        // -------------------------------------------------------
        [Header("Movement")]
        [Tooltip("Seconds to slide between tiles. Lower = snappier.")]
        public float MoveTime = 0.12f;

        [Tooltip("Minimum time between moves when holding a key (seconds).")]
        public float KeyRepeatDelay = 0.10f;

        [Header("Stats")]
        public int MaxHP = 100;
        public int EnemyDamage = 15;
        public int TrapDamage = 10;
        public int TreasureGold = 20;

        [Header("Visual Feedback")]
        [Tooltip("Auto-assigned by PlayerSetup. Drag your SpriteRenderer here if setting up manually.")]
        public SpriteRenderer PlayerSprite;
        public Color NormalColour = Color.white;
        public Color DamageColour = new Color(1f, 0.2f, 0.2f);
        public Color TreasureColour = new Color(1f, 0.9f, 0.2f);
        public Color ExitColour = new Color(0.2f, 1f, 0.9f);

        [Header("Camera")]
        [Tooltip("Assigned automatically via Camera.main. Drag a specific camera here to override.")]
        public Camera GameCamera;

        [Tooltip("How many tiles tall the viewport shows. Lower = more zoomed in.")]
        [Range(5f, 40f)]
        public float CameraViewTiles = 14f;

        [Tooltip("How fast the camera chases the player. Higher = snappier.")]
        public float CameraFollowSpeed = 8f;

        // -------------------------------------------------------
        // Events  (wire in Inspector or DungeonManager.WirePlayerEvents)
        // -------------------------------------------------------
        [Header("Events")]
        public UnityEvent<int, int> OnHPChanged;        // (currentHP, maxHP)
        public UnityEvent<int> OnGoldChanged;      // (totalGold)
        public UnityEvent<int> OnKillCountChanged; // (totalKills)
        public UnityEvent OnPlayerDied;
        public UnityEvent OnExitReached;
        public UnityEvent<string> OnTileEntered;      // description of the tile entered

        // -------------------------------------------------------
        // Public read-only state
        // -------------------------------------------------------
        public int CurrentHP { get; private set; }
        public int Gold { get; private set; }
        public int Kills { get; private set; }
        public int TreasureCount { get; private set; }
        public int Steps { get; private set; }
        public Vector2Int Cell { get; private set; }
        public bool IsAlive { get; private set; } = true;
        public bool HasWon { get; private set; }

        // -------------------------------------------------------
        // Private state
        // -------------------------------------------------------
        private DungeonMap _map;
        private bool _isMoving;
        private bool _isInputEnabled = true;   // see SetInputEnabled()
        private float _lastMoveTime;

        // -------------------------------------------------------
        // Unity lifecycle
        // -------------------------------------------------------
        private void Awake()
        {
            CurrentHP = MaxHP;
            if (GameCamera == null)
                GameCamera = Camera.main;
        }

        private void Update()
        {
            // Nothing to do until a map is loaded
            if (_map == null) return;

            // Input gate — DungeonManager can disable this
            if (_isInputEnabled && IsAlive && !HasWon)
                HandleInput();

            FollowCamera();
        }

        // -------------------------------------------------------
        // Public API — called by DungeonManager
        // -------------------------------------------------------

        /// <summary>
        /// Reset the player to the entrance of a new dungeon.
        /// Call this every time GenerateDungeon() produces a new map.
        /// </summary>
        public void Initialise(DungeonMap map)
        {
            _map = map;
            Cell = map.EntranceCell;
            CurrentHP = MaxHP;
            Gold = 0;
            Kills = 0;
            TreasureCount = 0;
            Steps = 0;
            IsAlive = true;
            HasWon = false;
            _isMoving = false;

            StopAllCoroutines();
            transform.position = CellToWorld(Cell);
            SnapCameraToPlayer();

            if (PlayerSprite) PlayerSprite.color = NormalColour;

            // Refresh HUD with starting values
            OnHPChanged?.Invoke(CurrentHP, MaxHP);
            OnGoldChanged?.Invoke(Gold);
            OnKillCountChanged?.Invoke(Kills);
            OnTileEntered?.Invoke("Entrance — find the Exit!");
        }

        /// <summary>
        /// Enable or disable keyboard movement.
        /// DungeonManager calls this — for example, if you wanted to
        /// freeze the player while a cutscene or win screen is shown.
        /// Currently the player CAN move in Debug mode (by design —
        /// exploring while watching the value heatmap is useful).
        /// </summary>
        public void SetInputEnabled(bool enabled)
        {
            _isInputEnabled = enabled;
        }

        // -------------------------------------------------------
        // Input
        // -------------------------------------------------------
        private void HandleInput()
        {
            if (_isMoving) return;
            if (Time.time - _lastMoveTime < KeyRepeatDelay) return;

            Vector2Int dir = Vector2Int.zero;

            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) dir = Vector2Int.up;
            else if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) dir = Vector2Int.down;
            else if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) dir = Vector2Int.left;
            else if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) dir = Vector2Int.right;

            if (dir != Vector2Int.zero)
                TryMove(dir);
        }

        // -------------------------------------------------------
        // Movement + collision + tile interactions
        // -------------------------------------------------------
        private void TryMove(Vector2Int dir)
        {
            Vector2Int target = Cell + dir;

            if (!_map.InBounds(target.x, target.y)) return;

            TileType tile = _map.Tiles[target.x, target.y];

            // Hard block on walls
            if (tile == TileType.Wall) return;

            // Commit the move
            _lastMoveTime = Time.time;
            Vector2Int from = Cell;
            Cell = target;
            Steps++;

            StartCoroutine(MoveCoroutine(from, target));
            ProcessTileInteraction(target, tile);
        }

        private void ProcessTileInteraction(Vector2Int cell, TileType tile)
        {
            switch (tile)
            {
                case TileType.Treasure:
                    Gold += TreasureGold;
                    TreasureCount++;
                    _map.Tiles[cell.x, cell.y] = TileType.Floor;   // collected
                    OnGoldChanged?.Invoke(Gold);
                    OnTileEntered?.Invoke($"Treasure! +{TreasureGold} Gold");
                    StartCoroutine(FlashColour(TreasureColour, 0.18f));
                    break;

                case TileType.Enemy:
                    TakeDamage(EnemyDamage);
                    _map.Tiles[cell.x, cell.y] = TileType.Floor;   // enemy defeated
                    Kills++;
                    OnKillCountChanged?.Invoke(Kills);
                    OnTileEntered?.Invoke($"Enemy! −{EnemyDamage} HP");
                    break;

                case TileType.Trap:
                    TakeDamage(TrapDamage);
                    _map.Tiles[cell.x, cell.y] = TileType.Floor;   // trap sprung
                    OnTileEntered?.Invoke($"Trap! −{TrapDamage} HP");
                    break;

                case TileType.Exit:
                    HasWon = true;
                    StartCoroutine(FlashColour(ExitColour, 0.5f));
                    OnTileEntered?.Invoke("EXIT — Victory!");
                    OnExitReached?.Invoke();
                    break;

                case TileType.Entrance:
                    OnTileEntered?.Invoke("Entrance");
                    break;

                    // Floor, Door, Corridor — no event, just movement
            }
        }

        private void TakeDamage(int amount)
        {
            CurrentHP = Mathf.Max(0, CurrentHP - amount);
            OnHPChanged?.Invoke(CurrentHP, MaxHP);
            StartCoroutine(FlashColour(DamageColour, 0.22f));

            if (CurrentHP <= 0)
            {
                IsAlive = false;
                if (PlayerSprite) PlayerSprite.color = DamageColour;
                OnPlayerDied?.Invoke();
            }
        }

        // -------------------------------------------------------
        // Smooth tile-to-tile slide
        // -------------------------------------------------------
        private IEnumerator MoveCoroutine(Vector2Int from, Vector2Int to)
        {
            _isMoving = true;

            Vector3 start = CellToWorld(from);
            Vector3 end = CellToWorld(to);
            float elapsed = 0f;

            while (elapsed < MoveTime)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / MoveTime);
                t = t * t * (3f - 2f * t);   // smoothstep easing
                transform.position = Vector3.Lerp(start, end, t);
                yield return null;
            }

            transform.position = end;
            _isMoving = false;
        }

        // -------------------------------------------------------
        // Visual colour flash (damage, treasure, exit)
        // -------------------------------------------------------
        private IEnumerator FlashColour(Color flashTo, float duration)
        {
            if (PlayerSprite == null) yield break;
            PlayerSprite.color = flashTo;
            yield return new WaitForSeconds(duration);
            if (PlayerSprite != null)
                PlayerSprite.color = IsAlive ? NormalColour : DamageColour;
        }

        // -------------------------------------------------------
        // Camera — centred on player, smooth follow
        // -------------------------------------------------------
        private void FollowCamera()
        {
            if (GameCamera == null) return;

            float targetSize = CameraViewTiles * 0.5f;
            GameCamera.orthographicSize = Mathf.Lerp(
                GameCamera.orthographicSize, targetSize, Time.deltaTime * 5f);

            Vector3 target = new Vector3(
                transform.position.x,
                transform.position.y,
                GameCamera.transform.position.z);

            GameCamera.transform.position = Vector3.Lerp(
                GameCamera.transform.position, target,
                Time.deltaTime * CameraFollowSpeed);
        }

        private void SnapCameraToPlayer()
        {
            if (GameCamera == null) return;
            GameCamera.orthographicSize = CameraViewTiles * 0.5f;
            GameCamera.transform.position = new Vector3(
                transform.position.x,
                transform.position.y,
                GameCamera.transform.position.z);
        }

        // -------------------------------------------------------
        // Helpers
        // -------------------------------------------------------
        private static Vector3 CellToWorld(Vector2Int cell) =>
            new Vector3(cell.x + 0.5f, cell.y + 0.5f, 0f);
    }
}