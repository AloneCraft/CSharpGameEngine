using CSharpGameEngine.Numerics;
namespace CSharpGameEngine.Navigation;
/// <summary>
/// ナビメッシュを作る既定値(ADR-0079/M0106)。UE5.8 の TopDown テンプレートの RecastNavMesh の値(m)で、Cook の道具(<c>NavMeshCooker</c>)と、
/// シーンの <see cref="NavMeshBounds"/> の既定値が同じ値を使う。出典は <c>Docs/Milestones/M0106-Navmesh-Bake-And-Path-Following.md</c>。
/// </summary>
public static class NavigationDefaults {
    /// <summary>歩く者の半径。レベルの RecastNavMesh の AgentRadius = 35 cm(DefaultEngine.ini は 34)。</summary>
    public static LogicalScalar AgentRadius=>LogicalScalar.FromRaw(LogicalScalar.One.Raw*35/100);
    /// <summary>歩く者の背丈。AgentHeight = 144 cm。</summary>
    public static LogicalScalar AgentHeight=>LogicalScalar.FromRaw(LogicalScalar.One.Raw*144/100);
    /// <summary>登れる段差。AgentMaxStepHeight = 35 cm(RecastNavMesh.h の既定)。</summary>
    public static LogicalScalar AgentMaxClimb=>LogicalScalar.FromRaw(LogicalScalar.One.Raw*35/100);
    /// <summary>歩ける坂の最大の角度(度)。AgentMaxSlope = 44。</summary>
    public static LogicalScalar AgentMaxSlopeDegrees=>LogicalScalar.FromInt64(44);
    /// <summary>格子の目の水平の大きさ。DefaultEngine.ini の CellSize = 19 cm。</summary>
    public static LogicalScalar CellSize=>LogicalScalar.FromRaw(LogicalScalar.One.Raw*19/100);
    /// <summary>格子の目の高さ。CellHeight = 10 cm。</summary>
    public static LogicalScalar CellHeight=>LogicalScalar.FromRaw(LogicalScalar.One.Raw/10);
    /// <summary>ナビメッシュの名前の既定。</summary>
    public const string Id="Default";
    /// <summary>ナビメッシュのファイルのフォルダ(プロジェクトからの相対)と拡張子。</summary>
    public const string Folder="Assets/Navigation";
    public const string Extension=".cgenav";
}
