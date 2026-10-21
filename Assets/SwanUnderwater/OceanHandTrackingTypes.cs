using System;
using UnityEngine;

namespace SwanUnderwater
{
    public enum OceanHandSide { HandLeft, HandRight }
    // Stable project order. XR Hands reverses the first two joints; never cast SDK enum indices.
    public enum OceanHandJointId
    {
        JointPalm, JointWrist, JointThumbMetacarpal, JointThumbProximal, JointThumbDistal, JointThumbTip,
        JointIndexMetacarpal, JointIndexProximal, JointIndexIntermediate, JointIndexDistal, JointIndexTip,
        JointMiddleMetacarpal, JointMiddleProximal, JointMiddleIntermediate, JointMiddleDistal, JointMiddleTip,
        JointRingMetacarpal, JointRingProximal, JointRingIntermediate, JointRingDistal, JointRingTip,
        JointLittleMetacarpal, JointLittleProximal, JointLittleIntermediate, JointLittleDistal, JointLittleTip, JointMax
    }
    [Flags] public enum OceanHandLocationStatus : ulong
    { None=0, OrientationValid=1, PositionValid=2, OrientationTracked=4, PositionTracked=8 }
    [Flags] public enum OceanHandAimStatus : ulong
    {
        None=0, AimComputed=1, AimRayValid=2, AimIndexPinching=4, AimMiddlePinching=8,
        AimRingPinching=16, AimLittlePinching=32, SystemGesture=64, AimRayTouched=512
    }
    public enum OceanHandAimSource { None, Pico, MetaAim, JointRay }
    public struct OceanHandJoint
    {
        public OceanHandLocationStatus locationStatus;
        // Unity coordinates, relative to tracking space, on every backend.
        public Pose pose;
        public float radius;
    }
    public struct OceanHandFrame
    {
        public uint isActive, jointCount;
        public float handScale;
        public int SampleFrame;
        // Borrowed provider array, read-only to callers. Gameplay and before-render use separate arrays.
        public OceanHandJoint[] jointLocations;
        public static OceanHandFrame Allocate() => new OceanHandFrame
        { jointLocations=new OceanHandJoint[26], jointCount=26, handScale=1, SampleFrame=-1 };
    }
    public struct OceanHandAim
    {
        public OceanHandAimStatus aimStatus;
        public Pose aimRayPose;
        public float touchStrengthRay;
        public OceanHandAimSource Source;
    }
}
