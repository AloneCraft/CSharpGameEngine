using CSharpGameEngine.Contracts;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Numerics;
namespace CSharpGameEngine.Navigation;
/// <summary>
/// ナビメッシュを作る領域(UE の NavMeshBoundsVolume に当たる)。シーンのノードに付け、ノードの Transform の位置と拡縮で置く(回転は、領域を含む軸に平行の箱にして使う)。
/// ビルドの Cook(<c>CSharpGameEngine.Navigation.Cook</c>)がこのシーンを読み、領域の中の静的な衝突形状から<c>Assets/Navigation/&lt;Id&gt;.cgenav</c>を作る。
/// 同じ<see cref="Id"/>の領域は 1 つのナビメッシュにまとまり、その値(歩く者と格子の大きさ)は同じでなければならない。実行時は、この Component のある Entity が現れると
/// その<see cref="Id"/>のナビメッシュを読む(<see cref="Navigator"/>)。既定値は UE5.8 の TopDown テンプレートの RecastNavMesh の値(<see cref="NavigationDefaults"/>)。
/// </summary>
[SceneComponentOrder(SceneComponentCategory.Gameplay,30)]
public struct NavMeshBounds() {
    /// <summary>ナビメッシュの名前。ファイル名(英数字と <c>-_.</c>)になる。</summary>
    public string Id=NavigationDefaults.Id;
    /// <summary>領域の半分の大きさ(m、拡縮の前)。</summary>
    public LogicalVector3 HalfExtents=new(LogicalScalar.FromInt64(10),LogicalScalar.FromInt64(5),LogicalScalar.FromInt64(10));
    /// <summary>歩く者の半径(m)。壁からこれだけ離れた所だけが歩ける。</summary>
    public LogicalScalar AgentRadius=NavigationDefaults.AgentRadius;
    /// <summary>歩く者の背丈(m)。これより低い所は歩けない。</summary>
    public LogicalScalar AgentHeight=NavigationDefaults.AgentHeight;
    /// <summary>登れる段差(m)。</summary>
    public LogicalScalar AgentMaxClimb=NavigationDefaults.AgentMaxClimb;
    /// <summary>歩ける坂の最大の角度。</summary>
    [Angle(AngleUnit.Degrees)]
    public LogicalScalar AgentMaxSlopeDegrees=NavigationDefaults.AgentMaxSlopeDegrees;
    /// <summary>格子の目の水平の大きさ(m)。小さいほど細かく、ベイクが重い。</summary>
    public LogicalScalar CellSize=NavigationDefaults.CellSize;
    /// <summary>格子の目の高さ(m)。</summary>
    public LogicalScalar CellHeight=NavigationDefaults.CellHeight;
}
/// <summary>領域を読み込んだ結果。<see cref="NavMeshBounds"/>に随伴する。</summary>
public struct NavMeshBoundsRuntime:IRuntimeComponent<NavMeshBounds> {
    /// <summary>読み込みを試みた(成功でも失敗でも)。</summary>
    public bool Attempted;
    /// <summary>ナビメッシュが読まれて、経路探索に使える。</summary>
    public bool Loaded;
    /// <summary>読めなかった理由。ファイルが無い、壊れている、など。成功なら null。</summary>
    public string?Problem;
}
/// <summary>
/// ナビメッシュの上を歩く者(ADR-0079/M0106)。<see cref="CharacterMotion"/>と<see cref="MoveIntent"/>を持つ Entity に付ける。
/// <c>MoveIntent.MoveTo</c>の目的地への経路をナビメッシュで探し、経由点を一つずつ<see cref="MoveIntent.Waypoint"/>へ置いて歩かせる。
/// ナビメッシュが読まれていない、または出発点がナビメッシュの外なら、従来どおり目的地へ直線で歩く。
/// </summary>
[SceneComponentOrder(SceneComponentCategory.Gameplay,31)]
public struct NavAgent() {
    /// <summary>途中の経由点に、水平にこの距離(m)まで近づいたら、着いたとみなして次へ進む。</summary>
    public LogicalScalar WaypointRadius=LogicalScalar.FromRaw(LogicalScalar.One.Raw*3/10);
    /// <summary>経路の線(出発点から次の経由点まで)から水平にこの距離(m)以上離れたら、経路を探し直す。</summary>
    public LogicalScalar ReplanDistance=LogicalScalar.FromRaw(LogicalScalar.One.Raw*3/2);
    /// <summary>目的地がこの距離(m)以上動いたら、経路を探し直す。押し続けてカーソルを動かすと目的地が毎 tick わずかに動くので、細かい動きでは探し直さない。</summary>
    public LogicalScalar GoalTolerance=LogicalScalar.FromRaw(LogicalScalar.One.Raw/4);
}
/// <summary>歩いている経路。実行時の状態で、スナップショットには入らない(巻き戻したあとは、目的地が残っていれば探し直す)。</summary>
public struct NavAgentState:IRuntimeComponent<NavAgent> {
    /// <summary>今の経路。ナビメッシュが無いか、出発点が外のときは null(直線で歩く)。</summary>
    public NavRoute?Route;
    /// <summary>次に向かう<see cref="NavRoute.Points"/>の番号。</summary>
    public int Next;
    /// <summary>経路を探したときの目的地と出発点。目的地が動いたか、経路から離れたかを判定する。</summary>
    public LogicalVector3 Goal;
    public LogicalVector3 Origin;
    /// <summary>目的地に対して経路を探し終えている(結果が null でも)。</summary>
    public bool Planned;
}
