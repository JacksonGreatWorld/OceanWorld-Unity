using System;
using UnityEngine;

namespace SwanUnderwater.EditorTests
{
    /// <summary>Actual source mute routing and persisted preferences; does not certify audible headset mixing.</summary>
    public static class OceanAudioPreferencesTests
    {
        public static int Assertions { get; private set; }
        static void Check(bool value, string message)
        { Assertions++; if (!value) throw new InvalidOperationException("Audio preferences: " + message); }

        public static void RunAll()
        {
            Assertions = 0;
            // Preserve exact stored values, including missing keys, on the Editor host.
            string[] keys = { "ocean_comfort_audio_environment_mute_v1", "ocean_comfort_audio_music_mute_v1", "ocean_comfort_audio_feedback_mute_v1" };
            bool[] existed = new bool[keys.Length]; int[] oldValues = new int[keys.Length];
            float masterVolume = AudioListener.volume;
            var root = new GameObject("Audio preference isolated test") { hideFlags = HideFlags.DontSave };
            Action<OceanAudioCategory, bool> changed = null;
            try
            {
                for (int i = 0; i < keys.Length; i++)
                { existed[i] = PlayerPrefs.HasKey(keys[i]); oldValues[i] = PlayerPrefs.GetInt(keys[i]); PlayerPrefs.DeleteKey(keys[i]); }
                Check(!OceanAudioPreferences.IsMuted(OceanAudioCategory.Environment) &&
                    !OceanAudioPreferences.IsMuted(OceanAudioCategory.Music) && !OceanAudioPreferences.IsMuted(OceanAudioCategory.Feedback), "missing preferences retain all existing sound");
                var environment = root.AddComponent<AudioSource>(); environment.volume = .22f;
                var music = root.AddComponent<AudioSource>(); music.volume = .55f;
                var feedback = root.AddComponent<AudioSource>(); feedback.volume = .55f;
                OceanAudioPreferences.Register(environment, OceanAudioCategory.Environment);
                OceanAudioPreferences.Register(music, OceanAudioCategory.Music);
                OceanAudioPreferences.Register(feedback, OceanAudioCategory.Feedback);
                Check(!environment.mute && !music.mute && !feedback.mute, "registration leaves all default sources audible");
                Check(root.GetComponents<OceanAudioRoute>().Length == 3, "multiple sources on the same audio root receive separate routes");
                int notifications = 0;
                changed = (category, muted) => notifications++;
                OceanAudioPreferences.Changed += changed;

                OceanAudioPreferences.SetMuted(OceanAudioCategory.Environment, true);
                Check(environment.mute && !music.mute && !feedback.mute, "environment mute is isolated from music and feedback");
                Check(PlayerPrefs.GetInt(keys[0], -1) == 1, "environment choice is persisted independently");
                OceanAudioPreferences.Register(environment, OceanAudioCategory.Environment);
                Check(root.GetComponents<OceanAudioRoute>().Length == 3, "repeat registration cannot duplicate subscriptions or routes");
                OceanAudioPreferences.SetMuted(OceanAudioCategory.Environment, true);
                Check(notifications == 1, "setting the same choice is not another change");
                Check(!OceanAudioPreferences.ToggleMute(OceanAudioCategory.Environment) && !environment.mute, "repeat registration does not capture a preference mute as the original source mute");

                Check(OceanAudioPreferences.ToggleMute(OceanAudioCategory.Music) && music.mute && !environment.mute && !feedback.mute, "music mute is isolated from environment and feedback");
                OceanAudioPreferences.SetMuted(OceanAudioCategory.Feedback, true);
                Check(feedback.mute && music.mute && !environment.mute, "feedback may mute independently while another category is muted");
                Check(PlayerPrefs.GetInt(keys[1], -1) == 1 && PlayerPrefs.GetInt(keys[2], -1) == 1, "music and feedback choices have separate persisted keys");
                Check(environment.volume == .22f && music.volume == .55f && feedback.volume == .55f && AudioListener.volume == masterVolume, "category changes preserve all source gains and the master volume");

                var later = root.AddComponent<AudioSource>();
                OceanAudioPreferences.Register(later, OceanAudioCategory.Music);
                Check(later.mute, "a newly created source reads the existing persisted category choice");
                OceanAudioPreferences.Register(later, OceanAudioCategory.Environment);
                Check(!later.mute, "rerouting one source keeps its original mute and adopts only the new category");
                var originallyMuted = root.AddComponent<AudioSource>(); originallyMuted.mute = true;
                OceanAudioPreferences.Register(originallyMuted, OceanAudioCategory.Music);
                OceanAudioPreferences.SetMuted(OceanAudioCategory.Music, false);
                Check(originallyMuted.mute && !music.mute, "category unmute preserves a source that was originally muted");

                root.SetActive(false);
                OceanAudioPreferences.SetMuted(OceanAudioCategory.Environment, true);
                Check(!environment.mute && !later.mute, "disabled routes unsubscribe until the root is enabled again");
                root.SetActive(true);
                Check(environment.mute && later.mute, "reenabling an audio root reapplies preferences changed while disabled");
                Check(AudioListener.volume == masterVolume, "disable and reenable cannot change master volume");
                var transientRoot = new GameObject("Transient audio route") { hideFlags = HideFlags.DontSave };
                transientRoot.transform.SetParent(root.transform, false);
                var transientSource = transientRoot.AddComponent<AudioSource>();
                OceanAudioPreferences.Register(transientSource, OceanAudioCategory.Environment);
                Check(transientSource.mute, "a later transient route immediately adopts the persisted choice");
                UnityEngine.Object.DestroyImmediate(transientRoot);
                OceanAudioPreferences.SetMuted(OceanAudioCategory.Environment, false);
                Check(transientSource == null && !environment.mute && !later.mute, "destroying a registered root leaves other routes responding without stale-source errors");
                var removedSource = root.AddComponent<AudioSource>();
                OceanAudioPreferences.Register(removedSource, OceanAudioCategory.Environment);
                UnityEngine.Object.DestroyImmediate(removedSource);
                OceanAudioPreferences.SetMuted(OceanAudioCategory.Environment, true);
                Check(environment.mute && later.mute, "destroying a source separately prunes its subscription without interrupting live routes");
                int routeCount = root.GetComponents<OceanAudioRoute>().Length;
                bool invalidRejected = false;
                try { OceanAudioPreferences.Register(environment, (OceanAudioCategory)99); }
                catch (ArgumentOutOfRangeException) { invalidRejected = true; }
                Check(invalidRejected && root.GetComponents<OceanAudioRoute>().Length == routeCount, "invalid category cannot create or mutate a route");
                bool missingRejected = false;
                try { OceanAudioPreferences.Register(null, OceanAudioCategory.Environment); }
                catch (ArgumentNullException) { missingRejected = true; }
                Check(missingRejected, "missing sources are rejected explicitly");
            }
            finally
            {
                if (changed != null) OceanAudioPreferences.Changed -= changed;
                UnityEngine.Object.DestroyImmediate(root);
                for (int i = 0; i < keys.Length; i++)
                {
                    // Other live routes must recover too if the suite runs after a scene was opened.
                    OceanAudioPreferences.SetMuted((OceanAudioCategory)i, existed[i] && oldValues[i] == 1);
                    if (existed[i]) PlayerPrefs.SetInt(keys[i], oldValues[i]); else PlayerPrefs.DeleteKey(keys[i]);
                }
                PlayerPrefs.Save();
            }
            Debug.Log("[OceanAudioPreferencesTests] PASS " + Assertions + " assertions; no audible headset claim.");
        }
    }
}
