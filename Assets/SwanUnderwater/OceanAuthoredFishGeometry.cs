using System;
using UnityEngine;

namespace SwanUnderwater
{
    /// <summary>Measured animated geometry in the unscaled navigation root's coordinates.</summary>
    [Serializable]
    public sealed class OceanAuthoredFishGeometry
    {
        public int schemaVersion = 1;
        public string sourceMeshSha256;
        public float firstPoseDiagonal;
        public Vector3 navigationCenter, navigationSize;
        public Bounds NavigationBounds => new Bounds(navigationCenter, navigationSize);
        public bool IsValid => schemaVersion == 1 && sourceMeshSha256 != null && sourceMeshSha256.Length == 64 &&
            firstPoseDiagonal > .005f && firstPoseDiagonal < 100f && Finite(navigationCenter) && Finite(navigationSize) &&
            navigationSize.x > 0f && navigationSize.y > 0f && navigationSize.z > 0f && navigationSize.magnitude < firstPoseDiagonal * 2f;

        static bool Finite(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsNaN(value.y) && !float.IsNaN(value.z) &&
            !float.IsInfinity(value.x) && !float.IsInfinity(value.y) && !float.IsInfinity(value.z);

        public static Bounds TransformBounds(Bounds local, Matrix4x4 matrix)
        {
            Vector3 min = local.min, max = local.max;
            Bounds world = new Bounds(matrix.MultiplyPoint3x4(min), Vector3.zero);
            for (int corner = 1; corner < 8; corner++)
                world.Encapsulate(matrix.MultiplyPoint3x4(new Vector3(
                    (corner & 1) == 0 ? min.x : max.x,
                    (corner & 2) == 0 ? min.y : max.y,
                    (corner & 4) == 0 ? min.z : max.z)));
            return world;
        }
    }
}
