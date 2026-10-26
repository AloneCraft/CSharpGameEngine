using System.Numerics;
namespace CSharpGameEngine.Rendering;
/// <summary>呼び出し側が値を所有するメインビュー入力。解決時に検証し、置き換えた描画面のアスペクト比を適用できる。</summary>
public readonly record struct RenderViewSource {
    private enum SourceKind:byte { Empty,Fixed,Perspective,PerspectiveMatrix }
    private readonly SourceKind Kind;
    private readonly RenderView FixedView;
    private readonly Matrix4x4 Transform;
    private readonly Vector3 CameraPosition;
    private readonly RadialViewParameters Parameters;
    private readonly float ExposureEv100;
    private readonly float AspectRatioOverride;
    private RenderViewSource(SourceKind Kind,RenderView FixedView,Matrix4x4 Transform,Vector3 CameraPosition,
        RadialViewParameters Parameters,float ExposureEv100,float AspectRatioOverride){
        this.Kind=Kind;
        this.FixedView=FixedView;
        this.Transform=Transform;
        this.CameraPosition=CameraPosition;
        this.Parameters=Parameters;
        this.ExposureEv100=ExposureEv100;
        this.AspectRatioOverride=AspectRatioOverride;
    }
    public bool HasValue=>this.Kind!=SourceKind.Empty;
    public static RenderViewSource Fixed(in RenderView View){
        if(!View.IsValid){ throw new ArgumentException("A fixed view must be validated.",nameof(View)); }
        return new(SourceKind.Fixed,View,default,default,default,0f,0f);
    }
    /// <summary>物理的なワールド・ビュー行列を保持する。正の上書き値があればアスペクト比を固定し、なければ描画面のアスペクト比を使う。</summary>
    public static RenderViewSource Perspective(in Matrix4x4 WorldToView,Vector3 CameraPosition,in RadialViewParameters Parameters,
        float ExposureEv100=0f,float AspectRatioOverride=0f)=>
        new(SourceKind.Perspective,default,WorldToView,CameraPosition,Parameters,ExposureEv100,AspectRatioOverride);
    /// <summary>呼び出し側の VP 行列を変更せずに保ち、現在の描画面に合わせて物理深度メタデータを解決する。</summary>
    public static RenderViewSource PerspectiveMatrix(in Matrix4x4 ViewProjection,Vector3 CameraPosition,in RadialViewParameters Parameters)=>
        new(SourceKind.PerspectiveMatrix,default,ViewProjection,CameraPosition,Parameters,0f,0f);
    public RenderView Resolve(int Width,int Height){
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Height);
        return this.Resolve((float)Width/Height);
    }
    public RenderView Resolve(float SurfaceAspectRatio){
        if(!this.HasValue){ throw new ArgumentException("A view source must be initialized."); }
        if(!float.IsFinite(SurfaceAspectRatio)||SurfaceAspectRatio<=0f){ throw new ArgumentException("A logarithmic perspective view needs a positive finite curve, valid clip range and field of view."); }
        if(this.Kind==SourceKind.Fixed){ return this.FixedView; }
        var AspectRatio=this.AspectRatioOverride>0f?this.AspectRatioOverride:SurfaceAspectRatio;
        if(!RadialDepthCurve.TryCreate(this.Parameters.TransitionMeters,out var Curve)
            ||!RadialDepthProjection.TryCreate(Curve,this.Parameters.NearZ,this.Parameters.FarZ,this.Parameters.FovYRadians,AspectRatio,out var Projection)){
            throw new ArgumentException("A logarithmic perspective view needs a positive finite curve, valid clip range and field of view.");
        }
        if(this.Kind==SourceKind.PerspectiveMatrix){
            return RenderView.Create(this.Transform,Projection.CreateAuxiliaryViewProjection(this.Transform),this.CameraPosition,0f,RenderViewDepth.ForPerspective(Projection));
        }
        var View=this.Transform;
        var Basis=RenderViewBasis.Create(new Vector3(View.M11,View.M21,View.M31),new Vector3(View.M12,View.M22,View.M32),
            -new Vector3(View.M13,View.M23,View.M33),MathF.Tan(this.Parameters.FovYRadians*0.5f),AspectRatio);
        return RenderView.Create(View*Matrix4x4.CreatePerspectiveFieldOfView(this.Parameters.FovYRadians,AspectRatio,this.Parameters.NearZ,this.Parameters.FarZ),
            View*Projection.CreateAuxiliaryProjection(this.Parameters.FovYRadians,AspectRatio),this.CameraPosition,this.ExposureEv100,RenderViewDepth.ForPerspective(Projection),Basis);
    }
}
