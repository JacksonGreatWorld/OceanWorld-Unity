#if UNITY_EDITOR
using System;
using UnityEngine;

namespace SwanUnderwater.EditorTests
{
    public static class TreasureCollectionTests
    {
        static int assertions;
        static void Require(bool condition, string detail)
        { assertions++; if (!condition) throw new InvalidOperationException("Treasure collection: " + detail); }

        public static void RunAll()
        {
            assertions = 0;
            // Check every species order, with physical duplicates and replayed collision events.
            Permute(new[] { 0, 1, 2, 3, 4 }, 0);
            var s = new TreasureCollectionState();
            Require(s.Collect(-1, 0).outcome == TreasureCollectionState.Outcome.Ignored, "invalid instance");
            Require(s.Collect(1, 5).outcome == TreasureCollectionState.Outcome.Ignored, "invalid species");
            Require(s.Collect(1, -2).outcome == TreasureCollectionState.Outcome.Ignored, "invalid negative species");
            Require(!s.IsConsumed(1) && s.Mask == 0, "invalid events cannot consume real pickup");
            Require(s.Collect(100, -1).outcome == TreasureCollectionState.Outcome.HiddenCoin, "coin before shells");
            Require(s.DistinctCount == 0 && !s.LevelComplete, "coin cannot advance shell objective");
            Require(s.Collect(101, -1).outcome == TreasureCollectionState.Outcome.Ignored, "hidden reward once");
            Debug.Log($"TreasureCollectionTests PASS: {assertions} assertions; all 120 species orders, duplicates, replay and hidden reward independence.");
        }
        static void Permute(int[] order, int depth)
        {
            if (depth == order.Length) { Expedition(order); return; }
            for (int i = depth; i < order.Length; i++)
            {
                (order[i], order[depth]) = (order[depth], order[i]);
                Permute(order, depth + 1);
                (order[i], order[depth]) = (order[depth], order[i]);
            }
        }
        static void Expedition(int[] order)
        {
            var s = new TreasureCollectionState();
            for (int i = 0; i < order.Length; i++)
            {
                int id = i * 3;
                var first = s.Collect(id, order[i]);
                Require(first.outcome == TreasureCollectionState.Outcome.NewSpecies && first.count == i + 1, "new species count");
                Require(first.completedNow == (i == 4), "complete exactly on fifth species");
                Require(s.Collect(id, order[i]).outcome == TreasureCollectionState.Outcome.Ignored, "two hands on same item");
                var duplicate = s.Collect(id + 1, order[i]);
                Require(duplicate.outcome == TreasureCollectionState.Outcome.DuplicateSpecies && duplicate.count == i + 1, "duplicate keeps count");
                Require(!duplicate.completedNow && s.IsConsumed(id + 1), "duplicate consumed without another win");
            }
            Require(s.Mask == 31 && s.LevelComplete, "all five species");
            Require(s.Collect(90, -1).outcome == TreasureCollectionState.Outcome.HiddenCoin, "coin after level complete");
            Require(s.DistinctCount == 5 && s.Collect(90, -1).outcome == TreasureCollectionState.Outcome.Ignored, "coin replay no extra reward");
        }
    }
}
#endif
