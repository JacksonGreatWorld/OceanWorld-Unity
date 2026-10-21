using UnityEngine;

namespace SwanUnderwater
{
    /// <summary>Routes one reusable source without changing its gain, clip, playback time or original mute.</summary>
    [ExecuteAlways, AddComponentMenu("")]
    public sealed class OceanAudioRoute : MonoBehaviour
    {
        AudioSource source;
        OceanAudioCategory category;
        bool originalMute, subscribed;
        public AudioSource Source => source;

        internal void Configure(AudioSource audioSource, OceanAudioCategory audioCategory)
        {
            if (source != audioSource)
            {
                if (source != null) source.mute = originalMute;
                source = audioSource;
                originalMute = source.mute;
            }
            category = audioCategory;
            // Register is explicit: it must work immediately during EditMode validation too.
            if (isActiveAndEnabled) Subscribe();
            Apply();
        }

        void OnEnable()
        {
            Subscribe();
            Apply();
        }

        void OnDisable() => Unsubscribe();
        void OnDestroy() => Unsubscribe();

        void Subscribe()
        {
            if (subscribed) return;
            OceanAudioPreferences.Changed += OnPreferenceChanged;
            subscribed = true;
        }

        void Unsubscribe()
        {
            if (!subscribed) return;
            OceanAudioPreferences.Changed -= OnPreferenceChanged;
            subscribed = false;
        }

        void OnPreferenceChanged(OceanAudioCategory changedCategory, bool muted)
        {
            if (source == null) { Unsubscribe(); return; }
            if (changedCategory == category) source.mute = originalMute || muted;
        }

        void Apply()
        {
            if (source != null) source.mute = originalMute || OceanAudioPreferences.IsMuted(category);
        }
    }
}
