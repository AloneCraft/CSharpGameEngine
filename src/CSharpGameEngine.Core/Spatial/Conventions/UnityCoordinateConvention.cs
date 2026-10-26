using CSharpGameEngine.Spatial.Abstractions;

namespace CSharpGameEngine.Spatial.Unity;

/// <summary>
/// Unity 互換の物理マッピング：R=+X、U=+Y、F=+Z、左手系 (Docs/Specifications/Spatial-RUF.md の「Unity互換」を参照)。成分の並べ替えを行わない恒等マッピングなので、<see cref="ToXyz"/>/<see cref="FromXyz"/> はそのままインライン化でき、分岐もない。
/// </summary>
public readonly struct UnityCoordinateConvention:ICoordinateConvention {
    public static CoordinateHandedness Handedness=>CoordinateHandedness.LeftHanded;

    public static XyzVector ToXyz(in RufVector Value)=>new(Value.R,Value.U,Value.F);

    public static RufVector FromXyz(in XyzVector Value)=>new(Value.X,Value.Y,Value.Z);

    public static XyzVector RightXyz=>new(1f,0f,0f);

    public static XyzVector UpXyz=>new(0f,1f,0f);

    public static XyzVector ForwardXyz=>new(0f,0f,1f);
}
