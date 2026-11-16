using CSharpGameEngine.Spatial.Abstractions;

namespace CSharpGameEngine.Spatial.Unreal;

/// <summary>
/// Unreal 互換の物理マッピング：R=+Y、U=+Z、F=+X、左手系 (Docs/Specifications/Spatial-RUF.md の「Unreal互換」を参照)。<see cref="ToXyz"/>/<see cref="FromXyz"/> は固定の成分並べ替えであり、分岐も割り当てもない。
/// </summary>
public readonly struct UnrealCoordinateConvention:ICoordinateConvention {
    public static CoordinateHandedness Handedness=>CoordinateHandedness.LeftHanded;

    public static XyzVector ToXyz(in RufVector Value)=>new(Value.F,Value.R,Value.U);

    public static RufVector FromXyz(in XyzVector Value)=>new(Value.Y,Value.Z,Value.X);

    public static XyzVector RightXyz=>new(0f,1f,0f);

    public static XyzVector UpXyz=>new(0f,0f,1f);

    public static XyzVector ForwardXyz=>new(1f,0f,0f);
}
