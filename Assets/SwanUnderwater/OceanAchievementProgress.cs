using System;
using System.Collections.Generic;

namespace SwanUnderwater
{
    /// <summary>Only authenticated, durable histories enter achievement evaluation. Counts are conservative lower bounds.</summary>
    public static class OceanAchievementProgress
    {
        public static OceanAchievementFacts FromSavedHistory(OceanSaveData history, int observedSpecies,
            long shellTotal = 0, long fishTotal = 0, long coinTotal = 0)
        {
            var facts = new OceanAchievementFacts { ObservedSpecies = Math.Min(OceanSpeciesCatalog.AvailableCount, Math.Max(0, observedSpecies)),
                ObservedTarget = OceanSpeciesCatalog.AvailableCount };
            if (history != null)
            {
                facts.ShellSpecies = Bits(history.shellMask & 1023);
                facts.FishSpecies = Bits(history.fishMask & 1023);
                facts.SharkCaught = history.shark;
                var environments = new HashSet<OceanEnvironmentIdentity>();
                int mechanics = 0;
                long provenCoins = 0;
                int count = Math.Min(history.bestStars?.Length ?? 0, OceanLevelCatalog.Count);
                for (int i = 0; i < count; i++)
                {
                    int stars = history.bestStars[i];
                    if (stars != 2 && stars != 3) continue;
                    facts.CompletedLevels++; facts.TotalStars += stars;
                    environments.Add(OceanEnvironmentIdentity.ForLevel(i + 1));
                    var definition = OceanLevelCatalog.Get(i + 1);
                    if (definition.Mechanic != OceanMechanicKind.None) mechanics |= 1 << ((int)definition.Mechanic - 1);
                    if (stars == 3) { facts.ThreeStarLevels++; provenCoins += definition.CoinCount; }
                    if (i + 1 == OceanLevelCatalog.Count) facts.CampaignFinished = true;
                }
                facts.CompletedBiomes = environments.Count;
                facts.CompletedMechanics = Bits(mechanics);
                // A current-run coin may already be included in its historical three-star result.
                // Taking a maximum, rather than adding either source, never credits it twice.
                provenCoins = Math.Max(provenCoins, Bits((history.currentRun?.coinMask ?? 0) & 31));
                facts.CoinTotal = provenCoins;
            }
            facts.ShellTotal = Math.Max(Math.Max(0, shellTotal), facts.ShellSpecies);
            facts.FishTotal = Math.Max(Math.Max(0, fishTotal), facts.FishSpecies + (facts.SharkCaught ? 1 : 0));
            facts.CoinTotal = Math.Max(Math.Max(0, coinTotal), facts.CoinTotal);
            return facts;
        }

        static int Bits(int value)
        { int result = 0; while (value != 0) { result += value & 1; value >>= 1; } return result; }
    }
}
