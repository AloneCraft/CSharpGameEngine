using System.Numerics;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Numerics;
namespace CSharpGameEngine.Destruction;
/// <summary>破壊時にも位置を保つ <see cref="Destructible"/> の破片（UE のアンカーフィールド）。</summary>
public enum DestructibleAnchor:byte {
    /// <summary>None：オブジェクトが壊れると、すべての破片が落下する。</summary>
    None,
    /// <summary>オブジェクトの最下部の面（1 cm 以内）にある破片。壁や柱の足元を保持する。<c>RigidBody</c> のないオブジェクトに限る。</summary>
    Bottom,
}
/// <summary>
/// 壊れるオブジェクト（ADR-0078/M0113、UE の Geometry Collection）。シーンノード上で <c>Collider</c>（ボックス、元の体積）と並べて配置する。落下・回転
/// するオブジェクトには <c>RigidBody</c> も付ける。ビルド時にノードの破断アセットを生成し、ボックス（または閉じた凸メッシュである <see cref="SourceMesh"/>）を
/// <see cref="Seed"/> に従って <see cref="FragmentCount"/> 個のボロノイ破片に分割する。それらを <see cref="ClusterCount"/> 個の
/// クラスターにまとめ、さらに階層ごとに <see cref="ClusterLevels"/> 段にまとめる。接触する破片同士は、共有面積と <see cref="BondStrength"/> の積を
/// 結合の強度として接続し、<c>Assets/Destruction/&lt;FractureAsset&gt;.cgefracture</c> に保存する。実行時は破片として描画し（外側は
/// <see cref="OuterColor"/>、切断面は <see cref="InteriorColor"/>）、衝突物の力積で結合にひずみを加える。過去の衝撃による内部ひずみは
/// <see cref="StrainDecay"/> に従って減衰し、その値を含むひずみが強度に達すると結合が切れる。分離した部分は独立したボディになり、固定された破片は動かない。破片は分離から
/// <see cref="FragmentLifetime"/> 秒後に削除され、一度に存在できる分離破片は最大 <see cref="FragmentBudget"/> 個で、古いものから削除される。
/// ノードのスケールは 1 でなければならない。サイズはコライダーで決まる。
/// </summary>
[SceneComponentOrder(SceneComponentCategory.Spatial,22)]
public struct Destructible() {
    /// <summary>
    /// 事前生成したアセットの名前（英字、数字、<c>-_.</c>）。ファイルは <c>Assets/Destruction/&lt;name&gt;.cgefracture</c>。同じ名前のノードは、同
    /// じ元形状と分割方法を共有する必要がある。
    /// </summary>
    public string FractureAsset=string.Empty;
    /// <summary>ボロノイ破片の数（2～256）。</summary>
    public int FragmentCount=DestructionDefaults.FragmentCount;
    /// <summary>破片を分割する生成点を決めるシード。同じシードなら、バイト単位で同じ分割結果になる。</summary>
    public int Seed=0;
    /// <summary>破片をまとめる最上位クラスターの数。0 の場合は √<see cref="FragmentCount"/> に最も近い整数を選ぶ。</summary>
    public int ClusterCount=0;
    /// <summary>
    /// クラスター階層の段数（1～4、M0124、UE の Geometry Collection の階層）。各段で、直前の段のクラスターをそれぞれ、その中の破片数の平方根に最も近い整数個のクラスターに分割す
    /// る。2 の場合、オブジェクトはまず大きな塊に分かれ、各塊がさらに小さな塊に分かれてから破片になる。
    /// </summary>
    public int ClusterLevels=DestructionDefaults.ClusterLevels;
    /// <summary>コライダーのボックスの代わりに分割する、ゲームのフォルダー内にある閉じた凸形状の <c>.glb</c>。空ならボックスを使う。</summary>
    public string SourceMesh=string.Empty;
    /// <summary>破片の密度（kg/m³。木材は約500、コンクリートは約2400）。</summary>
    public LogicalScalar Density=LogicalScalar.FromInt64(DestructionDefaults.Density);
    /// <summary>結合が切れるまでに、面積1平方メートルあたりが受けるひずみ（N·s/m²）。ひずみは力積であり、質量100 kgのキャラクターが速度6 m/sで衝突すると600 N·sになる。</summary>
    public LogicalScalar BondStrength=LogicalScalar.FromInt64(DestructionDefaults.BondStrength);
    /// <summary>同じクラスター内の結合を、クラスター間の結合よりどれだけ強くするか。両破片を含むクラスターの各階層で一度ずつ適用するため、衝撃でまず最上位のクラスター全体が外れ、続いて下位クラスター、最後に個々の破片が分離する。</summary>
    public LogicalScalar ClusterStrengthScale=LogicalScalar.FromInt64(2);
    /// <summary>
    /// 過去の衝撃による内部ひずみが、1秒あたりにどれだけ減衰するか（M0124、UE の Chaos internal strain）。各ティックで結合はその値の max(0, 1 − decay × Δt
    /// ) を保持するため、短い間隔で繰り返し叩けば、一度では壊れないものも壊れる。0 なら減衰しない。60 Hz で60以上なら一度で消え、M0113 と同じ動作になる。物体の上に載り続けるボディによる
    /// 静的荷重は内部ひずみに加算されない。
    /// </summary>
    public LogicalScalar StrainDecay=LogicalScalar.One/LogicalScalar.FromInt64(2);
    /// <summary>力積のうち、衝撃点から一つ隣の破片に伝わる割合（0～1）。隣接する輪ごとに適用する。</summary>
    public LogicalScalar DamagePropagation=LogicalScalar.One/LogicalScalar.FromInt64(2);
    public DestructibleAnchor Anchor=DestructibleAnchor.Bottom;
    /// <summary>同時に存在できる分離中（移動中）の破片の最大数。古いものから削除する。0以下なら上限なし。</summary>
    public int FragmentBudget=64;
    /// <summary>分離した破片が生存する秒数。0以下なら削除しない。</summary>
    public LogicalScalar FragmentLifetime=LogicalScalar.FromInt64(10);
    /// <summary>オブジェクト外側のリニア RGB 色（<c>ScreenText</c> の色と同様にチャンネルごとに指定）。</summary>
    public float OuterColorR=0.6f;
    public float OuterColorG=0.6f;
    public float OuterColorB=0.6f;
    /// <summary>切断面のリニア RGB 色（UE の interior material）。</summary>
    public float InteriorColorR=0.4f;
    public float InteriorColorG=0.38f;
    public float InteriorColorB=0.35f;
    internal readonly Vector3 OuterColor=>new(this.OuterColorR,this.OuterColorG,this.OuterColorB);
    internal readonly Vector3 InteriorColor=>new(this.InteriorColorR,this.InteriorColorG,this.InteriorColorB);
}
/// <summary><see cref="Destructible"/> の実行時状態。同じオブジェクトに付属し、ゲームとテストから参照できる値だけを保持する。</summary>
public struct DestructibleState:IRuntimeComponent<Destructible> {
    /// <summary>読み込みを試みた（試行は一度だけ）。</summary>
    public bool Attempted;
    /// <summary>事前生成したアセットを読み込み、破片を描画している。</summary>
    public bool Loaded;
    /// <summary>結合が切れた（設定されたボディはなくなり、各破片が独立したボディになった）。</summary>
    public bool Broken;
    public int Fragments;
    public int BrokenBonds;
    /// <summary>生存中の破片（固定されたものと分離したもの）。</summary>
    public int Pieces;
    /// <summary>生存中で分離した（移動中の）破片。</summary>
    public int LoosePieces;
    /// <summary>オブジェクトが壊れない理由（アセットがない、形式が不正、またはスケールが 1 でない）。壊せる場合は null。</summary>
    public string?Problem;
}
/// <summary>壊れた <see cref="Destructible"/> の破片から作られたボディ。破壊可能オブジェクトの Entity と、<c>FractureState</c> 内の破片番号を持つ。</summary>
public struct DestructiblePiece {
    public Entity Root;
    public int Piece;
}
/// <summary>
/// <see cref="Destructible"/> の描画用破片1つ。<see cref="Body"/>（壊れるまでは破壊可能オブジェクト、壊れた後は破片のボディ）に追従し、そのボディの座標枠で
/// <see cref="Offset"/> の位置に配置する。
/// </summary>
public struct DestructibleFragment {
    public Entity Root;
    public int Fragment;
    public Entity Body;
    public LogicalVector3 Offset;
}
/// <summary>
/// キャラクターが衝突した物体を壊せるようにする（ADR-0078/M0113。UE ではキャラクターが質量を使って物理ボディを押す）。<c>CharacterMotion</c> と同じノードに付ける。
/// キャラクターが破壊可能オブジェクト（またはその破片）に <see cref="MinimumSpeed"/> を超える速度で衝突すると、衝突点に <see cref="Mass"/> × 接近速度の
/// 力積を加え、分離した破片にはその力積で押す力を加える。
/// </summary>
[SceneComponentOrder(SceneComponentCategory.Gameplay,40)]
public struct DestructionImpact() {
    /// <summary>質量（kg。UE の CharacterMovement Mass は100）。</summary>
    public LogicalScalar Mass=LogicalScalar.FromInt64(100);
    /// <summary>速度（m/s）。これより遅い接近（壁にもたれるなど）では、ひずみも押す力も発生しない。</summary>
    public LogicalScalar MinimumSpeed=LogicalScalar.One;
}
/// <summary>
/// ひずみフィールド（ADR-0078/M0113、UE の External Strain field）。ノードから <see cref="Radius"/> 以内にある結合に
/// <see cref="Strain"/>（N·s）を加える。強さは距離とともに弱まり、半径上でゼロになる。存在する最初の固定ティックに作用し、<see cref="Repeat"/> が有効なら毎
/// ティック作用する。<see cref="ForceField"/> と <c>Lifetime</c> を組み合わせると爆発になる。
/// </summary>
[SceneComponentOrder(SceneComponentCategory.Gameplay,41)]
public struct StrainField() {
    public LogicalScalar Radius=LogicalScalar.FromInt64(2);
    public LogicalScalar Strain=LogicalScalar.FromInt64(5000);
    public bool Repeat=false;
}
/// <summary><see cref="StrainField"/> が作用済みかどうか。</summary>
public struct StrainFieldState:IRuntimeComponent<StrainField> {
    public bool Fired;
}
/// <summary>
/// フォースフィールド（ADR-0078/M0113、UE の radial vector field）。ノードから <see cref="Radius"/> 以内にいる移動中のボディを一度だけ押し離す。
/// 中心での強さは <see cref="Speed"/>（m/s）で、距離とともに弱まり半径上でゼロになる。フィールドの有効期間中に分離したボディも、出現した次のティックに押す。
/// </summary>
[SceneComponentOrder(SceneComponentCategory.Gameplay,42)]
public struct ForceField() {
    public LogicalScalar Radius=LogicalScalar.FromInt64(3);
    public LogicalScalar Speed=LogicalScalar.FromInt64(6);
}
/// <summary><see cref="ForceField"/> とともに付加する。押したボディは <see cref="Destruction"/> サービスが記録する。</summary>
public struct ForceFieldState:IRuntimeComponent<ForceField> {
    public int Pushed;
}
