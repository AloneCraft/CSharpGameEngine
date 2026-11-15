using System.Numerics;
namespace CSharpGameEngine.Import.Gltf;
/// <summary>
/// 1つのKHR_texture_transform：uv' = Offset + R * (Scale * uv)、R = [cos sin; -sin cos]。この行列順は拡張仕様が定める順序である
///（translation * rotation * scale）。
/// </summary>
internal readonly record struct GltfUvTransform(Vector2 Offset,Vector2 Scale,float Rotation){
    public Vector2 Apply(Vector2 Uv){
        var Scaled=Uv*this.Scale;
        var (Sin,Cos)=MathF.SinCos(this.Rotation);
        return this.Offset+new Vector2(Cos*Scaled.X+Sin*Scaled.Y,-Sin*Scaled.X+Cos*Scaled.Y);
    }
    /// <summary>ソース接線は+u方向に沿うため、変換による回転やミラーがない場合に限り有効なまま使える。</summary>
    public bool PreservesTangentFrame=>this.Rotation==0f&&this.Scale.X>0f&&this.Scale.Y>0f;
}
