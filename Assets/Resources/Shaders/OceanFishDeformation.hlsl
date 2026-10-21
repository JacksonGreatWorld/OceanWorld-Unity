#ifndef OCEAN_FISH_DEFORMATION_INCLUDED
#define OCEAN_FISH_DEFORMATION_INCLUDED

// Static-source meshes retain their original UVs. Bounds masks are conservative
// anatomical regions, not a replacement skeleton or a claim of authored animation.
// Normal/tangent corrections track the main spine/wing displacement without extra
// normal-map samples or finite-difference vertex evaluations.
void OceanDeformFish(float3 original, float3 normal, float3 tangent, float phase,
    float amplitudeScale, float turn, float4 body, float4 fins, float amplitude, float bob,
    out float3 position, out float3 deformedNormal, out float3 deformedTangent)
{
    // Great-white source vertices are diagonal in XZ. Deform in anatomical
    // coordinates, then return to mesh space: the species pivot owns visual alignment.
    // UVs stay unchanged and normals/tangents follow the same orthonormal basis.
    bool diagonalShark = body.x > 5.5 && body.x < 6.5;
    const float diagonal = .70710678118;
    if (diagonalShark)
    {
        original.xz = float2(original.x - original.z, original.x + original.z) * diagonal;
        normal.xz = float2(normal.x - normal.z, normal.x + normal.z) * diagonal;
        tangent.xz = float2(tangent.x - tangent.z, tangent.x + tangent.z) * diagonal;
    }
    position = original;
    deformedNormal = normal;
    deformedTangent = tangent;
    float hx = max(body.y, .01), hy = max(body.z, .01), hz = max(body.w, .01);
    if (body.x > 2.5 && body.x < 3.5)
    {
        // Imported ray nose -X, wings along Z: move the two broad wings up/down,
        // keeping the central body quiet rather than wagging the whole ray sideways.
        float wing = saturate((abs(original.z) / hz - .18) / .82);
        float wingPhase = phase - original.x / hx * 1.1;
        float wingAmplitude = fins.y * amplitudeScale;
        position.y += sin(wingPhase) * wing * wing * wingAmplitude;
        float dydx = -cos(wingPhase) * 1.1 / hx * wing * wing * wingAmplitude;
        float dydz = sin(wingPhase) * 2.0 * wing * sign(original.z) / (.82 * hz) * wingAmplitude;
        float tail = smoothstep(.28, 1.0, original.x / hx);
        position.z += sin(phase * .7 + original.x * 3.1) * tail * tail * amplitude;
        deformedNormal.x -= dydx * normal.y;
        deformedNormal.z -= dydz * normal.y;
        deformedTangent.y += dydx * tangent.x + dydz * tangent.z;
        return;
    }
    if (body.x > 1.5 && body.x < 2.5)
    {
        // Upright seahorse: small rapid dorsal/pectoral flutter, rigid upright
        // trunk and curled tail. World movement is left to the existing agent.
        float edge = smoothstep(.35, .9, abs(original.x) / hx);
        float torso = 1.0 - smoothstep(.35, .90, abs(original.y) / hy);
        position.z += sin(phase * fins.z + original.y * 7.0) * fins.y * edge * torso;
        return;
    }

    bool flat = body.x > 3.5 && body.x < 4.5;
    // Flatfish/eel source spine lies on X; preserve the source geometry orientation.
    float along = flat ? -original.x : original.z;
    float halfLength = flat ? hx : hz;
    float q = along / halfLength;
    float tailT = saturate((-q - fins.x) / max(.1, 1.0 - fins.x));
    float tail = tailT * tailT * (3.0 - 2.0 * tailT);
    float tailSlope = -6.0 * tailT * (1.0 - tailT) / (max(.1, 1.0 - fins.x) * halfLength);
    float spine = fins.w * (1.0 - smoothstep(-.1, .8, q));
    float bodyMask = tail * tail + spine;
    float wave = sin(phase + q * 2.1);
    float bend = amplitude * amplitudeScale;
    float displacement = bend * (wave * bodyMask + turn * .32 * tail * tail);
    float vertical = sin(phase * .5) * bob * tail * tail;
    // Body-mask derivative ignores the tiny, low-amplitude front-spine term.
    float derivative = bend * (cos(phase + q * 2.1) * 2.1 / halfLength * bodyMask +
        wave * 2.0 * tail * tailSlope + turn * .64 * tail * tailSlope);
    float verticalDerivative = sin(phase * .5) * bob * 2.0 * tail * tailSlope;
    if (flat)
    {
        position.z += displacement;
        position.y += vertical;
        deformedNormal.x += derivative * normal.z + verticalDerivative * normal.y;
        deformedTangent.z -= derivative * tangent.x;
        deformedTangent.y -= verticalDerivative * tangent.x;
        // A travelling wave along the thin skirt keeps a flatfish flat.
        float skirt = smoothstep(.45, .96, abs(original.z) / hz);
        position.y += sin(phase * fins.z + q * 4.0) * fins.y * skirt;
        return;
    }
    position.x += displacement;
    position.y += vertical;
    deformedNormal.z -= derivative * normal.x + verticalDerivative * normal.y;
    deformedTangent.x += derivative * tangent.z;
    deformedTangent.y += verticalDerivative * tangent.z;

    // Small pectoral strokes complement the tail. The round puffer's trunk stays
    // rigid; fin masks act near the lateral silhouette instead of its head.
    // Distant instanced schools retain the tail silhouette without this near-only detail.
    #if !defined(_SCHOOL_SIMPLE)
    float lateral = smoothstep(.48, .96, abs(original.x) / hx);
    float foreBody = 1.0 - smoothstep(.12, .72, abs(q - .08));
    float midHeight = 1.0 - smoothstep(.15, .95, abs(original.y) / hy);
    float finPhase = phase * fins.z + q * 2.0 + sign(original.x) * .35;
    float flutter = fins.y * lateral * foreBody * midHeight;
    position.y += sin(finPhase) * flutter;
    position.x += cos(finPhase) * flutter * .20 * sign(original.x);
    #endif
    if (diagonalShark)
    {
        position.xz = float2(position.x + position.z, position.z - position.x) * diagonal;
        deformedNormal.xz = float2(deformedNormal.x + deformedNormal.z, deformedNormal.z - deformedNormal.x) * diagonal;
        deformedTangent.xz = float2(deformedTangent.x + deformedTangent.z, deformedTangent.z - deformedTangent.x) * diagonal;
    }
}
#endif
