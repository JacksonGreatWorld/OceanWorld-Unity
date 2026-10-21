using System;
using System.Collections.Generic;
using UnityEngine;

namespace SwanUnderwater.EditorTests
{
    public static class RenderedSeabedSegmentTests
    {
        static int assertions;
        static readonly List<float> Scratch = new List<float>(1024);
        static void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("RenderedSeabedSegmentTests: " + message); assertions++; }

        public static void RunAll()
        {
            assertions = 0;
            float h = UnderwaterWorld.SampleRenderedSeabedHeight(3.12f, -6.27f);
            Check(UnderwaterWorld.TryIntersectRenderedSeabedSegment(new Vector3(3.12f, h + 1f, -6.27f),
                new Vector3(3.12f, h - 1f, -6.27f), .03f, Scratch, out float distance) && Mathf.Abs(distance - .97f) < .0002f,
                "vertical contact includes clearance");
            Vector3 point = new Vector3(3.12f, h - .1f, -6.27f);
            Check(UnderwaterWorld.TryIntersectRenderedSeabedSegment(point, point, 0, Scratch, out distance) && distance == 0,
                "zero-length buried origin is contact");
            point.y = h + .1f;
            Check(!UnderwaterWorld.TryIntersectRenderedSeabedSegment(point, point, 0, Scratch, out distance) && float.IsPositiveInfinity(distance),
                "zero-length clear origin has no hit");
            Check(!UnderwaterWorld.TryIntersectRenderedSeabedSegment(new Vector3(0, float.NaN, 0), point, 0, Scratch, out _), "invalid endpoint rejected");
            Check(!UnderwaterWorld.TryIntersectRenderedSeabedSegment(point, point, -.01f, Scratch, out _), "negative clearance rejected");
            Check(!UnderwaterWorld.TryIntersectRenderedSeabedSegment(point, point, 0, null, out _), "null scratch is unavailable");

            // Independent triangle enumeration, not another sampling step or copy of the partition solver.
            Compare(new Vector2(8.04f, -12.96f), new Vector2(9.21f, -11.82f), "near cells and diagonals");
            Compare(new Vector2(39.73f, -5.16f), new Vector2(40.78f, -4.83f), "near to far X seam");
            Compare(new Vector2(-40.91f, 7.14f), new Vector2(-39.62f, 7.68f), "far to near X seam");
            Compare(new Vector2(5.13f, 35.72f), new Vector2(5.68f, 36.95f), "near to far Z seam");
            Compare(new Vector2(40f, -44.4f), new Vector2(40f, -42.9f), "exact patch edge and corner");
            Compare(new Vector2(48.1f, 61.3f), new Vector2(65.2f, 63.4f), "nonuniform far cells");
            Compare(new Vector2(-720.3f, -3.73f), new Vector2(719.7f, -3.67f), "long segment across far and near");

            // Outside the finite mesh, this API intentionally follows SampleRendered's edge clamp.
            h = UnderwaterWorld.SampleRenderedSeabedHeight(850, 20);
            Vector3 outsideA = new Vector3(850, h + 1, 20), outsideB = new Vector3(950, h - 1, 20);
            Check(UnderwaterWorld.TryIntersectRenderedSeabedSegment(outsideA, outsideB, .02f, Scratch, out distance) &&
                Mathf.Abs(distance - Vector3.Distance(outsideA, outsideB) * .49f) < .002f, "far-domain clamp remains linear");

            // Find a real rendered diagonal crest and pass just below its top while both
            // endpoints stay above it. A fixed 8cm sampler can miss this narrow interval.
            bool crestFound = false;
            for (float x = -20; x < 20 && !crestFound; x += .5f)
                for (float z = -24; z < 16 && !crestFound; z += .5f)
                {
                    float ha = UnderwaterWorld.SampleRenderedSeabedHeight(x + .05f, z + .05f);
                    float hb = UnderwaterWorld.SampleRenderedSeabedHeight(x + .45f, z + .45f);
                    float peak = UnderwaterWorld.SampleRenderedSeabedHeight(x + .25f, z + .25f);
                    if (peak <= Mathf.Max(ha, hb) + .001f) continue;
                    float y = peak - .00015f;
                    Vector3 a = new Vector3(x + .05f, y, z + .05f), b = new Vector3(x + .45f, y, z + .45f);
                    Check(a.y > ha && b.y > hb, "crest trace endpoints are both clear");
                    CompareExact(a, b, 0f, "narrow actual diagonal crest");
                    crestFound = true;
                }
            Check(crestFound, "real terrain provides a diagonal-crest regression fixture");
            Debug.Log("RenderedSeabedSegmentTests PASS: " + assertions + " assertions; actual rendered heights, independent triangle oracle, seams, long spans, clamp and narrow diagonal crest.");
        }

