using System;
using System.Reflection;
using UnityEngine;

namespace SwanUnderwater.EditorTests
{
    // Synthetic mesh fixtures exercise the production query without importing assets,
    // creating colliders, or relying on a particular reef/LOD. Geometry is internal to
    // Assembly-CSharp, so only fixture setup crosses the editor assembly via reflection.
    public static class ReefSegmentGeometryTests
    {
        static int assertions;

        static void Require(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException("Reef segment: " + message);
        }

        static Func<Vector3, Vector3, bool> Fixture(Vector3[] vertices, int[] indices)
        {
            Type type = null;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType("SwanUnderwater.ReefSupportGeometry", false);
                if (type != null) break;
            }
            if (type == null) throw new InvalidOperationException("ReefSupportGeometry assembly not loaded.");
            object geometry = Activator.CreateInstance(type, true);
            const BindingFlags privateField = BindingFlags.Instance | BindingFlags.NonPublic;
            type.GetField("vertices", privateField).SetValue(geometry, vertices);
            type.GetField("triangles", privateField).SetValue(geometry, indices);
            var bounds = new Bounds(vertices[0], Vector3.zero);
            foreach (Vector3 vertex in vertices) bounds.Encapsulate(vertex);
            type.GetField("bounds").SetValue(geometry, bounds);
            return (Func<Vector3, Vector3, bool>)Delegate.CreateDelegate(typeof(Func<Vector3, Vector3, bool>),
                geometry, type.GetMethod("IntersectsSegment"));
        }

        public static void RunAll()
        {
            assertions = 0;
            var wallVertices = new[] { new Vector3(0f, -1f, -1f), new Vector3(0f, 1f, -1f),
                new Vector3(0f, 1f, 1f), new Vector3(0f, -1f, 1f) };
            var wall = Fixture(wallVertices, new[] { 0, 1, 2, 0, 2, 3 });
            Require(wall(new Vector3(-1f, .2f, .1f), new Vector3(1f, .2f, .1f)), "vertical wall blocks front-to-back");
            Require(wall(new Vector3(1f, .2f, .1f), new Vector3(-1f, .2f, .1f)), "vertical wall blocks back-to-front");
            var reversed = Fixture(wallVertices, new[] { 2, 1, 0, 3, 2, 0 });
            Require(reversed(new Vector3(-1f, .2f, .1f), new Vector3(1f, .2f, .1f)), "reverse winding remains blocking");
            Require(wall(Vector3.left, Vector3.right), "shared triangle seam has no crack");
            Require(wall(Vector3.left, Vector3.zero), "endpoint on surface is included");
            Require(wall(Vector3.zero, Vector3.right), "start on surface is included");
            Require(!wall(Vector3.zero, Vector3.zero), "zero-length segment has no traversal");
            Require(!wall(new Vector3(-1f, 2f, 0f), new Vector3(1f, 2f, 0f)), "outside wall bounds is clear");
            Require(!wall(new Vector3(.1f, -2f, 0f), new Vector3(.1f, 2f, 0f)), "parallel offset is clear");
            Require(wall(new Vector3(-.0000001f, -.8f, 0f), new Vector3(.0000001f, .8f, 0f)),
                "shallow-angle crossing remains a hit");
            Require(wall(new Vector3(0f, -2f, 0f), new Vector3(0f, 2f, 0f)), "coplanar crossing blocks");
            Require(wall(new Vector3(0f, -2f, -1f), new Vector3(0f, 2f, -1f)), "coplanar edge contact blocks");
            Require(wall(new Vector3(0f, -.2f, .1f), new Vector3(0f, .2f, .1f)), "coplanar contained segment blocks");
            Require(!wall(new Vector3(0f, 1.1f, 0f), new Vector3(0f, 2f, 0f)), "coplanar disjoint segment is clear");

            // Unreferenced vertices widen the valid mesh bounds so the finite-t and
            // triangle tests, rather than only the AABB, reject these misses.
            var wide = Fixture(new[] { wallVertices[0], wallVertices[1], wallVertices[2], wallVertices[3],
                new Vector3(-2f, -2f, -2f), new Vector3(2f, 2f, 2f) }, new[] { 0, 1, 2, 0, 2, 3 });
            Require(!wide(new Vector3(.2f, 0f, 0f), Vector3.right), "intersection behind segment start is rejected");
            Require(!wide(Vector3.left, new Vector3(-.2f, 0f, 0f)), "intersection beyond segment end is rejected");
            Require(!wide(Vector3.left, new Vector3(-.000001f, 0f, 0f)), "finite line does not extend through near endpoint");
            Require(!wide(new Vector3(.1f, -1f, 0f), new Vector3(.1f, 1f, 0f)), "parallel miss within broad bounds");
            Require(!wide(new Vector3(0f, -1.5f, .1f), new Vector3(0f, -1.2f, .1f)), "coplanar miss within broad bounds");
            Require(!wide(new Vector3(float.NaN, 0f, 0f), Vector3.right), "NaN input is rejected");
            Require(!wide(Vector3.left, new Vector3(float.PositiveInfinity, 0f, 0f)), "infinite input is rejected");

            var triangle = Fixture(wallVertices, new[] { 0, 1, 2 });
            Require(!triangle(new Vector3(-1f, -.8f, .8f), new Vector3(1f, -.8f, .8f)), "triangle AABB does not fill missing half");
            Require(!triangle(new Vector3(0f, -.8f, .6f), new Vector3(0f, -.8f, .8f)), "coplanar point outside triangle stays clear");
            Require(triangle(new Vector3(-1f, -1f, -1f), new Vector3(1f, -1f, -1f)), "triangle vertex contact blocks");
            var degenerate = Fixture(new[] { Vector3.zero, Vector3.up, Vector3.up * 2f }, new[] { 0, 1, 2 });
            Require(!degenerate(Vector3.left + Vector3.up, Vector3.right + Vector3.up), "degenerate triangle is ignored");

            // Geometry lives in each prefab's coordinates; non-unit asset scale must
            // not change the two-sided/finite result.
            foreach (float scale in new[] { .001f, 1000f })
            {
                var scaledVertices = new Vector3[wallVertices.Length];
                for (int i = 0; i < scaledVertices.Length; i++) scaledVertices[i] = wallVertices[i] * scale;
                var scaled = Fixture(scaledVertices, new[] { 0, 1, 2, 0, 2, 3 });
                Require(scaled(Vector3.left * scale, Vector3.right * scale), "scaled wall crossing " + scale);
                Require(!scaled(new Vector3(-1f, 2f, 0f) * scale, new Vector3(1f, 2f, 0f) * scale), "scaled outside miss " + scale);
            }
            Debug.Log("[ReefSegmentGeometryTests] PASS " + assertions + " assertions; synthetic geometry only.");
        }
    }
}
