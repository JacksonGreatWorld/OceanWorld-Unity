using UnityEngine;

namespace SwanUnderwater
{
    // One versioned source of runtime settings; scene/Inspector legacy values do not override it.
    [System.Serializable]
    public sealed class ReefExperienceSettings
    {
        public string version = "codex-v37";
        public int heroFish = 72;
        public int schoolingFish = 1000;
        public int schools = 10;
        public int largeFish = 4;
        public float largeFishDistance = 24f;
        public float requestedEyeScale = 1.0f;

        public static ReefExperienceSettings Load()
        {
            var asset = Resources.Load<TextAsset>("ReefExperience");
            var result = asset != null ? JsonUtility.FromJson<ReefExperienceSettings>(asset.text) : new ReefExperienceSettings();
            result.heroFish = Mathf.Clamp(result.heroFish, 1, 150);
            result.schoolingFish = Mathf.Clamp(result.schoolingFish, 10, 1500);
            result.schools = Mathf.Clamp(result.schools, 1, Mathf.Min(20, result.schoolingFish));
            result.largeFish = Mathf.Clamp(result.largeFish, 1, 6);
            result.largeFishDistance = Mathf.Clamp(result.largeFishDistance, 16f, 50f);
            result.requestedEyeScale = Mathf.Clamp(result.requestedEyeScale, 0.8f, 1.5f);
            Debug.Log("[Experience] " + JsonUtility.ToJson(result));
            return result;
        }
    }
}
