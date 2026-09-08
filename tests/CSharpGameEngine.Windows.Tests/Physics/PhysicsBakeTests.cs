using CSharpGameEngine.Entities;
using CSharpGameEngine.Runtime.Physics;
using CSharpGameEngine.Runtime.Simulation;
using CSharpGameEngine.Numerics;

namespace CSharpGameEngine.Runtime.Windows.Tests;

public sealed class PhysicsBakeTests {
    [Fact]
    public void ShapeArtifact_RoundTripsCanonicalConvexAndMeshData(){
        PhysicsShapeAsset Asset=PhysicsShapeAsset.CreateStaticTriangleMesh(
            "mesh",
            [new LogicalVector3(0,0,0),new LogicalVector3(1,0,0),new LogicalVector3(0,0,1)],
            [0,1,2]);
        using MemoryStream Stream=new();

        PhysicsShapeArtifact.Write(Stream,Asset);
        Stream.Position=0;

        Assert.True(PhysicsShapeArtifact.TryRead(Stream,out PhysicsShapeAsset? Restored,out string Error),Error);
        Assert.NotNull(Restored);
        Assert.Equal(Asset.Id,Restored.Id);
        Assert.Equal(Asset.Vertices,Restored.Vertices);
        Assert.Equal(Asset.Indices,Restored.Indices);
        Assert.Equal(Asset.Fingerprint,Restored.Fingerprint);
    }

    [Fact]
    public void ShapeCooker_PublishesAtomicallyReusesExactKeyAndRejectsCorruptExistingArtifact(){
        string Root=Path.Combine(Path.GetTempPath(),$"cge-physics-cook-{Guid.NewGuid():N}");
        try {
            PhysicsShapeAsset Asset=PhysicsShapeAsset.CreateStaticTriangleMesh(
                "mesh",
                [new LogicalVector3(0,0,0),new LogicalVector3(1,0,0),new LogicalVector3(0,0,1)],
                [0,1,2]);
            PhysicsCookingInput Input=new(
                new byte[]{1,2,3},"asset-guid","provider-neutral-ruf-v1","static-mesh","1,1,1","0",
                "cooker-3","physics-shape-artifact-1","LogicalScalar-Q64.64");

            PhysicsCookResult First=PhysicsShapeCooker.Cook(Root,Input,Asset);
            Assert.True(First.Success,First.Error);
            Assert.False(First.Reused);
            byte[] Canonical=File.ReadAllBytes(First.ArtifactPath);
            DateTime Written=File.GetLastWriteTimeUtc(First.ArtifactPath);

            PhysicsCookResult Second=PhysicsShapeCooker.Cook(Root,Input,Asset);
            Assert.True(Second.Success,Second.Error);
            Assert.True(Second.Reused);
            Assert.Equal(Written,File.GetLastWriteTimeUtc(Second.ArtifactPath));

            File.WriteAllBytes(First.ArtifactPath,[0,1,2,3]);
            PhysicsCookResult Corrupt=PhysicsShapeCooker.Cook(Root,Input,Asset);
            Assert.False(Corrupt.Success);
            Assert.Equal(new byte[]{0,1,2,3},File.ReadAllBytes(First.ArtifactPath));

            PhysicsCookResult Changed=PhysicsShapeCooker.Cook(Root,Input with { Seed="1" },Asset);
            Assert.True(Changed.Success,Changed.Error);
            Assert.NotEqual(First.Key,Changed.Key);
            Assert.Equal(Canonical,File.ReadAllBytes(Changed.ArtifactPath));
        }
        finally {
            if(Directory.Exists(Root)){ Directory.Delete(Root,true); }
        }
    }

