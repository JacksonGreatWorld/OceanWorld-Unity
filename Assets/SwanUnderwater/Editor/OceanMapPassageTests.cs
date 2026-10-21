#if UNITY_EDITOR
using System;
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SwanUnderwater.EditorTests
{
    /// <summary>Environment selection, cancellation ownership and actual main-camera render submission.</summary>
    public static class OceanMapPassageTests
    {
        public static int Assertions { get; private set; }
        static void Check(bool condition, string reason)
        { Assertions++; if (!condition) throw new InvalidOperationException("Ocean map passage: " + reason); }

        [MenuItem("OceanWorld/Validation/Map passage rules and ownership")]
        public static void RunAll()
        {
            Assertions = 0;
            for (int from = 1; from <= 20; from++) for (int to = 1; to <= 20; to++)
                Check(OceanEnvironmentTransition.Required(from, to) == ((from <= 5) != (to <= 5)), "only a real environment change plays the passage: " + from + " -> " + to);
            Check(Mathf.Abs(OceanEnvironmentTransition.CoverSeconds + OceanEnvironmentTransition.RevealSeconds - 1.2f) < .0001f,
                "presentation duration stays short independently of build duration");
            Shader shader = Resources.Load<Shader>("Shaders/OceanEnvironmentTransition");
            Check(shader != null && !ShaderUtil.ShaderHasError(shader), "real Resources shader imports without errors");
            // Ordinary MonoBehaviour OnDisable is a PlayMode lifecycle event.
            // The rendered scene suite runs this check; an EditMode build gate cannot.
            if (EditorApplication.isPlaying) ExplicitOwnerCancellation();
            // Existing production transaction tests include nested prepare failure,
            // cancellation/disposal and failure during cleanup; no fake hand samples here.
            OceanEnvironmentTransitionTests.RunAll();
            Debug.Log("OceanMapPassageTests rules PASS " + Assertions + " assertions; 400 environment pairs and imported shader. Lifecycle ownership runs in PlayMode; GPU submission is checked separately in RunInScene.");
        }
        static IEnumerator HeldOperation(Action cleanup)
        { try { while (true) yield return null; } finally { cleanup(); } }
        static void ExplicitOwnerCancellation()
        {
            var host = new GameObject("Isolated map switch cancellation owner");
            var owner = host.AddComponent<OceanCampaignController>();
            bool outerDisposed = false, nestedDisposed = false;
            IEnumerator outer = HeldOperation(() => outerDisposed = true), nested = HeldOperation(() => nestedDisposed = true);
            outer.MoveNext(); nested.MoveNext();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(OceanCampaignController).GetField("levelSwitchOperation", flags).SetValue(owner, outer);
            typeof(OceanCampaignController).GetField("activeSwitchStage", flags).SetValue(owner, nested);
            try
            {
                owner.enabled = false;
                Check(outerDisposed && nestedDisposed, "owner OnDisable explicitly disposes both the active prepare stage and outer transaction");
                Check(typeof(OceanCampaignController).GetField("levelSwitchOperation", flags).GetValue(owner) == null &&
                    typeof(OceanCampaignController).GetField("activeSwitchStage", flags).GetValue(owner) == null,
                    "disabled owner cannot retain a runnable half-switch");
            }
            finally { Object.DestroyImmediate(host); }
        }

        /// <summary>Call in an isolated, actually rendering PlayMode scene; never synthesizes a render callback or headset pose.</summary>
        public static IEnumerator RunInScene(Camera view)
        {
            Check(EditorApplication.isPlaying && view != null && view.isActiveAndEnabled, "use the real running main camera");
            Transform parent = view.transform.parent;
            Vector3 localPosition = view.transform.localPosition;
            Quaternion localRotation = view.transform.localRotation;
            OceanEnvironmentTransition effect = null;
            try
            {
                effect = OceanEnvironmentTransition.Create(view);
                effect.SetPresentationForValidation(true, true);
                IEnumerator cover = effect.Cover();
                for (int frame = 0; frame < 5; frame++) { Check(cover.MoveNext(), "paused cover remains pending"); yield return null; }
                Check(effect.Opacity == 0f && !effect.OpaqueFrameSubmitted, "pause neither advances nor approves opaque cover");
                effect.SetPresentationForValidation(false, false);
                for (int frame = 0; frame < 5; frame++) { Check(cover.MoveNext(), "unfocused cover remains pending"); yield return null; }
                Check(effect.Opacity == 0f && !effect.OpaqueFrameSubmitted, "focus loss cannot be replaced by a background camera callback");
                effect.SetPresentationForValidation(false, true);
                yield return cover;
                Check(effect.Opacity == 1f && effect.OpaqueFrameSubmitted && effect.OpaqueCameraId == view.GetInstanceID(),
                    "prepare permission follows the real main-camera endCameraRendering event at full opacity");
                effect.SetPresentationForValidation(true, true);
                IEnumerator reveal = effect.Reveal();
                for (int frame = 0; frame < 5; frame++) { Check(reveal.MoveNext(), "paused reveal remains pending"); yield return null; }
                Check(effect.Opacity == 1f, "a pause cannot reveal half-built geometry");
                effect.SetPresentationForValidation(false, true);
                yield return reveal;
                Check(effect.Opacity == 0f, "resumed presentation reaches a transparent new world");
                Check(view.transform.parent == parent && view.transform.localPosition == localPosition &&
                    Quaternion.Angle(view.transform.localRotation, localRotation) < .0001f, "passage never writes the tracked camera or its parent");
                effect.Cancel("validation_completed"); Object.Destroy(effect.gameObject); effect = null;
                yield return null; yield return null;

                effect = OceanEnvironmentTransition.Create(view);
                IEnumerator cancelledCover = effect.Cover(); Check(cancelledCover.MoveNext(), "second cover starts before cancellation");
                effect.enabled = false;
                bool cancelled = false; try { cancelledCover.MoveNext(); } catch (OperationCanceledException) { cancelled = true; }
                Check(cancelled && effect.Cancelled && !effect.OpaqueFrameSubmitted && !effect.GetComponent<MeshRenderer>().enabled,
                    "OnDisable cancels pending cover, hides geometry and revokes preparation permission");
                Check(view.transform.parent == parent && view.transform.localPosition == localPosition,
                    "cancelled presentation also preserves tracked camera ownership");
            }
            finally
            {
                if (effect != null) { effect.Cancel("validation_cleanup"); Object.Destroy(effect.gameObject); }
            }
            Debug.Log("OceanMapPassageTests actual-camera PASS " + Assertions + " assertions; pause/focus gating, real opaque submission, reveal and disable cancellation. No tracked-hand, compositor-present, device comfort or VST claim.");
        }
    }
}
#endif
