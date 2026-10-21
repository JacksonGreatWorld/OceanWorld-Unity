#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;
namespace SwanUnderwater.Editor
{
    public static class OceanInteractionSkyChecks
    {
        [Serializable] sealed class Report
        {
            public bool success; public int assertions, triangles, renderers, videoFrames;
            public float wingSpan; public string scope = "Actual Unity geometry/rendering and synthetic world grasp; does not assert physical hand tracking or headset comfort.";
            public List<string> checks = new List<string>();
        }
        public static IEnumerator Run(TreasureHunt hunt, Camera head, string output)
        {
            Directory.CreateDirectory(output); var report = new Report();
            void Check(bool value, string text) { report.assertions++; report.checks.Add(text + ": " + value); if (!value) throw new InvalidOperationException(text); }
            var campaign = hunt.Campaign;
            campaign.HandleAction("start_level", 3); yield return new WaitForSecondsRealtime(.8f);
            TreasureHunt.Pickup shell = null;
            foreach (var item in hunt.Pickups) if (item.kind == 0 && item.habitat == "sand") { shell = item; break; }
            OceanCampaignCreatures.CreatureTarget fish = null;
            foreach (var item in campaign.Creatures.Targets) if (!item.IsAmbient && !item.IsShark && item.Species == 0) { fish = item; break; }
            Check(shell != null && fish != null, "mixed level has a real shell and clownfish");
            Vector3 palm = shell.renderer.bounds.center + Vector3.up * .50f;
            fish.Body.transform.position = palm; // Production grasp synchronizes the moving collider itself.
            Check(hunt.TryWorldGraspForValidation(palm, true), "closer fish accepts a world grasp with a shell also in reach");
            Check((campaign.State.Current.fishMask & 1) != 0 && !shell.consumed && campaign.State.Current.shellMask == 0,
                "nearer fish wins; farther ground shell is not accidentally collected");
            Check(hunt.TryWorldGraspForValidation(shell.renderer.bounds.center + Vector3.up * .12f, false) && shell.consumed,
                "shell remains reachable on the next deliberate grasp");
            // A physical obstruction must still prevent the production capture transaction.
            TreasureHunt.Pickup blocked = null;
            foreach (var item in hunt.Pickups) if (item.kind == 1 && item.habitat == "sand") { blocked = item; break; }
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "Temporary capture occlusion test";
            wall.transform.position = blocked.renderer.bounds.center + Vector3.up * .21f; wall.transform.localScale = new Vector3(.8f, .1f, .8f);
            Physics.SyncTransforms();
            Check(!hunt.TryGraspForValidation(blocked.renderer.bounds.center + Vector3.up * .45f) && !blocked.consumed,
                "a solid obstacle still blocks a shell within nominal reach");
            Object.DestroyImmediate(wall);
            Check(campaign.State.Current.leaderboardShells.Count == 1, "one physical shell capture records one new scoring entity");
            campaign.HandleAction("start_level", 14); yield return new WaitForSecondsRealtime(.8f);
            TreasureHunt.Pickup outOfOrder = null;
            foreach (var item in hunt.Pickups) if (item.kind == 2) { outOfOrder = item; break; }
            OceanCampaignCreatures.CreatureTarget behind = null;
            foreach (var item in campaign.Creatures.Targets) if (!item.IsAmbient && !item.IsShark && item.Species == 0) { behind = item; break; }
            Check(outOfOrder != null && behind != null, "ordered-shell level provides real mixed targets");
            Vector3 orderedPalm = outOfOrder.graspPosition;
            behind.Body.transform.position = orderedPalm + Vector3.up * .55f;
            Check(hunt.TryWorldGraspForValidation(orderedPalm, true), "wrong-order shell handles one world grasp as a hint");
            Check(!outOfOrder.consumed && outOfOrder.visual.activeSelf && campaign.State.Current.shellMask == 0 && campaign.State.Current.fishMask == 0,
                "hint does not hide the shell or capture a farther fish");
            EditorTests.HandSwimMovementTests.RunPhysicsInScene();
            var guardian = Object.FindFirstObjectByType<OceanSkyGuardian>();
            Check(guardian != null, "actual world creates exactly one sky guardian");
            Check(Object.FindObjectsByType<OceanSkyGuardian>(FindObjectsSortMode.None).Length == 1, "one guardian only");
            report.wingSpan = guardian.ActualWingSpan; report.triangles = guardian.TriangleCount;
            report.renderers = guardian.GetComponentsInChildren<Renderer>().Length;
            Check(Mathf.Abs(guardian.ActualWingSpan - OceanSkyGuardian.WingSpan) < .05f && guardian.TriangleCount < 10000 && report.renderers <= 2, "large art within a small draw and triangle budget");
            Check(guardian.GetComponentsInChildren<Collider>().Length == 0, "distant guardian cannot block swimming or be captured");
            Check(Vector3.Dot(guardian.GetComponentsInChildren<Renderer>()[0].transform.TransformDirection(Vector3.left).normalized, guardian.transform.forward) > .9999f,
                "actual imported head direction follows orbit tangent instead of moving sideways");
            Vector3 oldHead = head.transform.position; Quaternion oldRotation = head.transform.rotation;
            foreach (float height in new[] { -1.5f, 9f, 40f, 100f })
            {
                head.transform.position = new Vector3(oldHead.x, height, oldHead.z);
                for (int sample = 0; sample < 24; sample++)
                {
                    guardian.PoseForValidation(sample * OceanSkyGuardian.OrbitSeconds / 24);
                    Check(guardian.ConservativeBottomY - height >= OceanSkyGuardian.MinimumBodyGap - .001f, "unreachable altitude at observer height " + height);
                    foreach (Renderer renderer in guardian.GetComponentsInChildren<Renderer>())
                        Check(renderer.bounds.min.y >= guardian.ConservativeBottomY - .01f, "shader-expanded mesh bounds stay inside the vertical safety envelope");
                }
            }
            head.transform.position = oldHead; head.transform.rotation = oldRotation; guardian.PoseForValidation(0);
            Vector3 anchored = guardian.transform.position;
            head.transform.rotation = Quaternion.Euler(0, 140, 0); guardian.PoseForValidation(0);
            Check(Vector3.Distance(anchored, guardian.transform.position) < .00001f, "turning the head does not drag the animal through the scene");
            head.transform.rotation = oldRotation;
            campaign.HandleAction("menu", 0);
            var cameraObject = new GameObject("Synthetic upward preview camera"); Camera camera = cameraObject.AddComponent<Camera>();
            camera.CopyFrom(head); camera.enabled = false; camera.GetUniversalAdditionalCameraData().allowXRRendering = false;
            camera.transform.position = oldHead; camera.transform.LookAt(guardian.transform.position);
            camera.fieldOfView = 76f; camera.aspect = 16f / 9f;
            var target = new RenderTexture(1280, 720, 24); target.Create(); var pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            string frames = Path.Combine(output, "frames"); Directory.CreateDirectory(frames);
            RenderTexture previous = RenderTexture.active;
            try
            {
                for (int frame = 0; frame < 300; frame++)
                {
                    guardian.PoseForValidation(frame / 30.0);
                    campaign.Panel.SetAvailable(false);
                    RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                    RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); pixels.Apply(false, false);
                    File.WriteAllBytes(Path.Combine(frames, frame.ToString("D4") + ".jpg"), pixels.EncodeToJPG(88));
                    if (frame == 0 || frame == 150) File.WriteAllBytes(Path.Combine(output, "guardian-" + frame + ".png"), pixels.EncodeToPNG());
                    report.videoFrames++;
                    yield return null;
                }
            }
            finally { RenderTexture.active = previous; target.Release(); Object.Destroy(target); Object.Destroy(pixels); Object.Destroy(cameraObject); }
            report.success = true; File.WriteAllText(Path.Combine(output, "interaction-sky-report.json"), JsonUtility.ToJson(report, true));
            Debug.Log("INTERACTION_SKY_CHECKS_PASSED assertions=" + report.assertions);
        }
    }
}
#endif
