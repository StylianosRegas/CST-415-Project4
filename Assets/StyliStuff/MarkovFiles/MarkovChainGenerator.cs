// ============================================================
// MarkovChainGenerator.cs
// Generates a sequence of room types using a first- or
// second-order Markov transition matrix.
//
// HOW IT WORKS
// ------------
// A Markov chain models the probability of transitioning
// from one state (room type) to another.  In 1st-order mode
// the next room depends only on the current room.  In 2nd-order
// mode it depends on the last two rooms, giving richer patterns.
//
// The Bellman Value-Iteration system later uses the resulting
// room layout to compute optimal agent behaviour.
// ============================================================
using System.Collections.Generic;
using UnityEngine;

namespace DungeonForge
{
    public class MarkovChainGenerator
    {
        // -------------------------------------------------------
        // 1st-Order transition table
        //   key   = current room type
        //   value = dictionary<next room type, probability weight>
        // -------------------------------------------------------
        private static readonly Dictionary<RoomType, Dictionary<RoomType, float>> _trans1st =
            new Dictionary<RoomType, Dictionary<RoomType, float>>
        {
            { RoomType.Entrance, new Dictionary<RoomType, float> {
                { RoomType.Corridor,  0.70f },
                { RoomType.Room,      0.20f },
                { RoomType.Shop,      0.10f } }},

            { RoomType.Corridor, new Dictionary<RoomType, float> {
                { RoomType.Room,      0.45f },
                { RoomType.Corridor,  0.25f },
                { RoomType.DeadEnd,   0.10f },
                { RoomType.Trap,      0.12f },
                { RoomType.Treasure,  0.08f } }},

            { RoomType.Room, new Dictionary<RoomType, float> {
                { RoomType.Corridor,  0.40f },
                { RoomType.Boss,      0.08f },
                { RoomType.Shop,      0.15f },
                { RoomType.Trap,      0.17f },
                { RoomType.Treasure,  0.10f },
                { RoomType.DeadEnd,   0.10f } }},

            { RoomType.Shop, new Dictionary<RoomType, float> {
                { RoomType.Corridor,  0.75f },
                { RoomType.Room,      0.25f } }},

            { RoomType.Trap, new Dictionary<RoomType, float> {
                { RoomType.Corridor,  0.55f },
                { RoomType.Room,      0.30f },
                { RoomType.DeadEnd,   0.15f } }},

            { RoomType.Treasure, new Dictionary<RoomType, float> {
                { RoomType.Corridor,  0.60f },
                { RoomType.Boss,      0.25f },
                { RoomType.Trap,      0.15f } }},

            { RoomType.Boss, new Dictionary<RoomType, float> {
                { RoomType.Exit,      0.80f },
                { RoomType.Corridor,  0.20f } }},

            { RoomType.DeadEnd, new Dictionary<RoomType, float> {
                { RoomType.Corridor,  1.00f } }},

            { RoomType.Exit, new Dictionary<RoomType, float> {
                { RoomType.Entrance,  1.00f } }},  // loop guard
        };

        // -------------------------------------------------------
        // 2nd-Order transition table
        //   key = "prevRoom,curRoom" string
        // -------------------------------------------------------
        private static readonly Dictionary<string, Dictionary<RoomType, float>> _trans2nd =
            new Dictionary<string, Dictionary<RoomType, float>>
        {
            { "Entrance,Corridor", new Dictionary<RoomType, float> {
                { RoomType.Room,     0.50f },
                { RoomType.Corridor, 0.30f },
                { RoomType.Trap,     0.20f } }},

            { "Corridor,Room", new Dictionary<RoomType, float> {
                { RoomType.Corridor,  0.40f },
                { RoomType.Treasure,  0.20f },
                { RoomType.Trap,      0.20f },
                { RoomType.Boss,      0.10f },
                { RoomType.DeadEnd,   0.10f } }},

            { "Corridor,Corridor", new Dictionary<RoomType, float> {
                { RoomType.Room,      0.50f },
                { RoomType.DeadEnd,   0.20f },
                { RoomType.Trap,      0.15f },
                { RoomType.Shop,      0.15f } }},

            { "Room,Corridor", new Dictionary<RoomType, float> {
                { RoomType.Room,      0.30f },
                { RoomType.Corridor,  0.30f },
                { RoomType.Treasure,  0.20f },
                { RoomType.Trap,      0.10f },
                { RoomType.Boss,      0.10f } }},

            { "Room,Treasure", new Dictionary<RoomType, float> {
                { RoomType.Corridor,  0.50f },
                { RoomType.Boss,      0.30f },
                { RoomType.Trap,      0.20f } }},

            { "Room,Trap", new Dictionary<RoomType, float> {
                { RoomType.Corridor,  0.60f },
                { RoomType.Room,      0.30f },
                { RoomType.DeadEnd,   0.10f } }},

            { "Treasure,Corridor", new Dictionary<RoomType, float> {
                { RoomType.Boss,      0.40f },
                { RoomType.Room,      0.40f },
                { RoomType.Trap,      0.20f } }},
        };

