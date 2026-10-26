using System.Numerics;
using CSharpGameEngine.Spatial.Abstractions;
using CSharpGameEngine.Spatial.Fbx;
using CSharpGameEngine.Spatial.Unity;

namespace CSharpGameEngine.Import.Fbx;

/// <summary>
/// AssimpNetの既定出力空間（<see cref="FbxCoordinateConvention"/>。右手系、Y-up、Forward=-Z。型の説明を参照）を、
/// Import境界でこのエンジン固定の<see cref="UnityCoordinateConvention"/>（左手系、Y-up）へ変換する
///（Docs/Decisions/ADR-0027-Gltf-Fbx-Import-Axis-Conversion.mdに従う）。
/// <see cref="FbxImporter"/>にはシーングラフもノードごとのワールド変換もなく、対象は単一メッシュに限られる
///（<c>CSharpGameEngine.Import.Gltf.GltfImporter.Import(string)</c>と同範囲）。したがって変換が必要なのはメッシュローカルの位置／法線と三角形の巻き順だけで、
/// <c>GltfAxisConversion.ConvertWorldTransform</c>のような回転行列の共役変換は不要。
/// </summary>
internal static class FbxAxisConversion {
    /// <summary>AssimpNet空間の点または方向を1つ、エンジン空間へ変換する。固定された2つの規約の間ではZの符号反転だけでよい。</summary>
    public static Vector3 ConvertVector(Vector3 AssimpSpaceValue){
        RufVector Ruf=Spatial<FbxCoordinateConvention>.FromXyz((XyzVector)AssimpSpaceValue);
        return(Vector3)Spatial<UnityCoordinateConvention>.ToXyz(Ruf);
    }

    /// <summary>
    /// 単一軸の位置ミラー後も表面向きの巻き順を保つため、各三角形の2つのインデックスを入れ替える。GltfAxisConversion.FlipTriangleWindingと同じ理由。現状、すべての利用側
    /// で裏面カリングを無効にしているため、表示への影響はない。
    /// </summary>
    public static void FlipTriangleWinding(List<uint>Indices){
        for(var I=0;I+2<Indices.Count;I+=3){
            (Indices[I+1],Indices[I+2])=(Indices[I+2],Indices[I+1]);
        }
    }
}
