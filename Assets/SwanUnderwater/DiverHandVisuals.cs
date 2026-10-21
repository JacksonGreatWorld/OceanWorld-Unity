using System;
using HandType = SwanUnderwater.OceanHandSide;
using HandJoint = SwanUnderwater.OceanHandJointId;
using HandJointLocation = SwanUnderwater.OceanHandJoint;
using HandJointLocations = SwanUnderwater.OceanHandFrame;
using HandLocationStatus = SwanUnderwater.OceanHandLocationStatus;
using HandAimState = SwanUnderwater.OceanHandAim;
using HandAimStatus = SwanUnderwater.OceanHandAimStatus;
using UnityEngine;
using UnityEngine.Rendering;

namespace SwanUnderwater
{
    /// <summary>Unity XR Hands sample meshes driven by visual poses; gameplay tracking qualifications stay independent.</summary>
    [DefaultExecutionOrder(110)]
    public sealed class DiverHandVisuals : MonoBehaviour
    {
        public static readonly string[] JointNames = { "Palm","Wrist","ThumbMetacarpal","ThumbProximal","ThumbDistal","ThumbTip",
            "IndexMetacarpal","IndexProximal","IndexIntermediate","IndexDistal","IndexTip",
            "MiddleMetacarpal","MiddleProximal","MiddleIntermediate","MiddleDistal","MiddleTip",
            "RingMetacarpal","RingProximal","RingIntermediate","RingDistal","RingTip",
            "LittleMetacarpal","LittleProximal","LittleIntermediate","LittleDistal","LittleTip" };
        HandSwimController hands;
        GameObject left, right;
        Transform[] leftBones, rightBones;
        HandJointLocations leftLocations, rightLocations;
        Material material;
        bool initialized;
        bool visionOSVisuals = UsesVisionOSVisuals;

        static bool UsesVisionOSVisuals
        {
            get
            {
#if UNITY_VISIONOS
                return true;
#else
                return false;
#endif
            }
        }

