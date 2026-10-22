#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SwanUnderwater.EditorTests
{
    /// <summary>Disabled releases must not package a private endpoint; future enabled releases require validated deployment configuration.</summary>
    public sealed class OceanLeaderboardReleaseGuard : IPreprocessBuildWithReport
    {
        public int callbackOrder => -1000;
        public static string ConfigurationFile => Path.Combine(Application.dataPath,"Resources/PrivateRelease/OceanLeaderboardConfig.json");
        public static void ValidateReleaseConfiguration(string path)
        {
            if(!OceanReleaseFeatures.LeaderboardsEnabled)
            {
                string parent=Path.GetDirectoryName(Path.GetFullPath(path));
                if(File.Exists(path)||(Path.GetFileName(parent)=="PrivateRelease"&&Directory.Exists(parent)))
                    throw new BuildFailedException("This release disables leaderboards. Remove the ignored PrivateRelease configuration from the build input; preserve any originals outside the project. No configuration contents have been logged.");
                return;
            }
            if(!File.Exists(path))throw new BuildFailedException("Leaderboard release configuration missing. Restore the ignored PrivateRelease/OceanLeaderboardConfig.json with Scripts/configure-leaderboard.py before an Android release build.");
            try { OceanLeaderboardConfiguration.Parse(File.ReadAllText(path,new System.Text.UTF8Encoding(false,true))); }
            catch(Exception) { throw new BuildFailedException("Leaderboard release configuration is invalid. Requires an HTTPS baseUrl without credentials, query or fragment. No configuration contents have been logged."); }
        }
        public void OnPreprocessBuild(BuildReport report)
        {
            if(report.summary.platform==BuildTarget.Android&&(!OceanReleaseFeatures.LeaderboardsEnabled||(report.summary.options&BuildOptions.Development)==0))
                ValidateReleaseConfiguration(ConfigurationFile);
        }
    }
}
#endif
