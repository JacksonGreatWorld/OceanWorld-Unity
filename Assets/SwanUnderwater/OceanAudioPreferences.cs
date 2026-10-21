using System;
using UnityEngine;

namespace SwanUnderwater
{
    public enum OceanAudioCategory { Environment, Music, Feedback }

    /// <summary>Independent user mute choices. Master volume remains owned by the comfort settings.</summary>
    public static class OceanAudioPreferences
    {
        public static event Action<OceanAudioCategory, bool> Changed;

        public static bool IsMuted(OceanAudioCategory category) => PlayerPrefs.GetInt(PreferenceKey(category), 0) == 1;

        public static void SetMuted(OceanAudioCategory category, bool muted)
        {
            string key = PreferenceKey(category);
            if (IsMuted(category) == muted) return;
            PlayerPrefs.SetInt(key, muted ? 1 : 0);
            PlayerPrefs.Save();
            Changed?.Invoke(category, muted);
        }

        public static bool ToggleMute(OceanAudioCategory category)
        {
            bool muted = !IsMuted(category);
            SetMuted(category, muted);
            return muted;
        }

        public static void Register(AudioSource source, OceanAudioCategory category)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            PreferenceKey(category); // Reject invalid categories before adding a component.
            foreach (var route in source.GetComponents<OceanAudioRoute>())
                if (route.Source == source) { route.Configure(source, category); return; }
            source.gameObject.AddComponent<OceanAudioRoute>().Configure(source, category);
        }

        internal static string PreferenceKey(OceanAudioCategory category)
        {
            switch (category)
            {
                case OceanAudioCategory.Environment: return "ocean_comfort_audio_environment_mute_v1";
                case OceanAudioCategory.Music: return "ocean_comfort_audio_music_mute_v1";
                case OceanAudioCategory.Feedback: return "ocean_comfort_audio_feedback_mute_v1";
                default: throw new ArgumentOutOfRangeException(nameof(category));
            }
        }
    }
}
