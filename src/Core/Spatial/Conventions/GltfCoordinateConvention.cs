using CSharpGameEngine.Spatial.Abstractions;

namespace CSharpGameEngine.Spatial.Gltf;

/// <summary>
/// glTF 2.0ファイル形式独自の、格納済み座標規約。右手系、+Yが上、単位はメートル。
/// <see cref="ForwardXyz"/>は-Z。glTFカメラと<c>KHR_lights_punctual</c>のライトは、仕様上、ノードのローカル-Z方向を向くためである
///（Docs/Decisions/ADR-0027-Gltf-Fbx-Import-Axis-Conversion.mdを参照）。
/// これはUnity／Unreal／Blenderのアセット作成ツール用プリセット（ADR-0025）ではなく、glTF Import境界
///（<c>CSharpGameEngine.Import.Gltf</c>）で、このエンジン固定の
/// <see cref="CSharpGameEngine.Spatial.Unity.UnityCoordinateConvention"/>（ADR-0026）へ変換するためだけに使う、ファイル形式本来の軸を表す。
/// 選択可能な<c>SpatialPreset</c>としては公開しない。
/// </summary>
public readonly struct GltfCoordinateConvention:ICoordinateConvention {
    public static CoordinateHandedness Handedness=>CoordinateHandedness.RightHanded;

    public static XyzVector ToXyz(in RufVector Value)=>new(Value.R,Value.U,-Value.F);

    public static RufVector FromXyz(in XyzVector Value)=>new(Value.X,Value.Y,-Value.Z);

    public static XyzVector RightXyz=>new(1f,0f,0f);

    public static XyzVector UpXyz=>new(0f,1f,0f);

    public static XyzVector ForwardXyz=>new(0f,0f,-1f);
}
