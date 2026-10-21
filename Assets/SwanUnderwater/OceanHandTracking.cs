using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;
#if UNITY_VISIONOS
using UnityEngine.XR.VisionOS;
#endif

namespace SwanUnderwater
{
    /// <summary>One gameplay snapshot per frame; late visual updates cannot mutate gesture input.</summary>
    public static class OceanHandTracking
    {
        static readonly List<XRHandSubsystem> subsystems=new List<XRHandSubsystem>();
        static readonly OceanHandFrame[] gameplay={OceanHandFrame.Allocate(),OceanHandFrame.Allocate()};
        static readonly OceanHandFrame[] visual={OceanHandFrame.Allocate(),OceanHandFrame.Allocate()};
        static readonly int[] picoGameplayFrame={-1,-1}, picoVisualFrame={-1,-1}, aimFrame={-1,-1};
        static readonly OceanHandAim[] aims=new OceanHandAim[2];
        static readonly bool[] aimAvailable=new bool[2];
        static XRHandSubsystem subsystem;
        static int refreshFrame=-1, invalidatedFrame=-1;
        internal static int LastInvalidationFrame => invalidatedFrame;
        static bool pico;
        // Explicit SDK mapping is part of the adapter contract, including Palm/Wrist reversal.
        static readonly XRHandJointID[] xrJoints={XRHandJointID.Palm,XRHandJointID.Wrist,
            XRHandJointID.ThumbMetacarpal,XRHandJointID.ThumbProximal,XRHandJointID.ThumbDistal,XRHandJointID.ThumbTip,
            XRHandJointID.IndexMetacarpal,XRHandJointID.IndexProximal,XRHandJointID.IndexIntermediate,XRHandJointID.IndexDistal,XRHandJointID.IndexTip,
            XRHandJointID.MiddleMetacarpal,XRHandJointID.MiddleProximal,XRHandJointID.MiddleIntermediate,XRHandJointID.MiddleDistal,XRHandJointID.MiddleTip,
            XRHandJointID.RingMetacarpal,XRHandJointID.RingProximal,XRHandJointID.RingIntermediate,XRHandJointID.RingDistal,XRHandJointID.RingTip,
            XRHandJointID.LittleMetacarpal,XRHandJointID.LittleProximal,XRHandJointID.LittleIntermediate,XRHandJointID.LittleDistal,XRHandJointID.LittleTip};
        public static XRHandJointID XrJointId(OceanHandJointId joint) => xrJoints[(int)joint];
        public static bool IsPicoBackend { get { RefreshBackend();return pico; } }
        public static string BackendName => IsPicoBackend?"PICO":subsystem!=null?"XR Hands":"Unavailable";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            if(subsystem!=null)subsystem.updatedHands-=OnUpdatedHands;
            subsystem=null;pico=false;refreshFrame=-1;Invalidate();invalidatedFrame=-1;
        }
        public static void Invalidate()
        {
            invalidatedFrame=Time.frameCount;
            for(int i=0;i<2;i++)
            {
                gameplay[i].isActive=visual[i].isActive=0;
                gameplay[i].SampleFrame=visual[i].SampleFrame=-1;
                picoGameplayFrame[i]=picoVisualFrame[i]=aimFrame[i]=-1;
            }
        }
        static void RefreshBackend()
        {
            if(refreshFrame==Time.frameCount)return;
            refreshFrame=Time.frameCount;
#if UNITY_VISIONOS
            if(OceanXRPlatform.IsVisionOS && !VisionOSHandsReady())Invalidate();
#endif
            bool nextPico=false;
#if ENABLE_PICO_XR_SDK && !OCEAN_XR_HANDS_ONLY
            var manager=XRGeneralSettings.Instance==null?null:XRGeneralSettings.Instance.Manager;
            nextPico=manager!=null && manager.activeLoader is ByteDance.PICO.XR.PXR_Loader;
#endif
            if(nextPico!=pico){Invalidate();pico=nextPico;}
            if(pico){if(subsystem!=null){subsystem.updatedHands-=OnUpdatedHands;subsystem=null;}return;}
            if(subsystem!=null && subsystem.running)return;
            if(subsystem!=null)subsystem.updatedHands-=OnUpdatedHands;
            subsystem=null;subsystems.Clear();SubsystemManager.GetSubsystems(subsystems);
            foreach(var candidate in subsystems)if(candidate.running){subsystem=candidate;break;}
            if(subsystem!=null)subsystem.updatedHands+=OnUpdatedHands;
        }
        static void OnUpdatedHands(XRHandSubsystem source,XRHandSubsystem.UpdateSuccessFlags success,XRHandSubsystem.UpdateType phase)
        {
            if(source!=subsystem || Time.frameCount<=invalidatedFrame)return;
#if UNITY_VISIONOS
            if(OceanXRPlatform.IsVisionOS && !VisionOSHandsReady()){Invalidate();return;}
#endif
            var target=phase==XRHandSubsystem.UpdateType.Dynamic?gameplay:visual;
            CopyXrHand(source.leftHand,(success&XRHandSubsystem.UpdateSuccessFlags.LeftHandJoints)!=0,ref target[0]);
            CopyXrHand(source.rightHand,(success&XRHandSubsystem.UpdateSuccessFlags.RightHandJoints)!=0,ref target[1]);
        }
        static void CopyXrHand(XRHand hand,bool updated,ref OceanHandFrame frame)
        {
            frame.SampleFrame=Time.frameCount;frame.isActive=updated&&hand.isTracked?1u:0u;
            frame.jointCount=26;frame.handScale=1;
            for(int i=0;i<26;i++)
            {
                OceanHandJoint sample=default;
                if(frame.isActive!=0)
                {
                    var joint=hand.GetJoint(xrJoints[i]);
                    if(joint.TryGetPose(out Pose pose) && Finite(pose))
                    {
                        sample.pose=pose;
                        sample.locationStatus=OceanHandLocationStatus.PositionValid|OceanHandLocationStatus.OrientationValid;
                        // Generic XR Hands exposes Pose even for estimated joints. On visionOS use
                        // its public per-joint extension so hidden/inferred fingers cannot fire gestures.
                        bool tracked=true;
#if UNITY_VISIONOS
                        if(OceanXRPlatform.IsVisionOS)
                            tracked=joint.TryGetVisionOSTrackingState(out bool visible) && visible;
#endif
                        if(tracked)sample.locationStatus|=OceanHandLocationStatus.PositionTracked|OceanHandLocationStatus.OrientationTracked;
                        if(joint.TryGetRadius(out float radius))sample.radius=radius;
                    }
                }
                frame.jointLocations[i]=sample;
            }
#if UNITY_VISIONOS
            if(OceanXRPlatform.IsVisionOS && TrySynthesizeVisionOSPalm(frame,out var palm))
                frame.jointLocations[(int)OceanHandJointId.JointPalm]=palm;
#endif
        }

#if UNITY_VISIONOS
        static bool VisionOSHandsReady() => OceanXRPlatform.VisionOSImmersiveSpaceReady &&
            OceanXRPlatform.VisionOSHandsAuthorized;
#endif

