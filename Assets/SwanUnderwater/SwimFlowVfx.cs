using UnityEngine;
using UnityEngine.Rendering;

namespace SwanUnderwater
{
    /// <summary>
    /// A small world-space field of water wisps at the edge of the view. Reads actual
    /// artificial travel after the swim motor; never writes tracking, speed or poses.
    /// </summary>
    [DefaultExecutionOrder(130)]
    public sealed class SwimFlowVfx : MonoBehaviour
    {
        const int RibbonLimit = 44;
        const int Segments = 8;
        const int VerticesPerRibbon = (Segments + 1) * 2;
        const float MaximumSampleSpeed = 3.5f;
        static readonly Color WaterColor = new Color(.37f, .74f, .80f, 1f);

        struct Ribbon
        {
            public bool active;
            public Vector3 position, direction;
            public float age, lifetime, width, length, bend, curl;
        }

        readonly Ribbon[] ribbons = new Ribbon[RibbonLimit];
        readonly Vector3[] vertices = new Vector3[RibbonLimit * VerticesPerRibbon];
        readonly Color[] colors = new Color[RibbonLimit * VerticesPerRibbon];
        HandSwimController hands;
        Camera view;
        Mesh mesh;
        Material material;
        MeshRenderer flowRenderer;
        GameObject rendererHost;
        Matrix4x4 previousOriginMatrix;
        Vector3 filteredDirection;
        float intensity, emitCarry;
        uint randomState = 0x53a2f19du;
        int nextRibbon;
        bool sampleReady, applicationPaused, applicationFocused = true;

        public int VisibleRibbonCount { get; private set; }
        public float CurrentIntensity => intensity;
        public bool HasRenderResources => mesh != null && material != null && flowRenderer != null;

        /// <summary>Idempotent owner-bound installation; no scene or input asset edits.</summary>
        public static SwimFlowVfx Install(HandSwimController controller, Camera camera)
        {
            if (controller == null || camera == null) return null;
            var flow = controller.GetComponent<SwimFlowVfx>();
            if (flow == null) flow = controller.gameObject.AddComponent<SwimFlowVfx>();
            flow.Configure(controller, camera);
            return flow;
        }

        public void Configure(HandSwimController controller, Camera camera)
        {
            hands = controller;
            view = camera;
            ResetField();
            if (hands == null || view == null) return;
            EnsureResources();
            previousOriginMatrix = hands.transform.localToWorldMatrix;
            sampleReady = true;
        }

        void EnsureResources()
        {
            if (HasRenderResources) return;
            var shader = Resources.Load<Shader>("Shaders/SwimWaterFlow");
            if (shader == null) shader = Shader.Find("SwanUnderwater/SwimWaterFlow");
            if (shader == null)
            {
                Debug.LogWarning("[SwimFlow] Water-flow shader unavailable; locomotion is unaffected.");
                return;
            }
            material = new Material(shader) { name = "Peripheral swimming water" };
            mesh = new Mesh { name = "Bounded swimming water wisps" };
            mesh.MarkDynamic();
            var uv = new Vector2[vertices.Length];
            var indices = new int[RibbonLimit * Segments * 6];
            for (int r = 0; r < RibbonLimit; r++)
                for (int s = 0; s <= Segments; s++)
                {
                    int v = r * VerticesPerRibbon + s * 2;
                    uv[v] = new Vector2(s / (float)Segments, 0f);
                    uv[v + 1] = new Vector2(s / (float)Segments, 1f);
                    if (s == Segments) continue;
                    int t = (r * Segments + s) * 6;
                    indices[t] = v; indices[t + 1] = v + 2; indices[t + 2] = v + 1;
                    indices[t + 3] = v + 2; indices[t + 4] = v + 3; indices[t + 5] = v + 1;
                }
            mesh.vertices = vertices;
            mesh.colors = colors;
            mesh.uv = uv;
            mesh.triangles = indices;
            rendererHost = new GameObject("Peripheral swimming water");
            rendererHost.transform.SetParent(transform, false);
            rendererHost.AddComponent<MeshFilter>().sharedMesh = mesh;
            flowRenderer = rendererHost.AddComponent<MeshRenderer>();
            flowRenderer.sharedMaterial = material;
            flowRenderer.shadowCastingMode = ShadowCastingMode.Off;
            flowRenderer.receiveShadows = false;
            flowRenderer.lightProbeUsage = LightProbeUsage.Off;
            flowRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            flowRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            flowRenderer.allowOcclusionWhenDynamic = false;
            flowRenderer.enabled = false;
        }