        // Fallback probabilities when no specific 2nd-order rule matches
        private static readonly Dictionary<RoomType, float> _defaultProbs =
            new Dictionary<RoomType, float>
        {
            { RoomType.Corridor,  0.50f },
            { RoomType.Room,      0.30f },
            { RoomType.Trap,      0.10f },
            { RoomType.DeadEnd,   0.10f },
        };

        // -------------------------------------------------------
        // Public API
        // -------------------------------------------------------

        /// <summary>
        /// Generate a room-type sequence of the requested depth using
        /// a first- or second-order Markov chain.
        /// The sequence always starts at Entrance and ends at Exit,
        /// with a Boss room immediately before the Exit.
        /// </summary>
        /// <param name="depth">Target number of rooms (Boss + Exit are appended automatically)</param>
        /// <param name="order">1 or 2 (chain order)</param>
        public List<RoomType> Generate(int depth, int order = 1)
        {
            var sequence = new List<RoomType> { RoomType.Entrance };

            int safety = 0;
            while (sequence.Count < depth && safety++ < 500)
            {
                RoomType current = sequence[sequence.Count - 1];
                if (current == RoomType.Exit) break;

                RoomType next = order == 2
                    ? SampleSecondOrder(sequence)
                    : SampleFirstOrder(current);

                // Avoid premature Boss or Exit
                if ((next == RoomType.Boss || next == RoomType.Exit) &&
                    sequence.Count < depth - 2)
                    continue;

                sequence.Add(next);
            }

            // Guarantee Boss → Exit at the end
            if (sequence[sequence.Count - 1] != RoomType.Boss)
                sequence.Add(RoomType.Boss);
            sequence.Add(RoomType.Exit);

            return sequence;
        }

        // -------------------------------------------------------
        // Sampling helpers
        // -------------------------------------------------------

        private RoomType SampleFirstOrder(RoomType current)
        {
            if (!_trans1st.TryGetValue(current, out var probs))
                probs = _defaultProbs;
            return WeightedSample(probs);
        }

        private RoomType SampleSecondOrder(List<RoomType> seq)
        {
            if (seq.Count >= 2)
            {
                string key = seq[seq.Count - 2] + "," + seq[seq.Count - 1];
                if (_trans2nd.TryGetValue(key, out var probs))
                    return WeightedSample(probs);
            }
            // Fall back to 1st-order
            return SampleFirstOrder(seq[seq.Count - 1]);
        }

        private RoomType WeightedSample(Dictionary<RoomType, float> weights)
        {
            float total = 0f;
            foreach (var w in weights.Values) total += w;

            float roll = Random.Range(0f, total);
            float cumulative = 0f;

            foreach (var kvp in weights)
            {
                cumulative += kvp.Value;
                if (roll <= cumulative)
                    return kvp.Key;
            }

            // Fallback (floating-point edge case)
            foreach (var k in weights.Keys) return k;
            return RoomType.Corridor;
        }

        // -------------------------------------------------------
        // Utility: get transition probability (for debug display)
        // -------------------------------------------------------
        public float GetTransitionProbability(RoomType from, RoomType to)
        {
            if (!_trans1st.TryGetValue(from, out var probs)) return 0f;

            float total = 0f;
            foreach (var w in probs.Values) total += w;

            return probs.TryGetValue(to, out float v) ? v / total : 0f;
        }
    }
}
