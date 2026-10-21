#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace SwanUnderwater.EditorTests
{
    /// <summary>Deterministic adapter contracts. Run in Unity; synthetic poses are not live Quest/PICO acceptance.</summary>
    public static class OceanHandTrackingTests
    {
        public static int Assertions { get; private set; }
        static void Check(bool value,string why)
        {if(!value)throw new InvalidOperationException("OceanHandTrackingTests: "+why);Assertions++;}
        public static void RunAll()
        {
            Assertions=0;
            Check(OceanHandTracking.XrJointId(OceanHandJointId.JointPalm)==XRHandJointID.Palm,"Palm explicitly maps to XR Palm");
            Check(OceanHandTracking.XrJointId(OceanHandJointId.JointWrist)==XRHandJointID.Wrist,"Wrist explicitly maps to XR Wrist");
            var seen=new System.Collections.Generic.HashSet<XRHandJointID>();
            for(int i=0;i<26;i++)Check(seen.Add(OceanHandTracking.XrJointId((OceanHandJointId)i)),"26 unique XR joints "+i);
            Check(OceanHandTracking.XrJointId(OceanHandJointId.JointLittleTip)==XRHandJointID.LittleTip,"last joint retained");
            var frame=OceanHandFrame.Allocate();frame.isActive=1;frame.SampleFrame=100;
            Check(OceanHandTracking.IsCurrent(frame,100,99),"current Dynamic snapshot accepted");
            Check(!OceanHandTracking.IsCurrent(frame,101,99),"previous-frame data cannot drive gameplay");
            Check(!OceanHandTracking.IsCurrent(frame,100,100),"focus reset rejects same-frame data");
            Check(!ReferenceEquals(frame.jointLocations,OceanHandFrame.Allocate().jointLocations),"visual and gameplay buffers have separate ownership");
            const OceanHandLocationStatus flags=OceanHandLocationStatus.PositionValid|OceanHandLocationStatus.PositionTracked;
            frame.jointLocations[0]=new OceanHandJoint{locationStatus=flags,pose=new Pose(new Vector3(.2f,1.2f,.4f),Quaternion.identity)};
            frame.jointLocations[1]=new OceanHandJoint{locationStatus=flags,pose=new Pose(new Vector3(.2f,1.2f,.35f),Quaternion.identity)};
            frame.jointLocations[7]=new OceanHandJoint{locationStatus=flags,pose=new Pose(new Vector3(.2f,1.2f,.45f),Quaternion.identity)};
            Check(OceanHandTracking.TryBuildJointRay(frame,out Pose ray),"valid wrist-to-knuckle fallback");
            Check(Vector3.Distance(ray.position,new Vector3(.2f,1.2f,.4f))<.00001f,"XR coordinates are not flipped");
            Check(Vector3.Dot(ray.rotation*Vector3.forward,Vector3.forward)>.9999f,"fallback direction forward");
            var original=frame.jointLocations[7];frame.jointLocations[7]=default;
            Check(!OceanHandTracking.TryBuildJointRay(frame,out _),"missing index cannot fabricate aim");frame.jointLocations[7]=original;
            frame.isActive=0;Check(!OceanHandTracking.TryBuildJointRay(frame,out _),"inactive hand cannot aim");frame.isActive=1;
            frame.jointLocations[7].pose.position=frame.jointLocations[1].pose.position;
            Check(!OceanHandTracking.TryBuildJointRay(frame,out _),"zero bone length rejected");
            frame.jointLocations[7].pose.position=new Vector3(float.NaN,0,0);
            Check(!OceanHandTracking.TryBuildJointRay(frame,out _),"non-finite position rejected");
            var validate=typeof(HandSwimController).GetMethod("TryValidateMenuAim",BindingFlags.NonPublic|BindingFlags.Static);
            Check(validate!=null,"production aim validator");
            var aim=new OceanHandAim{aimStatus=OceanHandAimStatus.AimComputed|OceanHandAimStatus.AimRayValid,
                aimRayPose=new Pose(new Vector3(.2f,1.2f,.4f),Quaternion.identity),Source=OceanHandAimSource.MetaAim};
            object[] arguments={aim,null};Check((bool)validate.Invoke(null,arguments),"valid Meta ray passes");
            aim.aimStatus|=OceanHandAimStatus.SystemGesture;arguments[0]=aim;
            Check(!(bool)validate.Invoke(null,arguments),"Meta system gesture blocked before menu press");
            aim.aimStatus=OceanHandAimStatus.AimComputed;arguments[0]=aim;
            Check(!(bool)validate.Invoke(null,arguments),"computed without ray validity rejected");
#if ENABLE_PICO_XR_SDK && !OCEAN_XR_HANDS_ONLY
            // Raw SDK fields are deliberate: this catches a Z/quaternion conversion regression,
            // unlike feeding already-converted Unity poses into an otherwise passing gameplay test.
            var raw=new ByteDance.PICO.XR.HandJointLocation{
                pose=new ByteDance.PICO.XR.Posef{
                    Position=new ByteDance.PICO.XR.Vector3f{x=.2f,y=1.2f,z=-.4f},
                    Orientation=new ByteDance.PICO.XR.Quatf{x=.1f,y=.2f,z=.3f,w=.9f}},
                locationStatus=ByteDance.PICO.XR.HandLocationStatus.PositionValid|ByteDance.PICO.XR.HandLocationStatus.PositionTracked,
                radius=.012f};
            var converted=OceanPicoHandTracking.ConvertJoint(raw);
            Check(converted.pose.position.z==.4f && converted.pose.position.x==.2f,"PICO native position Z flip");
            Check(converted.pose.rotation.x==.1f && converted.pose.rotation.y==.2f && converted.pose.rotation.z==-.3f && converted.pose.rotation.w==-.9f,"PICO native quaternion Z/W flip");
            Check(converted.locationStatus==flags && converted.radius==.012f,"PICO validity and radius retained");
            raw.locationStatus=ByteDance.PICO.XR.HandLocationStatus.PositionValid;
            Check((OceanPicoHandTracking.ConvertJoint(raw).locationStatus&OceanHandLocationStatus.PositionTracked)==0,"valid-only PICO position never promoted to tracked");
            var nativeFrame=new ByteDance.PICO.XR.HandJointLocations{isActive=1,jointCount=26,handScale=1,jointLocations=new ByteDance.PICO.XR.HandJointLocation[26]};
            nativeFrame.jointLocations[0]=raw;var dest=OceanHandFrame.Allocate();OceanPicoHandTracking.ConvertFrame(nativeFrame,ref dest);
            Check(dest.jointCount==26 && dest.jointLocations[0].pose.position.z==.4f,"PICO frame adapter retains Palm index0");
            nativeFrame.jointCount=1;OceanPicoHandTracking.ConvertFrame(nativeFrame,ref dest);
            Check(dest.jointLocations[1].locationStatus==0,"short native frame clears stale joints");
#endif
            Debug.Log("OceanHandTrackingTests PASS: "+Assertions+" synthetic adapter assertions; no live tracking claim.");
        }
    }
}
#endif
