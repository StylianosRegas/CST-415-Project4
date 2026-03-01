// ============================================================
// PlayerSetup.cs
//
// Guarantees the Player and AIAgent GameObjects exist and have
// the right components before DungeonManager.Start() runs.
//
// WHY THIS EXISTS
// ───────────────
// PlayerController is a MonoBehaviour — it must live on a
// GameObject in the scene.  Without this script, a developer
// would have to manually:
//   • Create a Player GameObject
//   • Add SpriteRenderer
//   • Add PlayerController
//   • Draw/import a sprite asset
//   • Drag the reference into DungeonManager's Inspector slot
//
// PlayerSetup does all of that automatically in Awake().
// The game is fully playable out of the box with no art assets.
//
// HOW TO USE
// ──────────
// Add this component to the same GameObject as DungeonManager.
// That's it.  On first Play, it creates the Player and AIAgent
// GameObjects, generates coloured square sprites procedurally,
// and registers them with DungeonManager.
//
// To use your own sprites: just replace the SpriteRenderer.sprite
// on the Player or AIAgent GameObject at any time.  PlayerSetup
// only creates sprites when none exist.
//
// EXECUTION ORDER
// ───────────────
// Unity calls Awake() on all MonoBehaviours before any Start().
// PlayerSetup.Awake() → creates Player/AIAgent GameObjects
// DungeonManager.Start() → Player reference is already set
//
// NOTE: There is no [RequireComponent] between PlayerSetup and
// DungeonManager to avoid a circular dependency.  You must add
// both components manually (or they can live on separate objects).
// ============================================================
using UnityEngine;

namespace DungeonForge
{
    public class PlayerSetup : MonoBehaviour
    {
        // -------------------------------------------------------
        // Inspector
        // -------------------------------------------------------
        [Header("Player")]
        [Tooltip("Colour of the auto-generated player sprite. " +
                 "Ignored if you assign your own sprite to the Player's SpriteRenderer.")]
        public Color PlayerColour    = new Color(0.85f, 0.92f, 1.00f);
        public int   PlayerPixelSize = 16;

        [Header("AI Agent  (debug only — hidden in Play mode)")]
        public Color AgentColour    = new Color(0.30f, 0.90f, 1.00f);
        public int   AgentPixelSize = 14;

        [Header("Sprite Sorting")]
        [Tooltip("Sorting layer for Player and Agent sprites. " +
                 "Create a layer called 'Characters' in Tags & Layers, or leave as Default.")]
        public string SortingLayerName  = "Default";
        public int    PlayerSortingOrder = 10;
        public int    AgentSortingOrder  = 9;

        // -------------------------------------------------------
        // Awake — runs before DungeonManager.Start()
        // -------------------------------------------------------
        private void Awake()
        {
            var manager = GetComponent<DungeonManager>();
            if (manager == null)
            {
                Debug.LogError("[PlayerSetup] DungeonManager not found on this GameObject. " +
                               "Add DungeonManager to the same GameObject as PlayerSetup.");
                return;
            }

            EnsurePlayer(manager);
            EnsureAIAgent(manager);
        }

        // -------------------------------------------------------
        // Player setup
        // -------------------------------------------------------
        private void EnsurePlayer(DungeonManager manager)
        {
            if (manager.Player != null)
            {
                // Developer dragged in their own Player — just make sure
                // it has a SpriteRenderer so PlayerController can colour it
                EnsureSpriteRenderer(manager.Player.gameObject,
                                     PlayerColour, PlayerPixelSize, PlayerSortingOrder);

                // Make sure PlayerController.PlayerSprite is wired
                if (manager.Player.PlayerSprite == null)
                    manager.Player.PlayerSprite =
                        manager.Player.GetComponent<SpriteRenderer>();

                Debug.Log("[PlayerSetup] Using existing Player reference.");
                return;
            }

            // No Player assigned — create one from scratch
            var go = new GameObject("Player");
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite            = MakeSquareSprite(PlayerColour, PlayerPixelSize);
            sr.sortingLayerName  = SortingLayerName;
            sr.sortingOrder      = PlayerSortingOrder;

            var pc          = go.AddComponent<PlayerController>();
            pc.PlayerSprite = sr;
            // PlayerController.GameCamera auto-finds Camera.main in its own Awake()

            manager.Player = pc;
            Debug.Log("[PlayerSetup] Created Player GameObject with procedural sprite.");
        }

        // -------------------------------------------------------
        // AI Agent setup
        // -------------------------------------------------------
        private void EnsureAIAgent(DungeonManager manager)
        {
            if (manager.AIAgent != null)
            {
                EnsureSpriteRenderer(manager.AIAgent.gameObject,
                                     AgentColour, AgentPixelSize, AgentSortingOrder);

                if (manager.AIAgent.AgentSprite == null)
                    manager.AIAgent.AgentSprite =
                        manager.AIAgent.GetComponent<SpriteRenderer>();

                // Start hidden — DungeonManager shows it when entering Debug mode
                manager.AIAgent.gameObject.SetActive(false);
                Debug.Log("[PlayerSetup] Using existing AIAgent reference.");
                return;
            }

            // Create AI agent — give it a rotated square (looks like a diamond)
            // so it's visually distinct from the player
            var go = new GameObject("AIAgent_Debug");
            go.transform.rotation = Quaternion.Euler(0f, 0f, 45f);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite            = MakeSquareSprite(AgentColour, AgentPixelSize);
            sr.sortingLayerName  = SortingLayerName;
            sr.sortingOrder      = AgentSortingOrder;

            var ac          = go.AddComponent<AgentController>();
            ac.AgentSprite  = sr;

            manager.AIAgent = ac;

            // Hidden by default — EnterDebugMode() shows it
            go.SetActive(false);
            Debug.Log("[PlayerSetup] Created AIAgent_Debug GameObject (hidden).");
        }

        // -------------------------------------------------------
        // Add a SpriteRenderer to an existing GameObject if it
        // doesn't already have one.
        // -------------------------------------------------------
        private void EnsureSpriteRenderer(GameObject go, Color colour,
                                          int pixelSize, int sortOrder)
        {
            if (go.GetComponent<SpriteRenderer>() != null) return;

            var sr               = go.AddComponent<SpriteRenderer>();
            sr.sprite            = MakeSquareSprite(colour, pixelSize);
            sr.sortingLayerName  = SortingLayerName;
            sr.sortingOrder      = sortOrder;
            Debug.Log($"[PlayerSetup] Added procedural SpriteRenderer to {go.name}.");
        }

        // -------------------------------------------------------
        // Generates a solid-colour square Texture2D and wraps it
        // in a Sprite.  Works with zero art assets.
        // pixelsPerUnit == pixelSize means the sprite is exactly
        // 1 Unity unit wide — same as one dungeon tile.
        // -------------------------------------------------------
        private static Sprite MakeSquareSprite(Color colour, int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false)
            {
                filterMode = FilterMode.Point,
                wrapMode   = TextureWrapMode.Clamp
            };

            var pixels = new Color[size * size];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = colour;

            tex.SetPixels(pixels);
            tex.Apply();

            return Sprite.Create(
                tex,
                new Rect(0, 0, size, size),
                pivot:          new Vector2(0.5f, 0.5f),
                pixelsPerUnit:  size   // 1 world unit = 1 tile = sprite width
            );
        }
    }
}