    [Fact]
    public void ShapeCooker_RejectsPathLikeIdentityAndExtraneousImmutableEntries(){
        string Root=Path.Combine(Path.GetTempPath(),$"cge-physics-cook-boundary-{Guid.NewGuid():N}");
        try {
            PhysicsShapeAsset Asset=PhysicsShapeAsset.CreateConvex(
                "shape",[new LogicalVector3(0,0,0),new LogicalVector3(1,0,0),new LogicalVector3(0,1,0),new LogicalVector3(0,0,1)]);
            PhysicsCookingInput Invalid=new(
                new byte[]{1},"../outside","provider-neutral-ruf-v1","convex","1,1,1","0",
                "cooker-3","physics-shape-artifact-1","LogicalScalar-Q64.64");
            Assert.Throws<ArgumentException>(()=>PhysicsShapeCooker.Cook(Root,Invalid,Asset));

            PhysicsCookingInput Valid=Invalid with { AssetGuid="asset-guid" };
            PhysicsCookResult Cooked=PhysicsShapeCooker.Cook(Root,Valid,Asset);
            Assert.True(Cooked.Success,Cooked.Error);
            string DirectoryPath=Path.GetDirectoryName(Cooked.ArtifactPath)!;
            File.WriteAllText(Path.Combine(DirectoryPath,"unexpected.txt"),"not part of the immutable artifact");

            Assert.False(PhysicsShapeCooker.ValidateExisting(DirectoryPath,Valid.AssetGuid,Cooked.Key,Asset.Fingerprint));
            Assert.False(PhysicsShapeCooker.Cook(Root,Valid,Asset).Success);
        }
        finally {
            if(Directory.Exists(Root)){ Directory.Delete(Root,true); }
        }
    }

    [Fact]
    public async Task ShapeCooker_ConcurrentIdenticalRequestsConvergeOnOneImmutableArtifact(){
        string Root=Path.Combine(Path.GetTempPath(),$"cge-physics-cook-race-{Guid.NewGuid():N}");
        try {
            PhysicsShapeAsset Asset=PhysicsShapeAsset.CreateStaticTriangleMesh(
                "mesh",
                [new LogicalVector3(0,0,0),new LogicalVector3(1,0,0),new LogicalVector3(0,0,1)],
                [0,1,2]);
            PhysicsCookingInput Input=new(
                new byte[]{4,5,6},"asset-guid","provider-neutral-ruf-v1","static-mesh","1,1,1","0",
                "cooker-3","physics-shape-artifact-1","LogicalScalar-Q64.64");

            PhysicsCookResult[]Results=await Task.WhenAll(Enumerable.Range(0,8)
                .Select(_=>Task.Run(()=>PhysicsShapeCooker.Cook(Root,Input,Asset))));

            Assert.All(Results,Result=>Assert.True(Result.Success,Result.Error));
            Assert.Single(Results.Select(Result=>Result.ArtifactPath).Distinct(StringComparer.OrdinalIgnoreCase));
            Assert.Equal(1,Results.Count(Result=>!Result.Reused));
            Assert.True(PhysicsShapeCooker.ValidateExisting(
                Path.GetDirectoryName(Results[0].ArtifactPath)!,Input.AssetGuid,Results[0].Key,Asset.Fingerprint));
        }
        finally {
            if(Directory.Exists(Root)){ Directory.Delete(Root,true); }
        }
    }

    [Fact]
    public void CookingKey_ChangesForEveryVersionedInput(){
        PhysicsCookingInput Baseline=new(new byte[]{1,2,3},"asset-guid","ruf-meters-v1","box","1,1,1","7","cooker-1","schema-1","q32.32");
        string First=PhysicsCookingKey.Compute(Baseline);
        string Same=PhysicsCookingKey.Compute(Baseline);
        string Changed=PhysicsCookingKey.Compute(Baseline with { Seed="8" });
        Assert.Equal(64,First.Length);
        Assert.Equal(First,Same);
        Assert.NotEqual(First,Changed);
    }

