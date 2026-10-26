using CSharpGameEngine.Entities;
using CSharpGameEngine.Runtime.Physics;
namespace CSharpGameEngine.Runtime.Windows.Tests;
/// <summary>
/// ADR-0078/M0113：破片の接触処理。凸多面体とその複合形状を、ボックス床および接触パッチが複数点を持つ三角形メッシュ床に置く。
/// 回転が止まり、沈み込まず、静止する。眠っているボディは支持物の上で眠り続け、活動中のボディが着地すると目を覚ます。
/// </summary>
public sealed class FragmentContactTests {
    private static float F(LogicalScalar Value)=>Value.TryToSingle(out var Result)?Result:float.NaN;
    private static readonly PrefractureAsset Asset=DestructionTests.Cook("Contact",DestructionTests.Half(0.5f,0.5f,0.5f),12,3);
    private static DeterministicPhysicsWorld BoxFloor(){
        var World=new DeterministicPhysicsWorld();
        World.Upsert(PhysicsBodyDescription.CreateStaticBox(new Entity(1),new LogicalVector3(0,LogicalScalar.FromSingle(-0.5f),0),new LogicalVector3(10,LogicalScalar.FromSingle(0.5f),10)));
        return World;
    }
    private static DeterministicPhysicsWorld MeshFloor(){
        var World=new DeterministicPhysicsWorld();
        var Floor=PhysicsShapeAsset.CreateStaticTriangleMesh("floor",
            [new LogicalVector3(-10,0,-10),new LogicalVector3(10,0,-10),new LogicalVector3(10,0,10),new LogicalVector3(-10,0,10)],[0,2,1,0,3,2]);
        World.Shapes.Add(Floor);
        World.Upsert(new PhysicsBodyDescription(new Entity(1),1,new Collider { Shape=ColliderShape.StaticTriangleMesh,Asset="floor" },null,LogicalVector3.Zero,LogicalQuaternion.Identity,PhysicsVelocity.Zero));
        return World;
    }
    private static Entity Drop(DeterministicPhysicsWorld World,PhysicsShapeAsset Shape,LogicalScalar Mass,ulong StableId,LogicalVector3 Position){
        World.Shapes.Add(Shape);
        var Entity=new Entity((int)StableId);
        var Kind=Shape.Kind==PhysicsShapeAssetKind.Compound?ColliderShape.Compound:ColliderShape.ConvexAsset;
        Assert.Equal(PhysicsUpdateStatus.Success,World.Upsert(new PhysicsBodyDescription(Entity,StableId,new Collider { Shape=Kind,Asset=Shape.Id },new RigidBody { Mass=Mass },Position,LogicalQuaternion.Identity,PhysicsVelocity.Zero)));
        return Entity;
    }
    private static PhysicsBodyState Run(DeterministicPhysicsWorld World,Entity Body,int Ticks,out int MostPoints){
        MostPoints=0;
        for(ulong Tick=0;Tick<(ulong)Ticks;Tick++){
            World.Step(Tick);
            List<PhysicsContactManifold>Manifolds=[];
            World.CollectContactManifolds(Manifolds);
            foreach(var Manifold in Manifolds){ MostPoints=Math.Max(MostPoints,Manifold.Points.Count); }
        }
        Assert.True(World.TryGetBodyState(Body,out var State));
        return State;
    }
    public static TheoryData<int,bool>Fragments=>new(){ { 0,false },{ 5,false },{ 7,false },{ 0,true },{ 5,true },{ 9,true } };
    [Theory]
    [MemberData(nameof(Fragments))]
    public void AFragmentFallsOntoAFloorAndRestsThereAsleepOnAPatchOfPoints(int Fragment,bool Mesh){
        var World=Mesh?MeshFloor():BoxFloor();
        var Body=Drop(World,Asset.CollisionShapes[Fragment],Asset.Fragments[Fragment].Mass,2,new LogicalVector3(0,1,0));
        var State=Run(World,Body,300,out var MostPoints);
        Assert.True(MostPoints>=3,$"the most points in a patch were {MostPoints}");
        Assert.True(State.IsSleeping,"the fragment did not fall asleep");
        var Lowest=Asset.CollisionShapes[Fragment].Vertices.Min(Vertex=>F((State.Rotation.TryRotate(Vertex,out var Turned)?Turned:Vertex).U))+F(State.Position.U);
        Assert.InRange(Lowest,-0.01f,0.001f);
    }
    [Fact]
    public void ACompoundPieceRestsAsleepOnTheFloorWithoutSinking(){
        var State=new FractureState(Asset,new FractureSettings(LogicalScalar.One,LogicalScalar.Zero,false));
        foreach(var Bond in Enumerable.Range(0,Asset.Bonds.Count).Where(Bond=>Asset.Bonds[Bond].FirstFragment==0||Asset.Bonds[Bond].SecondFragment==0)){ State.AddStrain(Bond,State.StrengthOf(Bond)); }
        var Piece=State.Resolve([]).Began[1];
        var Shape=State.ShapeOf(Piece);
        var World=BoxFloor();
        var Body=Drop(World,Shape,Piece.Mass,2,new LogicalVector3(0,LogicalScalar.FromSingle(1.2f),0)+Piece.Center);
        var Rested=Run(World,Body,400,out var MostPoints);
        Assert.True(MostPoints>=3,$"the most points in a patch were {MostPoints}");
        Assert.True(Rested.IsSleeping,"the piece did not fall asleep");
        var Lowest=Shape.Vertices.Min(Vertex=>F((Rested.Rotation.TryRotate(Vertex,out var Turned)?Turned:Vertex).U))+F(Rested.Position.U);
        Assert.InRange(Lowest,-0.01f,0.001f);
    }
    [Fact]
    public void TwoSeparatedFragmentsDoNotTouchAndTwoNeighboursRestingSideBySideStayPut(){
        var World=BoxFloor();
        var Bond=Asset.Bonds.First(static Bond=>true);
        var First=Asset.Fragments[Bond.FirstFragment];
        var Second=Asset.Fragments[Bond.SecondFragment];
        var Lift=new LogicalVector3(0,5,0);
        var A=Drop(World,Asset.CollisionShapes[Bond.FirstFragment],First.Mass,2,First.LocalCenter+Lift);
        var B=Drop(World,Asset.CollisionShapes[Bond.SecondFragment],Second.Mass,3,Second.LocalCenter+Lift+new LogicalVector3(3,0,0));
        World.Step(0);
        List<PhysicsContactManifold>Manifolds=[];
        World.CollectContactManifolds(Manifolds);
        Assert.Empty(Manifolds);
        // 2つを切り出したまま（面を接触させ、隙間なし）配置すると、互いに押し離されず一緒に落ちる。
        var Pair=BoxFloor();
        var C=Drop(Pair,Asset.CollisionShapes[Bond.FirstFragment],First.Mass,2,First.LocalCenter+new LogicalVector3(0,1,0));
        var D=Drop(Pair,Asset.CollisionShapes[Bond.SecondFragment],Second.Mass,3,Second.LocalCenter+new LogicalVector3(0,1,0));
        for(ulong Tick=0;Tick<10;Tick++){ Pair.Step(Tick); }
        Assert.True(Pair.TryGetBodyState(C,out var CState));
        Assert.True(Pair.TryGetBodyState(D,out var DState));
        var Apart=F((DState.Position-CState.Position).LengthSquared);
        var Cut=F((Second.LocalCenter-First.LocalCenter).LengthSquared);
        Assert.InRange(MathF.Sqrt(Apart)-MathF.Sqrt(Cut),-0.01f,0.01f);
    }
    /// <summary>
    /// 1つの破砕アセットから作ったクレート2つは、各破片で同じ形状を共有する。狭域処理が形状ごとにワールドフレームを1つしか保持しなかったため、
    /// 同一ペア内の2つ目のボディが1つ目を上書きし、ペアを自分自身と照合していた。任意の法線を持つ完全な重なりとして検出された結果、
    /// 上側のボディが下側をすり抜け、積み重ねでは毎ティックの貫通深度（破片の厚み）によってボディが持ち上がり、永久に跳ね続けていた。
    /// </summary>
    [Fact]
    public void TwoBodiesOfOneFragmentShapeDroppedOneOnTheOtherComeToRestApart(){
        var Shape=Asset.CollisionShapes[5];
        var Mass=Asset.Fragments[5].Mass;
        // 最も傾斜した面の法線方向に1 cm離す。ボックス同士は重なるが、形状自体は接触しない。
        var Slanted=Enumerable.Range(0,Shape.PlaneCount).Select(Shape.PlaneNormal).MinBy(static Normal=>MathF.Max(MathF.Abs(F(Normal.R)),MathF.Max(MathF.Abs(F(Normal.U)),MathF.Abs(F(Normal.F)))));
        var Width=Shape.Vertices.Max(Vertex=>F(LogicalVector3.Dot(Vertex,Slanted)))-Shape.Vertices.Min(Vertex=>F(LogicalVector3.Dot(Vertex,Slanted)));
        var Floating=new DeterministicPhysicsWorld();
        Floating.Shapes.Add(Shape);
        var Weightless=new RigidBody { Mass=Mass,GravityFactor=LogicalScalar.Zero };
        Assert.Equal(PhysicsUpdateStatus.Success,Floating.Upsert(new PhysicsBodyDescription(new Entity(2),2,new Collider { Shape=ColliderShape.ConvexAsset,Asset=Shape.Id },Weightless,LogicalVector3.Zero,LogicalQuaternion.Identity,PhysicsVelocity.Zero)));
        Assert.Equal(PhysicsUpdateStatus.Success,Floating.Upsert(new PhysicsBodyDescription(new Entity(3),3,new Collider { Shape=ColliderShape.ConvexAsset,Asset=Shape.Id },Weightless,Slanted*LogicalScalar.FromSingle(Width+0.01f),LogicalQuaternion.Identity,PhysicsVelocity.Zero)));
        Floating.Step(0);
        List<PhysicsContactManifold>Apart=[];
        Floating.CollectContactManifolds(Apart);
        Assert.Empty(Apart);
        var Stack=BoxFloor();
        var Lower=Drop(Stack,Shape,Mass,2,new LogicalVector3(0,LogicalScalar.FromSingle(0.6f),0));
        Assert.Equal(PhysicsUpdateStatus.Success,Stack.Upsert(new PhysicsBodyDescription(new Entity(3),3,new Collider { Shape=ColliderShape.ConvexAsset,Asset=Shape.Id },new RigidBody { Mass=Mass },new LogicalVector3(0,2,0),LogicalQuaternion.Identity,PhysicsVelocity.Zero)));
        var LowerState=Run(Stack,Lower,600,out _);
        Assert.True(Stack.TryGetBodyState(new Entity(3),out var UpperState));
        Assert.True(LowerState.IsSleeping,"the lower body did not come to rest");
        Assert.True(UpperState.IsSleeping,"the upper body did not come to rest");
        // 上側が上にとどまるか滑り落ちても、2つは互いに重ならない（修正前は同じ場所に重なっていた）。
        Assert.True(MathF.Sqrt(F((UpperState.Position-LowerState.Position).LengthSquared))>0.05f,"the two bodies lie in the same place");
        List<PhysicsContactManifold>Manifolds=[];
        Stack.CollectContactManifolds(Manifolds);
        foreach(var Point in Manifolds.SelectMany(static Manifold=>Manifold.Points)){
            Assert.True(F(Point.Penetration)<0.005f,$"a contact is {F(Point.Penetration)} m deep");
        }
    }
    [Fact]
    public void ASleepingFragmentStaysAsleepOnTheFloorAndWakesWhenAnotherLandsOnIt(){
        var World=BoxFloor();
        var Resting=Drop(World,Asset.CollisionShapes[5],Asset.Fragments[5].Mass,2,new LogicalVector3(0,LogicalScalar.FromSingle(0.6f),0));
        var Asleep=Run(World,Resting,240,out _);
        Assert.True(Asleep.IsSleeping);
        for(ulong Tick=240;Tick<300;Tick++){ World.Step(Tick); }
        Assert.True(World.TryGetBodyState(Resting,out var Still));
        Assert.True(Still.IsSleeping);
        Assert.Equal(Asleep.Position,Still.Position);
        Assert.Equal(Asleep.Rotation,Still.Rotation);
        var Falling=Drop(World,Asset.CollisionShapes[0],Asset.Fragments[0].Mass,3,Still.Position+new LogicalVector3(0,2,0));
        var Woken=false;
        for(ulong Tick=300;Tick<420;Tick++){
            World.Step(Tick);
            Woken|=World.Events.Any(Event=>Event.Type==PhysicsEventType.Wake&&Event.First==Resting);
        }
        Assert.True(Woken,"the resting fragment was not woken by the one that landed on it");
    }
    /// <summary>
    /// 眠っているボディは積分されない。そのボディを支えていた物体（破片の寿命終了、または下で破壊可能物が崩壊）がなくなった場合、
    /// 空中に浮かび続けないよう起こす必要がある。修正前は支持の接触が消えるだけで、ボディを起こしていなかった。
    /// </summary>
    [Fact]
    public void ASleepingFragmentWakesAndFallsWhenWhatHoldsItIsRemoved(){
        var World=BoxFloor();
        var Support=new Entity(9);
        Assert.Equal(PhysicsUpdateStatus.Success,World.Upsert(PhysicsBodyDescription.CreateStaticBox(Support,new LogicalVector3(0,1,0),new LogicalVector3(1,LogicalScalar.FromSingle(0.5f),1))));
        var Resting=Drop(World,Asset.CollisionShapes[5],Asset.Fragments[5].Mass,2,new LogicalVector3(0,LogicalScalar.FromSingle(2.1f),0));
        var Asleep=Run(World,Resting,240,out _);
        Assert.True(Asleep.IsSleeping,"the fragment did not fall asleep on the support");
        Assert.True(World.Remove(Support));
        var Woken=false;
        for(ulong Tick=240;Tick<420;Tick++){
            World.Step(Tick);
            Woken|=World.Events.Any(Event=>Event.Type==PhysicsEventType.Wake&&Event.First==Resting);
        }
        Assert.True(Woken,"the fragment was not woken when its support was removed");
        Assert.True(World.TryGetBodyState(Resting,out var Fallen));
        Assert.True(F(Fallen.Position.U)<F(Asleep.Position.U)-1f,$"the fragment stayed at {F(Fallen.Position.U)} m (it slept at {F(Asleep.Position.U)} m)");
    }
}
