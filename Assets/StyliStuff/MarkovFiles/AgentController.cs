// ============================================================
// AgentController.cs
//
// A simple agent that navigates the dungeon by following the
// greedy policy produced by Bellman value iteration.
//
// Attach to the player / enemy prefab.
// ============================================================
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DungeonForge
{
    public class AgentController : MonoBehaviour
    {
        [Header("Movement")]
        [Tooltip("World units per second the agent moves.")]
        public float MoveSpeed = 5f;

        [Tooltip("Pause between each step (seconds) when animating the path.")]
        public float StepDelay = 0.12f;

        [Header("Visual")]
        public SpriteRenderer AgentSprite;
        public Color          NormalColour = Color.white;
        public Color          OnTreasure   = Color.yellow;
        public Color          OnTrap       = Color.red;
        public Color          OnExit       = Color.cyan;

        // -------------------------------------------------------
        // State
        // -------------------------------------------------------
        private DungeonMap    _map;
        private BellmanResult _result;
        private Vector2Int    _currentCell;
        private bool          _isMoving;

        // -------------------------------------------------------
        // Public API
        // -------------------------------------------------------

        /// <summary>
        /// Initialise the agent with a solved dungeon.
        /// Places the agent at the Entrance cell.
        /// </summary>
        public void Initialise(DungeonMap map, BellmanResult result)
        {
            _map     = map;
            _result  = result;
            _currentCell = map.EntranceCell;

            transform.position = CellToWorld(_currentCell);
            UpdateColour(_currentCell);
        }

        /// <summary>
        /// Move the agent one step in the direction of the greedy policy.
        /// </summary>
        public void StepOnce()
        {
            if (_map == null || _result == null || _isMoving) return;
            if (_currentCell == _map.ExitCell) return;

            var next = _result.Policy[_currentCell.x, _currentCell.y];
            if (next.x < 0) return;   // wall or no policy

            StartCoroutine(SmoothMove(_currentCell, next));
            _currentCell = next;
        }

        /// <summary>
        /// Animate the agent following the complete policy path from
        /// Entrance to Exit.
        /// </summary>
        public void AnimatePath()
        {
            if (_map == null || _result == null || _isMoving) return;

            var solver = new BellmanSolver();
            var path   = solver.ExtractPath(_result.Policy, _map,
                                            _map.EntranceCell, _map.ExitCell);
            StartCoroutine(AnimateCoroutine(path));
        }

        /// <summary>Teleport agent back to Entrance.</summary>
        public void ResetToEntrance()
        {
            StopAllCoroutines();
            _isMoving    = false;
            _currentCell = _map?.EntranceCell ?? Vector2Int.zero;
            transform.position = CellToWorld(_currentCell);
            UpdateColour(_currentCell);
        }

        // -------------------------------------------------------
        // Coroutines
        // -------------------------------------------------------
        private IEnumerator SmoothMove(Vector2Int from, Vector2Int to)
        {
            _isMoving = true;
            Vector3 startPos = CellToWorld(from);
            Vector3 endPos   = CellToWorld(to);
            float   elapsed  = 0f;
            float   duration = 1f / MoveSpeed;

            while (elapsed < duration)
            {
                elapsed            += Time.deltaTime;
                transform.position  = Vector3.Lerp(startPos, endPos, elapsed / duration);
                yield return null;
            }

            transform.position = endPos;
            UpdateColour(to);
            _isMoving = false;
        }

        private IEnumerator AnimateCoroutine(List<Vector2Int> path)
        {
            _isMoving = true;

            for (int i = 0; i < path.Count; i++)
            {
                var from = i == 0 ? path[0] : path[i - 1];
                var to   = path[i];

                Vector3 startPos = CellToWorld(from);
                Vector3 endPos   = CellToWorld(to);
                float   elapsed  = 0f;
                float   duration = StepDelay * 0.9f;

                while (elapsed < duration)
                {
                    elapsed            += Time.deltaTime;
                    transform.position  = Vector3.Lerp(startPos, endPos, elapsed / duration);
                    yield return null;
                }

                transform.position = endPos;
                _currentCell       = to;
                UpdateColour(to);

                yield return new WaitForSeconds(StepDelay * 0.1f);

                // Stop at exit
                if (to == _map.ExitCell) break;
            }

            _isMoving = false;
        }

        // -------------------------------------------------------
        // Helpers
        // -------------------------------------------------------
        private Vector3 CellToWorld(Vector2Int cell)
        {
            // Tilemap cells are 1-unit squares; add 0.5 to centre the sprite
            return new Vector3(cell.x + 0.5f, cell.y + 0.5f, 0f);
        }

        private void UpdateColour(Vector2Int cell)
        {
            if (AgentSprite == null || _map == null) return;

            var t = _map.Tiles[cell.x, cell.y];
            AgentSprite.color = t switch
            {
                TileType.Treasure => OnTreasure,
                TileType.Trap     => OnTrap,
                TileType.Exit     => OnExit,
                TileType.Enemy    => OnTrap,
                _                 => NormalColour
            };
        }

        // -------------------------------------------------------
        // Optional: keyboard control for manual stepping
        // -------------------------------------------------------
        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Space))
                StepOnce();
        }
    }
}
