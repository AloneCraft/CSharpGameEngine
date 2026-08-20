namespace CSharpGameEngine.Entities.Tests;

/// <summary>
/// 案A(墓石のみ・ID再利用なし)の<b>代償を実測で記録する</b>ためのスペック(ADR-0040 リスクO-13)。
///
/// <para>
/// Entity Idを再利用しないため、Scene の load/unload を繰り返すと位置表が単調増加する。この増加が
/// 実用上どの程度かを数字で押さえておき、閾値を超えたと分かった時点で世代付きEntity + フリーリストへ
/// 加算的に移行する。ここで固定しているのは「増加が線形であって、それ以上悪化しない」ことである。
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
    public void RepeatedLoadUnload_GrowsThePositionTableLinearlyAndNothingElse(){
        const int Iterations=1000;
        World World=new();

        for(var Index=0;Index<Iterations;Index++){
            var Instance=Instantiate(ref World);
            World.UnloadScene(Instance);
        }

        // 位置表は単調増加する。これが案Aの唯一の代償である。
        Assert.Equal(Iterations*EntitiesPerScene,World.EntityArchetypeIndices.Count);

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
