using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SwanUnderwater
{
    /// <summary>Original low-poly mission markers; map ownership remains in the environment controller.</summary>
    public static class OceanMechanicEnvironment
    {
        public static Mesh MarkerMesh(OceanMechanicKind kind)
        {
            var b = new Builder();
            switch (kind)
            {
                case OceanMechanicKind.JellyfishDodge:
                    b.Octahedron(Vector3.zero, new Vector3(.26f, .18f, .26f));
                    for (int i = 0; i < 6; i++)
                    {
                        float a = i * Mathf.PI / 3;
                        b.Box(new Vector3(Mathf.Cos(a) * .15f, -.26f, Mathf.Sin(a) * .15f), new Vector3(.018f, .38f, .018f), Quaternion.Euler(0, 0, i * 4 - 10));
                    }
                    break;
                case OceanMechanicKind.OctopusTracking:
                    b.Octahedron(new Vector3(0, .08f, 0), new Vector3(.19f, .24f, .19f));
                    for (int i = 0; i < 8; i++)
                    {
                        float a = i * Mathf.PI / 4;
                        b.Box(new Vector3(Mathf.Cos(a) * .18f, -.1f, Mathf.Sin(a) * .18f), new Vector3(.045f, .06f, .38f), Quaternion.Euler(0, 90 - i * 45, 14));
                    }
                    break;
                case OceanMechanicKind.CoralRepair:
                    b.Box(new Vector3(0, .04f, 0), new Vector3(.07f, .36f, .07f), Quaternion.identity);
                    for (int i = 0; i < 3; i++) b.Box(new Vector3((i - 1) * .12f, .13f, 0), new Vector3(.05f, .25f, .05f), Quaternion.Euler(0, i * 31, (i - 1) * 33));
                    break;
                case OceanMechanicKind.ShipwreckLock:
                    b.Box(new Vector3(0, -.04f, 0), new Vector3(.06f, .38f, .06f), Quaternion.identity);
                    b.Ring(new Vector3(0, .17f, 0), .13f, .035f, 12);
                    b.Box(new Vector3(.06f, -.17f, 0), new Vector3(.16f, .05f, .06f), Quaternion.identity);
                    break;
                case OceanMechanicKind.RingRace:
                case OceanMechanicKind.KelpMaze:
                case OceanMechanicKind.TideGates:
                    b.Ring(Vector3.zero, .48f, .055f, 24); break;
                case OceanMechanicKind.Escort:
                case OceanMechanicKind.FishHerding:
                case OceanMechanicKind.Observation:
                    b.Octahedron(Vector3.zero, new Vector3(.29f, .14f, .14f));
                    b.Octahedron(new Vector3(-.3f, 0, 0), new Vector3(.12f, .18f, .035f)); break;
                case OceanMechanicKind.Cleanup:
                    b.Box(Vector3.zero, new Vector3(.17f, .28f, .17f), Quaternion.Euler(0, 18, 20));
                    b.Box(new Vector3(-.02f, .18f, 0), new Vector3(.08f, .08f, .08f), Quaternion.Euler(0, 18, 20)); break;
                default:
                    b.Octahedron(Vector3.zero, new Vector3(.2f, .2f, .2f)); break;
            }
            return b.Build("Mission " + kind);
        }
        public static Mesh RingMesh()
        { var b = new Builder(); b.Ring(Vector3.zero, .48f, .045f, 24); return b.Build("Expedition waypoint ring"); }
        public static Mesh BinMesh()
        {
            var b = new Builder();
            b.Box(new Vector3(0, -.18f, 0), new Vector3(.48f, .06f, .4f), Quaternion.identity);
            b.Box(new Vector3(-.22f, 0, 0), new Vector3(.04f, .38f, .4f), Quaternion.identity);
            b.Box(new Vector3(.22f, 0, 0), new Vector3(.04f, .38f, .4f), Quaternion.identity);
            b.Box(new Vector3(0, 0, .18f), new Vector3(.48f, .38f, .04f), Quaternion.identity);
            b.Box(new Vector3(0, -.1f, -.18f), new Vector3(.48f, .18f, .04f), Quaternion.identity);
            return b.Build("Original fixed recycling bin");
        }

        sealed class Builder
        {
            readonly List<Vector3> vertices = new List<Vector3>();
            readonly List<int> triangles = new List<int>();
            static readonly int[] Faces = { 0,2,1,0,3,2,4,5,6,4,6,7,0,1,5,0,5,4,2,3,7,2,7,6,0,4,7,0,7,3,1,2,6,1,6,5 };
            public void Box(Vector3 center, Vector3 size, Quaternion rotation)
            {
                int start = vertices.Count;
                for (int i = 0; i < 8; i++) vertices.Add(center + rotation * Vector3.Scale(size, new Vector3(
                    (i == 1 || i == 2 || i == 5 || i == 6) ? .5f : -.5f,
                    (i == 2 || i == 3 || i == 6 || i == 7) ? .5f : -.5f, i < 4 ? -.5f : .5f)));
                for (int i = 0; i < Faces.Length; i++) triangles.Add(start + Faces[i]);
            }
            public void Octahedron(Vector3 center, Vector3 size)
            {
                int s = vertices.Count;
                vertices.Add(center + Vector3.Scale(Vector3.right, size)); vertices.Add(center + Vector3.Scale(Vector3.left, size));
                vertices.Add(center + Vector3.Scale(Vector3.up, size)); vertices.Add(center + Vector3.Scale(Vector3.down, size));
                vertices.Add(center + Vector3.Scale(Vector3.forward, size)); vertices.Add(center + Vector3.Scale(Vector3.back, size));
                int[] faces = { 2,0,4,2,4,1,2,1,5,2,5,0,3,4,0,3,1,4,3,5,1,3,0,5 };
                for (int i = 0; i < faces.Length; i++) triangles.Add(s + faces[i]);
            }
            public void Ring(Vector3 center, float radius, float width, int segments)
            {
                for (int i = 0; i < segments; i++)
                {
                    float a = (i + .5f) * Mathf.PI * 2 / segments;
                    Box(center + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0) * radius,
                        new Vector3(radius * Mathf.PI * 2 / segments * 1.05f, width, width), Quaternion.Euler(0, 0, a * Mathf.Rad2Deg + 90));
                }
            }
            public Mesh Build(string name)
            {
                var mesh = new Mesh { name = name }; mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0);
                mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
            }
        }
    }
}