        void LateUpdate()
        {
            if (hands == null || view == null || !HasRenderResources) return;
            bool allowed = !applicationPaused && applicationFocused && Time.timeScale > .001f &&
                hands.CanReadTrackedVisuals && !hands.MenuLocomotionPaused && !hands.InteractionSuppressed &&
                (hands.CurrentMode == HandSwimController.HandMode.Controller ||
                    hands.LeftPaddleTracked || hands.RightPaddleTracked);
            float dt = Time.deltaTime;
            Matrix4x4 currentOrigin = hands.transform.localToWorldMatrix;
            if (!sampleReady || !allowed || dt <= 0f || dt > .12f)
            {
                ResetField();
                previousOriginMatrix = currentOrigin;
                sampleReady = allowed;
                return;
            }
            // Use the same current tracking-local eye in both rig transforms. This
            // removes physical head movement and a yaw pivot at the eye from travel.
            Vector3 moved = MeasureRigTravel(previousOriginMatrix, hands.transform, view.transform.position);
            previousOriginMatrix = currentOrigin;
            float actualSpeed = moved.magnitude / dt;
            if (actualSpeed > MaximumSampleSpeed)
            {
                ResetField(); // Relocation/recenter is not a burst of swimming water.
                return;
            }
            // CurrentSpeed excludes physical room-scale motion. Actual travel also
            // prevents a blocked motor, collision correction or a held pose faking flow.
            actualSpeed = Mathf.Min(actualSpeed, hands.CurrentSpeed);
            AdvanceField(moved.sqrMagnitude > .00000001f ? moved.normalized : filteredDirection,
                actualSpeed, hands.StrokeStrength, dt);
        }

        public static float EvaluateMotionIntensity(float actualSpeed, float strokeStrength)
        {
            if (float.IsNaN(actualSpeed) || float.IsInfinity(actualSpeed) || actualSpeed <= .08f) return 0f;
            if (float.IsNaN(strokeStrength) || float.IsInfinity(strokeStrength)) strokeStrength = 0f;
            // Normal .4–1 m/s exploration already reads as motion. The motor's
            // fastest swim adds density rather than being the first visible state.
            float speed = Mathf.SmoothStep(0f, 1f, Mathf.Sqrt(Mathf.InverseLerp(.08f, 1.5f, actualSpeed)));
            return speed * (.82f + .18f * Mathf.Clamp01(strokeStrength));
        }

        static Vector3 MeasureRigTravel(Matrix4x4 previousOrigin, Transform origin, Vector3 eyeWorld)
        {
            Vector3 eyeLocal = origin.InverseTransformPoint(eyeWorld);
            return eyeWorld - previousOrigin.MultiplyPoint3x4(eyeLocal);
        }

        void AdvanceField(Vector3 direction, float speed, float stroke, float dt)
        {
            float target = EvaluateMotionIntensity(speed, stroke);
            intensity = Mathf.MoveTowards(intensity, target, dt * (target > intensity ? 5f : 4f));
            if (direction.sqrMagnitude > .5f)
            {
                if (filteredDirection.sqrMagnitude < .1f) filteredDirection = direction;
                else filteredDirection = Vector3.Slerp(filteredDirection, direction, 1f - Mathf.Exp(-dt * 12f)).normalized;
            }
            if (target > .015f && filteredDirection.sqrMagnitude > .5f)
            {
                emitCarry += dt * Mathf.Lerp(15f, 38f, target) * target;
                int count = Mathf.Min(4, (int)emitCarry);
                emitCarry -= count;
                for (int i = 0; i < count; i++) SpawnRibbon(filteredDirection, speed);
            }
            else emitCarry = 0f;

            for (int i = 0; i < ribbons.Length; i++)
            {
                if (!ribbons[i].active) continue;
                Ribbon ribbon = ribbons[i];
                ribbon.age += dt;
                ribbon.position -= ribbon.direction * speed * .18f * dt;
                if (ribbon.age >= ribbon.lifetime || intensity < .002f ||
                    (ribbon.position - view.transform.position).sqrMagnitude > 25f)
                    ribbon.active = false;
                ribbons[i] = ribbon;
            }
            UploadField();
        }

        void SpawnRibbon(Vector3 direction, float speed)
        {
            int index = -1;
            for (int i = 0; i < ribbons.Length; i++)
            {
                int candidate = (nextRibbon + i) % ribbons.Length;
                if (!ribbons[candidate].active) { index = candidate; break; }
            }
            if (index < 0) return;
            nextRibbon = (index + 1) % ribbons.Length;
            float side = Random01() < .5f ? -1f : 1f;
            float depth = Mathf.Lerp(1.1f, 2.5f, Random01());
            float angle = Mathf.Lerp(32f, 52f, Random01()) * Mathf.Deg2Rad;
            Vector3 local = new Vector3(side * Mathf.Tan(angle) * depth,
                Mathf.Lerp(-.40f, .34f, Random01()) * depth, depth);
            ribbons[index] = new Ribbon
            {
                active = true,
                position = view.transform.TransformPoint(local),
                direction = Quaternion.AngleAxis(Mathf.Lerp(-3f, 3f, Random01()), view.transform.up) * direction,
                age = 0f,
                lifetime = Mathf.Lerp(.78f, 1.10f, Random01()),
                width = Mathf.Lerp(.014f, .030f, Random01()),
                length = Mathf.Lerp(.19f, .43f, Mathf.Clamp01(speed / 2.5f)) * Mathf.Lerp(.85f, 1.1f, Random01()),
                bend = Mathf.Lerp(-.036f, .036f, Random01()),
                curl = Mathf.Lerp(-.22f, .22f, Random01())
            };
        }

