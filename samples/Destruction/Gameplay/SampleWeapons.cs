using CSharpGameEngine.Destruction;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Numerics;
using CSharpGameEngine.Runtime.Character;
using CSharpGameEngine.Runtime.Physics;
using CSharpGameEngine.Runtime.Windows;
namespace CgeDestructionSample;
/// <summary>キャラクターが撃つボール：最初の物理ステップの前に、Transformの前方方向に<see cref="Speed"/>を掛けた値を速度とする。</summary>
[SceneComponentOrder(SceneComponentCategory.Gameplay,50)]
public struct Projectile() {
    /// <summary>ボールの前方軸に沿ったm/s。</summary>
    public LogicalScalar Speed=LogicalScalar.FromInt64(30);
    public bool Launched=false;
}
/// <summary>Fキーで視点の向きへキャラクターの胸元からボールを撃つ。Eキーでキャラクターの3メートル前に爆発を起こす。</summary>
internal struct SampleWeapons:ISystem {
    public void Update(in PlayerActions Input,in Transform Transform,in ViewAngles View,Spawner Spawner){
        if(!Input.Fire.Pressed&&!Input.Explode.Pressed){ return; }
        var Forward=PlayerControl.MoveDirection(View,0f,1f);
        if(Input.Fire.Pressed&&TryFace(Forward,out var Facing)){
            Spawner.Spawn<CgeDestructionSample.Scenes.Ball>(Transform.Position+Forward+new LogicalVector3(LogicalScalar.Zero,LogicalScalar.FromSingle(0.3f),LogicalScalar.Zero),Facing);
        }
        if(Input.Explode.Pressed){
            Spawner.Spawn<CgeDestructionSample.Scenes.Explosion>(Transform.Position+Forward*LogicalScalar.FromInt64(3));
        }
    }
    /// <summary>ボールの前方軸を<paramref name="Forward"/>に向ける、上方向を軸とした回転角（軸角表現の符号規約に従う）。</summary>
    private static bool TryFace(LogicalVector3 Forward,out LogicalQuaternion Facing){
        Facing=LogicalQuaternion.Identity;
        if(!Forward.R.TryToSingle(out var R)||!Forward.F.TryToSingle(out var F)){ return false; }
        var Angle=MathF.Atan2(R,F);
        foreach(var Turn in (ReadOnlySpan<float>)[Angle,-Angle]){
            if(LogicalQuaternion.TryCreateAxisAngle(LogicalVector3.UnitU,LogicalScalar.FromSingle(Turn),out var Candidate)
                &&Candidate.TryRotate(LogicalVector3.UnitF,out var Along)&&LogicalVector3.Dot(Along,Forward)>LogicalScalar.FromSingle(0.99f)){
                Facing=Candidate;
                return true;
            }
        }
        return false;
    }
}
/// <summary>新しいボールに前方軸方向の速度を与える（物理が剛体を取り込む前のFixedSimulation中に行う）。</summary>
[SystemPhase(SystemPhase.FixedSimulation)]
internal struct ProjectileLaunch:ISystem {
    public void Update(ref Transform Transform,ref Projectile Projectile,ref PhysicsVelocity Velocity){
        if(Projectile.Launched){ return; }
        Projectile.Launched=true;
        if(Transform.Rotation.TryRotate(LogicalVector3.UnitF,out var Forward)){ Velocity.Linear=Forward*Projectile.Speed; }
    }
}
/// <summary>
/// 自動実行（FeatureRunnerのverify-destruction）で表示フレームごとに1行出力する。破壊可能オブジェクト数、破壊数、切れた結合、分離した破片数、そのうち静止中の数、最も低い破片と最速の破片、過去の衝撃による内部ひずみを受けている未破断結合、および破壊可能オブジェクトの最大クラスターレベルを記録する（M0124）。CGE_AUTOMATION_FRAMESが設定されている場合のみ出力する。
/// </summary>
[SystemPhase(SystemPhase.Presentation)]
internal struct DestructionReport:IWorldSystem {
    private static readonly bool Enabled=!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CGE_AUTOMATION_FRAMES"));
    private int Frame;
    public void Update(Destruction Destruction,IPhysicsHost Host,ref State State){
        if(!Enabled){ return; }
        this.Frame++;
        var Broken=0;
        var Bonds=0;
        var Loose=0;
        var Sleeping=0;
        var Lowest=float.PositiveInfinity;
        var Fastest=0f;
        var Strained=0;
        var Levels=0;
        var World=Host.Physics.Active.World;
        for(var Slot=0;Slot<State.World.GetStatistics().EntitySlotCount;Slot++){
            var Entity=State.World.EntityArchetypeIndices.GetRefUnchecked(Slot).Entity;
            if(!State.World.IsAlive(Entity)){ continue; }
            if(State.World.TryGetComponent(Entity,out DestructibleState Runtime)){
                Broken+=Runtime.Broken?1:0;
                Bonds+=Runtime.BrokenBonds;
                if(Destruction.TryGetState(Entity,out var Fracture)){
                    Levels=Math.Max(Levels,Fracture.Asset.ClusterLevelCount);
                    for(var Bond=0;Bond<Fracture.Asset.Bonds.Count;Bond++){
                        if(!Fracture.IsBondBroken(Bond)&&Fracture.InternalStrainOf(Bond)>LogicalScalar.Zero){ Strained++; }
                    }
                }
            }
            if(State.World.TryGetComponent(Entity,out DestructiblePiece _)&&World.TryGetBodyState(Entity,out var Body)&&Body.MotionType==PhysicsMotionType.Dynamic){
                Loose++;
                Sleeping+=Body.IsSleeping?1:0;
                if(Body.Position.U.TryToSingle(out var Height)){ Lowest=MathF.Min(Lowest,Height); }
                if(Body.Velocity.Linear.LengthSquared.TryToSingle(out var Speed)){ Fastest=MathF.Max(Fastest,MathF.Sqrt(Speed)); }
            }
        }
        Console.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"CGE-DESTRUCTION-SAMPLE frame={this.Frame} tick={State.FixedTick} destructibles={Destruction.Count} broken={Broken} bonds={Bonds} loose={Loose} sleeping={Sleeping} lowest={(float.IsInfinity(Lowest)?0f:Lowest):F3} fastest={Fastest:F3} strained={Strained} levels={Levels}"));
    }
}