    [Fact]
    public void PhysicsCache_RoundTripsFullStateAndRejectsMismatchedIdentity(){
        SimulationCacheIdentity Identity=new("physics","LogicalScalar-Q64.64",60,"abc123",PhysicsCacheSerializer.SchemaVersion);
        PhysicsBodyState Body=new(
            new Entity(9,7),42,PhysicsMotionType.Dynamic,new LogicalVector3(1,2,3),LogicalQuaternion.Identity,
            new PhysicsVelocity { Linear=new LogicalVector3(4,5,6) },true,60);
        PhysicsEvent Event=new(7,99,PhysicsEventType.Sleep,new Entity(9,7),default,default,default);
        PhysicsCacheFrame Frame=new(7,[Body],[Event]);
        using MemoryStream Stream=new();
        PhysicsCacheSerializer.Write(Stream,Identity,[Frame]);

        Stream.Position=0;
        List<PhysicsCacheFrame> Frames=[];
        Assert.True(PhysicsCacheSerializer.TryRead(Stream,Identity,Frames,out string Error),Error);
        Assert.Single(Frames);
        Assert.Equal(Body,Assert.Single(Frames[0].Bodies));
        Assert.Equal(Event,Assert.Single(Frames[0].Events));

        Stream.Position=0;
        Assert.False(PhysicsCacheSerializer.TryRead(Stream,Identity with { DependencyHash="different" },[],out string Mismatch));
        Assert.Contains("does not match",Mismatch,StringComparison.Ordinal);
    }

    [Fact]
    public void PhysicsCache_RejectsTrailingDataTransactionallyAndNonCanonicalFrameOrder(){
        SimulationCacheIdentity Identity=new("physics","LogicalScalar-Q64.64",60,"dependency",PhysicsCacheSerializer.SchemaVersion);
        PhysicsCacheFrame First=new(2,[],[]);
        PhysicsCacheFrame Second=new(1,[],[]);
        using MemoryStream InvalidOrder=new();
        Assert.Throws<ArgumentException>(()=>PhysicsCacheSerializer.Write(InvalidOrder,Identity,[First,Second]));

        using MemoryStream Trailing=new();
        PhysicsCacheSerializer.Write(Trailing,Identity,[First]);
        Trailing.WriteByte(0x7f);
        Trailing.Position=0;
        List<PhysicsCacheFrame> Destination=[new PhysicsCacheFrame(99,[],[])];

        Assert.False(PhysicsCacheSerializer.TryRead(Trailing,Identity,Destination,out string Error));
        Assert.Contains("malformed",Error,StringComparison.OrdinalIgnoreCase);
        Assert.Single(Destination);
        Assert.Equal(99UL,Destination[0].Tick);
    }

    [Fact]
    public void PhysicsCacheStreamWriter_WritesCanonicalFramesWithoutOwningOutput(){
        SimulationCacheIdentity Identity=new(
            "physics","LogicalScalar-Q64.64",60,"stream-dependency",PhysicsCacheSerializer.SchemaVersion);
        PhysicsBodyState High=new(
            new Entity(2,1),20,PhysicsMotionType.Dynamic,new LogicalVector3(2,0,0),
            LogicalQuaternion.Identity,PhysicsVelocity.Zero,false,0);
        PhysicsBodyState Low=High with { Entity=new Entity(1,1),StableId=10,Position=new LogicalVector3(1,0,0) };
        PhysicsEvent Event=new(4,8,PhysicsEventType.Sleep,Low.Entity,default,default,default);
        using MemoryStream Stream=new();

        using(PhysicsCacheStreamWriter Writer=new(Stream,Identity,2)){
            Writer.WriteFrame(new PhysicsCacheFrame(4,[High,Low],[Event]));
            Writer.WriteFrame(new PhysicsCacheFrame(5,[Low,High],[]));
            Assert.Equal(2,Writer.FramesWritten);
            Writer.Complete();
        }

        Assert.True(Stream.CanWrite);
        Stream.Position=0;
        List<PhysicsCacheFrame>Frames=[];
        Assert.True(PhysicsCacheSerializer.TryRead(Stream,Identity,Frames,out string Error),Error);
        Assert.Equal([4UL,5UL],Frames.Select(static Frame=>Frame.Tick));
        Assert.Equal([10UL,20UL],Frames[0].Bodies.Select(static Body=>Body.StableId));
    }

