using System;

namespace SwanUnderwater
{
    /// <summary>Size-based ambient shark spacing. Quest sharks never use this policy.</summary>
    public static class OceanSharkPatrolPolicy
    {
        // The same metric NormalizeFishScale uses, measured before steering rotates the model.
        // A conservative full-body bound, not a claim about anatomical nose-to-tail length.
        public static float EnvelopeDiameter(float sizeX, float sizeY, float sizeZ)
            => (float)Math.Sqrt((double)sizeX * sizeX + (double)sizeY * sizeY + (double)sizeZ * sizeZ);

        public static int Largest(float[] measuredEnvelopes)
        {
            int result = -1; float largestEnvelope = 0;
            for (int i = 0; i < measuredEnvelopes.Length; i++)
                if (measuredEnvelopes[i] > largestEnvelope) { largestEnvelope = measuredEnvelopes[i]; result = i; }
            return result;
        }

        // Distance is to the body centre; the enclosing sphere radius precedes the diver buffer.
        public static float PlayerClearance(float envelopeDiameter, bool reefPatrol)
            => Math.Max(.1f, envelopeDiameter) * .5f + (reefPatrol ? 6f : 3f);
        public static float PatrolRadius(float reefHalfExtent, float envelopeDiameter)
            => Math.Max(8f, reefHalfExtent + envelopeDiameter * .5f + 2f);

        public static void Point(float centerX, float centerZ, float radiusX, float radiusZ,
            float phase, out float x, out float z)
        {
            x = centerX + (float)Math.Cos(phase) * radiusX;
            z = centerZ + (float)Math.Sin(phase) * radiusZ;
        }

        public static void KeepTargetOutsidePlayer(ref float x, ref float z, float playerX, float playerZ,
            float clearance, float fallbackPhase)
        {
            float dx = x - playerX, dz = z - playerZ;
            float distance = (float)Math.Sqrt(dx * dx + dz * dz);
            if (distance >= clearance) return;
            if (distance < .001f) { dx = (float)Math.Cos(fallbackPhase); dz = (float)Math.Sin(fallbackPhase); distance = 1f; }
            x = playerX + dx / distance * clearance;
            z = playerZ + dz / distance * clearance;
        }
    }
}
