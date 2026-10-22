using System;
using UnityEngine;

namespace SwanUnderwater
{
    /// <summary>Release endpoint is deployment configuration, never a production constant in source.</summary>
    public static class OceanLeaderboardConfiguration
    {
        public const string ResourcePath = "PrivateRelease/OceanLeaderboardConfig";
        public static string Resolve(string explicitBaseUrl = null)
        {
            if(!OceanReleaseFeatures.LeaderboardsEnabled)return null;
            if(explicitBaseUrl!=null) return OceanLeaderboardProtocol.ValidateBaseUrl(explicitBaseUrl);
            var asset=Resources.Load<TextAsset>(ResourcePath);
            return asset==null?null:Parse(asset.text);
        }
        public static string Parse(string json)
        {
            if(string.IsNullOrEmpty(json)||json.Length>8192)throw new ArgumentException("Invalid leaderboard release configuration size.");
            var values=OceanLeaderboardJson.Object(OceanLeaderboardJson.Parse(json));
            if(values.Count!=1||!values.ContainsKey("baseUrl"))
                throw new ArgumentException("Leaderboard release configuration accepts only baseUrl; no account credentials or signing secrets.");
            return OceanLeaderboardProtocol.ValidateBaseUrl(OceanLeaderboardJson.Text(values,"baseUrl"));
        }
    }
}
