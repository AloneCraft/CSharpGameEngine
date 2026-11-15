using System.Numerics;
namespace CSharpGameEngine.Rendering;
/// <summary>メッシュ送信経路間で共有する、移植可能なジオメトリ準備処理。</summary>
public static class StageMeshGeometry {
    /// <summary>法線マップの UV 配置に沿った接線がないメッシュで使う、法線と垂直な任意の単位接線。</summary>
    public static Vector4 ArbitraryTangentFor(Vector3 Normal){
        var Reference=MathF.Abs(Normal.Y)<0.99f?Vector3.UnitY:Vector3.UnitX;
        var Tangent=Vector3.Normalize(Vector3.Cross(Reference,Normal));
        return new Vector4(Tangent,1f);
    }
}
