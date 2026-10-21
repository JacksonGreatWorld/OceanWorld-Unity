#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEngine;

namespace SwanUnderwater.EditorTests
{
    /// <summary>Optional read-only check of an explicitly supplied local device-save backup.</summary>
    public static class OceanInstalledSaveChecks
    {
        public static void RunIfRequested()
        {
            string[] args=Environment.GetCommandLineArgs();
            int index=Array.IndexOf(args,"-codexExistingSaveDir");
            if(index<0) return;
            string directory=Path.GetFullPath(args[index+1]);
            string file=Path.Combine(directory,"ocean-campaign.json");
            byte[] original=File.ReadAllBytes(file);
            var raw=OceanLeaderboardJson.Object(OceanLeaderboardJson.Parse(System.Text.Encoding.UTF8.GetString(original)));
            if(!OceanSaveStore.TryLoad(directory,out OceanSaveData loaded,out string status))
                throw new InvalidOperationException("Installed save compatibility: "+status);
            var oldStars=OceanLeaderboardJson.Array(raw,"bestStars");
            bool retained=loaded.bestStars.Length==OceanLevelCatalog.Count;
            for(int i=0;i<oldStars.Count;i++) retained &= loaded.bestStars[i]==Convert.ToInt32(oldStars[i]);
            retained &= loaded.shellMask==OceanLeaderboardJson.Number(raw,"shellMask") && loaded.fishMask==OceanLeaderboardJson.Number(raw,"fishMask");
            if(raw.TryGetValue("currentRun",out object active)&&active!=null)
            {
                var run=OceanLeaderboardJson.Object(active);
                retained &= loaded.currentRun.level==OceanLeaderboardJson.Number(run,"level") &&
                    loaded.currentRun.shellMask==OceanLeaderboardJson.Number(run,"shellMask") &&
                    loaded.currentRun.fishMask==OceanLeaderboardJson.Number(run,"fishMask") &&
                    loaded.currentRun.spearShotsFired==OceanLeaderboardJson.Number(run,"spearShotsFired");
            }
            if(!retained||!File.ReadAllBytes(file).SequenceEqual(original)||!OceanCampaignState.Validate(loaded,out string error))
                throw new InvalidOperationException("Installed save compatibility did not retain authenticated progress.");
            using(var hash=SHA256.Create())
                Debug.Log("INSTALLED_SAVE_COMPATIBILITY_PASS oldVersion="+OceanLeaderboardJson.Number(raw,"version")+
                    " newVersion="+loaded.version+" oldStarSlots="+oldStars.Count+" newStarSlots="+loaded.bestStars.Length+
                    " sourceUnmodified=true sha256="+BitConverter.ToString(hash.ComputeHash(original)).Replace("-","").ToLowerInvariant());
        }
    }
}
#endif
