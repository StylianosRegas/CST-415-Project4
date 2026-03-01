// ============================================================
// DungeonManager.cs
//
// Central orchestrator.  Owns the GameMode state machine and
// wires every other system together.
//
// HOW PLAY vs DEBUG MODE ACTUALLY WORKS
// ──────────────────────────────────────
// The mode is stored as a GameMode enum in CurrentMode.
// SetMode(GameMode) is the ONLY place it changes — everything
// else reads CurrentMode to decide what to do.
//
//  ┌─────────────────────────────────────────────────────┐
//  │  GameMode.Play  (default)                           │
//  │  ─────────────────────────────────────────────────  │
//  │  • Player input: ON (PlayerController.Update runs)  │
//  │  • Bellman: BLOCKED (SolveBellman returns early)    │
//  │  • AI agent GameObject: SetActive(false) — hidden   │
//  │  • Value/policy overlays: cleared from tilemap      │
//  │  • DebugPanel: SetActive(false) — hidden            │
//  └─────────────────────────────────────────────────────┘
//
//  ┌─────────────────────────────────────────────────────┐
//  │  GameMode.Debug                                     │
//  │  ─────────────────────────────────────────────────  │
//  │  • Player input: ON (player can still explore)      │
//  │  • Bellman: allowed — press B or click Solve        │
//  │  • AI agent GameObject: SetActive(true) — visible   │
//  │  • Value overlay: shown when ShowValuesToggle is on │
//  │  • Policy arrows: shown when ShowPolicyToggle is on │
//  │  • DebugPanel: SetActive(true) — visible            │
//  └─────────────────────────────────────────────────────┘
//
// HOW THE PLAYER IS CONNECTED
// ────────────────────────────
// PlayerSetup.Awake() runs BEFORE DungeonManager.Start().
// It creates the Player and AIAgent GameObjects (or uses ones
// you've dragged in) and assigns them to manager.Player and
// manager.AIAgent.  By the time Start() runs here, both fields
// are guaranteed to be non-null (or an error is logged if
// PlayerSetup itself is missing).
//
// So the answer to "how does the player run?" is:
//   1. Add DungeonManager to an empty GameObject.
//   2. Add PlayerSetup to the same GameObject.
//   3. Press Play — PlayerSetup.Awake() fires, creates the
//      Player GameObject with SpriteRenderer + PlayerController,
//      then DungeonManager.Start() finds it and calls Initialise.
// ============================================================
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace DungeonForge
{
    // =========================================================
    // GameMode — single source of truth for the game's state.
    // Check CurrentMode everywhere; never use a separate bool.
    // =========================================================
    public enum GameMode
    {
        Play,   // Normal gameplay.  Bellman is off, AI agent hidden.
        Debug   // Developer view.  Bellman + overlays + AI agent.
    }

    public class DungeonManager : MonoBehaviour
    {
        // ═══════════════════════════════════════════════════════
        // Core Systems
        // Player and AIAgent are auto-filled by PlayerSetup.Awake().
        // You can also drag your own into these slots.
        // ═══════════════════════════════════════════════════════
        [Header("Core Systems  (auto-filled by PlayerSetup)")]
        public DungeonRenderer Renderer;
        public PlayerController Player;
        public AgentController AIAgent;

        // ═══════════════════════════════════════════════════════
        // Dungeon Settings
        // ═══════════════════════════════════════════════════════
        [Header("Dungeon Generation")]
        [Range(5, 25)] public int DungeonDepth = 10;
        [Range(7, 19)] public int RoomSize = 13;
        [Range(1, 2)] public int ChainOrder = 2;
        [Range(0f, 1f)] public float EnemyRate = 0.30f;
        [Range(0f, 1f)] public float TreasureRate = 0.15f;

        // ═══════════════════════════════════════════════════════
        // Bellman Settings  (used only in GameMode.Debug)
        // ═══════════════════════════════════════════════════════
        [Header("Bellman  (Debug mode only)")]
        [Range(0.01f, 0.99f)] public float Gamma = 0.85f;
        [Range(5, 500)] public int MaxIterations = 100;
        [Range(0.0001f, 0.5f)] public float Epsilon = 0.01f;
        public bool AnimateValueIteration = true;

        // ═══════════════════════════════════════════════════════
        // Starting mode
        // ═══════════════════════════════════════════════════════
        [Header("Mode")]
        public GameMode StartingMode = GameMode.Play;

        // ═══════════════════════════════════════════════════════
        // Play HUD  (all optional — game runs without any UI)
        // ═══════════════════════════════════════════════════════
        [Header("Play HUD  (optional)")]
        public Slider HPBar;
        public TextMeshProUGUI HPText;
        public TextMeshProUGUI GoldText;
        public TextMeshProUGUI KillsText;
        public TextMeshProUGUI StepsText;
        public TextMeshProUGUI EventText;
        public GameObject WinScreen;
        public GameObject LossScreen;
        public TextMeshProUGUI WinStatsText;
        public TextMeshProUGUI LossStatsText;

        // ═══════════════════════════════════════════════════════
        // Debug Panel  (shown only in GameMode.Debug)
        // ═══════════════════════════════════════════════════════
        [Header("Debug Panel  (optional — hidden in Play mode)")]
        public GameObject DebugPanel;
        public Button SolveButton;
        public Button AINavigateButton;
        public Toggle ShowValuesToggle;
        public Toggle ShowPolicyToggle;
        public Slider GammaSlider;
        public TextMeshProUGUI IterationText;
        public TextMeshProUGUI DeltaText;
        public TextMeshProUGUI ConvergedText;
        public TextMeshProUGUI MaxVText;
        public TextMeshProUGUI MinVText;

        // ═══════════════════════════════════════════════════════
        // Shared UI  (optional)
        // ═══════════════════════════════════════════════════════
        [Header("Shared UI  (optional)")]
        public Button GenerateButton;
        public Button ToggleModeButton;
        public TextMeshProUGUI ModeLabel;   // displays "PLAY" or "DEBUG"
        public TextMeshProUGUI InfoText;

        // ═══════════════════════════════════════════════════════
        // Public state
        // ═══════════════════════════════════════════════════════

        /// <summary>The current game mode.  Read this anywhere that needs to know.</summary>
        public GameMode CurrentMode { get; private set; }

        // ═══════════════════════════════════════════════════════
        // Private
        // ═══════════════════════════════════════════════════════
        private DungeonMap _map;
        private BellmanResult _result;
        private bool _isSolving;

        private readonly MarkovChainGenerator _markov = new MarkovChainGenerator();
        private readonly BellmanSolver _bellman = new BellmanSolver();

        // ═══════════════════════════════════════════════════════
        // Unity Lifecycle
        //
        // Execution order:
        //   PlayerSetup.Awake()      → creates Player + AIAgent GameObjects
        //   DungeonManager.Start()   → Player/AIAgent already set, safe to use
        // ═══════════════════════════════════════════════════════
        private void Start()
        {
            // Fail loudly if PlayerSetup didn't run or Renderer isn't wired
            if (!ValidateReferences()) return;

            WirePlayerEvents();
            WireUIButtons();

            // Apply starting mode — this shows/hides panels and sets CurrentMode
            SetMode(StartingMode);

            // Generate the first dungeon
            GenerateDungeon();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.G) || Input.GetKeyDown(KeyCode.F5))
                GenerateDungeon();

            // Tab toggles between Play and Debug
            if (Input.GetKeyDown(KeyCode.Tab))
                SetMode(CurrentMode == GameMode.Play ? GameMode.Debug : GameMode.Play);

            // Keyboard shortcuts that only work in Debug mode
            if (CurrentMode == GameMode.Debug)
            {
                if (Input.GetKeyDown(KeyCode.B) && !_isSolving)
                    SolveBellman();

                if (Input.GetKeyDown(KeyCode.P))
                    TogglePolicyArrows();
            }
        }

        // ═══════════════════════════════════════════════════════
        // MODE SWITCHING
        //
        // This is the ONLY method that changes CurrentMode.
        // Every behaviour difference between modes is triggered
        // from here — nothing else sets the mode directly.
        // ═══════════════════════════════════════════════════════
        public void SetMode(GameMode newMode)
        {
            CurrentMode = newMode;

            if (newMode == GameMode.Play)
                ApplyPlayMode();
            else
                ApplyDebugMode();

            if (ModeLabel)
                ModeLabel.text = newMode == GameMode.Play ? "● PLAY" : "⬡ DEBUG";

            Log($"[Mode] {newMode}");
        }

        // -------------------------------------------------------
        // What changes when entering Play mode:
        // -------------------------------------------------------
        private void ApplyPlayMode()
        {
            // 1. Hide the debug panel
            if (DebugPanel) DebugPanel.SetActive(false);

            // 2. Hide the AI agent — it only appears in Debug mode
            if (AIAgent != null)
                AIAgent.gameObject.SetActive(false);

            // 3. Clear Bellman overlays from the tilemap so the player
            //    sees clean tiles, not a leftover heatmap
            if (_map != null)
            {
                Renderer?.ClearValueOverlay(_map);
                Renderer?.ClearPolicyOverlay();
            }

            // 4. Player input is always on — PlayerController.HandleInput()
            //    runs every frame as long as _isInputEnabled is true.
            //    We leave it enabled here.  To freeze the player (e.g. on
            //    win/loss screens), call Player.SetInputEnabled(false).
        }

        // -------------------------------------------------------
        // What changes when entering Debug mode:
        // -------------------------------------------------------
        private void ApplyDebugMode()
        {
            // 1. Show the debug panel
            if (DebugPanel) DebugPanel.SetActive(true);

            // 2. Show the AI agent
            if (AIAgent != null)
                AIAgent.gameObject.SetActive(true);

            // 3. Re-apply overlays if we already have a Bellman result
            if (_map != null && _result != null)
            {
                if (ShowValuesToggle == null || ShowValuesToggle.isOn)
                    Renderer?.RenderValueOverlay(_map, _result);

                bool showArrows = ShowPolicyToggle != null && ShowPolicyToggle.isOn;
                Renderer?.ShowPolicyOverlay(showArrows);
            }

            // 4. Update button interactability based on current state
            SetSolveInteractable(!_isSolving && _map != null);
            SetAINavInteractable(_result != null);

            // 5. Player input stays ON — the player can still move in Debug
        }

        // ═══════════════════════════════════════════════════════
        // DUNGEON GENERATION
        // ═══════════════════════════════════════════════════════
        public void GenerateDungeon()
        {
            StopAllCoroutines();
            _result = null;
            _isSolving = false;

            HideEndScreens();
            Renderer?.ClearPolicyOverlay();

            // Step 1 — Markov chain produces a room type sequence
            var sequence = _markov.Generate(DungeonDepth, ChainOrder);
            Log($"[Markov] {sequence.Count} rooms: {string.Join(" → ", sequence)}");

            // Step 2 — Layout builder converts sequence to a tile grid
            _map = new RoomLayoutBuilder
            {
                RoomSize = RoomSize,
                CorridorLength = 3,
                EnemyRate = EnemyRate,
                TreasureRate = TreasureRate
            }.Build(sequence);

            Log($"[Layout] {_map.Width}×{_map.Height}  " +
                $"Entrance:{_map.EntranceCell}  Exit:{_map.ExitCell}");

            // Step 3 — Render tiles (clears old value overlay first)
            Renderer?.ClearValueOverlay(_map);
            Renderer?.RenderMap(_map);

            // Step 4 — Place player at entrance
            // Player was created by PlayerSetup.Awake() and is guaranteed
            // non-null at this point (ValidateReferences checked in Start).
            Player.Initialise(_map);

            // Step 5 — Reset debug UI readouts
            ResetDebugUI();
            SetSolveInteractable(true);
            SetAINavInteractable(false);
        }

        // ═══════════════════════════════════════════════════════
        // BELLMAN VALUE ITERATION  (GameMode.Debug only)
        //
        // SolveBellman() returns immediately if CurrentMode != Debug.
        // This is the code enforcement of the mode boundary —
        // not just a UI thing.
        // ═══════════════════════════════════════════════════════
        public void SolveBellman()
        {
            if (CurrentMode != GameMode.Debug)
            {
                Log("[Bellman] Switch to Debug mode first (press Tab).");
                return;
            }
            if (_map == null || _isSolving) return;

            if (AnimateValueIteration)
                StartCoroutine(SolveBellmanCoroutine());
            else
            {
                _result = _bellman.Solve(_map, Gamma, MaxIterations, Epsilon);
                OnSolveComplete(_result);
            }
        }

        private IEnumerator SolveBellmanCoroutine()
        {
            _isSolving = true;
            SetSolveInteractable(false);
            Log($"[Bellman] γ={Gamma:F2}  ε={Epsilon}  maxIter={MaxIterations}");

            yield return StartCoroutine(
                _bellman.SolveCoroutine(
                    _map, Gamma, MaxIterations, Epsilon,

                    // Per-iteration callback — live heatmap update
                    (values, iter, delta) =>
                    {
                        // If user switched back to Play mid-solve, stop updating overlays
                        if (CurrentMode != GameMode.Debug) return;

                        if (ShowValuesToggle == null || ShowValuesToggle.isOn)
                        {
                            Renderer?.RenderValueOverlay(_map, new BellmanResult
                            {
                                Values = values,
                                MaxValue = MaxOfPassable(values, _map),
                                MinValue = MinOfPassable(values, _map)
                            });
                        }
                        if (IterationText) IterationText.text = iter.ToString();
                        if (DeltaText) DeltaText.text = delta.ToString("F5");
                    },

                    // Completion callback
                    r =>
                    {
                        _result = r;
                        _isSolving = false;
                        OnSolveComplete(r);
                    }
                )
            );
        }

        private void OnSolveComplete(BellmanResult r)
        {
            Log($"[Bellman] Done — {r.IterationsRun} iters  " +
                $"Δ={r.FinalDelta:F5}  converged={r.Converged}");

            if (CurrentMode == GameMode.Debug)
            {
                if (ShowValuesToggle == null || ShowValuesToggle.isOn)
                    Renderer?.RenderValueOverlay(_map, r);

                // Pre-render policy arrows into the overlay tilemap.
                // ShowPolicyOverlay controls whether that tilemap is visible.
                Renderer?.RenderPolicyOverlay(_map, r);
                Renderer?.ShowPolicyOverlay(ShowPolicyToggle != null && ShowPolicyToggle.isOn);
            }

            if (IterationText) IterationText.text = r.IterationsRun.ToString();
            if (DeltaText) DeltaText.text = r.FinalDelta.ToString("F5");
            if (ConvergedText) ConvergedText.text = r.Converged ? "YES ✓" : "NO";
            if (MaxVText) MaxVText.text = r.MaxValue.ToString("F2");
            if (MinVText) MinVText.text = r.MinValue.ToString("F2");

            AIAgent?.Initialise(_map, r);
            SetSolveInteractable(true);
            SetAINavInteractable(true);
        }

        // ═══════════════════════════════════════════════════════
        // AI NAVIGATION  (GameMode.Debug only)
        // ═══════════════════════════════════════════════════════
        public void StartAINavigation()
        {
            if (CurrentMode != GameMode.Debug)
            {
                Log("[AI] Switch to Debug mode first (Tab).");
                return;
            }
            if (_result == null)
            {
                Log("[AI] Run Value Iteration first (B).");
                return;
            }
            AIAgent?.Initialise(_map, _result);
            AIAgent?.AnimatePath();
        }

        // ═══════════════════════════════════════════════════════
        // PLAYER EVENTS  (called via UnityEvents from PlayerController)
        // ═══════════════════════════════════════════════════════
        private void OnPlayerHPChanged(int current, int max)
        {
            if (HPBar) HPBar.value = (float)current / max;
            if (HPText) HPText.text = $"{current} / {max}";
        }

        private void OnPlayerGoldChanged(int gold)
        {
            if (GoldText) GoldText.text = gold.ToString();
        }

        private void OnPlayerKillsChanged(int kills)
        {
            if (KillsText) KillsText.text = kills.ToString();
        }

        private void OnPlayerTileEntered(string msg)
        {
            if (EventText) EventText.text = msg;
            if (StepsText) StepsText.text = Player.Steps.ToString();

            // Refresh the tile the player just stepped on — it may have
            // changed type (e.g. Treasure → Floor after collection)
            if (_map != null)
                Renderer?.RefreshTile(_map, Player.Cell.x, Player.Cell.y);
        }

        private void OnPlayerDied()
        {
            Player.SetInputEnabled(false);   // freeze player on death
            if (LossScreen) LossScreen.SetActive(true);
            if (LossStatsText != null)
                LossStatsText.text =
                    $"Steps: {Player.Steps}   Gold: {Player.Gold}   Kills: {Player.Kills}";
        }

        private void OnExitReached()
        {
            Player.SetInputEnabled(false);   // freeze player on win
            if (WinScreen) WinScreen.SetActive(true);
            if (WinStatsText != null)
                WinStatsText.text =
                    $"Steps: {Player.Steps}   Gold: {Player.Gold}   " +
                    $"Kills: {Player.Kills}   Treasures: {Player.TreasureCount}";
        }

        // ═══════════════════════════════════════════════════════
        // WIRING — called once from Start()
        // ═══════════════════════════════════════════════════════
        private void WirePlayerEvents()
        {
            if (Player == null) return;
            Player.OnHPChanged.AddListener(OnPlayerHPChanged);
            Player.OnGoldChanged.AddListener(OnPlayerGoldChanged);
            Player.OnKillCountChanged.AddListener(OnPlayerKillsChanged);
            Player.OnTileEntered.AddListener(OnPlayerTileEntered);
            Player.OnPlayerDied.AddListener(OnPlayerDied);
            Player.OnExitReached.AddListener(OnExitReached);
        }

        private void WireUIButtons()
        {
            GenerateButton?.onClick.AddListener(GenerateDungeon);

            ToggleModeButton?.onClick.AddListener(() =>
                SetMode(CurrentMode == GameMode.Play ? GameMode.Debug : GameMode.Play));

            SolveButton?.onClick.AddListener(SolveBellman);
            AINavigateButton?.onClick.AddListener(StartAINavigation);
            GammaSlider?.onValueChanged.AddListener(v => Gamma = v);

            ShowValuesToggle?.onValueChanged.AddListener(show =>
            {
                if (CurrentMode != GameMode.Debug || _map == null) return;
                if (show && _result != null) Renderer?.RenderValueOverlay(_map, _result);
                else Renderer?.ClearValueOverlay(_map);
            });

            ShowPolicyToggle?.onValueChanged.AddListener(show =>
            {
                if (CurrentMode != GameMode.Debug) return;
                if (show && _result == null)
                {
                    Log("[Debug] Run Value Iteration first to generate a policy.");
                    ShowPolicyToggle.SetIsOnWithoutNotify(false);
                    return;
                }
                Renderer?.ShowPolicyOverlay(show);
                if (show) Log("[Debug] Policy arrows = AI's best move per cell. NOT player controls.");
            });
        }

        // ═══════════════════════════════════════════════════════
        // HELPERS
        // ═══════════════════════════════════════════════════════
        private void TogglePolicyArrows()
        {
            if (ShowPolicyToggle != null)
                ShowPolicyToggle.isOn = !ShowPolicyToggle.isOn;
            else if (_result != null)
                Renderer?.ShowPolicyOverlay(true);
        }

        private bool ValidateReferences()
        {
            bool ok = true;
            if (Renderer == null)
            {
                Debug.LogError("[DungeonManager] Renderer is not assigned. " +
                               "Drag your DungeonTilemap (with DungeonRenderer) into the slot.");
                ok = false;
            }
            if (Player == null)
            {
                Debug.LogError("[DungeonManager] Player is null. " +
                               "Add PlayerSetup to this GameObject — it creates the Player automatically.");
                ok = false;
            }
            return ok;
        }

        private void HideEndScreens()
        {
            if (WinScreen) WinScreen.SetActive(false);
            if (LossScreen) LossScreen.SetActive(false);

            // Re-enable player input in case they're restarting from a win/loss screen
            Player?.SetInputEnabled(true);
        }

        private void SetSolveInteractable(bool b)
        {
            if (SolveButton) SolveButton.interactable = b;
        }

        private void SetAINavInteractable(bool b)
        {
            if (AINavigateButton) AINavigateButton.interactable = b;
        }

        private void ResetDebugUI()
        {
            if (IterationText) IterationText.text = "—";
            if (DeltaText) DeltaText.text = "—";
            if (ConvergedText) ConvergedText.text = "—";
            if (MaxVText) MaxVText.text = "—";
            if (MinVText) MinVText.text = "—";
        }

        private void Log(string msg)
        {
            Debug.Log(msg);
            if (InfoText) InfoText.text = msg;
        }

        private float MaxOfPassable(float[,] V, DungeonMap map)
        {
            float m = float.MinValue;
            for (int x = 0; x < map.Width; x++)
                for (int y = 0; y < map.Height; y++)
                    if (TileRewards.IsPassable(map.Tiles[x, y]))
                        m = Mathf.Max(m, V[x, y]);
            return m;
        }

        private float MinOfPassable(float[,] V, DungeonMap map)
        {
            float m = float.MaxValue;
            for (int x = 0; x < map.Width; x++)
                for (int y = 0; y < map.Height; y++)
                    if (TileRewards.IsPassable(map.Tiles[x, y]))
                        m = Mathf.Min(m, V[x, y]);
            return m;
        }
    }
}