        public static Transform[] MapBones(GameObject model, bool leftSide)
        {
            var result = new Transform[26];
            var all = model.GetComponentsInChildren<Transform>(true);
            string prefix = leftSide ? "L_" : "R_";
            for(int i=0;i<26;i++)
            {
                foreach(var bone in all) if(bone.name==prefix+JointNames[i]) { result[i]=bone;break; }
                if(result[i]==null) throw new InvalidOperationException("Hand mesh missing joint: "+prefix+JointNames[i]);
            }
            return result;
        }
        void Start()
        {
            InitializeHandVisuals(UsesVisionOSVisuals, FindFirstObjectByType<HandSwimController>());
        }
        void InitializeHandVisuals(bool visionOS, HandSwimController controller)
        {
            visionOSVisuals=visionOS;
            hands=controller;
            if(hands==null || hands.TrackingSpace==null) return;
            try
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name="Soft slate diving gloves" };
                material.SetColor("_BaseColor",new Color(0.10f,0.24f,0.27f));
                material.SetFloat("_Smoothness",0.32f); material.SetFloat("_Metallic",0.05f);
                left=Create(true,out leftBones);right=Create(false,out rightBones);
                initialized=true;
                hands.SetCustomHandVisualsActive(true);
                Debug.Log("[DiverHands] Unity XR Hands skinned models ready: 26 joints each; " +
                    (visionOSVisuals ? "visionOS valid visual poses, estimated joints allowed; gameplay flags unchanged" : "active XR provider"));
            }
            catch(Exception e)
            {
                if(left!=null)Destroy(left);if(right!=null)Destroy(right);
                hands.SetCustomHandVisualsActive(false);
                Debug.LogWarning("[DiverHands] Retaining SDK visuals: "+e.Message);
            }
        }
        GameObject Create(bool leftSide,out Transform[] bones)
        {
            var prefab=Resources.Load<GameObject>("DiverHands/"+(leftSide?"LeftHand":"RightHand"));
            if(prefab==null)throw new InvalidOperationException("Missing hand mesh asset");
            var model=Instantiate(prefab,hands.TrackingSpace,false);
            try
            {
                bones=MapBones(model,leftSide);
                foreach(var r in model.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    r.sharedMaterial=material;r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;
                    // Bounds track the wrist as fingers move; these small meshes must not disappear at the edges.
                    r.updateWhenOffscreen=true;
                }
                model.SetActive(false);return model;
            }
            catch {Destroy(model);throw;}
        }
        void OnEnable(){Application.onBeforeRender+=RenderHands;}
        void OnDisable()
        {
            Application.onBeforeRender-=RenderHands;
            if(left!=null)left.SetActive(false);if(right!=null)right.SetActive(false);
            if(hands!=null && !visionOSVisuals)hands.SetCustomHandVisualsActive(false);
        }
        void LateUpdate(){RenderHands(false);}
        void RenderHands(){RenderHands(true);}
        void RenderHands(bool beforeRender)
        {
            if(!initialized || hands==null || hands.TrackingSpace==null)return;
            bool hasLeft = Apply(HandType.HandLeft,left,leftBones,ref leftLocations,beforeRender);
            bool hasRight = Apply(HandType.HandRight,right,rightBones,ref rightLocations,beforeRender);
            // AVP keeps its joint-sphere fallback hidden even while a virtual hand loses its root pose.
            hands.SetCustomHandVisualsActive(visionOSVisuals || hasLeft,visionOSVisuals || hasRight);
        }
        bool Apply(HandType type,GameObject model,Transform[] bones,ref HandJointLocations locations,bool beforeRender)
        {
            if(!hands.CanReadTrackedVisuals || !OceanHandTracking.TryGetVisualFrame(type,beforeRender,out locations))
            {model.SetActive(false);return false;}
            return ApplyVisualFrame(model,bones,locations,Time.frameCount);
        }
        bool ApplyVisualFrame(GameObject model,Transform[] bones,HandJointLocations locations,int currentFrame)
        {
            bool valid=locations.SampleFrame==currentFrame && locations.isActive!=0 &&
                locations.jointLocations!=null && locations.jointCount>=2 && locations.jointLocations.Length>=2;
            if(visionOSVisuals)
            {
                // XR Hands can provide a useful estimated pose without per-joint tracked flags.
                // Display only uses valid current poses; it never promotes gesture or locomotion input.
                valid=valid && ValidVisualJoint(locations.jointLocations[1]);
                model.SetActive(valid);if(!valid)return false;
                SetBone(bones[1],locations.jointLocations[1]);
                for(int i=0;i<26;i++)
                    if(i!=1 && i<locations.jointCount && i<locations.jointLocations.Length && ValidVisualJoint(locations.jointLocations[i]))
                        SetBone(bones[i],locations.jointLocations[i]);
                // A missing joint retains its previous/bind local pose and follows its actual parent.
                // Valid descendants still receive exact poses; no joint is reparented to a valid ancestor.
                return true;
            }
            valid=valid && locations.jointCount>=26 && locations.jointLocations.Length>=26;
            const HandLocationStatus flags=HandLocationStatus.PositionValid|HandLocationStatus.OrientationValid;
            if(valid) for(int i=0;i<26;i++)
            {
                var joint = locations.jointLocations[i];
                var required = i < 2 ? flags | HandLocationStatus.PositionTracked : flags;
                Vector3 p = joint.pose.position; Quaternion q = joint.pose.rotation;
                if((joint.locationStatus&required)!=required || !Finite(p.x)||!Finite(p.y)||!Finite(p.z) ||
                    !Finite(q.x)||!Finite(q.y)||!Finite(q.z)||!Finite(q.w) || Quaternion.Dot(q,q)<.01f)
                {valid=false;break;}
            }
            model.SetActive(valid);if(!valid)return false;
            // PICO and the Unity sample use the same OpenXR joint axes. Set wrist first, then its children.
            SetBone(bones[1],locations.jointLocations[1]);
            for(int i=0;i<26;i++)if(i!=1)SetBone(bones[i],locations.jointLocations[i]);
            return true;
        }
        static bool ValidVisualJoint(HandJointLocation joint)
        {
            const HandLocationStatus required=HandLocationStatus.PositionValid|HandLocationStatus.OrientationValid;
            Vector3 p=joint.pose.position;Quaternion q=joint.pose.rotation;float norm=Quaternion.Dot(q,q);
            return (joint.locationStatus&required)==required && Finite(p.x)&&Finite(p.y)&&Finite(p.z) &&
                Finite(q.x)&&Finite(q.y)&&Finite(q.z)&&Finite(q.w) && Finite(norm) && norm>.01f;
        }
        static bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
        void SetBone(Transform bone,HandJointLocation joint)
        {
            // Parent-first world pose assignment computes local pose against the actual model parent,
            // including when an intermediate joint retains its local pose. Tracking axes are converted once upstream.
            bone.SetPositionAndRotation(hands.TrackingSpace.TransformPoint(joint.pose.position),
                hands.TrackingSpace.rotation*joint.pose.rotation);
        }
        void OnDestroy()
        {
            if(left!=null)Destroy(left);if(right!=null)Destroy(right);if(material!=null)Destroy(material);
        }
    }
}
