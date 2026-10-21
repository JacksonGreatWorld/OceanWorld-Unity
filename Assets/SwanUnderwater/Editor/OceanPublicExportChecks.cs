#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace SwanUnderwater.EditorTests
{
    /// <summary>Public-edition contract: real temporary durable ledgers, no synthetic ACK or global leaderboard.</summary>
    public static class OceanPublicExportChecks
    {
        public static int Assertions { get; private set; }
        static void Check(bool condition, string reason)
        { Assertions++; if (!condition) throw new InvalidOperationException("Public offline edition: " + reason); }

        [MenuItem("OceanWorld/Validation/Public offline edition")]
        public static void RunAll()
        {
            Assertions = 0;
            string directory = Path.Combine(Path.GetTempPath(), "ocean-public-contract-" + Guid.NewGuid().ToString("N"));
            GameObject firstHost = null, coldHost = null;
            try
            {
                firstHost = new GameObject("Isolated public offline contract");
                var client = firstHost.AddComponent<OceanLeaderboardClient>();
                bool rejected = false;
                try { client.Initialize("https://example.invalid"); } catch (ArgumentException) { rejected = true; }
                Check(rejected && !client.Ready, "a supplied endpoint must be rejected before any initialization");
                client.Initialize(storageDirectory: directory);
                Check(client.Ready && client.OfflineOnly && !client.IsBusy && client.DeviceId.Length == 64,
                    "fresh real local ledgers become ready without transport or a signer");
                Check(client.EnsureSession(), "a local session is durably saved");
                string run = Guid.NewGuid().ToString("N");
                Check(client.BeginRun(run, 3), "an isolated local run is durably saved");
                Check(client.RecordShell(run, 0, 300) && client.RecordFish(run, 0, 10000) && client.RecordCoin(run, 20000),
                    "actual shell/fish/coin transactions persist");
                Check(client.LocalScore == 13 && client.LocalShellTotal == 1 && client.LocalFishTotal == 1 && client.LocalCoinTotal == 1,
                    "real local score follows one/two/ten rules");
                Check(client.RecordShell(run, 0, 300) && client.LocalScore == 13, "a duplicate pickup does not inflate the score");
                Check(client.RecordDiscovery(0, 3) && client.LocalDiscoveryCount == 1, "real observation fact is retained");
                Check(client.SetNickname("珊瑚旅人") && client.Nickname == "珊瑚旅人" && client.NicknameStatus == "local",
                    "Chinese nickname is saved locally without claiming global uniqueness or server approval");
                client.RefreshPage();
                Check(!client.CachedPageIsLive && client.CachedPage.nextCursor == "" && client.CachedPage.entries.Length == 1 &&
                    client.CachedPage.entries[0].score == 13 && client.CachedPage.serverTime == "本机记录",
                    "the displayed page contains only this installation's real record");
                client.RefreshPage(discovery: true);
                Check(client.CachedPage.IsDiscovery && client.CachedPage.entries[0].discoveryCount == 1,
                    "discovery page uses the actual local observation count");
                coldHost = new GameObject("Cold public offline contract");
                var cold = coldHost.AddComponent<OceanLeaderboardClient>(); cold.Initialize(storageDirectory: directory);
                Check(cold.Ready && cold.DeviceId == client.DeviceId && cold.LocalScore == 13 && cold.LocalDiscoveryCount == 1 && cold.Nickname == "珊瑚旅人",
                    "cold reload retains exact identity, counters, nickname and observation facts");
                Check(cold.EnsureSession() && cold.LocalDemoStarts == client.LocalDemoStarts, "the same process session is not counted twice");
                foreach (FieldInfo field in typeof(OceanLeaderboardClient).GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
                    Check(!field.FieldType.FullName.Contains("UnityEngine.Networking") && !typeof(IEnumerator).IsAssignableFrom(field.FieldType),
                        "client cannot retain a web request or coroutine transport: " + field.Name);
                Check(typeof(OceanLeaderboardClient).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic) == null,
                    "there is no background upload update loop");
                Check(typeof(OceanLeaderboardProtocol).GetField("ProductionBaseUrl") == null, "no production endpoint is exported");
                Debug.Log("OceanPublicExportChecks PASS " + Assertions + " assertions; actual temporary local ledgers; no network, player data or headset execution.");
            }
            finally
            {
                if (coldHost != null) UnityEngine.Object.DestroyImmediate(coldHost);
                if (firstHost != null) UnityEngine.Object.DestroyImmediate(firstHost);
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }
    }
}
#endif
