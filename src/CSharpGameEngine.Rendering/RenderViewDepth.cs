using System.Numerics;
namespace CSharpGameEngine.Rendering;
/// <summary>ビューの投影が指定する深度符号化方式（ADR-0084）。</summary>
public enum RenderDepthEncoding:byte {
    Linear=0,
    Logarithmic=1,
    Observer=2,
}
/// <summary>検証済みの L4 深度パケット。透視ビューは物理的な対数距離を格納する。線形パケットには正射影が必要。</summary>
public readonly record struct RenderViewDepth {
    private RenderViewDepth(RenderDepthEncoding Encoding,RadialDepthProjection Projection,Vector4 Packet0,Vector4 Packet1){
        this.Encoding=Encoding;
        this.Projection=Projection;
        this.Packet0=Packet0;
        this.Packet1=Packet1;
        this.IsValid=true;
    }
    public bool IsValid { get; }
    public RenderDepthEncoding Encoding { get; }
    public RadialDepthProjection Projection { get; }
    public Vector4 Packet0 { get; }
    public Vector4 Packet1 { get; }
    public static RenderViewDepth ForPerspective(in RadialDepthProjection Projection,bool ConservativeRasterDepth=true){
        ValidateProjection(Projection);
        return new(RenderDepthEncoding.Logarithmic,Projection,LogarithmicPacket(RenderDepthEncoding.Logarithmic,Projection),new Vector4(0f,0f,0f,ConservativeRasterDepth?1f:0f));
    }
    public static RenderViewDepth ForOrthographic(in Matrix4x4 ViewProjection){
        if(!IsFinite(ViewProjection)||RadialDepthProjection.IsPerspective(ViewProjection)||ViewProjection.M44==0f
            ||!Matrix4x4.Invert(ViewProjection,out var Inverse)||!IsFinite(Inverse)){
            throw new ArgumentException("Linear view depth requires a finite orthographic projection.",nameof(ViewProjection));
        }
        var Z=new Vector3(ViewProjection.M13,ViewProjection.M23,ViewProjection.M33)/ViewProjection.M44;
        var Scale=MathF.Max(MathF.Abs(Z.X),MathF.Max(MathF.Abs(Z.Y),MathF.Abs(Z.Z)));
        var Slope=Scale>0f?Scale*(Z/Scale).Length():0f;
        var Offset=ViewProjection.M43/ViewProjection.M44;
        if(!(float.IsFinite(Slope)&&Slope>0f&&float.IsFinite(Offset))){
            throw new ArgumentException("Orthographic depth must have a finite nonzero range.",nameof(ViewProjection));
        }
        return new(RenderDepthEncoding.Linear,default,new Vector4(0f,Slope,Offset,0f),Vector4.Zero);
    }
    public static RenderViewDepth ForObserver(in RadialDepthProjection Projection,Vector3 Center){
        ValidateProjection(Projection);
        if(!float.IsFinite(Center.X)||!float.IsFinite(Center.Y)||!float.IsFinite(Center.Z)){
            throw new ArgumentException("An observer center must be finite.",nameof(Center));
        }
        return new(RenderDepthEncoding.Observer,Projection,LogarithmicPacket(RenderDepthEncoding.Observer,Projection),new Vector4(Center,0f));
    }
    private static Vector4 LogarithmicPacket(RenderDepthEncoding Encoding,in RadialDepthProjection Projection)=>
        new((float)Encoding,Projection.Curve.TransitionMeters,Projection.InverseMaximumG,Projection.FarDiscardDepth);
    private static void ValidateProjection(in RadialDepthProjection Projection){
        if(!(Projection.Curve.TransitionMeters>0f&&Projection.InverseMaximumG>0f&&float.IsFinite(Projection.InverseMaximumG)
            &&Projection.FarDiscardDepth>0f&&float.IsFinite(Projection.FarDiscardDepth))){
            throw new ArgumentException("Logarithmic view depth requires a validated physical projection.",nameof(Projection));
        }
    }
    private static bool IsFinite(in Matrix4x4 Matrix)=>
        float.IsFinite(Matrix.M11)&&float.IsFinite(Matrix.M12)&&float.IsFinite(Matrix.M13)&&float.IsFinite(Matrix.M14)
        &&float.IsFinite(Matrix.M21)&&float.IsFinite(Matrix.M22)&&float.IsFinite(Matrix.M23)&&float.IsFinite(Matrix.M24)
        &&float.IsFinite(Matrix.M31)&&float.IsFinite(Matrix.M32)&&float.IsFinite(Matrix.M33)&&float.IsFinite(Matrix.M34)
        &&float.IsFinite(Matrix.M41)&&float.IsFinite(Matrix.M42)&&float.IsFinite(Matrix.M43)&&float.IsFinite(Matrix.M44);
}
