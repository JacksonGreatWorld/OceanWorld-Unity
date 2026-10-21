#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SwanUnderwater.EditorTests
{
    /// <summary>Production gun, trigger, swept PhysX flight, recovery input and durable state in an isolated far-away scene fixture.</summary>
    public static class OceanSpearRecoverySceneTests
    {
        public static int Assertions { get; private set; }
        static int frame = 2000000;
        static void Check(bool condition, string message)
        { Assertions++; if (!condition) throw new InvalidOperationException("Spear recovery scene: " + message); }
        sealed class Scene : IDisposable
        {
            public readonly GameObject root;
            public readonly Camera camera;
            public OceanCampaignState state;
            public OceanSpearGun gun;
            public bool reachable = true;
            readonly Vector3 spawn;
            public Scene(int level = 96, OceanCampaignState restored = null)
            {
                spawn = new Vector3(1000, 0, 1000);
                root = new GameObject("Isolated synthetic spear recovery fixture");
                var view = new GameObject("Isolated recovery camera"); view.transform.SetParent(root.transform);
                camera = view.AddComponent<Camera>(); camera.enabled = false;
                camera.transform.position = spawn + Vector3.up * (UnderwaterWorld.SampleRenderedSeabedHeight(spawn.x, spawn.z) + 1.6f);
                state = restored ?? new OceanCampaignState();
                if (restored == null) { state.StartLevel(level); state.AcquireTool(); }
                gun = OceanSpearGun.Create(root.transform, camera, null, state, null, spawn,
                    Array.Empty<Bounds>(), (from, to) => reachable, null);
            }
            public Vector3 Palm => camera.transform.position + Vector3.down * .3f;
            public void Sample(float curl, Vector3? aim = null)
            {
                gun.DrivePoseForValidation(Palm, aim ?? Vector3.right, true, true, true, true, curl, .02f, true, frame++);
                gun.TickForValidation(.02f, true);
            }
            public void Fire(Vector3? aim = null)
            {
                for (int i = 0; i < 10; i++) Sample(0, aim);
                for (int i = 0; i < 3; i++) Sample(.8f, aim);
            }
            public void Resolve()
            { for (int i = 0; i < 160 && gun.InFlight; i++) gun.TickForValidation(.02f, true); }
            public bool Pointer(float pinch, bool fresh = true)
            {
                var ray = new Ray(camera.transform.position, gun.PickupBounds.center - camera.transform.position);
                return gun.RecoveryPointerForValidation(0, frame++, .05f, ray, pinch, fresh);
            }
            public void ReleasedPointer()
            { for (int i = 0; i < 5; i++) Pointer(0); }
            public void Dispose()
            { UnityEngine.Object.DestroyImmediate(root); }
        }
        public static void RunInScene()
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Spear recovery scene tests require actual Unity Play Mode for PhysX/effect lifetime.");
            Assertions = 0;
            LegacyAndRecovery(); RestoreAndInterruptions(); LastCartridge();
            Debug.Log("OceanSpearRecoverySceneTests PASS " + Assertions + " assertions; production trigger, ray pinch, near grasp, one-flight latch, no ammo refill, pending restoration and last-round hit/miss settlement. Synthetic Editor input, no headset tracking/comfort claim.");
        }
        static void LegacyAndRecovery()
        {
            using (var old = new Scene(20))
            {
                old.Fire(); old.Fire();
                Check(old.state.Current.spearShotsFired == 2 && !old.state.Current.pendingSpearRecovery,
                    "published level20 keeps shared multi-shot behavior and no recovery latch");
            }
            using (var scene = new Scene())
            {
                scene.Fire();
                Check(scene.state.Current.spearShotsFired == 1 && scene.state.Current.pendingSpearRecovery && scene.gun.InFlight,
                    "cartridge and recovery latch commit before first flight completes");
                scene.Fire();
                Check(scene.state.Current.spearShotsFired == 1 && scene.state.SpearAmmoRemaining == 19,
                    "fresh trigger cycles cannot fire again while a recoverable spear is pending");
                Check(!scene.gun.TryPickup(scene.Palm, true), "in-flight spear cannot be retrieved");
                scene.Resolve();
                Check(!scene.gun.InFlight && scene.gun.CanRecover && scene.gun.ToolTransform.gameObject.activeSelf,
                    "resolved spear returns as same visible owned tool at safe retrieval point");
                Check(Vector3.Distance(scene.camera.transform.position, scene.gun.PickupBounds.center) < 1.5f,
                    "return point is within seated hand-ray range");
                for (int i = 0; i < 5; i++) scene.Pointer(1);
                Check(scene.state.Current.pendingSpearRecovery, "held pinch on arrival cannot retrieve");
                scene.ReleasedPointer(); Check(scene.Pointer(1), "released then freshly pinched recovery ray retrieves tool");
                Check(!scene.state.Current.pendingSpearRecovery && scene.state.SpearAmmoRemaining == 19 && scene.state.Current.sharkHits == 0,
                    "ray recovery neither refills ammunition nor invents a hit");
                scene.Fire(); scene.Resolve();
                Check(scene.gun.TryPickup(scene.gun.PickupBounds.center, false), "fresh near-grasp retrieval uses the existing world pickup path");
                Check(scene.state.Current.spearShotsFired == 2 && scene.state.SpearAmmoRemaining == 18,
                    "second retrieval preserves spent cartridges");
                Check(!scene.gun.TryPickup(scene.Palm, false), "duplicate pickup cannot retrieve or refill again");
            }
        }
        static void RestoreAndInterruptions()
        {
            OceanCampaignState pending;
            using (var original = new Scene())
            {
                original.Fire(); pending = new OceanCampaignState(original.state.Save);
                Check(pending.Current.pendingSpearRecovery && pending.Current.spearShotsFired == 1, "in-flight save contains durable pending latch and spent round");
            }
            using (var restored = new Scene(96, pending))
            {
                Check(restored.gun.CanRecover && !restored.gun.InFlight && restored.state.Current.sharkHits == 0,
                    "restart restores only a returnable tool, never a projectile or a hit");
                restored.ReleasedPointer(); restored.Pointer(.9f, false); restored.Pointer(1);
                Check(restored.state.Current.pendingSpearRecovery, "tracking loss clears recovery pinch qualification");
                restored.ReleasedPointer(); restored.gun.TickForValidation(.02f, false); restored.Pointer(1);
                Check(restored.state.Current.pendingSpearRecovery, "menu pause clears an armed retrieval gesture");
                restored.ReleasedPointer(); restored.gun.TickForValidation(.2f, true); restored.Pointer(1);
                Check(restored.state.Current.pendingSpearRecovery, "a long frame cancels recovery hover qualification before the next closed pinch");
                restored.reachable = false; restored.ReleasedPointer(); restored.Pointer(1);
                Check(restored.state.Current.pendingSpearRecovery && !restored.gun.TryPickup(restored.gun.PickupBounds.center, true),
                    "occluded hand ray and near grasp both refuse recovery");
                restored.reachable = true; restored.ReleasedPointer(); Check(restored.Pointer(1), "fresh unobstructed input can retrieve after recovery");
                Check(restored.state.SpearAmmoRemaining == 19 && restored.state.Current.spearShotsFired == 1,
                    "restore and repeated retries do not mint cartridges");
            }
        }
        static void LastCartridge()
        {
            var spent = new OceanCampaignState(); spent.StartLevel(96); spent.AcquireTool(); spent.Current.spearShotsFired = 19;
            using (var scene = new Scene(96, spent))
            {
                scene.Fire(); Check(scene.gun.InFlight && !scene.state.Failed, "last cartridge remains live until actual resolution");
                var interrupted = new OceanCampaignState(scene.state.Save);
                using (var restored = new Scene(96, interrupted))
                    Check(restored.state.Failed && restored.state.AmmoExhausted && restored.state.Current.pendingSpearRecovery &&
                        restored.state.Current.sharkHits == 0 && !restored.gun.InFlight,
                        "interrupted final projectile settles as unresolved miss on restart, never a new hit or free round");
                scene.Resolve();
                Check(scene.state.Failed && scene.state.AmmoExhausted && scene.state.Current.pendingSpearRecovery && scene.state.SpearAmmoRemaining == 0,
                    "final miss ends run without a free recovery cartridge");
                Check(!scene.gun.CanRecover && !scene.state.TryRecoverSpear(), "failed run cannot use recovery as retry");
                Check(OceanCampaignState.Validate(scene.state.Save, out string error), "failed pending state is a valid durable terminal state: " + error);
            }
            var almost = new OceanCampaignState(); almost.StartLevel(96); almost.AcquireTool(); almost.Current.spearShotsFired = 19;
            almost.Current.sharkHits = almost.SharkHitTarget - 1;
            almost.Current.sharkProgress[0].hits = almost.SharkHitTarget - 1;
            using (var scene = new Scene(96, almost))
            {
                var shark = new GameObject("Synthetic final-round shark trigger"); shark.transform.SetParent(scene.root.transform);
                shark.transform.position = scene.Palm + Vector3.forward * 1.5f + Vector3.up * OceanSpearGunVisual.MuzzleLocalPosition.y;
                var body = shark.AddComponent<SphereCollider>(); body.radius = .27f; body.isTrigger = true;
                scene.gun.RegisterShark(body, 0); Physics.SyncTransforms();
                scene.Fire(Vector3.forward); scene.Resolve();
                Check(scene.state.Current.sharkHits == scene.state.SharkHitTarget && !scene.state.Failed && scene.state.SpearAmmoRemaining == 0,
                    "last swept PhysX hit reaches threshold before exhaustion decision");
                var success = new OceanCampaignState(scene.state.Save);
                using (var restored = new Scene(96, success))
                    Check(!restored.state.Failed && restored.gun.CanRecover && restored.state.CatchShark(0),
                        "saved final-round hit restores its threshold and permits capture without mandatory extra recovery");
                Check(scene.gun.CanRecover && scene.gun.TryPickup(scene.gun.PickupBounds.center, true), "successful last cartridge remains recoverable at zero ammunition");
                Check(scene.state.CatchShark(0) && scene.state.Current.sharkCaught && scene.state.SpearAmmoRemaining == 0,
                    "zero-ammunition threshold still permits shark capture");
            }
        }
    }
}
#endif
