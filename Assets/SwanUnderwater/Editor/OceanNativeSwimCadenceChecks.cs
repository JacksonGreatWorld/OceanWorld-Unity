#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SwanUnderwater.EditorTests
{
    /// <summary>Real native Animator and production navigation owner, with isolated time and obstacles. Never writes player state.</summary>
    public static class OceanNativeSwimCadenceChecks
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly MethodInfo Tick = typeof(OceanWildlifeResident).GetMethod("TickResident", Private);
        static readonly FieldInfo Phase = typeof(OceanWildlifeResident).GetField("phase", Private);
        static readonly FieldInfo Solids = typeof(OceanWildlifeResident).GetField("nearbySolids", Private);
        static readonly FieldInfo Context = typeof(OceanWildlifeResident).GetField("contextResolved", Private);
        static readonly List<string> issues = new List<string>();
        public static int Assertions { get; private set; }
        public static IReadOnlyList<string> Issues => issues;
        static void Check(bool value, string text) { Assertions++; if (!value) issues.Add(text); }
        static void Require(bool value, string text) { if (!value) throw new InvalidOperationException("Native cadence fixture: " + text); }

        public static void RunInScene(UnderwaterWorld world)
        {
            Assertions = 0; issues.Clear();
            Require(EditorApplication.isPlaying && world != null && world.IsReady && world.ActiveEnvironmentRoot != null,
                "requires the existing ready PlayMode world");
            Require(Tick != null && Phase != null && Solids != null && Context != null, "original production entry points exist");
            foreach (string resource in new[] { "mackerel", "sea-turtle", "dolphin" }) CheckResident(world, resource);
            if (issues.Count > 0) throw new InvalidOperationException("NATIVE_SWIM_CADENCE_FAILED assertions=" + Assertions +
                " failures=" + issues.Count + "; " + string.Join("; ", issues.GetRange(0, Mathf.Min(8, issues.Count))));
            Debug.Log("NATIVE_SWIM_CADENCE_PASS assertions=" + Assertions +
                "; actual three native Animators, production cruise/turn/block/recovery and PlayMode lifecycle; isolated synthetic time, no wearer claim.");
        }

        static void CheckResident(UnderwaterWorld world, string resource)
        {
            string path = resource == "mackerel" ? OceanSpeciesCatalog.Get(27).ResourcePath : "MarineExpansion/" + resource;
            var prefab = Resources.Load<GameObject>(path);
            var asset = Resources.Load<TextAsset>(path + "-geometry");
            Require(prefab != null && asset != null, "admitted native resource exists: " + resource);
            var geometry = JsonUtility.FromJson<OceanAuthoredFishGeometry>(asset.text);
            Require(geometry != null && geometry.IsValid, "native geometry exists: " + resource);
            GameObject model = Object.Instantiate(prefab, world.ActiveEnvironmentRoot, false);
            try
            {
                float diagonal = resource == "dolphin" ? 1.65f : resource == "sea-turtle" ? 1f : .62f;
                model.name = "Isolated native cadence " + resource;
                model.transform.localScale = Vector3.one * (diagonal / geometry.firstPoseDiagonal);
                Vector3 at = world.ActiveSpawn; at.y = UnderwaterWorld.SampleRenderedSeabedHeight(at.x, at.z) + 40f;
                model.transform.SetPositionAndRotation(at, Quaternion.identity);
                Animator animator = model.GetComponentInChildren<Animator>(true);
                SkinnedMeshRenderer skin = model.GetComponentInChildren<SkinnedMeshRenderer>(true);
                Require(animator != null && skin != null && animator.runtimeAnimatorController != null, "real native skin/controller present");
                RuntimeAnimatorController controller = animator.runtimeAnimatorController;
                Mesh mesh = skin.sharedMesh; Material[] materials = skin.sharedMaterials;
                animator.enabled = true; animator.Rebind(); animator.Update(0f);
                animator.speed = 1.17f; const float originalSpeed = 1.17f;
                bool originalRootMotion = animator.applyRootMotion;
                var resident = model.AddComponent<OceanWildlifeResident>();
                resident.Initialize(world, diagonal, 6f, 0, true);
                resident.ConfigureCruise(.5f, .18f, 0f);
                resident.ConfigureNativeBounds(geometry.NavigationBounds);
                Context.SetValue(resident, true); // This fixture does not borrow fresh player hands.
                var solids = (List<Bounds>)Solids.GetValue(resident); solids.Clear();
                model.transform.rotation = Quaternion.identity;
                Advance(resident, animator, 120, true, resource, originalSpeed);
                Check(animator.speed >= originalSpeed * .96f && animator.speed <= originalSpeed * 1.1f,
                    resource + " clear-water cruise retains authored playback cadence");

                Vector3 beforeTurn = model.transform.position;
                model.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
                Advance(resident, animator, 90, false, resource, originalSpeed);
                Check(Vector3.Distance(model.transform.position, beforeTurn) < .00001f,
                    resource + " the actual nose-first turn gate withholds translation");
                Check(animator.speed > originalSpeed * .30f && animator.speed < originalSpeed * .62f,
                    resource + " turning in place uses a gentle positive cadence instead of full-speed swimming");

                model.transform.rotation = Quaternion.identity;
                float slow = animator.speed;
                Advance(resident, animator, 90, true, resource, originalSpeed);
                Check(animator.speed > slow + originalSpeed * .25f && animator.speed > originalSpeed * .94f,
                    resource + " resumed production travel continuously restores cruising cadence");

                Vector3 blocked = model.transform.position;
                solids.Add(new Bounds(blocked, Vector3.one * 10f)); // Owned cache only: no world or collider is changed.
                Advance(resident, animator, 100, false, resource, originalSpeed);
                Check(model.transform.position == blocked, resource + " production clearance rejects actual attempted travel");
                Check(animator.speed > originalSpeed * .30f && animator.speed < originalSpeed * .62f,
                    resource + " blocked translation no longer plays a full-speed swim");

                float timeBefore = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
                resident.enabled = false; // Actual PlayMode OnDisable, rather than invoking the private callback.
                Check(Mathf.Abs(animator.speed - originalSpeed) < .00001f, resource + " disable restores the borrowed original speed");
                Check(animator.GetCurrentAnimatorStateInfo(0).normalizedTime == timeBefore,
                    resource + " disable never resets native normalized time");
                resident.enabled = true;
                solids.Clear(); model.transform.rotation = Quaternion.identity;
                Advance(resident, animator, 30, true, resource, originalSpeed);
                Check(animator.speed >= originalSpeed * .96f, resource + " enable reacquires the real original cadence");

                // Reinitialize while slowed: the previous owned multiplier must not become the new base.
                solids.Add(new Bounds(model.transform.position, Vector3.one * 10f));
                Advance(resident, animator, 100, false, resource, originalSpeed);
                timeBefore = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
                resident.Initialize(world, diagonal, 6f, 0, true); resident.ConfigureCruise(.5f, .18f, 0f);
                resident.ConfigureNativeBounds(geometry.NavigationBounds); Context.SetValue(resident, true);
                solids = (List<Bounds>)Solids.GetValue(resident); solids.Clear();
                Check(Mathf.Abs(animator.speed - originalSpeed) < .00001f, resource + " reinitialize restores original speed without multiplier drift");
                Check(animator.GetCurrentAnimatorStateInfo(0).normalizedTime == timeBefore,
                    resource + " reinitialize preserves the original animation phase");
                model.transform.rotation = Quaternion.identity;
                Advance(resident, animator, 60, true, resource, originalSpeed);
                Check(animator.speed > originalSpeed * .94f, resource + " repeated initialization still recovers the same cruise");

                // A separate writer owns its explicit override. This component must not restore over it.
                animator.speed = .91f; resident.enabled = false;
                Check(Mathf.Abs(animator.speed - .91f) < .00001f, resource + " relinquishing ownership preserves another explicit speed writer");
                resident.enabled = true; resident.enabled = false;
                Check(Mathf.Abs(animator.speed - .91f) < .00001f, resource + " reenabling then releasing preserves a newly borrowed base speed");
                RuntimeAnimatorController replacement = Object.Instantiate(controller);
                try
                {
                    resident.enabled = true;
                    solids.Add(new Bounds(model.transform.position, Vector3.one * 10f));
                    Advance(resident, animator, 100, false, resource, .91f);
                    float borrowedSpeed = animator.speed;
                    animator.runtimeAnimatorController = replacement;
                    Tick.Invoke(resident, new object[] { .02f });
                    Check(Mathf.Abs(animator.speed - borrowedSpeed) < .00001f,
                        resource + " a new controller keeps the external owner's current playback speed");
                    resident.enabled = false;
                    Check(Mathf.Abs(animator.speed - borrowedSpeed) < .00001f,
                        resource + " disable cannot restore a speed borrowed from a different controller");
                }
                finally { animator.runtimeAnimatorController = controller; Object.DestroyImmediate(replacement); }
                Check(animator.runtimeAnimatorController == controller && skin.sharedMesh == mesh && animator.applyRootMotion == originalRootMotion,
                    resource + " clip/controller/skin/root-motion configuration is untouched");
                Material[] after = skin.sharedMaterials;
                Check(after.Length == materials.Length, resource + " native material roster is unchanged");
                for (int i = 0; i < after.Length; i++) Check(after[i] == materials[i], resource + " native material identity remains unchanged");
            }
            finally { Object.DestroyImmediate(model); }
        }

        static void Advance(OceanWildlifeResident resident, Animator animator, int frames, bool traveling, string resource, float baseSpeed)
        {
            for (int frame = 0; frame < frames; frame++)
            {
                Vector3 before = resident.transform.position;
                Quaternion heading = resident.transform.rotation;
                float playback = animator.speed;
                AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
                Phase.SetValue(resident, -.02f * .18f); // Fix one existing orbit goal; actual steering/clearance still decides travel.
                Tick.Invoke(resident, new object[] { .02f });
                Vector3 delta = resident.transform.position - before;
                Check(!traveling || delta.sqrMagnitude > 1e-10f, resource + " clear stage exercises actual successful production travel");
                Check(Quaternion.Angle(heading, resident.transform.rotation) <= .561f, resource + " navigation turn-rate remains unchanged");
                if (delta.sqrMagnitude > 1e-12f)
                    Check(Vector3.Dot(delta.normalized, resident.transform.forward) > .999f, resource + " production translation stays nose-first");
                Check(Mathf.Abs(animator.speed - playback) <= baseSpeed * .13f,
                    resource + " cadence changes smoothly instead of snapping or restarting");
                Check(animator.speed > baseSpeed * .30f && animator.speed <= baseSpeed * 1.11f,
                    resource + " cadence stays in a positive bounded range");
                animator.Update(.02f);
                AnimatorStateInfo next = animator.GetCurrentAnimatorStateInfo(0);
                Check(next.fullPathHash == state.fullPathHash && next.normalizedTime >= state.normalizedTime - .00001f,
                    resource + " native state and phase remain continuous across cadence changes");
                Check(next.normalizedTime - state.normalizedTime < .15f,
                    resource + " native phase cannot jump forward when movement resumes");
            }
        }
    }
}
#endif
