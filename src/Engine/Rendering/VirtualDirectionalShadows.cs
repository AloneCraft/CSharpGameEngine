using System.Numerics;
namespace CSharpGameEngine.Rendering;
public enum DirectionalShadowMode { Cascaded,Virtual }
/// <summary>
/// 太陽の影の設定。<see cref="PageBudget"/> は仮想シャドウの置き場のページの数（1〜4096、M0132）。計画は <see cref="VirtualShadowMapPlanner"/>。
/// </summary>
public readonly record struct DirectionalShadowSettings(DirectionalShadowMode Mode,int PageBudget=1024,bool Compare=false,bool SoftShadows=false,float SourceAngularDiameterDegrees=0.5357f,float MaximumSoftShadowRadiusMeters=2f) {
    public bool IsValid=>this.Mode is DirectionalShadowMode.Cascaded or DirectionalShadowMode.Virtual&&this.PageBudget is >=1 and <=VirtualShadowMapPlanner.MaximumPhysicalPages
        &&float.IsFinite(this.SourceAngularDiameterDegrees)&&this.SourceAngularDiameterDegrees is >=0f and <=10f
        &&float.IsFinite(this.MaximumSoftShadowRadiusMeters)&&this.MaximumSoftShadowRadiusMeters is >0f and <=16f;
}
/// <summary>影を落とす物の包む箱（描画空間）。<see cref="Id"/> でフレームをまたいで比べる。</summary>
public readonly record struct VirtualShadowBounds(int Id,Vector3 Minimum,Vector3 Maximum,bool Visible,bool CastsShadow,bool Deforming=false,int GeometryId=0,Matrix4x4 Transform=default);
public readonly record struct VirtualShadowPageKey(int Level,int X,int Y);
/// <summary>1 フレームの計画の数: 要求、置いてある、作った、作り直さずに使った、置けなかった、古くした。</summary>
public readonly record struct VirtualShadowStatistics(int Requested,int Resident,int Rendered,int Reused,int Missing,int Invalidated);
