#if UNITY_EDITOR
using System;
using System.Collections;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SwanUnderwater.EditorTests
{
    /// <summary>Bounded real-mesh lifecycle checks; synthetic travel is not headset acceptance.</summary>
    public static class SwimFlowVfxChecks
    {
        static int assertions;
        static void Check(bool value, string message)
        {
            assertions++;
            if (!value) throw new InvalidOperationException("SwimFlowVfxChecks: " + message);
        }

        public static int RunAll()
        {
            assertions = 0;
            var owner = new GameObject("Isolated swimming-water validation");
            var cameraHost = new GameObject("Isolated swimming-water camera");
            cameraHost.transform.SetParent(owner.transform, false);
            cameraHost.transform.localPosition = new Vector3(.2f, 1.6f, .1f);
            var camera = cameraHost.AddComponent<Camera>();
            camera.enabled = false;
            var hands = owner.AddComponent<HandSwimController>();
            hands.enabled = false; // The test never starts/replaces the real tracking source.
            try
            {
                var flow = SwimFlowVfx.Install(hands, camera);
                Check(flow != null && flow.HasRenderResources, "resource shader, mesh and owner exist");
                Check(SwimFlowVfx.Install(hands, camera) == flow && owner.GetComponents<SwimFlowVfx>().Length == 1,
                    "repeated installation has one owner and one field");
                Check(flow.VisibleRibbonCount == 0 && flow.CurrentIntensity == 0f, "initial view is clear");
                Check(SwimFlowVfx.EvaluateMotionIntensity(0f, 1f) == 0f,
                    "stroke gesture without actual displacement cannot fabricate water speed");
                Check(SwimFlowVfx.EvaluateMotionIntensity(float.NaN, 1f) == 0f &&
                    SwimFlowVfx.EvaluateMotionIntensity(float.PositiveInfinity, 1f) == 0f, "invalid travel is rejected");
                Check(SwimFlowVfx.EvaluateMotionIntensity(1.2f, 0f) > .1f,
                    "real glide retains flow between strokes");
                Check(SwimFlowVfx.EvaluateMotionIntensity(2.5f, 1f) <= 1f, "intensity has a finite upper bound");

                Matrix4x4 prior = owner.transform.localToWorldMatrix;
                cameraHost.transform.localPosition += new Vector3(.12f, -.05f, .08f);
                Check(SwimFlowVfx.MeasureRigTravelForValidation(prior, owner.transform, camera.transform.position).sqrMagnitude < .00000001f,
                    "physical head movement without rig locomotion is not swimming flow");
                Vector3 pivot = camera.transform.position;
                owner.transform.RotateAround(pivot, Vector3.up, 30f);
                Check(SwimFlowVfx.MeasureRigTravelForValidation(prior, owner.transform, camera.transform.position).sqrMagnitude < .00000001f,
                    "a snap turn about the tracked eye does not fake translation");
                prior = owner.transform.localToWorldMatrix;
                owner.transform.position += new Vector3(.01f, .005f, .03f);
                Check(Vector3.Distance(SwimFlowVfx.MeasureRigTravelForValidation(prior, owner.transform, camera.transform.position),
                    new Vector3(.01f, .005f, .03f)) < .00001f, "travel follows actual world axes including vertical motion");

                owner.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                for (int i = 0; i < 90; i++)
                {
                    owner.transform.position += Vector3.forward * (1.6f / 60f);
                    flow.SimulateForValidation(Vector3.forward * 1.6f, .8f, 1f / 60f);
                }
                Check(flow.CurrentIntensity > .5f && flow.VisibleRibbonCount > 0 && flow.VisibleRibbonCount <= 44,
                    "forward travel creates a visible bounded peripheral field");
                var renderer = flow.GetComponentInChildren<MeshRenderer>();
                var filter = flow.GetComponentInChildren<MeshFilter>();
                Check(renderer != null && renderer.enabled && filter != null,
                    "travel produces an actual renderer and populated mesh");
                Check(filter.sharedMesh.vertexCount == 792 && filter.sharedMesh.triangles.Length / 3 == 704,
                    "one mesh has the agreed fixed geometry budget");
                Check(renderer.sharedMaterials.Length == 1 && renderer.sharedMaterial.passCount == 1,
                    "one shared material and one shader pass");
                Debug.Log("[SwimFlowBudget] meshVertices=" + filter.sharedMesh.vertexCount +
                    " meshTriangles=" + filter.sharedMesh.triangles.Length / 3 +
                    " materials=" + renderer.sharedMaterials.Length +
                    " shaderPasses=" + renderer.sharedMaterial.passCount +
                    " visibleRibbons=" + flow.VisibleRibbonCount);
                for (int i = 0; i < 60; i++) flow.SimulateForValidation(Vector3.zero, 0f, 1f / 60f);
                Check(flow.VisibleRibbonCount == 0 && !renderer.enabled && flow.CurrentIntensity == 0f,
                    "idle drains and hides the field");
                for (int i = 0; i < 45; i++) flow.SimulateForValidation(Vector3.forward * 2f, 1f, 1f / 60f);
                Check(flow.VisibleRibbonCount > 0, "repeated swimming recovers without allocating new meshes");
                Mesh existing = filter.sharedMesh;
                flow.SimulateForValidation(Vector3.forward * 2f, 1f, 1f / 60f, false);
                Check(flow.VisibleRibbonCount == 0 && !renderer.enabled && flow.CurrentIntensity == 0f,
                    "menu, intro or tracking-loss gate clears all residual streaks");
                flow.enabled = false;
                Check(flow.VisibleRibbonCount == 0 && !renderer.enabled, "disable leaves no residual visual");
                flow.enabled = true;
                flow.Configure(hands, camera);
                Check(filter.sharedMesh == existing && flow.HasRenderResources,
                    "disable/reconfigure reuses its bounded render resources");
            }
            finally { Object.DestroyImmediate(owner); }
            Check(owner == null && cameraHost == null, "teardown releases the effect hierarchy with the owner");
            Debug.Log("SwimFlowVfxChecks PASS: " + assertions + " assertions; synthetic travel, real mesh, lifecycle and one-pass budget.");
            return assertions;
        }

        /// <summary>
        /// A scoped Editor art comparison using the existing scene camera. The camera
        /// and temporary virtual rig translate together at .8 m/s; production field
        /// stepping is shared, while no live motor/private tracking state is changed.
        /// The capture delegate should render synchronously before returning.
        /// </summary>
        public static IEnumerator RunInScene(UnderwaterWorld world, Camera camera, Action<string> capture)
        {
            if (world == null || !world.IsReady || camera == null || capture == null)
                throw new ArgumentException("A ready scene, isolated Editor camera and capture delegate are required.");
            if (!Application.isEditor || !Application.isPlaying)
                throw new InvalidOperationException("Use the isolated graphical Editor preview, never a player.");
            var owner = new GameObject("Isolated swimming-water capture rig");
            Vector3 originalPosition = camera.transform.position;
            Quaternion originalRotation = camera.transform.rotation;
            owner.transform.position = originalPosition;
            var hands = owner.AddComponent<HandSwimController>();
            hands.enabled = false;
            var flow = SwimFlowVfx.Install(hands, camera);
            flow.enabled = false; // Explicit bounded stepping only; no live input source.
            Vector3 velocity = camera.transform.forward * .8f;
            try
            {
                yield return null; // Allow the real resource shader/renderer to warm up.
                capture("swim-flow-01-idle");
                Check(flow.VisibleRibbonCount == 0, "idle art capture has no overlay");
                for (int frame = 0; frame < 90; frame++)
                {
                    owner.transform.position += velocity / 60f;
                    camera.transform.position += velocity / 60f;
                    flow.SimulateForValidation(velocity, .5f, 1f / 60f);
                    yield return null;
                }
                // One last step immediately before a synchronous Camera.Render.
                flow.SimulateForValidation(velocity, .5f, 1f / 60f);
                Check(flow.VisibleRibbonCount > 0, "normal .8m/s travel is visibly populated");
                capture("swim-flow-02-forward-080mps");
                for (int frame = 0; frame < 45; frame++)
                {
                    flow.SimulateForValidation(Vector3.zero, 0f, 1f / 60f);
                    yield return null;
                }
                Check(flow.VisibleRibbonCount == 0 && flow.CurrentIntensity == 0f, "art stop drains the field");
                capture("swim-flow-03-stopped");
                for (int frame = 0; frame < 45; frame++)
                {
                    owner.transform.position += velocity / 60f;
                    camera.transform.position += velocity / 60f;
                    flow.SimulateForValidation(velocity, .7f, 1f / 60f);
                    yield return null;
                }
                flow.SimulateForValidation(velocity, .7f, 1f / 60f, false);
                Check(flow.VisibleRibbonCount == 0, "menu/tracking gate immediately clears the scene capture");
                capture("swim-flow-04-menu-or-tracking-cleared");
            }
            finally
            {
                camera.transform.SetPositionAndRotation(originalPosition, originalRotation);
                Object.DestroyImmediate(owner);
            }
        }
    }
}
#endif
