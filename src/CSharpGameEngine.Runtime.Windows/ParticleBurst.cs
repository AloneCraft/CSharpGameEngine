using CSharpGameEngine.Entities;
using CSharpGameEngine.Numerics;
namespace CSharpGameEngine.Runtime.Windows;
/// <summary>
/// 短時間だけ存在するメッシュのバースト（ADR-0078/M0105）。一度だけ使うUnrealテンプレートのNiagaraシステム
///（例：<c>FX_Cursor_Success</c>。1回、1個、寿命0.7秒、白色）に相当する。
/// <see cref="Lifetime"/>とともにSceneのノードへ置き、<see cref="Spawner"/>でSceneを生成する。
/// 最初の固定ティックでノードのワールド位置から<see cref="Count"/>個のパーティクルを放出する。各パーティクルは独自の<see cref="Lifetime"/>を持つEntityであり、
/// 飛行し、落下し、サイズが変化して消える。レンダラーにスプライト描画パスはないため、パーティクルも他のオブジェクトと同様にメッシュで表す
///（<see cref="StaticMesh"/>アセットまたは<see cref="PrimitiveMesh"/>）。同じように照明され、不透明で描画する。
/// 乱数には<see cref="Seed"/>を使うため、同じバーストは毎回同じ見た目になる。
/// </summary>
[SceneComponentOrder(SceneComponentCategory.Rendering,30)]
public struct ParticleBurst() {
    /// <summary><see cref="Count"/>の上限。これを超える値を指定すると、この数だけ放出する。</summary>
    public const int MaxCount=256;
    /// <summary>1つのパーティクルのメッシュ。ゲームのAssets配下のパス（<c>.glb</c>）、または空なら<see cref="Shape"/>の<see cref="PrimitiveMesh"/>。</summary>
    public string AssetPath=string.Empty;
    /// <summary><see cref="AssetPath"/>が空の場合に使う、1つのパーティクルのプリミティブ。単位サイズの形状を<see cref="StartScale"/>と<see cref="EndScale"/>でスケーリングする。</summary>
    public PrimitiveType Shape=PrimitiveType.Sphere;
    /// <summary>パーティクル数。0～<see cref="MaxCount"/>。</summary>
    public int Count=8;
    /// <summary>1つのパーティクルの寿命（秒）。</summary>
    public LogicalScalar Lifetime=LogicalScalar.FromSingle(0.7f);
    /// <summary>誕生時のパーティクルの進行方向に沿った速度（m/s）。0なら誕生位置にとどまる。</summary>
    public LogicalScalar Speed=LogicalScalar.Zero;
    /// <summary><see cref="Speed"/>と比較したパーティクル速度の増減率（その速度に対する割合。0.25なら±25 %）。</summary>
    public LogicalScalar SpeedJitter=LogicalScalar.Zero;
    /// <summary>方向が横へ傾く度合い。上方向1単位あたりの横方向距離で表し、0なら真上、1なら45°の円錐、大きな値ならほぼ水平。</summary>
    public LogicalScalar Spread=LogicalScalar.One;
    /// <summary>パーティクルを下へ引く加速度（m/s²）。</summary>
    public LogicalScalar Gravity=LogicalScalar.Zero;
    /// <summary>誕生時のパーティクルのスケール。</summary>
    public LogicalScalar StartScale=LogicalScalar.One;
    /// <summary>寿命終了時のパーティクルのスケール。<see cref="StartScale"/>からこの値まで直線的に変化する。</summary>
    public LogicalScalar EndScale=LogicalScalar.Zero;
    /// <summary>ランダムな方向と速度のシード。</summary>
    public uint Seed=1u;
}
/// <summary><see cref="ParticleBurst"/>が一度発生済みかどうか。一度だけ発生する。</summary>
public struct ParticleBurstRuntime:IRuntimeComponent<ParticleBurst> {
    public bool Emitted;
}
/// <summary><see cref="ParticleBurst"/>に属するパーティクル1つ。位置とサイズはEntityの<see cref="Transform"/>、経過時間は<see cref="Lifetime"/>で表す。</summary>
public struct Particle {
    public LogicalVector3 Velocity;
    public LogicalScalar Gravity;
    public LogicalScalar StartScale;
    public LogicalScalar EndScale;
    /// <summary>開始時の寿命。これと<see cref="Lifetime"/>の残り時間から経過時間を求める。</summary>
    public LogicalScalar Duration;
}
/// <summary>バーストが作成する形状を登録する。メッシュアセット由来とプリミティブ由来の各パーティクル。</summary>
internal struct ParticleShapes:ISystem {
    public void Spawn(ref Transform Transform,ref StaticMesh Mesh,ref Particle Particle,ref Lifetime Lifetime){}
    public void Spawn(ref Transform Transform,ref PrimitiveMesh Mesh,ref Particle Particle,ref Lifetime Lifetime){}
}
/// <summary>ADR-0078/M0105：まだ発生していないすべての<see cref="ParticleBurst"/>を、ノードのワールド位置から発生させる。</summary>
[SystemPhase(SystemPhase.FixedSimulation)]
internal struct ParticleBurstEmit:ISystem {
    public void Update(Entity Entity,ref Transform Transform,ref ParticleBurst Burst,ref ParticleBurstRuntime Runtime,ref State State){
        if(Runtime.Emitted){ return; }
        // 親の姿勢をまだ解決できないノードは、次のティックでもう一度試す。
        if(RuntimeWorldTransform.TryResolveWorld(Entity,State.World,out var World)!=FixedTransformResolveStatus.Success){ return; }
        Runtime.Emitted=true;
        var Origin=new LogicalVector3(World.M41,World.M42,World.M43);
        var Count=Math.Clamp(Burst.Count,0,ParticleBurst.MaxCount);
        var Duration=Burst.Lifetime>LogicalScalar.Zero?Burst.Lifetime:LogicalScalar.Zero;
        var Random=new Random(Burst.Seed);
        for(var Index=0;Index<Count;Index++){
            var Direction=Random.Direction(Burst.Spread);
            var Speed=Burst.Speed*(LogicalScalar.One+Burst.SpeedJitter*Random.Signed());
            var Placed=new Transform {
                Frame=Transform.Frame,
                Position=Origin,
                Scale=new LogicalVector3(Burst.StartScale,Burst.StartScale,Burst.StartScale),
            };
            var Particle=new Particle {
                Velocity=Direction*Speed,
                Gravity=Burst.Gravity,
                StartScale=Burst.StartScale,
                EndScale=Burst.EndScale,
                Duration=Duration,
            };
            var Life=new Lifetime { Seconds=Duration };
            if(string.IsNullOrEmpty(Burst.AssetPath)){
                State.World.CreateDeferred(Placed,new PrimitiveMesh { Type=Burst.Shape },Particle,Life);
            } else {
                State.World.CreateDeferred(Placed,new StaticMesh { AssetPath=Burst.AssetPath },Particle,Life);
            }
        }
    }
    /// <summary>小さな決定的ジェネレーター（xorshift32）。すべてのマシンで同じバーストを再現するため、システムの乱数生成器は使わない。</summary>
    private struct Random(uint Seed) {
        private uint State=Seed==0u?0x9E3779B9u:Seed;
        private uint Next(){
            var Value=this.State;
            Value^=Value<<13;
            Value^=Value>>17;
            Value^=Value<<5;
            return this.State=Value;
        }
        /// <summary>[-1, 1)の範囲の数値。小数部16ビットで、固定小数点上では正確。</summary>
        internal LogicalScalar Signed()=>LogicalScalar.FromInt64((long)(this.Next()>>16)-32768L)/LogicalScalar.FromInt64(32768L);
        /// <summary>上方向1単位あたり最大<paramref name="Spread"/>だけ横に傾き、横方向の傾きが作る円盤内に均等分布する単位方向。</summary>
        internal LogicalVector3 Direction(LogicalScalar Spread){
            LogicalScalar R;
            LogicalScalar F;
            do {
                R=this.Signed();
                F=this.Signed();
            } while(R*R+F*F>LogicalScalar.One);
            var Lean=new LogicalVector3(R*Spread,LogicalScalar.One,F*Spread);
            return Lean.TryNormalize(out var Unit)?Unit:LogicalVector3.UnitU;
        }
    }
}
/// <summary>ADR-0078/M0105：<see cref="Lifetime"/>を減算する前に、すべての<see cref="Particle"/>を移動し、落下させ、スケールを変更する。</summary>
[SystemPhase(SystemPhase.FixedSimulation)]
[RunsBefore(typeof(LifetimeExpire))]
internal struct ParticleMotion:ISystem {
    public void Update(ref Transform Transform,ref Particle Particle,ref Lifetime Lifetime,ref State State){
        var Step=LogicalScalar.One/LogicalScalar.FromInt64(State.FixedTickRate);
        Transform.Position+=Particle.Velocity*Step;
        Particle.Velocity=new LogicalVector3(Particle.Velocity.R,Particle.Velocity.U-Particle.Gravity*Step,Particle.Velocity.F);
        var Age=Particle.Duration>LogicalScalar.Zero?LogicalScalar.One-Lifetime.Seconds/Particle.Duration:LogicalScalar.One;
        Age=Age<LogicalScalar.Zero?LogicalScalar.Zero:Age>LogicalScalar.One?LogicalScalar.One:Age;
        var Scale=Particle.StartScale+(Particle.EndScale-Particle.StartScale)*Age;
        Transform.Scale=new LogicalVector3(Scale,Scale,Scale);
    }
}
