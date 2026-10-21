using System.Collections.Generic;

namespace SwanUnderwater
{
    /// <summary>One expedition's inventory. A physical pickup is consumed once; species are counted once.</summary>
    public sealed class TreasureCollectionState
    {
        public const int SpeciesCount = 5;
        public const int CoinKind = -1;
        public enum Outcome { Ignored, NewSpecies, DuplicateSpecies, HiddenCoin }
        public readonly struct Result
        {
            public readonly Outcome outcome;
            public readonly int count;
            public readonly bool completedNow;
            public Result(Outcome outcome, int count, bool completedNow = false)
            { this.outcome = outcome; this.count = count; this.completedNow = completedNow; }
        }

        readonly HashSet<int> consumed = new HashSet<int>();
        public int Mask { get; private set; }
        public int DistinctCount { get; private set; }
        public bool LevelComplete => DistinctCount == SpeciesCount;
        public bool HiddenComplete { get; private set; }
        public bool IsConsumed(int instanceId) => consumed.Contains(instanceId);

        public Result Collect(int instanceId, int kind)
        {
            if (instanceId < 0 || kind < CoinKind || kind >= SpeciesCount || consumed.Contains(instanceId))
                return new Result(Outcome.Ignored, DistinctCount);
            if (kind == CoinKind)
            {
                if (HiddenComplete) return new Result(Outcome.Ignored, DistinctCount);
                consumed.Add(instanceId);
                HiddenComplete = true;
                return new Result(Outcome.HiddenCoin, DistinctCount);
            }
            consumed.Add(instanceId);
            int bit = 1 << kind;
            if ((Mask & bit) != 0) return new Result(Outcome.DuplicateSpecies, DistinctCount);
            Mask |= bit;
            DistinctCount++;
            return new Result(Outcome.NewSpecies, DistinctCount, LevelComplete);
        }
    }
}
