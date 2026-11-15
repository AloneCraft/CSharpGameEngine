using System.Numerics;
using CSharpGameEngine.Spatial.Abstractions;
using CSharpGameEngine.Spatial.Gltf;
using CSharpGameEngine.Spatial.Unity;

namespace CSharpGameEngine.Import.Gltf;

/// <summary>
/// glTF ファイル空間のデータ（<see cref="GltfCoordinateConvention"/>：右手系、Y-up）を、Import 境界でこのエンジンの固定規約 <see cref="UnityCoordinateConvention"/>（左手系、Y-up）へ変換する。規則は Docs/Decisions/ADR-0027-Gltf-Fbx-Import-Axis-Conversion.md に従う。<see cref="GltfImporter.ImportScene"/> が返すもの（メッシュローカルの頂点データとノードごとのワールド変換）は、このプロジェクトを出る時点ですでにエンジン空間にある。ほとんどの利用側（<c>StageRenderer</c> など）は glTF 独自の規約を知る必要がない。
/// <c>UserGameProject.SceneLoader</c> も正当な第 2 の呼び出し元であるため、internal ではなく public とする。<c>scene_meta.json</c> の PlayerStart は <c>tools/UnrealExport/export_lvl_firstperson.py</c> が独自の UE-&gt;glTF 変換で glTF 空間に書き込む。そのため <see cref="GltfImporter"/> を経由しなくても、ここで同じ glTF-&gt;engine 変換を適用する必要がある。従来の単一プリミティブ用 <see cref="GltfImporter.Import(string)"/> プレビュー経路では使わない（対象外。ADR-0027 の「対象外」を参照）。
/// </summary>
public static class GltfAxisConversion {
    /// <summary>glTF空間の点または方向を1つエンジン空間へ変換する。ここではどちらも単純な線形写像で、別扱いする平行移動成分はない。固定された2つの規約の間ではZの符号反転だけでよい。</summary>
    public static Vector3 ConvertVector(Vector3 GltfSpaceValue){
        RufVector Ruf=Spatial<GltfCoordinateConvention>.FromXyz((XyzVector)GltfSpaceValue);
        return(Vector3)Spatial<UnityCoordinateConvention>.ToXyz(Ruf);
    }

    /// <summary>
    /// glTF接線（xyz=接線方向、w=従接線の符号）をエンジン空間へ変換する。xyz部分は通常の方向と同様に変換する。
    /// 単一軸のミラーは行列式が-1となり、<c>bitangent = cross(normal, tangent) * w</c>の関係における向きが反転するため、
    /// 補正としてwの符号も反転する（ADR-0027で代数的に検証済み。任意の直交ミラーについて
    /// cross(mirror(a), mirror(b)) = -mirror(cross(a, b))）。
    /// </summary>
    public static Vector4 ConvertTangent(Vector4 GltfSpaceTangent){
        Vector3 Converted=ConvertVector(new Vector3(GltfSpaceTangent.X,GltfSpaceTangent.Y,GltfSpaceTangent.Z));
        return new Vector4(Converted,-GltfSpaceTangent.W);
    }

    /// <summary>
    /// glTF空間のワールド変換（平行移動／回転／スケール）をエンジン空間へ変換する。
    /// 平行移動は点として変換し（<see cref="ConvertVector"/>）、単一軸ミラーでもスケールの大きさは変わらないため、そのまま使う。
    /// 回転には反射に対する標準的な相似変換<c>R' = C * R * C</c>（<c>C = diag(1, 1, -1)</c>）が必要。
    /// 回転クォータニオンのZだけを単純に反転する方法は、適正回転（行列式+1）の場合にしか正しくない。
    /// 再合成には<see cref="StandardTransformConvention.Compose"/>を使い、エンジン全体と同じScale→Rotation→Translation規約を維持する（ADR-0025）。
    /// </summary>
    public static Matrix4x4 ConvertWorldTransform(Matrix4x4 GltfSpaceWorldTransform){
        if(!Matrix4x4.Decompose(GltfSpaceWorldTransform,out Vector3 Scale,out Quaternion Rotation,out Vector3 Translation)){
            //（特異行列や退化行列など）分解できないソース行列は、破棄せず未変換のまま渡す。
            // インポート時にエンジンが別途検証しないコンテンツに対する、
            // 最善努力でのフォールバックである。
            return GltfSpaceWorldTransform;
        }

        Vector3 ConvertedTranslation=ConvertVector(Translation);
        Quaternion ConvertedRotation=MirrorRotationAroundZ(Rotation);

        RufVector TranslationRuf=new(ConvertedTranslation.X,ConvertedTranslation.Y,ConvertedTranslation.Z);
        RufQuaternion RotationRuf=new(ConvertedRotation.X,ConvertedRotation.Y,ConvertedRotation.Z,ConvertedRotation.W);
        RufVector ScaleRuf=new(Scale.X,Scale.Y,Scale.Z);

        return StandardTransformConvention.Compose(TranslationRuf,RotationRuf,ScaleRuf);
    }

    /// <summary>glTF空間の回転を変換する。<c>C = diag(1, 1, -1)</c>に対してミラー<c>C * R * C</c>を適用する。角度を保ち、軸を(-x, -y, z)へ写す。</summary>
    public static Quaternion ConvertRotation(Quaternion GltfSpaceRotation)=>new(-GltfSpaceRotation.X,-GltfSpaceRotation.Y,GltfSpaceRotation.Z,GltfSpaceRotation.W);

    /// <summary>行ベクトル規約でのglTF空間アフィン行列を、逆バインド行列など分解できないものも含めて<c>C * M * C</c>で変換する。</summary>
    public static Matrix4x4 ConvertMatrix(Matrix4x4 GltfSpaceMatrix){
        var C=Matrix4x4.CreateScale(1f,1f,-1f);
        return C*GltfSpaceMatrix*C;
    }

    /// <summary>
    /// 単一軸の位置ミラーで反転する各三角形の向きを保つため、2つのインデックスを入れ替える（GltfAxisConversion.FlipTriangleWindingと同じ）。現在はすべての利用側で裏面
    /// カリングを無効にしているため表示に影響しないが、将来設定を変えた場合も巻き順を正しく保ち、既に反転した接線／法線データとも三角形の向きを一致させる。
    /// </summary>
    public static void FlipTriangleWinding(uint[]Indices){
        for(var I=0;I+2<Indices.Length;I+=3){
            (Indices[I+1],Indices[I+2])=(Indices[I+2],Indices[I+1]);
        }
    }

    private static Quaternion MirrorRotationAroundZ(Quaternion Rotation){
        Matrix4x4 R=Matrix4x4.CreateFromQuaternion(Rotation);
        Matrix4x4 Mirrored=new(
            R.M11,R.M12,-R.M13,0f,
            R.M21,R.M22,-R.M23,0f,
            -R.M31,-R.M32,R.M33,0f,
            0f,0f,0f,1f);
        return Quaternion.CreateFromRotationMatrix(Mirrored);
    }
}