        /// <summary>
        /// visionOS has no Palm joint. Approximate its center halfway from wrist to middle knuckle,
        /// in unchanged tracking-local Unity coordinates. Estimated input stays valid-only.
        /// </summary>
        public static bool TrySynthesizeVisionOSPalm(OceanHandFrame frame,out OceanHandJoint palm)
        {
            palm=default;
            int wristIndex=(int)OceanHandJointId.JointWrist, knuckleIndex=(int)OceanHandJointId.JointMiddleProximal;
            if(frame.isActive==0 || frame.jointLocations==null || frame.jointCount<=knuckleIndex ||
                frame.jointLocations.Length<=knuckleIndex)return false;
            var wrist=frame.jointLocations[wristIndex];var knuckle=frame.jointLocations[knuckleIndex];
            const OceanHandLocationStatus valid=OceanHandLocationStatus.PositionValid|OceanHandLocationStatus.OrientationValid;
            if((wrist.locationStatus&valid)!=valid || (knuckle.locationStatus&OceanHandLocationStatus.PositionValid)==0 ||
                !Finite(wrist.pose) || !Finite(knuckle.pose.position))return false;
            float span=(knuckle.pose.position-wrist.pose.position).sqrMagnitude;
            if(span<.0004f || span>.04f)return false; // Reject collapsed or discontinuous hand geometry (2–20 cm).
            palm.pose=new Pose((wrist.pose.position+knuckle.pose.position)*.5f,wrist.pose.rotation);
            palm.radius=.025f;palm.locationStatus=valid;
            const OceanHandLocationStatus tracked=OceanHandLocationStatus.PositionTracked|OceanHandLocationStatus.OrientationTracked;
            if((wrist.locationStatus&tracked)==tracked && (knuckle.locationStatus&OceanHandLocationStatus.PositionTracked)!=0)
                palm.locationStatus|=tracked;
            return true;
        }
        public static bool IsCurrent(OceanHandFrame frame,int currentFrame,int lastInvalidationFrame) =>
            currentFrame>lastInvalidationFrame && frame.SampleFrame==currentFrame && frame.isActive!=0;

