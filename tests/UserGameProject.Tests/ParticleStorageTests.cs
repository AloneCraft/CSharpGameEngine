using System.Numerics;
using UserGameProject.Entities;

namespace UserGameProject.Tests;

/// <summary>
/// Verifies the AoS and SoA backends that GenerateEntityGenerator produces from the single
/// <see cref="Particle"/> declaration. The most important assertions are the equivalence ones:
/// swapping the physical storage must not change any observable result.
/// </summary>
/// <remarks>
/// ECS types are fully qualified on purpose. This namespace is a child of UserGameProject, whose
/// parent namespace declares unrelated Entity/World types in Scene1.cs; parent-namespace members
/// win over compilation-unit using aliases, so bare "Entity"/"World" would bind to the wrong types.
/// </remarks>
public class ParticleStorageTests
{
    private static ParticleCreateInfo InfoFor(int index)
    {
        return new ParticleCreateInfo
        {
            Transform = new LocalTransform { Position = new Vector3(index, 0f, 0f) },
            Velocity = new LinearVelocity { Value = new Vector3(1f, 2f, 3f) },
            Durability = new Durability { Value = 100 },
        };
    }

    [Fact]
    public void Aos_CreateThenReadThroughFacade_RoundTrips()
    {
        ParticleAosStore store = new();

        Particle particle = store.Create(InfoFor(7));

        Assert.Equal(7f, particle.Transform.Position.X, precision: 4);
        Assert.Equal(2f, particle.Velocity.Value.Y, precision: 4);
        Assert.Equal(100, particle.Durability.Value);
        Assert.True(particle.IsAlive);
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void Soa_CreateThenReadThroughStoreRef_RoundTrips()
    {
        global::CSharpGameEngine.Ecs.World world = new();
        ParticleSoaStore store = new(world);

        global::CSharpGameEngine.Ecs.Entity entity = store.Create(InfoFor(7));

        Assert.Equal(7f, store.GetTransformRef(entity).Position.X, precision: 4);
        Assert.Equal(2f, store.GetVelocityRef(entity).Value.Y, precision: 4);
        Assert.Equal(100, store.GetDurabilityRef(entity).Value);
        Assert.True(store.IsAlive(entity));
    }

    [Fact]
    public void Aos_UserDefinedMove_UpdatesTransformThroughRefAccessor()
    {
        ParticleAosStore store = new();
        Particle particle = store.Create(InfoFor(0));

        particle.Move(2f);

        Assert.Equal(2f, particle.Transform.Position.X, precision: 4);
        Assert.Equal(4f, particle.Transform.Position.Y, precision: 4);
    }

    [Fact]
    public void Aos_UserDefinedApplyWear_ClampsAtZero()
    {
        ParticleAosStore store = new();
        Particle particle = store.Create(InfoFor(0));

        particle.ApplyWear(30);
        Assert.Equal(70, particle.Durability.Value);
        Assert.False(particle.IsWornOut);

        particle.ApplyWear(1000);
        Assert.Equal(0, particle.Durability.Value);
        Assert.True(particle.IsWornOut);
    }

    [Fact]
    public void Aos_CopiedFacadeHandle_PointsAtTheSameEntity()
    {
        ParticleAosStore store = new();
        Particle original = store.Create(InfoFor(0));
        Particle copy = original;

        copy.DurabilityRef.Value = 42;

        Assert.Equal(original.Entity, copy.Entity);
        Assert.Equal(42, original.Durability.Value);
        Assert.Equal(original, copy);
    }

    [Fact]
    public void Aos_DestroyedHandle_IsRejected()
    {
        ParticleAosStore store = new();
        Particle particle = store.Create(InfoFor(0));

        particle.Destroy();

        Assert.False(particle.IsAlive);
        Assert.Throws<InvalidOperationException>(() => particle.Move(1f));
    }

    [Fact]
    public void Soa_DestroyedEntity_IsRejected()
    {
        global::CSharpGameEngine.Ecs.World world = new();
        ParticleSoaStore store = new(world);
        global::CSharpGameEngine.Ecs.Entity entity = store.Create(InfoFor(0));

        store.Destroy(entity);

        Assert.False(store.IsAlive(entity));
        Assert.Throws<InvalidOperationException>(() => ParticleSystems.MoveSingleSoa(store, entity, 1f));
    }

    [Fact]
    public void Aos_DestroyThenCreate_ReusesIdWithIncrementedGeneration()
    {
        ParticleAosStore store = new();
        Particle first = store.Create(InfoFor(0));
        int firstId = first.Entity.Id;
        int firstGeneration = first.Entity.Generation;

        first.Destroy();
        Particle second = store.Create(InfoFor(1));

        Assert.Equal(firstId, second.Entity.Id);
        Assert.Equal(firstGeneration + 1, second.Entity.Generation);
        Assert.False(first.IsAlive);
    }

    [Fact]
    public void Aos_DestroyMiddleEntity_KeepsSurvivorsIntactAndDense()
    {
        ParticleAosStore store = new();
        Particle first = store.Create(InfoFor(1));
        Particle middle = store.Create(InfoFor(2));
        Particle last = store.Create(InfoFor(3));

        middle.Destroy();

        Assert.Equal(2, store.Count);
        Assert.True(first.IsAlive);
        Assert.False(middle.IsAlive);
        Assert.True(last.IsAlive);
        Assert.Equal(1f, first.Transform.Position.X, precision: 4);
        Assert.Equal(3f, last.Transform.Position.X, precision: 4);
    }

    [Fact]
    public void BatchMoveAll_MatchesPerEntityMove_OnBothBackends()
    {
        const int count = 64;
        const float deltaTime = 0.5f;

        ParticleAosStore perEntityStore = new();
        Particle[] handles = new Particle[count];
        for (int i = 0; i < count; i++)
        {
            handles[i] = perEntityStore.Create(InfoFor(i));
        }

        for (int i = 0; i < count; i++)
        {
            handles[i].Move(deltaTime);
        }

        ParticleAosStore aosBatchStore = new();
        global::CSharpGameEngine.Ecs.World world = new();
        ParticleSoaStore soaBatchStore = new(world);
        for (int i = 0; i < count; i++)
        {
            aosBatchStore.Create(InfoFor(i));
            soaBatchStore.Create(InfoFor(i));
        }

        ParticleSystems.MoveAllAos(aosBatchStore, deltaTime);
        ParticleSystems.MoveAllSoa(soaBatchStore, deltaTime);

        Vector3 expected = ParticleSystems.SumPositionsAos(perEntityStore);

        Assert.Equal(expected, ParticleSystems.SumPositionsAos(aosBatchStore));
        Assert.Equal(expected, ParticleSystems.SumPositionsSoa(soaBatchStore));
    }

    [Fact]
    public void BothBackends_ProduceIdenticalResults_AfterTheSameOperationSequence()
    {
        const int count = 128;

        ParticleAosStore aosStore = new();
        global::CSharpGameEngine.Ecs.World world = new();
        ParticleSoaStore soaStore = new(world);

        for (int i = 0; i < count; i++)
        {
            aosStore.Create(InfoFor(i));
            soaStore.Create(InfoFor(i));
        }

        for (int frame = 0; frame < 10; frame++)
        {
            ParticleSystems.MoveAllAos(aosStore, 1f / 60f);
            ParticleSystems.MoveAllSoa(soaStore, 1f / 60f);
            ParticleSystems.ApplyWearAllAos(aosStore, 3);
            ParticleSystems.ApplyWearAllSoa(soaStore, 3);
        }

        // Swapping the physical storage layout must not change a single observable value.
        Assert.Equal(ParticleSystems.SumPositionsAos(aosStore), ParticleSystems.SumPositionsSoa(soaStore));
    }

    [Fact]
    public void SingleEntityAccess_MatchesBetweenFacadeAndStoreRef()
    {
        ParticleAosStore aosStore = new();
        global::CSharpGameEngine.Ecs.World world = new();
        ParticleSoaStore soaStore = new(world);

        Particle aosParticle = aosStore.Create(InfoFor(5));
        global::CSharpGameEngine.Ecs.Entity soaEntity = soaStore.Create(InfoFor(5));

        aosParticle.Move(0.25f);
        ParticleSystems.MoveSingleSoa(soaStore, soaEntity, 0.25f);

        Assert.Equal(aosParticle.Transform.Position, ParticleSystems.GetTransformSoa(soaStore, soaEntity).Position);
    }

    [Fact]
    public void EmptyStores_BatchOperationsAreNoOps()
    {
        ParticleAosStore aosStore = new();
        global::CSharpGameEngine.Ecs.World world = new();
        ParticleSoaStore soaStore = new(world);

        ParticleSystems.MoveAllAos(aosStore, 1f);
        ParticleSystems.MoveAllSoa(soaStore, 1f);

        Assert.Equal(Vector3.Zero, ParticleSystems.SumPositionsAos(aosStore));
        Assert.Equal(Vector3.Zero, ParticleSystems.SumPositionsSoa(soaStore));
    }

    [Fact]
    public void Soa_TwoStoresOnTheSameWorld_ShareOneArchetypeStorage()
    {
        global::CSharpGameEngine.Ecs.World world = new();
        ParticleSoaStore first = new(world);
        ParticleSoaStore second = new(world);

        first.Create(InfoFor(1));
        second.Create(InfoFor(2));

        // Both stores resolve to the same normalized archetype, so a chunk obtained through either
        // one covers every row - this is the archetype-sharing behaviour inherited from World.
        Assert.Equal(new Vector3(3f, 0f, 0f), ParticleSystems.SumPositionsSoa(first));
        Assert.Equal(new Vector3(3f, 0f, 0f), ParticleSystems.SumPositionsSoa(second));
    }
}