    [Fact]
    public void PhysicsCacheStreamWriter_RejectsIncompleteExtraAndInvalidFrames(){
        SimulationCacheIdentity Identity=new(
            "physics","LogicalScalar-Q64.64",60,"stream-boundary",PhysicsCacheSerializer.SchemaVersion);
        using MemoryStream IncompleteStream=new();
        using(PhysicsCacheStreamWriter Incomplete=new(IncompleteStream,Identity,2)){
            Incomplete.WriteFrame(new PhysicsCacheFrame(1,[],[]));
            Assert.Throws<InvalidOperationException>(Incomplete.Complete);
        }

        using MemoryStream ExtraStream=new();
        using(PhysicsCacheStreamWriter Extra=new(ExtraStream,Identity,1)){
            Extra.WriteFrame(new PhysicsCacheFrame(2,[],[]));
            Assert.Throws<InvalidOperationException>(()=>Extra.WriteFrame(new PhysicsCacheFrame(3,[],[])));
            Extra.Complete();
        }

        using MemoryStream InvalidStream=new();
        using(PhysicsCacheStreamWriter Invalid=new(InvalidStream,Identity,2)){
            Invalid.WriteFrame(new PhysicsCacheFrame(5,[],[]));
            Assert.Throws<ArgumentException>(()=>Invalid.WriteFrame(new PhysicsCacheFrame(5,[],[])));
        }

        using MemoryStream EventStream=new();
        using PhysicsCacheStreamWriter InvalidEvent=new(EventStream,Identity,1);
        PhysicsEvent WrongTick=new(8,1,PhysicsEventType.Sleep,default,default,default,default);
        Assert.Throws<ArgumentException>(()=>InvalidEvent.WriteFrame(new PhysicsCacheFrame(7,[],[WrongTick])));
    }

    [Fact]
    public void GeneratedSceneBakeHost_BakesRealSceneTicksWithoutPresentation(){
        const string DependencyHash="generated-scene-bake-test";
        using MemoryStream Stream=new();

        Assert.True(
            CSharpGameEngine.Generated.GeneratedSceneBakeHost.TryBake(
                "UserEcsProject.Scenes.PhysicsStackBottomScene",4,DependencyHash,Stream,
                CancellationToken.None,out string BakeError),
            BakeError);
        Assert.True(Stream.CanWrite);

        Stream.Position=0;
        SimulationCacheIdentity Identity=new(
            "physics","LogicalScalar-Q64.64",60,DependencyHash,PhysicsCacheSerializer.SchemaVersion);
        List<PhysicsCacheFrame>Frames=[];
        Assert.True(PhysicsCacheSerializer.TryRead(Stream,Identity,Frames,out string ReadError),ReadError);
        Assert.Equal([0UL,1UL,2UL,3UL],Frames.Select(static Frame=>Frame.Tick));
        Assert.All(Frames,static Frame=>{
            Assert.Single(Frame.Bodies);
            Assert.All(Frame.Events,Event=>Assert.Equal(Frame.Tick,Event.Tick));
        });
        Assert.Equal(
            Frames[0].Bodies[0].StableId,
            Frames[^1].Bodies[0].StableId);
        Assert.True(Frames[^1].Bodies[0].Position.U<Frames[0].Bodies[0].Position.U);
    }

