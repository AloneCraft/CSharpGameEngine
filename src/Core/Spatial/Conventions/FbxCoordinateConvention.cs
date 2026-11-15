using CSharpGameEngine.Spatial.Abstractions;

namespace CSharpGameEngine.Spatial.Fbx;

/// <summary>
/// AssimpNetが既定で出力する座標規約。Assimp内部で形式ごとの軸を正規化するため、FBXを含むすべてのインポート形式に適用される。
/// 右手系、+Yが上、+Zがビューアー側を向く（AssimpNetの<c>PostProcessSteps.MakeLeftHanded</c> XML説明では、
/// 既定で+Xが右、+Zがビューアー側、+Yが上の右手座標系として返すとされている）。「+Zがビューアー側」はForwardが-Zであることを意味し、
/// <see cref="CSharpGameEngine.Spatial.Gltf.GltfCoordinateConvention"/>と数学的に同一である。偶然ではなく（glTF自体が右手系／Y-up／-Z-forward）、
/// それでも別の型として定義する。これにより<c>CSharpGameEngine.Import.Fbx</c>が<c>CSharpGameEngine.Import.Gltf</c>に依存せず、
/// 将来Assimpが同じように正規化しない形式を扱う場合も、型名「Gltf」を変更せずに済む。
/// Docs/Decisions/ADR-0027-Gltf-Fbx-Import-Axis-Conversion.mdを参照。
/// </summary>
public readonly struct FbxCoordinateConvention:ICoordinateConvention {
    public static CoordinateHandedness Handedness=>CoordinateHandedness.RightHanded;

    public static XyzVector ToXyz(in RufVector Value)=>new(Value.R,Value.U,-Value.F);

    public static RufVector FromXyz(in XyzVector Value)=>new(Value.X,Value.Y,-Value.Z);

    public static XyzVector RightXyz=>new(1f,0f,0f);

    public static XyzVector UpXyz=>new(0f,1f,0f);

    public static XyzVector ForwardXyz=>new(0f,0f,-1f);
}
