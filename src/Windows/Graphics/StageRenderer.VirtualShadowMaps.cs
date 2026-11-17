using System.Numerics;
using CSharpGameEngine.Rendering;
using static CSharpGameEngine.Rendering.Pipeline.StageGpuLayouts;
namespace CSharpGameEngine.Graphics.D3D12;
/// <summary>
/// 作り直した仮想シャドウ（ADR-0100 決定 5、M0132）。計画は L4 の <see cref="VirtualShadowMapPlanner"/>、ここは実行だけを持つ。
/// 単位 2 では、試験の印があるときだけ主パスの後にページの要求を作る（単位 3 で M0115 の経路と置き換える）。
/// </summary>
public sealed unsafe partial class StageRenderer {
    private readonly VirtualShadowMapPlanner ShadowMapPlanner=new();
    private StageVirtualShadowRequests?ShadowRequests;
    /// <summary>格子を世界に固定する位置（既定は区画 0、ずれ 0。描画空間がそのまま世界の位置のとき）。</summary>
    internal VirtualShadowAnchor ShadowAnchor { get;set; }
    /// <summary>試験用: 主パスの後に仮想シャドウのページの要求を作る。</summary>
    internal bool VirtualShadowRequestsForTesting { get;set; }
    private readonly (int X,int Y)[]ShadowRequestOrigins=new (int X,int Y)[VirtualShadowMapPlanner.LevelCount];
    private readonly Matrix4x4[]ShadowRequestProjections=new Matrix4x4[VirtualShadowMapPlanner.LevelCount];
    /// <summary>主パスの後: 光の向きで段の窓と行列を求め、距離の画像からページの要求を作る。計算の束縛に置き換わるので印を立てる。</summary>
    private void RecordVirtualShadowRequests(ref FrameRecording Recording){
        if(!this.VirtualShadowRequestsForTesting||this.DirectionalShadowIndex<0){ return; }
        var Frame=this.FrameSlot(this.FrameIndex);
        var Light=*(Vector4*)(Frame+FrameDirectionalLightsOffset+(uint)this.DirectionalShadowIndex*DirectionalLightStride);
        this.ShadowMapPlanner.Plan(this.ShadowAnchor,this.CurrentCameraPosition,new Vector3(Light.X,Light.Y,Light.Z),[],[],VirtualShadowMapPlanner.MaximumPhysicalPages,RenderBudget:0);
        for(var Level=0;Level<VirtualShadowMapPlanner.LevelCount;Level++){
            this.ShadowRequestOrigins[Level]=this.ShadowMapPlanner.WindowOrigin(Level);
            this.ShadowRequestProjections[Level]=this.ShadowMapPlanner.Projections[Level];
        }
        Matrix4x4.Invert(this.InvertibleViewProjection,out var Inverse);
        var Metric=VirtualGeometryMetric(this.CurrentViewProjection,this.Height);
        this.ShadowRequests!.Record(this.CommandList,this.FrameIndex,this.SceneDistanceBuffer,this.Width,this.Height,Inverse,this.ShadowMapPlanner.Projections,this.CurrentCameraPosition,Metric.ShaderParameters.X);
        Recording.RanComputeAfterMainPass=true;
    }
    /// <summary>
    /// 試験用: 最後のフレームの要求の印、そのフレームの段の窓の位置と行列、要求を作った逆ビュー投影・カメラの位置・1 m 先の 1 m の画素数。GPU の完了を待って読む。
    /// </summary>
    internal (uint[]Bits,(int X,int Y)[]Origins,Matrix4x4[]Projections,Matrix4x4 InverseViewProjection,Vector3 Camera,float PixelsPerMeter)ReadVirtualShadowRequestsForTesting(){
        this.ThrowIfDisposed();
        this.WaitForGpuIdle();
        Matrix4x4.Invert(this.InvertibleViewProjection,out var Inverse);
        return (this.ShadowRequests!.ReadLatestForTesting(),[..this.ShadowRequestOrigins],[..this.ShadowRequestProjections],Inverse,this.CurrentCameraPosition,
            VirtualGeometryMetric(this.CurrentViewProjection,this.Height).ShaderParameters.X);
    }
}
