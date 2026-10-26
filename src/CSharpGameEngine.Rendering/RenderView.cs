using System.Numerics;
namespace CSharpGameEngine.Rendering;
/// <summary>ライトの区分けと観測者マーカーで使う、任意の物理カメラ基底。</summary>
public readonly record struct RenderViewBasis {
    private RenderViewBasis(Vector3 Right,Vector3 Up,Vector3 Forward,float TanHalfFovY,float AspectRatio){
        this.Right=Right;
        this.Up=Up;
        this.Forward=Forward;
        this.TanHalfFovY=TanHalfFovY;
        this.AspectRatio=AspectRatio;
        this.IsValid=true;
    }
    public bool IsValid { get; }
    public Vector3 Right { get; }
    public Vector3 Up { get; }
    public Vector3 Forward { get; }
    public float TanHalfFovY { get; }
    public float AspectRatio { get; }
    public static RenderViewBasis Create(Vector3 Right,Vector3 Up,Vector3 Forward,float TanHalfFovY,float AspectRatio){
        if(!Usable(Right)||!Usable(Up)||!Usable(Forward)
            ||!float.IsFinite(TanHalfFovY)||TanHalfFovY<=0f||!float.IsFinite(AspectRatio)||AspectRatio<=0f){
            throw new ArgumentException("A camera basis must have finite nonzero axes and positive finite projection scales.");
        }
        if(MathF.Abs(Right.LengthSquared()-1f)>1e-4f||MathF.Abs(Up.LengthSquared()-1f)>1e-4f||MathF.Abs(Forward.LengthSquared()-1f)>1e-4f
            ||MathF.Abs(Vector3.Dot(Right,Up))>1e-4f||MathF.Abs(Vector3.Dot(Right,Forward))>1e-4f||MathF.Abs(Vector3.Dot(Up,Forward))>1e-4f){
            throw new ArgumentException("Camera axes must be orthonormal.");
        }
        return new(Right,Up,Forward,TanHalfFovY,AspectRatio);
    }
    private static bool Usable(Vector3 Value)=>float.IsFinite(Value.X)&&float.IsFinite(Value.Y)&&float.IsFinite(Value.Z)
        &&float.IsFinite(Value.LengthSquared())&&Value.LengthSquared()>0f;
}
/// <summary>検証済みでポータブルなメインビュー送信データ。カメラ制御器やネイティブバインディングは含まない。</summary>
public readonly record struct RenderView {
    private RenderView(Matrix4x4 ViewProjection,Matrix4x4 AuxiliaryViewProjection,Matrix4x4 InverseViewProjection,
        Vector3 CameraPosition,float ExposureEv100,RenderViewDepth Depth,RenderViewBasis?Basis){
        this.ViewProjection=ViewProjection;
        this.AuxiliaryViewProjection=AuxiliaryViewProjection;
        this.InverseViewProjection=InverseViewProjection;
        this.CameraPosition=CameraPosition;
        this.ExposureEv100=ExposureEv100;
        this.Depth=Depth;
        this.Basis=Basis;
        this.IsValid=true;
    }
    public bool IsValid { get; }
    public Matrix4x4 ViewProjection { get; }
    public Matrix4x4 AuxiliaryViewProjection { get; }
    public Matrix4x4 InverseViewProjection { get; }
    public Vector3 CameraPosition { get; }
    public float ExposureEv100 { get; }
    public RenderViewDepth Depth { get; }
    public RenderViewBasis?Basis { get; }
    public static RenderView Create(in Matrix4x4 ViewProjection,in Matrix4x4 AuxiliaryViewProjection,
        Vector3 CameraPosition,float ExposureEv100,in RenderViewDepth Depth,RenderViewBasis?Basis=null){
        if(!Depth.IsValid||Depth.Encoding is not (RenderDepthEncoding.Linear or RenderDepthEncoding.Logarithmic)
            ||RadialDepthProjection.IsPerspective(ViewProjection)!=(Depth.Encoding==RenderDepthEncoding.Logarithmic)){
            throw new ArgumentException("Main-view projection and depth encoding must agree.",nameof(Depth));
        }
        if(!Finite(ViewProjection)||!Finite(AuxiliaryViewProjection)
            ||!Matrix4x4.Invert(AuxiliaryViewProjection,out var Inverse)||!Finite(Inverse)){
            throw new ArgumentException("A view needs finite matrices and an invertible auxiliary projection.",nameof(ViewProjection));
        }
        if(ViewProjection.M11!=AuxiliaryViewProjection.M11||ViewProjection.M21!=AuxiliaryViewProjection.M21
            ||ViewProjection.M31!=AuxiliaryViewProjection.M31||ViewProjection.M41!=AuxiliaryViewProjection.M41
            ||ViewProjection.M12!=AuxiliaryViewProjection.M12||ViewProjection.M22!=AuxiliaryViewProjection.M22
            ||ViewProjection.M32!=AuxiliaryViewProjection.M32||ViewProjection.M42!=AuxiliaryViewProjection.M42
            ||ViewProjection.M14!=AuxiliaryViewProjection.M14||ViewProjection.M24!=AuxiliaryViewProjection.M24
            ||ViewProjection.M34!=AuxiliaryViewProjection.M34||ViewProjection.M44!=AuxiliaryViewProjection.M44
            ||(Depth.Encoding==RenderDepthEncoding.Linear&&ViewProjection!=AuxiliaryViewProjection)){
            throw new ArgumentException("Auxiliary reconstruction must preserve the view rays.",nameof(AuxiliaryViewProjection));
        }
        if(!float.IsFinite(CameraPosition.X)||!float.IsFinite(CameraPosition.Y)||!float.IsFinite(CameraPosition.Z)){
            throw new ArgumentException("Camera position must be finite.",nameof(CameraPosition));
        }
        if(!float.IsFinite(ExposureEv100)){ throw new ArgumentOutOfRangeException(nameof(ExposureEv100)); }
        if(Basis is { IsValid:false }){ throw new ArgumentException("A supplied camera basis must be validated.",nameof(Basis)); }
        if(Basis is { }CameraBasis){
            if(Depth.Encoding!=RenderDepthEncoding.Logarithmic){ throw new ArgumentException("A perspective basis requires a perspective view.",nameof(Basis)); }
            var Right=new Vector3(ViewProjection.M11,ViewProjection.M21,ViewProjection.M31);
            var Up=new Vector3(ViewProjection.M12,ViewProjection.M22,ViewProjection.M32);
            var Forward=new Vector3(ViewProjection.M14,ViewProjection.M24,ViewProjection.M34);
            if(Vector3.Dot(Vector3.Normalize(Right),CameraBasis.Right)<1f-1e-4f
                ||Vector3.Dot(Vector3.Normalize(Up),CameraBasis.Up)<1f-1e-4f
                ||Vector3.Dot(Vector3.Normalize(Forward),CameraBasis.Forward)<1f-1e-4f
                ||MathF.Abs(Up.Length()*CameraBasis.TanHalfFovY-1f)>1e-4f
                ||MathF.Abs(Right.Length()*CameraBasis.AspectRatio/Up.Length()-1f)>1e-4f){
                throw new ArgumentException("Camera basis and projection rays must agree.",nameof(Basis));
            }
        }
        if(Depth.Encoding==RenderDepthEncoding.Linear&&Depth!=RenderViewDepth.ForOrthographic(ViewProjection)){
            throw new ArgumentException("Linear depth must describe the submitted orthographic projection.",nameof(Depth));
        }
        return new(ViewProjection,AuxiliaryViewProjection,Inverse,CameraPosition,ExposureEv100,Depth,Basis);
    }
    private static bool Finite(in Matrix4x4 Value)=>
        float.IsFinite(Value.M11)&&float.IsFinite(Value.M12)&&float.IsFinite(Value.M13)&&float.IsFinite(Value.M14)
        &&float.IsFinite(Value.M21)&&float.IsFinite(Value.M22)&&float.IsFinite(Value.M23)&&float.IsFinite(Value.M24)
        &&float.IsFinite(Value.M31)&&float.IsFinite(Value.M32)&&float.IsFinite(Value.M33)&&float.IsFinite(Value.M34)
        &&float.IsFinite(Value.M41)&&float.IsFinite(Value.M42)&&float.IsFinite(Value.M43)&&float.IsFinite(Value.M44);
}
