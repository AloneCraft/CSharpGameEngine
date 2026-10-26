using CSharpGameEngine.Entities;
using CSharpGameEngine.Numerics;
using CSharpGameEngine.Runtime.Physics;
using Xunit.Abstractions;
namespace CSharpGameEngine.Runtime.Windows.Tests;
/// <summary>
/// 箱・凸多面体・複合形状と静的な三角形メッシュの接触が、複数の面の向きの接触点を同時に持つこと(M0124)。M0112 の事実: 縁石の斜面と平らな地面にまたがった
/// 車体が沈んだ。接触が 1 tick に 1 つの面の向き(一番深い角の面)しか使わず、ほかの向きの面(と、箱の底面に入るメッシュの稜線)の支えを捨てていたため。
/// ここでは、断面(F に沿った折れ線)を R に押し出したメッシュの上に、車体と同じ大きさ(1.9 × 0.9 × 4.5 m、1500 kg)の形を置いて、外から見える振る舞いを測る:
/// 沈み込み(形の底面の標本点が地面より下にある深さと、メッシュの稜線が形の中に入る深さの大きいほう)、接触の法線の向き、接触の連続、決定性。
/// </summary>
public sealed class PhysicsMeshMultiFaceContactTests(ITestOutputHelper Output) {
    private static float F(LogicalScalar Value)=>Value.TryToSingle(out var Result)?Result:float.NaN;
    private static readonly LogicalVector3 Half=new(LogicalScalar.FromSingle(0.95f),LogicalScalar.FromSingle(0.45f),LogicalScalar.FromSingle(2.25f));
    /// <summary>平らな地面(F &lt; 0)から 0.4 m で 0.2 m 上がる斜面(26.6°)と、その上の平らな段(縁石の頂と同じ高さ)。</summary>
    private static readonly (float F,float U)[]Step=[(-20f,0f),(0f,0f),(0.4f,0.2f),(20f,0.2f)];
    /// <summary>縁石の断面: 0.4 m で 0.2 m 上がり、0.4 m の平らな頂、0.4 m で下がる。</summary>
    private static readonly (float F,float U)[]Curb=[(-20f,0f),(0f,0f),(0.4f,0.2f),(0.8f,0.2f),(1.2f,0f),(20f,0f)];
    public enum Shape { Box,Polyhedron,Compound }
    private static float SurfaceAt((float F,float U)[]Profile,float At){
        for(var Index=0;Index+1<Profile.Length;Index++){
            var (F0,U0)=Profile[Index];
            var (F1,U1)=Profile[Index+1];
            if(At>=F0&&At<=F1){ return U0+(U1-U0)*(At-F0)/(F1-F0); }
        }
        return Profile[^1].U;
    }
    /// <summary>
    /// 断面を R の −10〜10 m に押し出した静的な三角形メッシュ(断面の区間ごとに 2 つの三角形)。<paramref name="Cell"/> を与えると、各区間を F に
    /// その長さの格子へ細かく分ける(面の境の多い床)。
    /// </summary>
    internal static DeterministicPhysicsWorld Ground((float F,float U)[]Profile,float Cell=0f,float Friction=1f){
        var World=new DeterministicPhysicsWorld();
        List<(float F,float U)>Points=[];
        for(var Index=0;Index+1<Profile.Length;Index++){
            var (F0,U0)=Profile[Index];
            var (F1,U1)=Profile[Index+1];
            var Pieces=Cell>0f?Math.Max(1,(int)MathF.Round((F1-F0)/Cell)):1;
            for(var Piece=0;Piece<Pieces;Piece++){ Points.Add((F0+(F1-F0)*Piece/Pieces,U0+(U1-U0)*Piece/Pieces)); }
        }
        Points.Add(Profile[^1]);
        List<LogicalVector3>Vertices=[];
        List<int>Indices=[];
        foreach(var (At,Height) in Points){
            Vertices.Add(new LogicalVector3(-10,LogicalScalar.FromSingle(Height),LogicalScalar.FromSingle(At)));
            Vertices.Add(new LogicalVector3(10,LogicalScalar.FromSingle(Height),LogicalScalar.FromSingle(At)));
        }
        for(var Index=0;Index+1<Points.Count;Index++){
            var A=Index*2;
            Indices.AddRange([A,A+2,A+1,A+1,A+2,A+3]);
        }
        World.Shapes.Add(PhysicsShapeAsset.CreateStaticTriangleMesh("ground",Vertices,Indices));
        Assert.Equal(PhysicsUpdateStatus.Success,World.Upsert(new PhysicsBodyDescription(
            new Entity(1),1,new Collider { Shape=ColliderShape.StaticTriangleMesh,Asset="ground",Material=new PhysicsMaterial { Friction=LogicalScalar.FromSingle(Friction) } },null,
            LogicalVector3.Zero,LogicalQuaternion.Identity,PhysicsVelocity.Zero)));
        return World;
    }
    /// <summary>中心が <paramref name="Center"/>、半分の大きさが <paramref name="Extent"/> の箱の 8 頂点と 12 個の三角形の凸多面体。</summary>
    private static PhysicsShapeAsset BoxPolyhedron(string Id,LogicalVector3 Center,LogicalVector3 Extent){
        List<LogicalVector3>Vertices=[];
        for(var Index=0;Index<8;Index++){
            Vertices.Add(Center+new LogicalVector3((Index&1)==0?-Extent.R:Extent.R,(Index&2)==0?-Extent.U:Extent.U,(Index&4)==0?-Extent.F:Extent.F));
        }
        return PhysicsShapeAsset.CreateConvexPolyhedron(Id,Vertices,[0,1,3,0,3,2,4,6,7,4,7,5,0,4,5,0,5,1,2,3,7,2,7,6,0,2,6,0,6,4,1,5,7,1,7,3]);
    }
    /// <summary>
    /// 車体の大きさの形を 1 つ置く(stable ID 2)。箱、同じ形の凸多面体、前後の 2 つの凸多面体の複合形状のどれでも、形の中心が物体の位置。
    /// <paramref name="Friction"/> は形の材質の摩擦(接触は両方の小さいほう)。
    /// </summary>
    internal static Entity Place(DeterministicPhysicsWorld World,Shape Kind,LogicalVector3 Position,LogicalScalar?Friction=null,PhysicsVelocity?Velocity=null){
        var Material=new PhysicsMaterial { Friction=Friction??LogicalScalar.FromSingle(0.7f) };
        Collider Collider;
        switch(Kind){
            case Shape.Polyhedron:
                World.Shapes.Add(BoxPolyhedron("body",LogicalVector3.Zero,Half));
                Collider=new Collider { Shape=ColliderShape.ConvexAsset,Asset="body",Material=Material };
                break;
            case Shape.Compound:
                var Quarter=Half with { F=Half.F/LogicalScalar.FromInt64(2) };
                var Front=BoxPolyhedron("front",new LogicalVector3(0,0,Quarter.F),Quarter);
                var Back=BoxPolyhedron("back",new LogicalVector3(0,0,-Quarter.F),Quarter);
                World.Shapes.Add(PhysicsShapeAsset.CreateCompound("body",[Front,Back]));
                Collider=new Collider { Shape=ColliderShape.Compound,Asset="body",Material=Material };
                break;
            default:
                Collider=new Collider { Shape=ColliderShape.Box,HalfExtents=Half,Material=Material };
                break;
        }
        var Body=new Entity(2);
        Assert.Equal(PhysicsUpdateStatus.Success,World.Upsert(new PhysicsBodyDescription(
            Body,2,Collider,new RigidBody { MotionType=PhysicsMotionType.Dynamic,Mass=1500,AllowSleep=false },Position,LogicalQuaternion.Identity,Velocity??PhysicsVelocity.Zero)));
        return Body;
    }
    private static PhysicsBodyState BodyOf(DeterministicPhysicsWorld World,Entity Body)=>World.TryGetBodyState(Body,out var State)?State:throw new InvalidOperationException();
    /// <summary>
    /// 形がメッシュに沈んだ深さ(m)。底面の 11 × 21 の標本点が地面より下にある深さと、断面の頂点(メッシュの稜線。R の −0.95〜0.95 の 11 点)が形の中に入った深さの、
    /// 大きいほう。形は箱(中心が物体の位置)。
    /// </summary>
    internal static float Sinking((float F,float U)[]Profile,in PhysicsBodyState State){
        var Deepest=0f;
        var Conjugate=State.Rotation.Conjugate();
        for(var Row=0;Row<=20;Row++){
            for(var Column=0;Column<=10;Column++){
                var Local=new LogicalVector3(Half.R*LogicalScalar.FromSingle(Column/5f-1f),-Half.U,Half.F*LogicalScalar.FromSingle(Row/10f-1f));
                Assert.True(State.Rotation.TryRotate(Local,out var Turned));
                var World=State.Position+Turned;
                Deepest=MathF.Max(Deepest,SurfaceAt(Profile,F(World.F))-F(World.U));
            }
        }
        foreach(var (At,Height) in Profile){
            for(var Column=0;Column<=10;Column++){
                var Point=new LogicalVector3(Half.R*LogicalScalar.FromSingle(Column/5f-1f)+State.Position.R,LogicalScalar.FromSingle(Height),LogicalScalar.FromSingle(At));
                Assert.True(Conjugate.TryRotate(Point-State.Position,out var Local));
                var Inside=MathF.Min(F(Half.R)-MathF.Abs(F(Local.R)),MathF.Min(F(Half.U)-MathF.Abs(F(Local.U)),F(Half.F)-MathF.Abs(F(Local.F))));
                Deepest=MathF.Max(Deepest,Inside);
            }
        }
        return Deepest;
    }
    private static List<PhysicsContactManifold>Manifolds(DeterministicPhysicsWorld World){
        List<PhysicsContactManifold>Result=[];
        World.CollectContactManifolds(Result);
        return Result;
    }
    /// <summary>接触の点の法線の向きの数(内積 0.95 以上を同じ向きとみなす)。</summary>
    private static int Orientations(IEnumerable<PhysicsContactPoint>Points){
        List<LogicalVector3>Seen=[];
        foreach(var Point in Points){
            if(!Seen.Any(Normal=>F(LogicalVector3.Dot(Normal,Point.Normal))>=0.95f)){ Seen.Add(Point.Normal); }
        }
        return Seen.Count;
    }
    /// <summary>メッシュ(stable ID 1、組の 1 つ目)から形(2 つ目)へ向く法線は、地面から上へ押す向き(U が正)で、反転していない。</summary>
    private static void AssertNormalsPushTheShapeOutOfTheGround(IEnumerable<PhysicsContactManifold>Contacts,ulong Tick){
        foreach(var Manifold in Contacts){
            Assert.Equal(new Entity(1),Manifold.First);
            foreach(var Point in Manifold.Points){
                Assert.True(F(Point.Normal.U)>0.3f,$"tick {Tick}: a contact normal ({F(Point.Normal.R):F3}, {F(Point.Normal.U):F3}, {F(Point.Normal.F):F3}) at ({F(Point.Position.R):F3}, {F(Point.Position.U):F3}, {F(Point.Position.F):F3}) {F(Point.Penetration):F4} m deep does not push the shape out of the ground");
            }
        }
    }
    public static TheoryData<Shape>Shapes=>new(){ Shape.Box,Shape.Polyhedron,Shape.Compound };
    [Theory]
    [MemberData(nameof(Shapes))]
    public void AShapeStraddlingASlopeAndTheFlatGroundRestsOnBothWithoutSinking(Shape Kind){
        // 後ろ半分は平らな地面、前半分は斜面と段の上。止まった姿勢は、後ろの角が地面に、底面が段の縁(稜線)に載って傾いたもの。修正前は後ろの角の
        // 面(上向き)だけを使い、稜線が底面に 8 cm 入った(段の縁の高さ 0.2 m、底面のその位置の高さ 0.12 m)。
        var World=Ground(Step);
        var Body=Place(World,Kind,new LogicalVector3(0,LogicalScalar.FromSingle(0.9f),0));
        var Worst=0f;
        for(ulong Tick=1;Tick<=300;Tick++){
            World.Step(Tick);
            AssertNormalsPushTheShapeOutOfTheGround(Manifolds(World),Tick);
            if(Tick>60){ Worst=MathF.Max(Worst,Sinking(Step,BodyOf(World,Body))); }
        }
        var State=BodyOf(World,Body);
        Output.WriteLine(FormattableString.Invariant($"{Kind}: worst {Worst:F4} m, at rest ({F(State.Position.U):F3}, {F(State.Position.F):F3}) v {F(State.Velocity.Linear.LengthSquared):E2}"));
        // 範囲の根拠: 修正後は 1 cm 未満(接触の許容 0.1 mm と、1 tick の重力の沈み g·Δt² = 2.7 mm)。修正前は 8 cm。
        Assert.True(Worst<0.01f,$"{Kind} sank {Worst:F4} m into the ground");
        Assert.True(F(State.Velocity.Linear.LengthSquared)<1e-4f,$"{Kind} did not come to rest");
    }
    [Theory]
    [MemberData(nameof(Shapes))]
    public void AShapeRestingOnBothSlopesOfAValleyIsHeldOnEachSlopeAtOnce(Shape Kind){
        // 左右に 26.6° で上がる谷(底は形より下)。形の前後の底の角が、向きの違う 2 つの斜面(法線の内積 0.6)に同時に載る。修正前は一番深い角の
        // 斜面だけを使うので、もう片方の斜面の角が支えを失った。
        (float F,float U)[]Valley=[(-6f,3f),(0f,0f),(6f,3f)];
        var World=Ground(Valley);
        var Body=Place(World,Kind,new LogicalVector3(0,LogicalScalar.FromSingle(1.75f),0));
        var Worst=0f;
        var Fewest=int.MaxValue;
        for(ulong Tick=1;Tick<=300;Tick++){
            World.Step(Tick);
            var Contacts=Manifolds(World);
            AssertNormalsPushTheShapeOutOfTheGround(Contacts,Tick);
            if(Tick>60){
                Worst=MathF.Max(Worst,Sinking(Valley,BodyOf(World,Body)));
                Fewest=Math.Min(Fewest,Orientations(Contacts.SelectMany(static Manifold=>Manifold.Points)));
            }
        }
        var State=BodyOf(World,Body);
        Output.WriteLine(FormattableString.Invariant($"{Kind}: worst {Worst:F4} m, fewest orientations {Fewest}, at rest ({F(State.Position.U):F3}, {F(State.Position.F):F3})"));
        Assert.True(Worst<0.01f,$"{Kind} sank {Worst:F4} m into the valley");
        Assert.Equal(2,Fewest);
        // 谷の真ん中で止まる(左右の支えが釣り合う)。
        Assert.InRange(F(State.Position.F),-0.01f,0.01f);
    }
    [Theory]
    [MemberData(nameof(Shapes))]
    public void AShapePushedAtTheStepClimbsOntoItWithoutSinkingOrBeingThrown(Shape Kind){
        // 摩擦の無い形を 2 m/s² で 2.7 秒押して(段の 4 m 手前から、4 m/s ほどで段へ)、段へ乗り上げさせる。前の角が斜面に、後ろの角が地面に、のちに底面が
        // 段の縁に載る。押し終えたら滑り続ける(メッシュの端 F = 20 m には着かない)。
        var World=Ground(Step,Friction:0f);
        var Body=Place(World,Kind,new LogicalVector3(0,LogicalScalar.FromSingle(0.46f),-4),LogicalScalar.Zero);
        var Worst=0f;
        var Highest=0f;
        for(ulong Tick=1;Tick<=360;Tick++){
            if(Tick<=160){ World.Commands.Force(Tick,2,0,Body,new LogicalVector3(0,0,3000)); }
            World.Step(Tick);
            AssertNormalsPushTheShapeOutOfTheGround(Manifolds(World),Tick);
            var State=BodyOf(World,Body);
            Worst=MathF.Max(Worst,Sinking(Step,State));
            Highest=MathF.Max(Highest,F(State.Position.U));
        }
        var Final=BodyOf(World,Body);
        Output.WriteLine(FormattableString.Invariant($"{Kind}: worst {Worst:F4} m, highest {Highest:F3}, final ({F(Final.Position.U):F3}, {F(Final.Position.F):F3})"));
        // 範囲の根拠: 乗り上げる間の衝突の 1 tick の深さは 1〜2 cm(4 m/s で斜面へ入る)。段の上で底面は 0.2 m、中心は 0.65 m。跳ね上がらない(0.9 m 未満)。
        Assert.True(Worst<0.03f,$"{Kind} sank {Worst:F4} m into the ground");
        Assert.True(F(Final.Position.F)>3f,$"{Kind} did not climb the step: {F(Final.Position.F):F3}");
        Assert.InRange(F(Final.Position.U),0.62f,0.68f);
        Assert.True(Highest<0.9f,$"{Kind} was thrown up to {Highest:F3} m");
    }
    [Theory]
    [MemberData(nameof(Shapes))]
    public void AShapeSlidingOverTheSeamsOfAFineFloorKeepsItsPatchAndItsHeight(Shape Kind){
        // 0.25 m ごとの三角形の床(面の境が多い)の上を 3 m/s で滑らせる。面の境で接触が途切れると、その tick に点が減り、形が沈む。
        (float F,float U)[]Flat=[(-20f,0f),(20f,0f)];
        var World=Ground(Flat,Cell:0.25f,Friction:0f);
        var Body=Place(World,Kind,new LogicalVector3(0,LogicalScalar.FromSingle(0.4505f),-8),LogicalScalar.Zero,new PhysicsVelocity { Linear=new LogicalVector3(0,0,3) });
        var FewestPoints=int.MaxValue;
        var Lowest=float.MaxValue;
        var HighestCentre=float.MinValue;
        for(ulong Tick=1;Tick<=240;Tick++){
            World.Step(Tick);
            var Contacts=Manifolds(World);
            AssertNormalsPushTheShapeOutOfTheGround(Contacts,Tick);
            if(Tick<=10){ continue; }
            FewestPoints=Math.Min(FewestPoints,Contacts.Sum(static Manifold=>Manifold.Points.Count));
            var State=BodyOf(World,Body);
            Lowest=MathF.Min(Lowest,F(State.Position.U));
            HighestCentre=MathF.Max(HighestCentre,F(State.Position.U));
        }
        Output.WriteLine(FormattableString.Invariant($"{Kind}: fewest points {FewestPoints}, centre {Lowest:F4}..{HighestCentre:F4}, at F {F(BodyOf(World,Body).Position.F):F2}"));
        Assert.True(FewestPoints>=4,$"{Kind}'s patch dropped to {FewestPoints} point(s) at a seam");
        Assert.InRange(Lowest,0.447f,0.452f);
        Assert.InRange(HighestCentre,0.447f,0.452f);
    }
    [Fact]
    public void ABoxRestingOnACurbAndTheGroundDoesNotSinkAndTheSameRunIsTheSameEveryTime(){
        // 縁石(0.4 m で 0.2 m 上がる、平らな頂、下がる)の上に車体の箱を落とす。2 回の実行の、毎 tick の物体の状態と接触の点(位置・法線・深さ・順)が同じ。
        static List<string>Run(out float Worst,out int Orientation,out int MostPoints){
            var World=Ground(Curb);
            var Body=Place(World,Shape.Box,new LogicalVector3(0,LogicalScalar.FromSingle(0.9f),LogicalScalar.FromSingle(-1.2f)));
            List<string>Trace=[];
            Worst=0f;
            Orientation=0;
            MostPoints=0;
            for(ulong Tick=1;Tick<=300;Tick++){
                World.Step(Tick);
                var Contacts=Manifolds(World);
                AssertNormalsPushTheShapeOutOfTheGround(Contacts,Tick);
                var Points=Contacts.SelectMany(static Manifold=>Manifold.Points).ToList();
                MostPoints=Math.Max(MostPoints,Points.Count);
                Trace.Add($"{Tick} {World.ComputeStateHash():X16} "+string.Join(";",Points.Select(static Point=>$"{Point.Position.R.Raw},{Point.Position.U.Raw},{Point.Position.F.Raw},{Point.Normal.U.Raw},{Point.Penetration.Raw}")));
                if(Tick>60){ Worst=MathF.Max(Worst,Sinking(Curb,BodyOf(World,Body))); }
                if(Tick==300){ Orientation=Orientations(Points); }
            }
            return Trace;
        }
        var First=Run(out var Worst,out var Orientation,out var MostPoints);
        var Second=Run(out _,out _,out _);
        Output.WriteLine(FormattableString.Invariant($"worst {Worst:F4} m, orientations {Orientation}, most points {MostPoints}"));
        Assert.Equal(First,Second);
        Assert.True(Worst<0.01f,$"the box sank {Worst:F4} m into the curb");
        // 1 組の接触の点の最大数(面の向き 3 つ × 4 点)。
        Assert.InRange(MostPoints,1,12);
    }
}
