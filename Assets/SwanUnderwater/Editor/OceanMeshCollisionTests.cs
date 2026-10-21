#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SwanUnderwater.EditorTests
{
    /// <summary>Actual PhysX geometry queries with a synthetic rig; no SDK input or simulation step.</summary>
    public static class OceanMeshCollisionTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        public static int Assertions { get; private set; }
        static void Check(bool condition, string detail)
        { Assertions++; if (!condition) throw new InvalidOperationException("Mesh swimmer collision: " + detail); }
        static void Set(object owner, string field, object value) => owner.GetType().GetField(field, Private).SetValue(owner, value);
        static T Get<T>(object owner, string field) => (T)owner.GetType().GetField(field, Private).GetValue(owner);
        static object Call(object owner, string name, params object[] arguments) => owner.GetType().GetMethod(name, Private).Invoke(owner, arguments);

        [UnityEditor.MenuItem("OceanWorld/Checks/Mesh swimmer collision")]
        public static void RunAll()
        {
            Assertions = 0;
            MeshDepenetrationKeepsBudget(); SeparatingStrokeExitsMesh(); ArchOpeningRemainsOpen(); ClassicSphereBehavior();
            Debug.Log("OceanMeshCollisionTests PASS: " + Assertions + " assertions; actual capsule/nonconvex-mesh penetration, bounded recovery, separating stroke, concave arch opening and unchanged sphere correction. Synthetic rig, not tracked-headset acceptance.");
        }

        public static void Diagnose()
        {
            using (var f = new Fixture())
            {
                MeshCollider wall = f.Wall();
                bool production = f.Penetration(wall, out float depth);
                CapsuleCollider probe = Get<CapsuleCollider>(f.Controller, "meshOverlapProbe");
                object[] body = { Vector3.zero, Vector3.zero, 0f }; Call(f.Controller, "GetBodyCapsule", body);
                Vector3 center = ((Vector3)body[0] + (Vector3)body[1]) * .5f;
                bool disabled = Physics.ComputePenetration(probe, center, Quaternion.identity, wall, wall.transform.position, wall.transform.rotation, out Vector3 offDirection, out float offDepth);
                probe.enabled = true; Physics.SyncTransforms();
                bool enabled = Physics.ComputePenetration(probe, center, Quaternion.identity, wall, wall.transform.position, wall.transform.rotation, out Vector3 onDirection, out float onDepth);
                probe.enabled = false;
                Debug.Log($"[MeshCollisionDiagnostic] production={production} depth={depth:R} bottom={body[0]} top={body[1]} radius={body[2]} eye={f.Head.position} center={center} height={probe.height:R} wall={wall.bounds} meshBounds={wall.sharedMesh.bounds} disabled={disabled}/{offDepth:R}/{offDirection} enabled={enabled}/{onDepth:R}/{onDirection}");
            }
        }

        sealed class Fixture : IDisposable
        {
            public readonly GameObject Rig;
            public readonly Transform Head;
            public readonly HandSwimController Controller;
            public readonly Vector3 Origin;
            readonly List<Object> owned = new List<Object>();
            public Fixture()
            {
                float floor = UnderwaterWorld.SampleRenderedSeabedHeight(200f, 200f);
                Origin = new Vector3(200, floor, 200);
                Rig = new GameObject("Mesh collision synthetic rig"); Rig.transform.position = Origin;
                Head = new GameObject("Synthetic physical head").transform; Head.SetParent(Rig.transform, false); Head.localPosition = Vector3.up * 1.6f;
                Controller = Rig.AddComponent<HandSwimController>(); Controller.enabled = false;
                Set(Controller, "xrOrigin", Rig.transform); Set(Controller, "head", Head); Set(Controller, "trackingSpace", Rig.transform);
            }
            public MeshCollider Boxes(string name, Vector3[] centers, Vector3[] sizes)
            {
                var primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Mesh source = primitive.GetComponent<MeshFilter>().sharedMesh;
                var combine = new CombineInstance[centers.Length];
                for (int i = 0; i < centers.Length; i++) combine[i] = new CombineInstance { mesh = source, transform = Matrix4x4.TRS(centers[i], Quaternion.identity, sizes[i]) };
                var mesh = new Mesh { name = name }; mesh.CombineMeshes(combine, true, true); mesh.RecalculateBounds();
                Object.DestroyImmediate(primitive);
                var owner = new GameObject(name); owner.transform.position = Origin;
                var collider = owner.AddComponent<MeshCollider>(); collider.convex = false; collider.sharedMesh = mesh;
                owned.Add(owner); owned.Add(mesh); Physics.SyncTransforms(); return collider;
            }
            public MeshCollider Wall() => Boxes("Nonconvex reef wall", new[] { new Vector3(.4f, 1.4f, 0) }, new[] { new Vector3(.5f, 3, 3) });
            public bool Penetration(MeshCollider mesh, out float depth)
            {
                object[] body = { Vector3.zero, Vector3.zero, 0f };
                Call(Controller, "GetBodyCapsule", body);
                object[] args = { mesh, body[0], body[1], body[2], Vector3.zero, 0f };
                bool overlap = (bool)Call(Controller, "TryMeshPenetration", args); depth = (float)args[5]; return overlap;
            }
            public bool Separating(string method, Collider obstacle, Vector3 movement)
            {
                object[] body = { Vector3.zero, Vector3.zero, 0f }; Call(Controller, "GetBodyCapsule", body);
                return (bool)Call(Controller, method, obstacle, body[0], body[1], body[2], movement);
            }
            public void Resolve(float budget)
            { Set(Controller, "remainingOverlapCorrection", budget); Call(Controller, "ResolveRockOverlaps"); }
            public void Move(Vector3 displacement) => Call(Controller, "MoveWithCollision", displacement);
            public void Dispose()
            {
                Object.DestroyImmediate(Rig);
                foreach (Object item in owned) if (item != null) Object.DestroyImmediate(item);
                Physics.SyncTransforms();
            }
        }

        static void MeshDepenetrationKeepsBudget()
        {
            using (var f = new Fixture())
            {
                MeshCollider wall = f.Wall();
                Check(f.Penetration(wall, out float before) && before > .03f, "query-only capsule detects the real nonconvex mesh overlap");
                CapsuleCollider probe = Get<CapsuleCollider>(f.Controller, "meshOverlapProbe");
                Check(probe != null && !probe.enabled && probe.gameObject.layer == 2, "probe cannot appear in movement/grasp overlap queries");
                Vector3 local = f.Head.localPosition;
                for (int frame = 0; frame < 20; frame++)
                {
                    Vector3 old = f.Rig.transform.position; f.Resolve(.01f);
                    Check(Vector3.Distance(old, f.Rig.transform.position) <= .01001f, "all mesh correction passes share the original frame budget");
                }
                Check(!f.Penetration(wall, out _) && f.Rig.transform.position.x < f.Origin.x - .05f, "repeated bounded correction recovers an initial physical overlap");
                Check(Vector3.Distance(local, f.Head.localPosition) < .000001f, "recovery moves only the origin, preserving the physical head local pose");
                Check(Mathf.Abs(f.Rig.transform.position.y - f.Origin.y) < .0001f, "a vertical reef wall cannot cause vertical sea-floor flicker");
            }
        }

        static void SeparatingStrokeExitsMesh()
        {
            using (var f = new Fixture())
            {
                MeshCollider wall = f.Wall();
                Check(f.Separating("IsSeparatingMeshOverlap", wall, Vector3.left * .03f), "outward stroke is accepted only after measuring reduced penetration");
                Check(!f.Separating("IsSeparatingMeshOverlap", wall, Vector3.right * .03f), "deeper movement does not qualify for the initial-overlap exception");
                Check(!f.Separating("IsSeparatingMeshOverlap", wall, Vector3.left), "a large displacement cannot bypass the overlapping mesh");
                float before = f.Rig.transform.position.x; f.Move(Vector3.left * .03f);
                Check(f.Rig.transform.position.x < before - .025f, "a zero-distance initial cast hit cannot trap an outward stroke");
            }
        }

        static void ArchOpeningRemainsOpen()
        {
            using (var f = new Fixture())
            {
                MeshCollider arch = f.Boxes("One concave mesh with an open arch passage",
                    new[] { new Vector3(-1.5f, 1.5f, 1), new Vector3(1.5f, 1.5f, 1), new Vector3(0, 2.8f, 1) },
                    new[] { new Vector3(.6f, 3, .6f), new Vector3(.6f, 3, .6f), new Vector3(3.6f, .6f, .6f) });
                Check(!f.Penetration(arch, out _), "a collider's broad bounds do not fill the actual arch opening");
                Vector3 start = f.Rig.transform.position;
                for (int i = 0; i < 24; i++) { f.Move(Vector3.forward * .1f); f.Resolve(.01f); }
                Check(Mathf.Abs(f.Rig.transform.position.z - start.z - 2.4f) < .002f, "the capsule can swim through the real nonconvex arch opening");
                Check(Mathf.Abs(f.Rig.transform.position.x - start.x) < .0001f && Mathf.Abs(f.Rig.transform.position.y - start.y) < .0001f, "passing a clear arch cannot push or jump the whole scene");
            }
        }

        static void ClassicSphereBehavior()
        {
            using (var f = new Fixture())
            {
                var owner = new GameObject("Classic reef sphere"); owner.transform.position = f.Origin + new Vector3(.4f, 1, 0);
                var sphere = owner.AddComponent<SphereCollider>(); sphere.radius = .3f; Physics.SyncTransforms();
                try
                {
                    Check(f.Separating("IsSeparatingSphereOverlap", sphere, Vector3.left * .02f), "classic outward sphere exception remains accepted");
                    Check(!f.Separating("IsSeparatingSphereOverlap", sphere, Vector3.right * .02f), "classic deeper sphere movement remains rejected");
                    Vector3 before = f.Rig.transform.position; f.Resolve(.01f);
                    Check(Mathf.Abs(f.Rig.transform.position.x - before.x + .01f) < .00005f && f.Rig.transform.position.y == before.y, "classic sphere correction keeps its exact horizontal budget and direction");
                    Check(Get<CapsuleCollider>(f.Controller, "meshOverlapProbe") == null, "classic-only play does not allocate the mesh query primitive");
                }
                finally { Object.DestroyImmediate(owner); }
            }
        }
    }
}
#endif
