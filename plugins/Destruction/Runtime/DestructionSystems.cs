using CSharpGameEngine.Entities;
using CSharpGameEngine.Numerics;
using CSharpGameEngine.Runtime.Character;
using CSharpGameEngine.Runtime.Physics;
using CSharpGameEngine.Runtime.Windows;
namespace CSharpGameEngine.Destruction;
/// <summary>実行時に破壊プラグインが作成する Entity（描画用破片、分離破片のボディ、固定破片のボディ）を登録する（ADR-0078/M0113）。アーキタイプは固定集合なので、ここで宣言する。</summary>
internal struct DestructionShapes:ISystem {
    public void Spawn(ref Transform Transform,ref FragmentMesh Mesh,ref DestructibleFragment Fragment){}
    public void Spawn(ref Transform Transform,ref Collider Collider,ref RigidBody Body,ref PhysicsRotation Rotation,ref PhysicsVelocity Velocity,ref PhysicsStableId StableId,ref DestructiblePiece Piece){}
    public void Spawn(ref Transform Transform,ref Collider Collider,ref PhysicsRotation Rotation,ref PhysicsStableId StableId,ref DestructiblePiece Piece){}
}
/// <summary>各 <see cref="Destructible"/> を、存在して最初の固定ティックに読み込む（一度だけ。問題は一度だけ出力し、<see cref="DestructibleState.Problem"/> に保持する）。</summary>
[SystemPhase(SystemPhase.FixedSimulation)]
internal struct DestructibleLoad:ISystem {
    public void Update(Entity Entity,ref Transform Transform,ref Destructible Destructible,ref DestructibleState Runtime,Destruction Destruction,ref State State){
        if(Runtime.Attempted){ return; }
        Destruction.Load(Entity,in Transform,in Destructible,ref Runtime,ref State);
    }
}
/// <summary>
/// <see cref="DestructionImpact"/> を持つキャラクターが、今ティックに破壊可能オブジェクト（またはその破片）へ衝突しようとしている場合、衝突させる。接触点にはキャラクターの
/// 質量と接近速度の積に相当するひずみを加え、分離中の破片には同じ力積で押す。物理ステップ後、キャラクターの移動前に実行するため、接近速度には到達時の値を使う。M0124：ひずみが衝撃（内部ひずみに加算）
/// になるのは、そのボディへの接触を始めたティックだけ。
/// </summary>
[SystemPhase(SystemPhase.PhysicsSimulation)]
[RunsAfter(typeof(PhysicsTransformWriteback))]
[RunsBefore(typeof(CharacterMotionUpdate))]
internal struct DestructionImpacts:ISystem {
    public void Update(Entity Entity,ref Transform Transform,ref CharacterMotion Motion,ref CharacterMotionState Moving,ref DestructionImpact Impact,Destruction Destruction,IPhysicsHost Host,ref State State){
        if(Transform.Frame!=Host.Physics.Frame||State.FixedTickRate<=0||Destruction.Count==0){ return; }
        var Velocity=Moving.Velocity;
        if(Velocity.LengthSquared<=Impact.MinimumSpeed*Impact.MinimumSpeed){ return; }
        var Step=LogicalScalar.One/LogicalScalar.FromInt64(State.FixedTickRate);
        var Radius=Motion.CapsuleRadius;
        var SegmentHalf=Motion.CapsuleHalfHeight-Radius;
        var Capsule=new Collider { Shape=ColliderShape.Capsule,Radius=Radius,HalfHeight=SegmentHalf<LogicalScalar.Zero?LogicalScalar.Zero:SegmentHalf };
        List<PhysicsShapeContact>Contacts=[];
        Host.Physics.Active.MutableWorld.CollectContacts(Capsule,Transform.Position+Velocity*Step,LogicalQuaternion.Identity,Motion.CollidesWith,Contacts);
        var Sequence=0u;
        foreach(var Contact in Contacts){
            if(!Destruction.Owns(Contact.Entity)){ continue; }
            var Approach=LogicalVector3.Dot(Velocity,Contact.Normal);
            if(Approach<Impact.MinimumSpeed){ continue; }
            var Impulse=Impact.Mass*Approach;
            Destruction.ApplyCharacterImpulse(Entity,Contact.Entity,Contact.Point,Impulse,State.FixedTick);
            if(Host.Physics.Active.World.TryGetBodyState(Contact.Entity,out var Body)&&Body.MotionType==PhysicsMotionType.Dynamic){
                Host.Physics.Active.Commands.Impulse(State.FixedTick+1,(1UL<<61)|(ulong)(uint)Entity.Index,Sequence++,Contact.Entity,Contact.Normal*Impulse);
            }
        }
    }
}
/// <summary>各 <see cref="StrainField"/> を、このティックの<see cref="Destruction"/>処理（最初のティック、または繰り返し設定時は毎ティック）へ渡す。</summary>
[SystemPhase(SystemPhase.PhysicsSimulation)]
[RunsAfter(typeof(PhysicsStep))]
[RunsBefore(typeof(DestructionStep))]
internal struct StrainFieldApply:ISystem {
    public void Update(Entity Entity,ref Transform Transform,ref StrainField Field,ref StrainFieldState Runtime,Destruction Destruction,ref State State){
        if(Runtime.Fired&&!Field.Repeat){ return; }
        if(RuntimeWorldTransform.TryResolveWorld(Entity,State.World,out var World)!=FixedTransformResolveStatus.Success){ return; }
        Runtime.Fired=true;
        Destruction.ApplyStrain(new LogicalVector3(World.M41,World.M42,World.M43),Field.Radius,Field.Strain);
    }
}
/// <summary>ティックの破壊処理（<see cref="Destruction"/>）。物理ステップと衝撃処理の後、ボディの状態を書き戻す前に実行する。</summary>
[SystemPhase(SystemPhase.PhysicsSimulation)]
[RunsAfter(typeof(PhysicsStep))]
[RunsAfter(typeof(DestructionImpacts))]
internal struct DestructionStep:IWorldSystem {
    public void Update(Destruction Destruction,IPhysicsHost Host,ref State State){
        if(State.ExecutionMode!=EngineExecutionMode.Runtime||Destruction.Count==0){ return; }
        Destruction.Step(Host,ref State);
    }
}
/// <summary>すべての <see cref="ForceField"/> の周囲にある移動中のボディを押す（フィールドの有効期間中、各ボディにつき一度）。力は次のティックに適用する。</summary>
[SystemPhase(SystemPhase.PhysicsSimulation)]
[RunsAfter(typeof(DestructionStep))]
internal struct ForceFieldApply:ISystem {
    public void Update(Entity Entity,ref Transform Transform,ref ForceField Field,ref ForceFieldState Runtime,Destruction Destruction,IPhysicsHost Host,ref State State){
        if(State.ExecutionMode!=EngineExecutionMode.Runtime||Transform.Frame!=Host.Physics.Frame){ return; }
        if(RuntimeWorldTransform.TryResolveWorld(Entity,State.World,out var World)!=FixedTransformResolveStatus.Success){ return; }
        Destruction.Push(Entity,new LogicalVector3(World.M41,World.M42,World.M43),in Field,ref Runtime,Host,ref State);
    }
}
/// <summary>
/// 描画用の各破片をボディの位置に配置する。ボディの位置に、その回転（<c>PhysicsRotation</c> がある場合はその回転）で回転させた破片のオフセットを加える。物理処理によるボディ状態の書
/// き戻し後に実行し、このティックの姿勢で破片を描画する。
/// </summary>
[SystemPhase(SystemPhase.PostPhysics)]
internal struct DestructionFollow:ISystem {
    public void Update(ref Transform Transform,ref DestructibleFragment Fragment,ref State State){
        if(!State.World.TryGetComponent(Fragment.Body,out Transform Body)){ return; }
        var Rotation=Destruction.CurrentRotation(Fragment.Body,State.World,Body.Rotation);
        if(!Rotation.TryRotate(Fragment.Offset,out var Offset)){ return; }
        Transform.Position=Body.Position+Offset;
        Transform.Rotation=Rotation;
    }
}
/// <summary>Entity が消えた（シーンのアンロードまたは破棄）破壊可能オブジェクトのボディと描画用破片を、次のティックに削除する。破壊可能オブジェクトが消えたティック内に作成されたボディは、そのティック中には破棄できないため。</summary>
[SystemPhase(SystemPhase.FixedSimulation)]
internal struct DestructionOrphans:ISystem {
    public void Update(Entity Entity,ref DestructiblePiece Piece,ref State State){
        if(!State.World.IsAlive(Piece.Root)){ State.World.DestroyDeferred(Entity,ref State); }
    }
    public void Update(Entity Entity,ref DestructibleFragment Fragment,ref State State){
        if(!State.World.IsAlive(Fragment.Root)){ State.World.DestroyDeferred(Entity,ref State); }
    }
}