        /// <summary>AVP paddles use a visible wrist; other backends retain the tracked palm.</summary>
        public static bool TryReadPaddlePosition(OceanHandFrame frame,bool visionOS,int currentFrame,
            int lastInvalidationFrame,out Vector3 position)
        {
            position=Vector3.zero;
            int index=visionOS?(int)OceanHandJointId.JointWrist:(int)OceanHandJointId.JointPalm;
            if(!IsCurrent(frame,currentFrame,lastInvalidationFrame) || frame.jointLocations==null ||
                index>=frame.jointCount || index>=frame.jointLocations.Length)return false;
            var joint=frame.jointLocations[index];
            const OceanHandLocationStatus required=OceanHandLocationStatus.PositionValid|OceanHandLocationStatus.PositionTracked;
            if((joint.locationStatus&required)!=required || !Finite(joint.pose.position) ||
                (visionOS && !Finite(joint.pose)))return false;
            position=joint.pose.position;return true;
        }

        public static bool TryGetFrame(OceanHandSide side,out OceanHandFrame frame)
        {
            RefreshBackend();int i=(int)side;
            if(i<0||i>1){frame=default;return false;}
#if ENABLE_PICO_XR_SDK && !OCEAN_XR_HANDS_ONLY
            if(pico && picoGameplayFrame[i]!=Time.frameCount)
            {picoGameplayFrame[i]=Time.frameCount;OceanPicoHandTracking.Read(side,ref gameplay[i]);}
#endif
            frame=gameplay[i];
            return IsCurrent(frame,Time.frameCount,invalidatedFrame);
        }
        public static bool TryGetVisualFrame(OceanHandSide side,bool beforeRender,out OceanHandFrame frame)
        {
            RefreshBackend();int i=(int)side;
            if(i<0||i>1){frame=default;return false;}
#if ENABLE_PICO_XR_SDK && !OCEAN_XR_HANDS_ONLY
            int stamp=Time.frameCount*2+(beforeRender?1:0);
            if(pico && picoVisualFrame[i]!=stamp)
            {picoVisualFrame[i]=stamp;OceanPicoHandTracking.Read(side,ref visual[i]);}
#endif
            frame=visual[i].SampleFrame==Time.frameCount?visual[i]:gameplay[i];
            return IsCurrent(frame,Time.frameCount,invalidatedFrame);
        }
        public static bool TryGetAim(OceanHandSide side,out OceanHandAim aim)
        {
            if(!TryGetFrame(side,out OceanHandFrame frame)){aim=default;return false;}
            int i=(int)side;
            if(aimFrame[i]!=Time.frameCount)
            {
                aimFrame[i]=Time.frameCount;aims[i]=default;aimAvailable[i]=false;
#if ENABLE_PICO_XR_SDK && !OCEAN_XR_HANDS_ONLY
                if(pico)aimAvailable[i]=OceanPicoHandTracking.ReadAim(side,out aims[i]);
                else
#endif
                aimAvailable[i]=ReadXrAim(side,frame,out aims[i]);
            }
            aim=aims[i];return aimAvailable[i];
        }
        static bool ReadXrAim(OceanHandSide side,OceanHandFrame frame,out OceanHandAim aim)
        {
            aim=default;
#if ENABLE_VR || UNITY_GAMECORE
            var native=side==OceanHandSide.HandLeft?MetaAimHand.left:MetaAimHand.right;
            if(!OceanXRPlatform.IsVisionOS && native!=null && native.added)
            {
                // An existing but invalid/system aim must not silently switch to a permissive joint ray.
                aim.Source=OceanHandAimSource.MetaAim;
                var flags=(MetaAimFlags)(ulong)native.aimFlags.ReadValue();
                if((flags&MetaAimFlags.Computed)!=0)aim.aimStatus|=OceanHandAimStatus.AimComputed;
                if((flags&MetaAimFlags.Valid)!=0 && native.isTracked.isPressed)aim.aimStatus|=OceanHandAimStatus.AimRayValid;
                if((flags&MetaAimFlags.SystemGesture)!=0)aim.aimStatus|=OceanHandAimStatus.SystemGesture;
                if((flags&MetaAimFlags.IndexPinching)!=0)aim.aimStatus|=OceanHandAimStatus.AimIndexPinching;
                aim.aimRayPose=new Pose(native.devicePosition.ReadValue(),native.deviceRotation.ReadValue());
                aim.touchStrengthRay=native.pinchStrengthIndex.ReadValue();return true;
            }
#endif
            Pose ray;
            bool rayValid=OceanXRPlatform.IsVisionOS
                ? TryBuildVisionOSJointRay(frame,Time.frameCount,invalidatedFrame,out ray)
                : TryBuildJointRay(frame,out ray);
            if(!rayValid)return false;
            aim=new OceanHandAim{Source=OceanHandAimSource.JointRay,aimRayPose=ray,
                aimStatus=OceanHandAimStatus.AimComputed|OceanHandAimStatus.AimRayValid};return true;
        }
        /// <summary>Current visible index metacarpal direction; fingertip flexion does not steer the AVP ray.</summary>
        public static bool TryBuildVisionOSJointRay(OceanHandFrame frame,int currentFrame,int lastInvalidationFrame,out Pose ray)
        {
            ray=default;
            if(!IsCurrent(frame,currentFrame,lastInvalidationFrame) || frame.jointLocations==null ||
                frame.jointCount<8 || frame.jointLocations.Length<8)return false;
            var palm=frame.jointLocations[0];var wrist=frame.jointLocations[1];
            var metacarpal=frame.jointLocations[6];var proximal=frame.jointLocations[7];
            const OceanHandLocationStatus required=OceanHandLocationStatus.PositionValid|OceanHandLocationStatus.OrientationValid|
                OceanHandLocationStatus.PositionTracked|OceanHandLocationStatus.OrientationTracked;
            if((palm.locationStatus&required)!=required || (wrist.locationStatus&required)!=required ||
                (metacarpal.locationStatus&required)!=required || (proximal.locationStatus&required)!=required ||
                !Finite(palm.pose) || !Finite(wrist.pose) || !Finite(metacarpal.pose) || !Finite(proximal.pose))return false;
            Vector3 direction=proximal.pose.position-metacarpal.pose.position;
            float lengthSquared=direction.sqrMagnitude;
            // The metacarpal segment crosses the palm (sample bind length ~6 cm), not the wrist-to-knuckle diagonal.
            if(!Finite(direction) || !Finite(lengthSquared) || lengthSquared<.0004f || lengthSquared>.0144f)return false; // 2–12 cm.
            direction.Normalize();Vector3 up=Vector3.up;
            if(Mathf.Abs(Vector3.Dot(direction,up))>.98f)up=Vector3.forward;
            ray=new Pose(palm.pose.position,Quaternion.LookRotation(direction,up));return true;
        }
        public static bool TryBuildJointRay(OceanHandFrame frame,out Pose ray)
        {
            ray=default;
            if(frame.isActive==0 || frame.jointLocations==null || frame.jointCount<8 || frame.jointLocations.Length<8)return false;
            var palm=frame.jointLocations[0];var wrist=frame.jointLocations[1];var index=frame.jointLocations[7];
            const OceanHandLocationStatus flags=OceanHandLocationStatus.PositionValid|OceanHandLocationStatus.PositionTracked;
            if((palm.locationStatus&flags)!=flags||(wrist.locationStatus&flags)!=flags||(index.locationStatus&flags)!=flags)return false;
            Vector3 direction=index.pose.position-wrist.pose.position;
            if(!Finite(palm.pose.position)||!Finite(direction)||direction.sqrMagnitude<.0004f||direction.sqrMagnitude>.0625f)return false;
            // Wrist-to-knuckle direction remains stable while the index fingertip pinches.
            direction.Normalize();Vector3 up=Vector3.up;
            if(Mathf.Abs(Vector3.Dot(direction,up))>.98f)up=Vector3.forward;
            ray=new Pose(palm.pose.position,Quaternion.LookRotation(direction,up));return true;
        }
        static bool Finite(float n)=>!float.IsNaN(n)&&!float.IsInfinity(n);
        static bool Finite(Vector3 p)=>Finite(p.x)&&Finite(p.y)&&Finite(p.z);
        static bool Finite(Pose p)=>Finite(p.position)&&Finite(p.rotation.x)&&Finite(p.rotation.y)&&Finite(p.rotation.z)&&Finite(p.rotation.w)&&Quaternion.Dot(p.rotation,p.rotation)>.0001f;
    }
}
