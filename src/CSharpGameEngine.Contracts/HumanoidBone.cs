namespace CSharpGameEngine.Contracts;
/// <summary>VRM 1.0の55種類のヒューマンボーン役割。値は安定したエンジン識別子。</summary>
public enum HumanoidBone:byte {
    Hips,Spine,Chest,UpperChest,Neck,Head,LeftEye,RightEye,Jaw,
    LeftUpperLeg,LeftLowerLeg,LeftFoot,LeftToes,RightUpperLeg,RightLowerLeg,RightFoot,RightToes,
    LeftShoulder,LeftUpperArm,LeftLowerArm,LeftHand,RightShoulder,RightUpperArm,RightLowerArm,RightHand,
    LeftThumbMetacarpal,LeftThumbProximal,LeftThumbDistal,
    LeftIndexProximal,LeftIndexIntermediate,LeftIndexDistal,
    LeftMiddleProximal,LeftMiddleIntermediate,LeftMiddleDistal,
    LeftRingProximal,LeftRingIntermediate,LeftRingDistal,
    LeftLittleProximal,LeftLittleIntermediate,LeftLittleDistal,
    RightThumbMetacarpal,RightThumbProximal,RightThumbDistal,
    RightIndexProximal,RightIndexIntermediate,RightIndexDistal,
    RightMiddleProximal,RightMiddleIntermediate,RightMiddleDistal,
    RightRingProximal,RightRingIntermediate,RightRingDistal,
    RightLittleProximal,RightLittleIntermediate,RightLittleDistal
}
public static class HumanoidBones {
    public static IReadOnlyList<HumanoidBone>Required{get;}=Array.AsReadOnly(new[]{
        HumanoidBone.Hips,HumanoidBone.Spine,HumanoidBone.Head,
        HumanoidBone.LeftUpperLeg,HumanoidBone.LeftLowerLeg,HumanoidBone.LeftFoot,
        HumanoidBone.RightUpperLeg,HumanoidBone.RightLowerLeg,HumanoidBone.RightFoot,
        HumanoidBone.LeftUpperArm,HumanoidBone.LeftLowerArm,HumanoidBone.LeftHand,
        HumanoidBone.RightUpperArm,HumanoidBone.RightLowerArm,HumanoidBone.RightHand
    });
}
