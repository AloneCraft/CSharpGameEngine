using CSharpGameEngine.Spatial.Abstractions;

namespace CSharpGameEngine.Spatial.Blender;

/// <summary>
/// Blender のワールド空間との交換用マッピング：R=+X、U=+Z、F=+Y、右手系 (Docs/Specifications/Spatial-RUF.md の「Blender交換用」を参照)。Blender 固有のワールド軸だけを扱う。エクスポート固有の Forward/Up 設定、メッシュローカルの前方、カメラのローカル視線方向、ライトのローカル方向は意図的に分離している（この型 1 つでは表現しない。今期 <c>BlenderGltfExportConvention</c>/<c>BlenderFbxExportConvention</c> を実装しない理由は「未確定事項」を参照）。<see cref="ToXyz"/>/<see cref="FromXyz"/> は固定の成分並べ替えであり、分岐も割り当てもない。
/// </summary>
public readonly struct BlenderWorldConvention:ICoordinateConvention {
    public static CoordinateHandedness Handedness=>CoordinateHandedness.RightHanded;

    public static XyzVector ToXyz(in RufVector Value)=>new(Value.R,Value.F,Value.U);

    public static RufVector FromXyz(in XyzVector Value)=>new(Value.X,Value.Z,Value.Y);

    public static XyzVector RightXyz=>new(1f,0f,0f);

    public static XyzVector UpXyz=>new(0f,0f,1f);

    public static XyzVector ForwardXyz=>new(0f,1f,0f);
}
