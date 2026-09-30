using System.Numerics;
namespace CSharpGameEngine.Import.Gltf;
/// <summary>
/// One KHR_texture_transform: uv' = Offset + R * (Scale * uv) with R = [cos sin; -sin cos], the
/// matrix order the extension specifies (translation * rotation * scale).
/// </summary>
internal readonly record struct GltfUvTransform(Vector2 Offset,Vector2 Scale,float Rotation){
    public Vector2 Apply(Vector2 Uv){
        var Scaled=Uv*this.Scale;
        var (Sin,Cos)=MathF.SinCos(this.Rotation);
        return this.Offset+new Vector2(Cos*Scaled.X+Sin*Scaled.Y,-Sin*Scaled.X+Cos*Scaled.Y);
    }
    /// <summary>Source tangents follow +u; they stay valid only when the transform neither rotates nor mirrors.</summary>
    public bool PreservesTangentFrame=>this.Rotation==0f&&this.Scale.X>0f&&this.Scale.Y>0f;
}
