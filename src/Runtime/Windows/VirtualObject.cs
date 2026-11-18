using System.Numerics;
using CSharpGameEngine.Authoring;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Rendering;
using CSharpGameEngine.Rendering.Streaming;
namespace CSharpGameEngine.Runtime.Windows;
/// <summary>
/// 画面に見える大きさ（画素）で描き方の段を切り替える物（ADR-0100 決定 6、M0133）。親の Entity に付ける。段は子の Entity で、子に <see cref="VirtualObjectLevel"/> を付ける。
/// 段 0 がいちばん粗い。<see cref="Enter1"/>〜<see cref="Enter4"/> は段 1〜4 へ上がる画素の大きさ（0 は使わない。使う分は正の昇順）。
/// 画素の大きさは、半径 <see cref="RadiusMeters"/>（Entity の拡大を掛ける）の球の直径が画面で占める画素で、仮想ジオメトリと同じ式（<see cref="ScreenSizeMetric"/>）で測る。
/// 段の境では <see cref="Hysteresis"/> 倍で行き来しない。上の段は、その段の子がすべて準備できたときだけ選ぶ。
/// </summary>
[SceneComponentOrder(SceneComponentCategory.Rendering,13)]
public struct VirtualObject() {
    /// <summary>画素の大きさを測る球の半径（m、0 より大きい）。</summary>
    public float RadiusMeters=1f;
    public float Enter1=0f;
    public float Enter2=0f;
    public float Enter3=0f;
    public float Enter4=0f;
    /// <summary>段の境の幅（1 以上）。上がるには Enter × h 以上、下がるには Enter ÷ h 未満。</summary>
    public float Hysteresis=VirtualObjectSelector.DefaultHysteresis;
}
/// <summary>VirtualObject がうまく測れなかった理由（<see cref="VirtualObjectRuntime.Failure"/>）。測れない間は今の段を描き続ける。</summary>
public enum VirtualObjectFailure {
    None,
    /// <summary>閾値が昇順でない、半径やヒステリシスが範囲の外。</summary>
    InvalidSettings,
    /// <summary>カメラの姿勢か画面の大きさが無い。</summary>
    NoCamera,
    /// <summary>物の位置をカメラから見た位置にできなかった。</summary>
    NoTransform,
}
/// <summary><see cref="VirtualObject"/> の実行時の状態。<see cref="Level"/> は描く段、<see cref="Desired"/> は画素だけで決めた段（その段の準備を進めてよい）。</summary>
public struct VirtualObjectRuntime:IRuntimeComponent<VirtualObject> {
    public int Level;
    public int Desired;
    public bool Started;
    /// <summary>段が替わった回数（最初に描き始めたフレームは数えない）。</summary>
    public int Transitions;
    public float Pixels;
    public VirtualObjectFailure Failure;
}
/// <summary>
/// VirtualObject の段 1 つ（ADR-0100 決定 6、M0133）。<see cref="VirtualObject"/> を持つ Entity の直接の子に付ける。親の段がこの段のときだけ、この Entity と子孫のメッシュを描く
/// （<see cref="MeshVisibility"/> より優先して隠す）。<see cref="Ready"/> が false の間、親はこの段へ上がらない（読み込み中の表現などをゲームが知らせる）。
/// </summary>
[SceneComponentOrder(SceneComponentCategory.Rendering,14)]
public struct VirtualObjectLevel() {
    /// <summary>段の番号（0 がいちばん粗い）。</summary>
    public int Level=0;
    public bool Ready=true;
}
/// <summary><see cref="VirtualObjectLevel"/> の実行時の状態。<see cref="Shown"/> はこのフレームに描くか。</summary>
public struct VirtualObjectLevelRuntime:IRuntimeComponent<VirtualObjectLevel> {
    public bool Shown;
}
/// <summary>
/// 段の子から親への「準備できた」の受け渡し（子は親の部品へ書けないので、エンジンのサービスを通す）。子の段の報告をフレームごとに集め、親が段を選ぶときに受け取る。
/// 同じ段の子が複数あれば、すべてが準備できたときだけ準備できたとする。
/// </summary>
[EngineService]
public sealed class VirtualObjectReadiness {
    private readonly Dictionary<Entity,(uint Ready,uint NotReady)>Reports=[];
    /// <summary>段の子が一度でもあったか（無いゲームでは、描画の同期がメッシュの祖先を調べずに済むように）。</summary>
    internal bool HasLevels;
    internal void Report(Entity Owner,int Level,bool Ready){
        if((uint)Level>=VirtualObjectSelector.MaximumLevels){ return; }
        var Bit=1u<<Level;
        this.Reports.TryGetValue(Owner,out var Report);
        this.Reports[Owner]=Ready?(Report.Ready|Bit,Report.NotReady):(Report.Ready,Report.NotReady|Bit);
    }
    /// <summary>親 <paramref name="Owner"/> の段の準備（ビット i が段 i）を受け取り、報告を消す。</summary>
    internal uint Take(Entity Owner){
        if(!this.Reports.Remove(Owner,out var Report)){ return 0u; }
        return Report.Ready&~Report.NotReady;
    }
}
/// <summary>段の子が、自分の段の準備を親へ知らせる（段を選ぶ前）。</summary>
[SystemPhase(SystemPhase.Presentation)]
[RunsBefore(typeof(VirtualObjectSelect))]
internal struct VirtualObjectLevelReport:ISystem {
    public void Update(in VirtualObjectLevel Level,in Parent<VirtualObject> Owner,VirtualObjectReadiness Readiness){
        Readiness.Report(Owner.Entity,Level.Level,Level.Ready);
    }
}
/// <summary>
/// 親が、カメラから見た画素の大きさで段を選ぶ（カメラの姿勢が決まった後）。位置の差は固定小数点で引いてから float にする（<see cref="RuntimeWorldTransform"/>）。
/// 測れないときは理由を残し、今の段を描き続ける。
/// </summary>
[SystemPhase(SystemPhase.Presentation)]
[RunsAfter(typeof(CameraOriginSync))]
internal struct VirtualObjectSelect:ISystem {
    public void Update(Entity Entity,ref VirtualObject Settings,ref VirtualObjectRuntime Runtime,VirtualObjectReadiness Readiness,EngineHost Host,ref State State){
        var Ready=Readiness.Take(Entity);
        Span<float>Enter=stackalloc float[4];
        var Count=0;
        foreach(var Value in (ReadOnlySpan<float>)[Settings.Enter1,Settings.Enter2,Settings.Enter3,Settings.Enter4]){
            if(Value!=0f){ Enter[Count++]=Value; }
        }
        Enter=Enter[..Count];
        if(VirtualObjectSelector.Validate(Enter,Settings.Hysteresis) is not null||!(Settings.RadiusMeters>0f)||!float.IsFinite(Settings.RadiusMeters)){
            Runtime.Failure=VirtualObjectFailure.InvalidSettings;
            return;
        }
        var Height=Host.Renderer?.SurfaceHeight??0;
        var Camera=Host.RenderCamera;
        if(Height<=0||!(Camera.FovYRadians>0f&&Camera.FovYRadians<MathF.PI)||!(Camera.NearZ>0f)){
            Runtime.Failure=VirtualObjectFailure.NoCamera;
            return;
        }
        if(RuntimeWorldTransform.TryGetCameraRelativeMatrix(Entity,State.World,Host,out var Matrix,out _)!=FixedTransformStatus.Success){
            Runtime.Failure=VirtualObjectFailure.NoTransform;
            return;
        }
        var Scale=MathF.Max(new Vector3(Matrix.M11,Matrix.M12,Matrix.M13).Length(),MathF.Max(new Vector3(Matrix.M21,Matrix.M22,Matrix.M23).Length(),new Vector3(Matrix.M31,Matrix.M32,Matrix.M33).Length()));
        var Metric=ScreenSizeMetric.Perspective(Camera.FovYRadians,Height,Camera.NearZ);
        Runtime.Pixels=Metric.SphereProjectedPixels(Matrix.Translation,Settings.RadiusMeters*Scale);
        Runtime.Failure=VirtualObjectFailure.None;
        var Level=VirtualObjectSelector.Step(Enter,Settings.Hysteresis,Runtime.Pixels,Runtime.Level,Runtime.Started,Ready,out Runtime.Desired);
        if(Runtime.Started&&Level!=Runtime.Level){ Runtime.Transitions++; }
        Runtime.Level=Level;
        Runtime.Started=true;
    }
}
/// <summary>
/// 段の子が、親の段を読んで自分を描くか決める（段を選んだ後、描画の同期の前）。新しい段と古い段の切り替えは同じフレームに起きるので、何も描かないフレームはできない。
/// </summary>
[SystemPhase(SystemPhase.Presentation)]
[RunsAfter(typeof(VirtualObjectSelect))]
[RunsBefore(typeof(TransformSync))]
internal struct VirtualObjectLevelShow:ISystem {
    public void Update(ref VirtualObjectLevelRuntime Runtime,in VirtualObjectLevel Level,in Parent<VirtualObjectRuntime> Owner,VirtualObjectReadiness Readiness){
        Runtime.Shown=Owner.Value.Started&&Owner.Value.Level==Level.Level;
        Readiness.HasLevels=true;
    }
}
/// <summary>描画の同期が使う: メッシュの Entity か祖先が VirtualObject の段で、その段を描かないなら true。</summary>
internal static class VirtualObjectVisibility {
    internal static bool IsHidden(Entity Entity,CSharpGameEngine.Entities.World World){
        for(var Depth=0;Depth<64;Depth++){
            if(World.TryGetComponent(Entity,out VirtualObjectLevelRuntime Level)){ return !Level.Shown; }
            if(!World.TryGetComponent(Entity,out Parent Hanger)){ return false; }
            Entity=Hanger.Value;
        }
        return false;
    }
}
