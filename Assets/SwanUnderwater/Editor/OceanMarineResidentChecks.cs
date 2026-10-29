#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SwanUnderwater.EditorTests
{
    /// <summary>Actual imported resident checks and isolated synthetic animation samples; never drives a headset or live actor.</summary>
    public static class OceanMarineResidentChecks
    {
        public static int Assertions { get; private set; }
        public static float MaximumBoneAngle { get; private set; }
        public static float MaximumVertexTravel { get; private set; }
        public static float LoopVertexGap { get; private set; }

        static void Check(bool value, string message)
        { Assertions++; if (!value) throw new InvalidOperationException("Marine resident: " + message); }

        public static GameObject FindResident(UnderwaterWorld world)
        {
            if (world == null || world.ActiveEnvironmentRoot == null) return null;
            GameObject result = null;
            foreach (var tag in world.ActiveEnvironmentRoot.GetComponentsInChildren<OceanNaturalistTag>(true))
                if (tag.Species == 27 && tag.gameObject.activeInHierarchy)
                {
                    if(result==null)result=tag.gameObject;
                }
            return result;
        }

        public static void RunInScene(UnderwaterWorld world, Camera view)
        {
            Assertions = 0; MaximumBoneAngle = MaximumVertexTravel = LoopVertexGap = 0f;
            CheckNativeSteering();
            Check(EditorApplication.isPlaying && !EditorApplication.isPaused, "requires an actual running scene");
            Check(world != null && view != null && world.IsReady && world.ActiveEnvironment == OceanEnvironmentKind.CoralSanctuary,
                "actual sanctuary and view are ready");
            CheckBorrowedPlayerContext(world);
            GameObject resident = FindResident(world);
            Check(resident != null, "an active appended mackerel group member is present");
            Check(resident.transform.IsChildOf(world.ActiveEnvironmentRoot) && resident.GetComponent<OceanWildlifeResident>() != null,
                "world owns the resident and its navigation wrapper");
            Check(OceanSpeciesCatalog.Get(27).ResourcePath == "MarineExpansion/mackerel-volume" && OceanSpeciesCatalog.Get(27).FirstLevel == 6,
                "appended ID and availability remain explicit");
            var skins = resident.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Check(skins.Length == 1, "one original skinned renderer, not a rigid replacement");
            var skin = skins[0]; Mesh source = skin.sharedMesh;
            Check(skin.enabled && skin.gameObject.activeInHierarchy && source != null && source.vertexCount > 0 && source.isReadable,
                "actual imported mesh is active and readable for bounded verification");
            Check(source.vertexCount == 3354 && skin.bones.Length == 16 && source.bindposes.Length == 16 && skin.rootBone != null,
                "actual Unity import retains 3354 vertices, 16 skinned bones and matching bind poses");
            foreach (var bone in skin.bones) Check(bone != null && bone.IsChildOf(resident.transform), "bone belongs to the original resident hierarchy");
            long triangles = 0;
            for (int submesh = 0; submesh < source.subMeshCount; submesh++)
            {
                Check(source.GetTopology(submesh) == MeshTopology.Triangles, "source submesh remains triangles");
                triangles += source.GetIndexCount(submesh) / 3;
            }
            Check(triangles == 5986, "actual Unity triangulation remains 5986; original FBX declares 6002 before importer processing");
            var animators = resident.GetComponentsInChildren<Animator>(true);
            Check(animators.Length == 1, "one original Animator controls the actual skin");
            Animator animator = animators[0];
            Check(animator.isActiveAndEnabled && animator.avatar != null && animator.avatar.isValid && !animator.avatar.isHuman &&
                animator.runtimeAnimatorController != null && !animator.applyRootMotion, "active Generic Animator owns in-place swimming");
            AnimationClip[] clips = animator.runtimeAnimatorController.animationClips;
            Check(clips.Length == 1 && clips[0] != null && clips[0].name.IndexOf("Swim", StringComparison.OrdinalIgnoreCase) >= 0 &&
                clips[0].name.IndexOf("Death", StringComparison.OrdinalIgnoreCase) < 0 && clips[0].name.IndexOf("Idle", StringComparison.OrdinalIgnoreCase) < 0,
                "controller contains only the authored Swim clip");
            AnimationClip clip = clips[0];
            Check(Finite(clip.length) && clip.length > 0f && clip.isLooping, "original Swim has a finite positive looping duration");
            foreach (var renderer in resident.GetComponentsInChildren<Renderer>(true))
                foreach (var material in renderer.sharedMaterials)
                    Check(material != null && material.shader != null &&
                        material.shader.name.IndexOf("FishSwim", StringComparison.OrdinalIgnoreCase) < 0 &&
                        material.shader.name.IndexOf("IntroGuideFish", StringComparison.OrdinalIgnoreCase) < 0,
                        "native animation is not double-deformed by a procedural fish shader");

            Bounds liveBounds = skin.bounds;
            Check(Finite(liveBounds.center) && Finite(liveBounds.size) && liveBounds.size.magnitude > .1f && liveBounds.size.magnitude < 2f,
                "actual normalized resident has a finite plausible visual envelope");
            Check(resident.transform.position.y >= UnderwaterWorld.SampleRenderedSeabedHeight(resident.transform.position.x, resident.transform.position.z) + .12f,
                "navigation root is clear above the real rendered seabed");
            foreach (Bounds solid in world.ActiveSolidBounds)
            {
                Bounds expanded = solid; expanded.Expand(.24f);
                Check(!expanded.Contains(resident.transform.position), "resident root is clear of actual reef solid bounds");
            }

            Transform[] liveTransforms = resident.GetComponentsInChildren<Transform>(true);
            var positions = new Vector3[liveTransforms.Length]; var rotations = new Quaternion[liveTransforms.Length]; var scales = new Vector3[liveTransforms.Length];
            for (int i = 0; i < liveTransforms.Length; i++)
            { positions[i] = liveTransforms[i].localPosition; rotations[i] = liveTransforms[i].localRotation; scales[i] = liveTransforms[i].localScale; }
            Transform liveParent = resident.transform.parent;
            GameObject clone = null; Mesh baked = null;
            try
            {
                // Sample the Animator's FBX child: the outer navigation/heading wrappers do not own clip paths.
                clone = Object.Instantiate(resident);
                clone.name = "Isolated marine animation validation"; clone.hideFlags = HideFlags.HideAndDontSave;
                clone.transform.SetParent(null, false); clone.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                clone.transform.localScale = resident.transform.lossyScale;
                foreach (MonoBehaviour script in clone.GetComponentsInChildren<MonoBehaviour>(true)) script.enabled = false;
                Animator sampledAnimator = clone.GetComponentInChildren<Animator>(true);
                sampledAnimator.enabled = false;
                SkinnedMeshRenderer sampledSkin = clone.GetComponentInChildren<SkinnedMeshRenderer>(true);
                Check(sampledSkin != null && sampledAnimator != null && sampledSkin.sharedMesh == source, "isolated clone retains the same real imported skin and clip hierarchy");
                baked = new Mesh { name = "Isolated mackerel pose sample" };
                clip.SampleAnimation(sampledAnimator.gameObject, 0f); sampledSkin.BakeMesh(baked, false);
                Vector3[] first = WorldVertices(sampledSkin, baked);
                Bounds initial = BoundsOf(first); float diagonal = initial.size.magnitude;
                Debug.Log("[OceanMarineResidentChecks] scale_metrics diagonal=" + diagonal.ToString("G9") +
                    " skinLocalScale=" + sampledSkin.transform.localScale.ToString("G9") +
                    " skinLossyScale=" + sampledSkin.transform.lossyScale.ToString("G9") +
                    " residentLossyScale=" + resident.transform.lossyScale.ToString("G9") +
                    " cloneLossyScale=" + clone.transform.lossyScale.ToString("G9") + " contract=BakeMesh(false)+TRS(scale1)");
                Check(diagonal > .05f && diagonal < 2f, "sampled actual world scale is plausible");
                Vector2[] uv = source.uv;
                Vector3 firstHead = FrontProfileCenter(first, uv);
                var firstBoneRotations = new Quaternion[sampledSkin.bones.Length];
                for (int i = 0; i < firstBoneRotations.Length; i++) firstBoneRotations[i] = sampledSkin.bones[i].localRotation;
                Vector3 sampledRootPosition = sampledAnimator.transform.position;
                Quaternion sampledRootRotation = sampledAnimator.transform.rotation;
                Vector3 clonePosition = clone.transform.position;
                Bounds envelope = initial; float centerTravel = 0f, animationRootTravel = 0f, animationRootTurn = 0f;
                float headTravel = 0f, maxPosedDiagonal = diagonal;
                float[] phases = { 0f, .25f, .5f, .75f, 1f };
                float endpointEpsilon = Mathf.Min(.0001f, clip.length * .0001f);
                foreach (float phase in phases)
                {
                    // Avoid LoopTime wrapping the exact duration to zero and hiding an authored endpoint pop.
                    float sampleTime = phase == 1f ? clip.length - endpointEpsilon : clip.length * phase;
                    clip.SampleAnimation(sampledAnimator.gameObject, sampleTime); sampledSkin.BakeMesh(baked, false);
                    Vector3[] vertices = WorldVertices(sampledSkin, baked);
                    Check(vertices.Length == first.Length, "pose sampling does not replace topology");
                    Bounds bounds = BoundsOf(vertices); envelope.Encapsulate(bounds);
                    maxPosedDiagonal = Mathf.Max(maxPosedDiagonal, bounds.size.magnitude);
                    Check(bounds.size.magnitude >= diagonal * .5f && bounds.size.magnitude <= diagonal * 1.3f, "no collapsed/exploding sampled envelope at phase " + phase);
                    centerTravel = Mathf.Max(centerTravel, Vector3.Distance(bounds.center, initial.center));
                    headTravel = Mathf.Max(headTravel, Vector3.Distance(firstHead, FrontProfileCenter(vertices, uv)));
                    animationRootTravel = Mathf.Max(animationRootTravel, Vector3.Distance(sampledRootPosition, sampledAnimator.transform.position));
                    animationRootTurn = Mathf.Max(animationRootTurn, Quaternion.Angle(sampledRootRotation, sampledAnimator.transform.rotation));
                    for (int i = 0; i < vertices.Length; i++)
                    {
                        float delta = Vector3.Distance(first[i], vertices[i]);
                        MaximumVertexTravel = Mathf.Max(MaximumVertexTravel, delta);
                        if (phase == 1f) LoopVertexGap = Mathf.Max(LoopVertexGap, delta);
                    }
                    for (int i = 0; i < sampledSkin.bones.Length; i++)
                    {
                        Transform bone = sampledSkin.bones[i];
                        Check(Finite(bone.localPosition) && Finite(bone.localRotation) && Finite(bone.localScale), "sampled bones remain finite");
                        MaximumBoneAngle = Mathf.Max(MaximumBoneAngle, Quaternion.Angle(firstBoneRotations[i], bone.localRotation));
                    }
                    Check(Vector3.Distance(clonePosition, clone.transform.position) < .000001f && Quaternion.Angle(clone.transform.rotation, Quaternion.identity) < .0001f,
                        "authored animation cannot move or rotate the navigation wrapper");
                }
                Check(MaximumBoneAngle > .1f && MaximumVertexTravel > diagonal * .002f, "the original Swim visibly changes real bone poses and deformed vertices");
                Check(MaximumVertexTravel <= diagonal * .60f && centerTravel <= diagonal * .30f && maxPosedDiagonal <= diagonal * 1.3f,
                    "native tail flex stays inside the visually reviewed source envelope");
                Check(headTravel <= diagonal * .08f, "tail flex does not conceal translation of the fish head or whole body");
                Check(LoopVertexGap <= diagonal * .02f && animationRootTravel <= diagonal * .005f && animationRootTurn <= .05f,
                    "the complete original loop closes without moving the imported animation root");
                Debug.Log("[OceanMarineResidentChecks] pose_metrics samples=" + phases.Length + " triangles=" + triangles +
                    " bones=" + skin.bones.Length + " clip=" + clip.name + " seconds=" + clip.length.ToString("F4") +
                    " worldDiagonal=" + diagonal.ToString("F6") + " maxBoneDegrees=" + MaximumBoneAngle.ToString("F4") +
                    " maxVertexTravel=" + MaximumVertexTravel.ToString("F6") + " loopGap=" + LoopVertexGap.ToString("F6") +
                    " centerTravel=" + centerTravel.ToString("F6") + " headTravel=" + headTravel.ToString("F6") + " maxPosedDiagonal=" + maxPosedDiagonal.ToString("F6") +
                    " endpointEpsilon=" + endpointEpsilon.ToString("F7") + " animationRootTravel=" + animationRootTravel.ToString("F6") +
                    " animationRootTurn=" + animationRootTurn.ToString("F6"));
            }
            finally
            {
                if (baked != null) Object.DestroyImmediate(baked);
                if (clone != null) Object.DestroyImmediate(clone);
            }
            Check(resident.transform.parent == liveParent && resident.transform.IsChildOf(world.ActiveEnvironmentRoot), "live resident parent ownership is unchanged");
            for (int i = 0; i < liveTransforms.Length; i++)
                Check(liveTransforms[i] != null && liveTransforms[i].localPosition == positions[i] &&
                    Quaternion.Angle(liveTransforms[i].localRotation, rotations[i]) < .0001f && liveTransforms[i].localScale == scales[i],
                    "isolated animation samples never modify any live imported transform");
            Debug.Log("OceanMarineResidentChecks PASS " + Assertions + " assertions; one actual animated ID 27, native skin/Swim loop and scene placement. Isolated synthetic pose samples; no hand input or headset performance claim.");
        }

        static Vector3[] WorldVertices(SkinnedMeshRenderer skin, Mesh mesh)
        {
            Vector3[] vertices = mesh.vertices;
            // false includes hierarchy scale already; never multiply the scaled navigation parent a second time.
            Matrix4x4 unscaled = Matrix4x4.TRS(skin.transform.position, skin.transform.rotation, Vector3.one);
            for (int i = 0; i < vertices.Length; i++) vertices[i] = unscaled.MultiplyPoint3x4(vertices[i]);
            return vertices;
        }
        static Vector3 FrontProfileCenter(Vector3[] vertices, Vector2[] uv)
        {
            Check(uv.Length == vertices.Length, "source profile UVs map one-to-one to posed vertices");
            Vector3 sum = Vector3.zero; int count = 0;
            for (int i = 0; i < vertices.Length; i++)
                if (uv[i].x < .12f && uv[i].y > .25f && uv[i].y < .75f) { sum += vertices[i]; count++; }
            Check(count > 2, "reviewed front UV profile contains real head vertices");
            return sum / count;
        }
        // Actual runtime steering mathematics with synthetic goals/time, independent of asset import and headset input.
        public static void CheckNativeSteering()
        {
            CheckResidentProximityPolicy();
            float phase = 27 * 1.37f;
            Vector3 goal = new Vector3(Mathf.Sin(phase) * 1.7f, Mathf.Sin(phase * .7f) * .055f, Mathf.Cos(phase) * 1.7f * .55f);
            Vector3 position = Vector3.zero;
            Quaternion heading = Quaternion.LookRotation(goal, Vector3.up);
            float travelled = 0f;
            for (int i = 0; i < 600; i++)
            {
                phase += .02f * .22f;
                goal = new Vector3(Mathf.Sin(phase) * 1.7f, Mathf.Sin(phase * .7f) * .055f, Mathf.Cos(phase) * 1.7f * .55f);
                Vector3 next = OceanWildlifeResident.SteerNativeForward(position, heading, goal, .02f, out Quaternion nextHeading);
                Vector3 step = next - position; travelled += step.magnitude;
                Check(step.magnitude <= .22f * .02f + .000001f, "native advance is bounded by actual movement speed");
                Check(Quaternion.Angle(heading, nextHeading) <= 28f * .02f + .05f, "native turning is bounded without orientation snaps");
                if (step.sqrMagnitude > .0000000001f)
                {
                    Check(Vector3.Dot(step.normalized, nextHeading * Vector3.forward) > .999f, "native swimmer never translates sideways or backwards relative to its nose");
                    Check(Vector3.Dot(step, goal - position) > 0f, "native swimmer slows/turns before advancing toward a goal behind it");
                }
                position = next; heading = nextHeading;
            }
            Check(travelled > 1f, "nose-first swimming still makes meaningful progress along the moving route");
            position = Vector3.zero; heading = Quaternion.identity; goal = Vector3.back * 2f;
            Vector3 stopped = OceanWildlifeResident.SteerNativeForward(position, heading, goal, .02f, out Quaternion turning);
            Check(stopped == position && Quaternion.Angle(heading, turning) <= .61f, "a reversed goal causes an in-place turn, never backward motion");
            foreach (float invalid in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
            {
                Vector3 next = OceanWildlifeResident.SteerNativeForward(position, heading, Vector3.forward, invalid, out Quaternion stable);
                Check(next == position && stable == heading, "invalid time does not move or rotate a native swimmer");
            }
            Vector3 bounded = OceanWildlifeResident.SteerNativeForward(position, heading, Vector3.forward, 99f, out _);
            Check(bounded.magnitude <= .22f * .05f + .000001f, "a long frame cannot teleport the native swimmer");
        }
        static void CheckResidentProximityPolicy()
        {
            const BindingFlags fields=BindingFlags.Instance|BindingFlags.NonPublic;
            Type type=typeof(OceanWildlifeResident);
            var host=new GameObject("Isolated production resident proximity policy");
            try
            {
                Vector3 home=new Vector3(12f,25f,18f);host.transform.SetPositionAndRotation(home,Quaternion.identity);
                var resident=host.AddComponent<OceanWildlifeResident>();
                type.GetField("home",fields).SetValue(resident,home);
                type.GetField("radius",fields).SetValue(resident,3f);
                type.GetField("clearance",fields).SetValue(resident,.5f);
                MethodInfo step=type.GetMethod("StepAvoidance",fields),context=type.GetMethod("InteractionContextReadable",fields);
                Check(step!=null&&context!=null,"production native proximity owner and context gate exist");
                Func<string,Vector3> vector=name=>(Vector3)type.GetField(name,fields).GetValue(resident);
                Func<string,float> scalar=name=>(float)type.GetField(name,fields).GetValue(resident);
                Action<float,bool,bool,Vector3> tick=(dt,readable,near,threat)=>step.Invoke(resident,new object[]{dt,readable,near,threat});
                Check(!(bool)context.Invoke(resident,null),"missing borrowed player/campaign cannot enable an origin threat");
                tick(.02f,false,true,Vector3.zero);
                Check(vector("avoidanceOffset")==Vector3.zero&&scalar("avoidanceRemaining")==0f,"unreadable context cannot react to a default world origin");
                tick(.02f,true,false,home+Vector3.forward*10f);
                Check(vector("avoidanceOffset")==Vector3.zero,"a valid distant observation leaves the existing home orbit unchanged");
                Vector3 threat=home-Vector3.right*.3f;
                tick(.02f,true,true,threat);
                Vector3 target=vector("avoidanceTarget");
                Check(scalar("avoidanceRemaining")>1f&&target.magnitude<=.75001f&&target.y==0f,"near threat starts a bounded horizontal home-orbit offset");
                Check(vector("avoidanceOffset").magnitude<=.01401f&&host.transform.position==home,"first response eases its goal without relocating the native navigation wrapper");
                Check(Vector3.Dot(target,home-threat)>0f&&Vector3.Dot(target,host.transform.forward)>0f,"detour combines an outward and nose-compatible lateral direction");
                for(int frame=0;frame<150;frame++)
                {
                    Vector3 previous=vector("avoidanceOffset");
                    // Holding or sweeping a hand does not retarget the short response.
                    tick(.02f,true,true,home+Vector3.left*(frame%2==0?.2f:-.2f));
                    Check(Vector3.Distance(previous,vector("avoidanceOffset"))<=.01401f,"response and return goal changes are bounded each active step");
                    Check(vector("avoidanceOffset").magnitude<=.75001f,"player interaction cannot turn the fixed home orbit into a chase");
                    Check(vector("home")==home&&host.transform.position==home,"interaction never mutates home or bypasses the movement owner");
                    if(frame<58)Check(vector("avoidanceTarget")==target,"held hand cannot rewrite the current avoidance route");
                }
                Check(scalar("avoidanceRemaining")==0f&&vector("avoidanceOffset")==Vector3.zero&&scalar("avoidanceCooldown")>1f,
                    "continued proximity gets a real calm window after the short response");
                for(int frame=0;frame<70;frame++)tick(.02f,true,false,Vector3.zero);
                tick(.02f,true,true,threat);
                Check(scalar("avoidanceRemaining")>1f,"a later fresh approach can start a new bounded response after cooldown");
                tick(.02f,false,false,Vector3.zero);
                Check(scalar("avoidanceRemaining")==0f&&vector("avoidanceTarget")==Vector3.zero,
                    "menu/tracking/context loss cancels the response rather than following a stale palm");
                for(int frame=0;frame<70;frame++)tick(.02f,false,false,Vector3.zero);
                Check(vector("avoidanceOffset")==Vector3.zero,"suppressed context smoothly restores the original home-orbit goal");
                for(int frame=0;frame<160;frame++)tick(.02f,true,false,Vector3.zero);
                tick(.02f,true,true,new Vector3(float.NaN,0f,0f));
                Check(vector("avoidanceOffset")==Vector3.zero&&scalar("avoidanceRemaining")==0f,"non-finite threat never poisons a native pose");
                foreach(float invalid in new[]{0f,-1f,float.NaN,float.PositiveInfinity})
                {tick(invalid,true,true,threat);Check(vector("avoidanceOffset")==Vector3.zero,"invalid interaction time cannot advance a response");}
                tick(99f,true,true,threat);
                Check(vector("avoidanceOffset").magnitude<=.03501f,"a resumed long frame cannot jump the avoidance goal");
                type.GetMethod("OnDisable",fields).Invoke(resident,null);
                Check(vector("avoidanceOffset")==Vector3.zero&&scalar("avoidanceRemaining")==0f,
                    "hidden retained residents clear the old avoidance rather than replay it on re-entry");
                Check(host.GetComponentsInChildren<Collider>(true).Length==0,"proximity response creates no player collision");
            }
            finally{Object.DestroyImmediate(host);}
        }
        static void CheckBorrowedPlayerContext(UnderwaterWorld world)
        {
            const BindingFlags fields=BindingFlags.Instance|BindingFlags.NonPublic;
            Type type=typeof(OceanWildlifeResident),handType=typeof(HandSwimController);
            var fixture=new GameObject("Isolated native resident input-context fixture");
            var contextRoot=new GameObject("Isolated fixed native player context");
            try
            {
                var resident=fixture.AddComponent<OceanWildlifeResident>();
                Vector3 origin=world.ActiveSpawn+Vector3.up*20f;fixture.transform.position=origin;
                resident.Initialize(world,.62f,3f,27,true);
                type.GetMethod("ResolvePlayerContext",fields).Invoke(resident,null);
                var campaign=world.GetComponentInChildren<OceanCampaignController>(true);
                var borrowedView=(Camera)typeof(UnderwaterWorld).GetProperty("WildlifeView",fields).GetValue(world);
                var borrowedHands=(HandSwimController)typeof(UnderwaterWorld).GetProperty("WildlifeHands",fields).GetValue(world);
                Check(campaign!=null&&campaign.GameplayAllowed,"native input integration check starts in real unblocked campaign gameplay");
                Check((bool)type.GetField("contextResolved",fields).GetValue(resident)&&
                    ReferenceEquals(type.GetField("playerView",fields).GetValue(resident),borrowedView)&&
                    ReferenceEquals(type.GetField("playerHands",fields).GetValue(resident),borrowedHands)&&
                    ReferenceEquals(type.GetField("campaign",fields).GetValue(resident),campaign),
                    "ready-world resolution borrows its own player and already-created campaign exactly once");
                var cameraObject=new GameObject("Isolated native threat camera");cameraObject.transform.SetParent(contextRoot.transform,false);
                var camera=cameraObject.AddComponent<Camera>();
                var handsObject=new GameObject("Isolated fresh native threat hands");handsObject.transform.SetParent(contextRoot.transform,false);
                var hands=handsObject.AddComponent<HandSwimController>();
                camera.transform.position=origin+Vector3.forward*10f;
                handType.GetProperty("LeftHandActive").SetValue(hands,true);
                handType.GetProperty("RightHandActive").SetValue(hands,false);
                handType.GetProperty("LeftPalmPos").SetValue(hands,origin-Vector3.right*.3f);
                handType.GetField("handSampleFrame",fields).SetValue(hands,Time.frameCount);
                type.GetField("playerView",fields).SetValue(resident,camera);
                type.GetField("playerHands",fields).SetValue(resident,hands);
                type.GetField("clearance",fields).SetValue(resident,.5f);
                MethodInfo gate=type.GetMethod("InteractionContextReadable",fields),sense=type.GetMethod("TryProximityThreat",fields);
                Func<bool> readable=()=>(bool)gate.Invoke(resident,null);
                Func<bool> near=()=>{object[] args={Vector3.zero};return (bool)sense.Invoke(resident,args);};
                Check((bool)typeof(UnderwaterWorld).GetMethod("WildlifeHandResponsesAllowed",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{hands}),
                    "isolated fresh open context qualifies the shared world response gate");
                Check(readable()==(campaign!=null&&campaign.GameplayAllowed),"campaign presentation/transition authority remains part of native interaction gating");
                Check(near(),"one fresh near hand is enough; inactive second hand is never averaged in");
                handType.GetField("menuLocomotionPaused",fields).SetValue(hands,true);
                Check(!readable(),"menu hand poses cannot scare native residents");
                handType.GetField("menuLocomotionPaused",fields).SetValue(hands,false);
                handType.GetField("interactionSuppressed",fields).SetValue(hands,true);
                Check(!readable(),"suppressed transition/entry context cannot scare native residents");
                handType.GetField("interactionSuppressed",fields).SetValue(hands,false);
                handType.GetField("appFocused",fields).SetValue(hands,false);
                Check(!readable(),"unfocused input cannot continue a native reaction");
                handType.GetField("appFocused",fields).SetValue(hands,true);
                handType.GetField("handSampleFrame",fields).SetValue(hands,Time.frameCount-1);
                Check(!readable()&&!near(),"stale hand sample is rejected instead of becoming an origin threat");
                handType.GetField("handSampleFrame",fields).SetValue(hands,Time.frameCount);
                handType.GetProperty("LeftPalmPos").SetValue(hands,origin+Vector3.right*10f);
                Check(!near(),"far fresh hand and far borrowed head do not trigger proximity");
                camera.transform.position=origin+Vector3.forward*.4f;
                Check(near(),"a fresh qualifying hand permits body-aware head proximity to the same resident");
                camera.transform.position=origin+Vector3.forward*10f;
                handType.GetProperty("LeftPalmPos").SetValue(hands,new Vector3(float.NaN,0f,0f));
                Check(!near(),"non-finite palm input cannot trigger an invalid native goal");
                handType.GetProperty("LeftPalmPos").SetValue(hands,origin-Vector3.right*.3f);
                Check(near()&&readable()==(campaign!=null&&campaign.GameplayAllowed),"fresh restored input requalifies through the same campaign gate");
                MethodInfo tick=type.GetMethod("TickResident",fields);
                Func<string,Vector3> vector=name=>(Vector3)type.GetField(name,fields).GetValue(resident);
                Func<string,float> scalar=name=>(float)type.GetField(name,fields).GetValue(resident);
                Check(borrowedView!=null,"native integration retains its real tracked camera reference");
                Vector3 playerCameraBefore=camera.transform.position,trackedCameraBefore=borrowedView.transform.position;
                tick.Invoke(resident,new object[]{.02f});
                Check(scalar("avoidanceRemaining")>1f,"actual Update owner reads fresh proximity and starts a response inside the ready world");
                for(int frame=0;frame<125;frame++)
                {
                    Vector3 previous=fixture.transform.position;Quaternion heading=fixture.transform.rotation;
                    tick.Invoke(resident,new object[]{.02f});Vector3 step=fixture.transform.position-previous;
                    Check(step.magnitude<=.00441f,"actual resident owner retains its unchanged .22m/s cruise limit");
                    Check(Quaternion.Angle(heading,fixture.transform.rotation)<=.62f,"actual native detour cannot snap its heading");
                    if(step.sqrMagnitude>.0000000001f)Check(Vector3.Dot(step.normalized,fixture.transform.forward)>.999f,"actual response translation still follows the visible native nose");
                    Check(vector("home")==origin,"actual resident update cannot follow the player by moving home");
                }
                Check(scalar("avoidanceRemaining")==0f&&vector("avoidanceOffset")==Vector3.zero,
                    "actual owner returns to its original orbit goal after the response and calm period");
                Check(camera.transform.position==playerCameraBefore&&borrowedView.transform.position==trackedCameraBefore,
                    "native integration fixture never moves its borrowed player or the tracked camera");
                type.GetField("avoidanceCooldown",fields).SetValue(resident,0f);
                handType.GetProperty("LeftPalmPos").SetValue(hands,fixture.transform.position-Vector3.right*.2f);
                handType.GetField("menuLocomotionPaused",fields).SetValue(hands,true);
                tick.Invoke(resident,new object[]{.02f});
                Check(scalar("avoidanceRemaining")==0f,"actual update does not start a response to menu hands");
                handType.GetField("menuLocomotionPaused",fields).SetValue(hands,false);
                handType.GetField("handSampleFrame",fields).SetValue(hands,Time.frameCount-1);
                tick.Invoke(resident,new object[]{.02f});
                Check(scalar("avoidanceRemaining")==0f,"actual update rejects a stale near palm after menu resume");
                handType.GetField("handSampleFrame",fields).SetValue(hands,Time.frameCount);
                tick.Invoke(resident,new object[]{.02f});
                Check(scalar("avoidanceRemaining")>1f,"actual update accepts a new fresh palm after context recovery");
                resident.enabled=false;
                Check(scalar("avoidanceRemaining")==0f&&vector("avoidanceOffset")==Vector3.zero,
                    "real PlayMode OnDisable clears a retained native response before re-entry");
            }
            finally{Object.DestroyImmediate(fixture);Object.DestroyImmediate(contextRoot);}
        }
        static Bounds BoundsOf(Vector3[] vertices)
        {
            Check(vertices.Length > 0 && Finite(vertices[0]), "sampled geometry is nonempty and finite");
            Bounds bounds = new Bounds(vertices[0], Vector3.zero);
            foreach (Vector3 vertex in vertices) { Check(Finite(vertex), "sampled deformed vertex remains finite"); bounds.Encapsulate(vertex); }
            return bounds;
        }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        static bool Finite(Quaternion value) => Finite(value.x) && Finite(value.y) && Finite(value.z) && Finite(value.w);
    }
}
#endif