        void UploadField()
        {
            VisibleRibbonCount = 0;
            Matrix4x4 toLocal = rendererHost.transform.worldToLocalMatrix;
            for (int i = 0; i < ribbons.Length; i++)
            {
                Ribbon r = ribbons[i];
                float alpha = 0f;
                Vector3 across = Vector3.right;
                if (r.active)
                {
                    Vector3 toEye = view.transform.position - r.position;
                    across = Vector3.Cross(r.direction, toEye);
                    if (across.sqrMagnitude < .0001f) across = view.transform.right;
                    else across.Normalize();
                    Vector3 local = view.transform.InverseTransformPoint(r.position);
                    float peripheral = local.z > .01f
                        ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.38f, .58f, Mathf.Abs(local.x) / local.z)) : 0f;
                    float age = r.age / r.lifetime;
                    float fade = Mathf.SmoothStep(0f, 1f, age / .16f) *
                        (1f - Mathf.SmoothStep(0f, 1f, (age - .58f) / .42f));
                    float proximity = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.65f, 1.05f, toEye.magnitude));
                    alpha = fade * peripheral * proximity * intensity * .30f;
                    if (alpha > .005f) VisibleRibbonCount++;
                }
                var color = new Color(WaterColor.r, WaterColor.g, WaterColor.b, alpha);
                for (int s = 0; s <= Segments; s++)
                {
                    float along = s / (float)Segments;
                    // A single, gently asymmetric bow has no inflection corner.
                    // Eight small segments keep its outline round at close range.
                    float bow = Mathf.Sin(along * Mathf.PI) * r.bend * (1f + r.curl * (along - .5f));
                    Vector3 center = r.position + r.direction * ((along - .5f) * r.length) + across * bow;
                    Vector3 edge = across * r.width * .5f;
                    int v = i * VerticesPerRibbon + s * 2;
                    vertices[v] = toLocal.MultiplyPoint3x4(center - edge);
                    vertices[v + 1] = toLocal.MultiplyPoint3x4(center + edge);
                    colors[v] = colors[v + 1] = color;
                }
            }
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.bounds = new Bounds(toLocal.MultiplyPoint3x4(view.transform.position), Vector3.one * 12f);
            flowRenderer.enabled = VisibleRibbonCount > 0;
        }

        float Random01()
        {
            randomState ^= randomState << 13;
            randomState ^= randomState >> 17;
            randomState ^= randomState << 5;
            return (randomState & 0x00ffffffu) / 16777216f;
        }

        void ResetField()
        {
            for (int i = 0; i < ribbons.Length; i++) ribbons[i] = default;
            intensity = emitCarry = 0f;
            filteredDirection = Vector3.zero;
            nextRibbon = VisibleRibbonCount = 0;
            sampleReady = false;
            if (flowRenderer != null) flowRenderer.enabled = false;
        }

        void OnDisable() => ResetField();
        void OnApplicationPause(bool paused) { applicationPaused = paused; ResetField(); }
        void OnApplicationFocus(bool focused) { applicationFocused = focused; ResetField(); }
        void OnDestroy()
        {
            ResetField();
            DestroyOwned(rendererHost);
            DestroyOwned(mesh);
            DestroyOwned(material);
        }

        static void DestroyOwned(Object resource)
        {
            if (resource == null) return;
            if (Application.isPlaying) Destroy(resource);
            else DestroyImmediate(resource);
        }

#if UNITY_EDITOR
        // Editor previews exercise the same bounded field without fabricating hand
        // samples or changing motor state. This is not headset comfort acceptance.
        public void SimulateForValidation(Vector3 actualVelocity, float stroke, float dt, bool allowed = true)
        {
            if (!allowed || view == null || !HasRenderResources) { ResetField(); return; }
            float speed = actualVelocity.magnitude;
            AdvanceField(speed > .0001f ? actualVelocity / speed : filteredDirection,
                speed, stroke, Mathf.Clamp(dt, 0f, .1f));
        }

        public static Vector3 MeasureRigTravelForValidation(Matrix4x4 previousOrigin, Transform origin, Vector3 eyeWorld)
            => MeasureRigTravel(previousOrigin, origin, eyeWorld);
#endif
    }
}
