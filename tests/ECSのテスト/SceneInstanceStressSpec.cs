namespace CSharpGameEngine.Entities.Tests;

/// <summary>
/// 世代付きEntityとfree-listが長時間streamingでもslot数をactive-set上限へ保つスペック。
///
/// <para>
/// Sceneのload/unload回数ではなく同時所有Entity数だけに位置表を比例させ、再利用前のstale Generationが
/// 新しいEntityへ化けないことを固定する。
/// </para>
/// </summary>
public sealed class SceneInstanceStressSpec {
    private const int EntitiesPerScene=5;

    private static SceneInstance Instantiate(ref World World){
        var Instance=World.BeginSceneInstance();
        var Root=World.AttachToScene(Instance,World.Create(new Position()));

        for(var Index=1;Index<EntitiesPerScene;Index++){
            World.AttachToScene(Instance,World.Create(new Position { X=Index },new Parent { Value=Root }));
        }

        World.EndSceneInstance(Instance,Root);
        return Instance;
    }

    [Fact]
    public void RepeatedLoadUnload_ReusesEntitySlotsAndKeepsThePositionTableBounded(){
        const int Iterations=1000;
        World World=new();

        for(var Index=0;Index<Iterations;Index++){
            var Instance=Instantiate(ref World);
            World.UnloadScene(Instance);
        }

        Assert.Equal(EntitiesPerScene,World.EntityArchetypeIndices.Count);
        Assert.Equal(0,World.GetStatistics().AliveEntityCount);

        // Archetypeのストレージは一切増えない(swap-backで再利用される)。
        Assert.Equal(0,World.Archetype0.Count);
        Assert.Equal(0,World.Archetype2.Count);
    }

    [Fact]
    public void RepeatedLoadUnload_ReusesSceneSlotsSoTheSceneTableStaysConstant(){
        // Sceneスロットは再利用するので、こちらは増えない。
        World World=new();
        var First=Instantiate(ref World);
        World.UnloadScene(First);

        for(var Index=0;Index<1000;Index++){
            var Instance=Instantiate(ref World);

            Assert.Equal(First.Slot,Instance.Slot);

            World.UnloadScene(Instance);
        }
    }

    [Fact]
    public void ScenesLoadedWithoutUnloadingKeepDistinctSlots(){
        World World=new();
        HashSet<int>Slots=[];

        for(var Index=0;Index<100;Index++){
            Slots.Add(Instantiate(ref World).Slot);
        }

        Assert.Equal(100,Slots.Count);
    }
}
