#if ENABLE_PICO_XR_SDK && !OCEAN_XR_HANDS_ONLY
using ByteDance.PICO.XR;
using UnityEngine;

namespace SwanUnderwater
{
    /// <summary>The only hand-tracking adapter that knows native PICO coordinates/types.</summary>
    public static class OceanPicoHandTracking
    {
        static HandJointLocations left, right;
        public static bool Read(OceanHandSide side, ref OceanHandFrame destination)
        {
            bool ok = side==OceanHandSide.HandLeft
                ? PXR_HandTracking.GetJointLocations(HandType.HandLeft,ref left)
                : PXR_HandTracking.GetJointLocations(HandType.HandRight,ref right);
            if(!ok){destination.isActive=0;destination.SampleFrame=-1;return false;}
            ConvertFrame(side==OceanHandSide.HandLeft?left:right,ref destination);
            destination.SampleFrame=Time.frameCount;
            return destination.isActive!=0;
        }
        public static void ConvertFrame(HandJointLocations source, ref OceanHandFrame destination)
        {
            if(destination.jointLocations==null)destination=OceanHandFrame.Allocate();
            destination.isActive=source.isActive;
            destination.handScale=source.handScale;
            destination.jointCount=(uint)Mathf.Min(26,source.jointLocations==null?0:Mathf.Min((int)source.jointCount,source.jointLocations.Length));
            for(int i=0;i<26;i++) destination.jointLocations[i]=i<destination.jointCount
                ? ConvertJoint(source.jointLocations[i]) : default;
        }
        public static OceanHandJoint ConvertJoint(HandJointLocation source) => new OceanHandJoint
        {
            // These SDK conversion methods encode PICO's native-to-Unity handedness contract.
            pose=new Pose(source.pose.Position.ToVector3(),source.pose.Orientation.ToQuat()),
            locationStatus=(OceanHandLocationStatus)(ulong)source.locationStatus, radius=source.radius
        };
        public static OceanHandAim ConvertAim(HandAimState source) => new OceanHandAim
        {
            aimStatus=(OceanHandAimStatus)(ulong)source.aimStatus,
            aimRayPose=new Pose(source.aimRayPose.Position.ToVector3(),source.aimRayPose.Orientation.ToQuat()),
            touchStrengthRay=source.touchStrengthRay, Source=OceanHandAimSource.Pico
        };
        public static bool ReadAim(OceanHandSide side,out OceanHandAim result)
        {
            HandAimState raw=default;
            bool ok=PXR_HandTracking.GetAimState(side==OceanHandSide.HandLeft?HandType.HandLeft:HandType.HandRight,ref raw);
            result=ok?ConvertAim(raw):default;return ok;
        }
    }
}
#endif