        static void Compare(Vector2 start, Vector2 end, string name)
        {
            Vector3 a = new Vector3(start.x, UnderwaterWorld.SampleRenderedSeabedHeight(start.x, start.y) + .31f, start.y);
            Vector3 b = new Vector3(end.x, UnderwaterWorld.SampleRenderedSeabedHeight(end.x, end.y) - .27f, end.y);
            CompareExact(a, b, .025f, name);
            // Both above along the same vertical offset from a very high ray must miss.
            Vector3 highA = a + Vector3.up * 100, highB = b + Vector3.up * 100;
            Check(!UnderwaterWorld.TryIntersectRenderedSeabedSegment(highA, highB, .025f, Scratch, out _), name + " high ray misses");
        }

        static void CompareExact(Vector3 a, Vector3 b, float clearance, string name)
        {
            float expected = TriangleReference(a, b, clearance);
            bool hit = UnderwaterWorld.TryIntersectRenderedSeabedSegment(a, b, clearance, Scratch, out float distance);
            Check(!float.IsPositiveInfinity(expected), name + " oracle has a hit");
            Check(hit && Mathf.Abs(distance - expected) < .004f, name + " first hit: actual=" + distance + " expected=" + expected);
        }

        static float TriangleReference(Vector3 from, Vector3 to, float clearance)
        {
            if (from.y <= UnderwaterWorld.SampleRenderedSeabedHeight(from.x, from.z) + clearance) return 0;
            float best = float.PositiveInfinity;
            float minX = Mathf.Min(from.x, to.x), maxX = Mathf.Max(from.x, to.x);
            float minZ = Mathf.Min(from.z, to.z), maxZ = Mathf.Max(from.z, to.z);
            for (int x = 0; x < 160; x++)
            {
                float x0 = -40 + x * .5f, x1 = x0 + .5f;
                if (x1 < minX || x0 > maxX) continue;
                for (int z = 0; z < 160; z++)
                {
                    float z0 = -44 + z * .5f, z1 = z0 + .5f;
                    if (z1 >= minZ && z0 <= maxZ) CellReference(from, to, x0, x1, z0, z1, clearance, ref best);
                }
            }
            var xs = new SortedSet<float> { -40, 40 }; var zs = new SortedSet<float> { -44, 36 };
            for (int i = 0; i <= 128; i++) { xs.Add(-800 + i * 12.5f); zs.Add(-800 + i * 12.5f); }
            float[] ax = new float[xs.Count], az = new float[zs.Count]; xs.CopyTo(ax); zs.CopyTo(az);
            for (int x = 0; x + 1 < ax.Length; x++)
            {
                if (ax[x + 1] < minX || ax[x] > maxX) continue;
                for (int z = 0; z + 1 < az.Length; z++)
                {
                    if (az[z + 1] < minZ || az[z] > maxZ ||
                        (ax[x] >= -40 && ax[x + 1] <= 40 && az[z] >= -44 && az[z + 1] <= 36)) continue;
                    CellReference(from, to, ax[x], ax[x + 1], az[z], az[z + 1], clearance, ref best);
                }
            }
            return best * Vector3.Distance(from, to);
        }

        static void CellReference(Vector3 from, Vector3 to, float x0, float x1, float z0, float z1, float clearance, ref float best)
        {
            Vector3 a = Vertex(x0, z0, clearance), b = Vertex(x1, z0, clearance), c = Vertex(x0, z1, clearance), d = Vertex(x1, z1, clearance);
            Triangle(from, to, a, c, b, ref best); Triangle(from, to, b, c, d, ref best);
        }
        static Vector3 Vertex(float x, float z, float clearance) => new Vector3(x, UnderwaterWorld.SampleRenderedSeabedHeight(x, z) + clearance, z);
        static void Triangle(Vector3 from, Vector3 to, Vector3 a, Vector3 b, Vector3 c, ref float best)
        {
            Vector3 edge1 = b - a, edge2 = c - a, delta = to - from;
            Vector3 p = Vector3.Cross(delta, edge2); float determinant = Vector3.Dot(edge1, p);
            if (Mathf.Abs(determinant) < 1e-9f) return;
            float inverse = 1f / determinant;
            Vector3 offset = from - a; float u = Vector3.Dot(offset, p) * inverse;
            if (u < -.000002f || u > 1.000002f) return;
            Vector3 q = Vector3.Cross(offset, edge1); float v = Vector3.Dot(delta, q) * inverse;
            if (v < -.000002f || u + v > 1.000002f) return;
            float t = Vector3.Dot(edge2, q) * inverse;
            if (t >= 0 && t <= 1 && t < best) best = t;
        }
    }
}