    [Fact]
    public void GeneratedSceneBakeHost_RejectsUnknownSceneAndCancellationExplicitly(){
        using MemoryStream Unknown=new();
        Assert.False(CSharpGameEngine.Generated.GeneratedSceneBakeHost.TryBake(
            "Missing.Scene",1,"dependency",Unknown,CancellationToken.None,out string UnknownError));
        Assert.Contains("not part",UnknownError,StringComparison.Ordinal);
        Assert.Equal(0,Unknown.Length);

        using CancellationTokenSource Cancellation=new();
        Cancellation.Cancel();
        using MemoryStream Cancelled=new();
        Assert.False(CSharpGameEngine.Generated.GeneratedSceneBakeHost.TryBake(
            "UserEcsProject.Scenes.PhysicsStackBottomScene",1,"dependency",Cancelled,
            Cancellation.Token,out string CancelError));
        Assert.Contains("cancelled",CancelError,StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PhysicsCachePlayer_AppliesFramesTransactionallyAndHandsOffOnTheNextTick(){
        SimulationCacheIdentity Identity=new("physics","LogicalScalar-Q64.64",60,"dependency",PhysicsCacheSerializer.SchemaVersion);
        Entity Entity=new(1);
        PhysicsBodyState First=new(
            Entity,1,PhysicsMotionType.Dynamic,new LogicalVector3(0,2,0),LogicalQuaternion.Identity,
            new PhysicsVelocity { Linear=new LogicalVector3(1,0,0) },false,0);
        PhysicsBodyState Second=First with { Position=new LogicalVector3(1,2,0) };
        using MemoryStream Stream=new();
        PhysicsCacheSerializer.Write(Stream,Identity,[
            new PhysicsCacheFrame(10,[First],[]),
            new PhysicsCacheFrame(11,[Second],[]),
        ]);
        Stream.Position=0;
        Assert.True(PhysicsCachePlayer.TryLoad(Stream,Identity,true,out PhysicsCachePlayer? Player,out string Error),Error);
        Assert.NotNull(Player);
        DeterministicPhysicsWorld World=new();
        Assert.Equal(PhysicsUpdateStatus.Success,World.Upsert(new PhysicsBodyDescription(
            Entity,1,new Collider(),new RigidBody { Mass=LogicalScalar.One,GravityFactor=LogicalScalar.Zero },
            LogicalVector3.Zero,LogicalQuaternion.Identity,PhysicsVelocity.Zero)));

        Assert.True(Player.TryApply(10,World));
        Assert.True(Player.TryApply(11,World));
        Assert.True(World.TryGetBodyState(Entity,out PhysicsBodyState Applied));
        Assert.Equal(Second.Position,Applied.Position);
        Assert.True(Player.TryHandoffToRealtime(12));
        Assert.Equal(PhysicsCachePlaybackState.HandedOffToRealtime,Player.State);

        World.Step(12);
        Assert.True(World.TryGetBodyState(Entity,out PhysicsBodyState Realtime));
        Assert.True(Realtime.Position.R>Applied.Position.R);
    }

    [Fact]
    public void PhysicsCachePlayer_MismatchFaultsWithoutPartiallyMutatingWorld(){
        SimulationCacheIdentity Identity=new("physics","LogicalScalar-Q64.64",60,"dependency",PhysicsCacheSerializer.SchemaVersion);
        PhysicsBodyState Valid=new(
            new Entity(1),1,PhysicsMotionType.Dynamic,new LogicalVector3(0,2,0),LogicalQuaternion.Identity,
            PhysicsVelocity.Zero,false,0);
        PhysicsBodyState Missing=Valid with { Entity=new Entity(2),StableId=2,Position=new LogicalVector3(0,99,0) };
        using MemoryStream Stream=new();
        PhysicsCacheSerializer.Write(Stream,Identity,[new PhysicsCacheFrame(1,[Valid,Missing],[])]);
        Stream.Position=0;
        Assert.True(PhysicsCachePlayer.TryLoad(Stream,Identity,false,out PhysicsCachePlayer? Player,out string Error),Error);
        DeterministicPhysicsWorld World=new();
        World.Upsert(new PhysicsBodyDescription(
            new Entity(1),1,new Collider(),new RigidBody { Mass=LogicalScalar.One,GravityFactor=LogicalScalar.Zero },
            new LogicalVector3(0,5,0),LogicalQuaternion.Identity,PhysicsVelocity.Zero));

        Assert.False(Player!.TryApply(1,World));
        Assert.Equal(PhysicsCachePlaybackState.Faulted,Player.State);
        Assert.True(World.TryGetBodyState(new Entity(1),out PhysicsBodyState State));
        Assert.Equal(new LogicalVector3(0,5,0),State.Position);
    }

    [Fact]
    public void Prefracture_ResolvesBondsInStableIndexOrder(){
        PrefractureAsset Asset=new(){
            SourceHash="hash",Seed=17,
            Fragments=[new(0,"a",default,1),new(1,"b",default,1),new(2,"c",default,1)],
            Bonds=[new(0,1,2),new(1,2,3)],
        };
        Assert.True(Asset.IsDeterministicallyOrdered());
        PrefractureRuntime Runtime=new();
        Runtime.Accumulate(5,1,4);
        Runtime.Accumulate(5,0,2);
        List<FractureBreak> Breaks=[];
        Assert.Equal(2,Runtime.Resolve(5,Asset,Breaks));
        Assert.Equal([0,1],Breaks.Select(Break=>Break.BondIndex));
        Breaks.Clear();
        Assert.Equal(0,Runtime.Resolve(5,Asset,Breaks));
    }

    [Fact]
    public void PrefractureCooker_PartitionsClosedConvexMeshDeterministicallyAndConservesVolume(){
        PhysicsShapeAsset Source=CreateClosedCubeMesh("fracture-source");
        PrefractureCookSettings Settings=new(
            FragmentCount:4,Seed:17,Density:LogicalScalar.One,
            BondStrengthPerArea:LogicalScalar.FromInt64(2));

        Assert.True(PrefractureCooker.TryGenerate(Source,Settings,out PrefractureAsset? First,out string FirstError),FirstError);
        Assert.True(PrefractureCooker.TryGenerate(Source,Settings,out PrefractureAsset? Second,out string SecondError),SecondError);
        Assert.NotNull(First);
        Assert.NotNull(Second);
        Assert.True(First.IsDeterministicallyOrdered());
        Assert.Equal(4,First.Fragments.Count);
        Assert.Equal(First.Fragments,Second.Fragments);
        Assert.Equal(First.Bonds,Second.Bonds);
        Assert.Equal(
            First.CollisionShapes.Select(Shape=>Shape.Fingerprint),
            Second.CollisionShapes.Select(Shape=>Shape.Fingerprint));
        Assert.All(First.Meshes,Mesh=>Assert.True(Mesh.IsClosedManifold()));
        Assert.NotEmpty(First.Bonds);
        LogicalScalar TotalMass=First.Fragments.Aggregate(
            LogicalScalar.Zero,static(Total,Fragment)=>Total+Fragment.Mass);
        Assert.True(TotalMass>=LogicalScalar.FromSingle(7.999f));
        Assert.True(TotalMass<=LogicalScalar.FromSingle(8.001f));

        using MemoryStream Stream=new();
        PrefractureArtifact.Write(Stream,First);
        Stream.Position=0;
        Assert.True(PrefractureArtifact.TryRead(Stream,out PrefractureAsset? Restored,out string Error),Error);
        Assert.NotNull(Restored);
        Assert.Equal(First.Fragments,Restored.Fragments);
        Assert.Equal(First.Bonds,Restored.Bonds);
        Assert.Equal(
            First.CollisionShapes.Select(Shape=>Shape.Fingerprint),
            Restored.CollisionShapes.Select(Shape=>Shape.Fingerprint));

        Stream.Position=Stream.Length;
        Stream.WriteByte(0x7f);
        Stream.Position=0;
        Assert.False(PrefractureArtifact.TryRead(Stream,out _,out _));
    }

    [Fact]
    public void PrefractureCooker_RejectsOpenMeshAndChangesCanonicalOutputWithSeed(){
        PhysicsShapeAsset Open=PhysicsShapeAsset.CreateStaticTriangleMesh(
            "open",
            [new LogicalVector3(0,0,0),new LogicalVector3(1,0,0),new LogicalVector3(0,1,0),new LogicalVector3(0,0,1)],
            [0,1,2,0,3,1,0,2,3]);
        PrefractureCookSettings Settings=new(3,1,LogicalScalar.One,LogicalScalar.One);
        Assert.False(PrefractureCooker.TryGenerate(Open,Settings,out _,out string Error));
        Assert.Contains("closed",Error,StringComparison.OrdinalIgnoreCase);
        Assert.False(PrefractureCooker.TryGenerate(
            CreateClosedCubeMesh("invalid-settings"),Settings with { FragmentCount=1 },out _,out _));

        PhysicsShapeAsset Source=CreateClosedCubeMesh("seeded-source");
        Assert.True(PrefractureCooker.TryGenerate(Source,Settings,out PrefractureAsset? First,out string FirstError),FirstError);
        Assert.True(PrefractureCooker.TryGenerate(
            Source,Settings with { Seed=2 },out PrefractureAsset? Second,out string SecondError),SecondError);
        Assert.NotEqual(
            First!.CollisionShapes.Select(Shape=>Shape.Fingerprint),
            Second!.CollisionShapes.Select(Shape=>Shape.Fingerprint));
    }

    [Fact]
    public void PrefractureArtifactCooker_PublishesAtomicallyAndRejectsCorruptImmutableEntry(){
        string Root=Path.Combine(Path.GetTempPath(),$"cge-prefracture-cook-{Guid.NewGuid():N}");
        try {
            PhysicsShapeAsset Source=CreateClosedCubeMesh("published-source");
            PrefractureCookSettings Settings=new(3,23,LogicalScalar.One,LogicalScalar.One);
            Assert.True(PrefractureCooker.TryGenerate(Source,Settings,out PrefractureAsset? Asset,out string GenerateError),GenerateError);
            PhysicsCookingInput Input=new(
                new byte[]{1,2,3},"fracture-guid","provider-neutral-ruf-v1","prefracture:3","1,1,1","23",
                "cooker-3","prefracture-artifact-1","LogicalScalar-Q64.64");

            PhysicsCookResult First=PrefractureArtifactCooker.Cook(Root,Input,Asset!);
            Assert.True(First.Success,First.Error);
            Assert.False(First.Reused);
            PhysicsCookResult Second=PrefractureArtifactCooker.Cook(Root,Input,Asset!);
            Assert.True(Second.Success,Second.Error);
            Assert.True(Second.Reused);

            File.WriteAllBytes(First.ArtifactPath,[0,1,2,3]);
            PhysicsCookResult Corrupt=PrefractureArtifactCooker.Cook(Root,Input,Asset!);
            Assert.False(Corrupt.Success);
            Assert.Equal(new byte[]{0,1,2,3},File.ReadAllBytes(First.ArtifactPath));
        }
        finally {
            if(Directory.Exists(Root)){ Directory.Delete(Root,true); }
        }
    }

    [Theory]
    [InlineData(2,0,1,1)]
    [InlineData(3,23,1,1)]
    [InlineData(4,17,1,1)]
    [InlineData(8,7919,1,1)]
    [InlineData(16,-104729,1,1)]
    [InlineData(4,123,1,16)]
    [InlineData(4,-17,1024,1)]
    public void PrefractureCooker_PreservesClosedVolumeAcrossSeedsCountsAndScales(
        int FragmentCount,int Seed,long ScaleNumerator,long ScaleDenominator){
        LogicalScalar HalfExtent=LogicalScalar.FromInt64(ScaleNumerator)
            /LogicalScalar.FromInt64(ScaleDenominator);
        PhysicsShapeAsset Source=CreateClosedCubeMesh(
            $"property-{FragmentCount}-{Seed}-{ScaleNumerator}-{ScaleDenominator}",HalfExtent);
        PrefractureCookSettings Settings=new(
            FragmentCount,Seed,LogicalScalar.One,LogicalScalar.One);

        Assert.True(
            PrefractureCooker.TryGenerate(Source,Settings,out PrefractureAsset?Asset,out string Error),
            Error);
        Assert.NotNull(Asset);
        Assert.Equal(FragmentCount,Asset.Fragments.Count);
        Assert.All(Asset.Meshes,Mesh=>Assert.True(Mesh.IsClosedManifold()));
        Assert.True(Asset.Bonds.Count>=FragmentCount-1);
        LogicalScalar TotalMass=Asset.Fragments.Aggregate(
            LogicalScalar.Zero,static(Total,Fragment)=>Total+Fragment.Mass);
        LogicalScalar Side=HalfExtent*LogicalScalar.FromInt64(2);
        LogicalScalar ExpectedMass=Side*Side*Side;
        LogicalScalar Tolerance=ExpectedMass/LogicalScalar.FromInt64(1000);
        Assert.True(TotalMass>=ExpectedMass-Tolerance);
        Assert.True(TotalMass<=ExpectedMass+Tolerance);
    }

    [Fact]
    public void FractureMesh_RejectsDisconnectedShellsInconsistentWindingAndUnusedVertices(){
        LogicalVector3[]FirstTetrahedron=[
            new(0,0,0),new(1,0,0),new(0,1,0),new(0,0,1),
        ];
        int[]ClosedIndices=[0,2,1,0,1,3,1,2,3,0,3,2];
        FractureMesh Closed=new(FirstTetrahedron,ClosedIndices);
        Assert.True(Closed.IsClosedManifold());

        LogicalVector3[]DisconnectedVertices=[
            ..FirstTetrahedron,
            new(10,0,0),new(11,0,0),new(10,1,0),new(10,0,1),
        ];
        int[]DisconnectedIndices=[
            ..ClosedIndices,
            4,6,5,4,5,7,5,6,7,4,7,6,
        ];
        Assert.False(new FractureMesh(DisconnectedVertices,DisconnectedIndices).IsClosedManifold());

        int[]WrongWinding=[..ClosedIndices];
        (WrongWinding[0],WrongWinding[1])=(WrongWinding[1],WrongWinding[0]);
        Assert.False(new FractureMesh(FirstTetrahedron,WrongWinding).IsClosedManifold());

        Assert.False(new FractureMesh(
            [..FirstTetrahedron,new LogicalVector3(2,2,2)],ClosedIndices).IsClosedManifold());
    }

    private static PhysicsShapeAsset CreateClosedCubeMesh(string Id){
        return CreateClosedCubeMesh(Id,LogicalScalar.One);
    }

    private static PhysicsShapeAsset CreateClosedCubeMesh(string Id,LogicalScalar HalfExtent){
        LogicalScalar Minimum=-HalfExtent;
        LogicalScalar Maximum=HalfExtent;
        LogicalVector3[]Vertices=[
            new(Minimum,Minimum,Minimum),new(Maximum,Minimum,Minimum),
            new(Maximum,Maximum,Minimum),new(Minimum,Maximum,Minimum),
            new(Minimum,Minimum,Maximum),new(Maximum,Minimum,Maximum),
            new(Maximum,Maximum,Maximum),new(Minimum,Maximum,Maximum),
        ];
        int[]Indices=[
            0,3,2,0,2,1,
            4,5,6,4,6,7,
            0,4,7,0,7,3,
            1,2,6,1,6,5,
            0,1,5,0,5,4,
            3,7,6,3,6,2,
        ];
        return PhysicsShapeAsset.CreateStaticTriangleMesh(Id,Vertices,Indices);
    }
}
