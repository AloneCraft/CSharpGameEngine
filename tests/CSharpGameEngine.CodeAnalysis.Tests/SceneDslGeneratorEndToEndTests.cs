using System.Reflection;

namespace CSharpGameEngine.CodeAnalysis.Tests;

/// <summary>
/// Scene DSLを実際にコンパイル・実行して、<c>world.InstantiateScene&lt;TScene&gt;()</c>がEntityを作ることを
/// 確かめる(ADR-0040)。ここが通って初めて「C# Scene DSL → Roslyn → SceneModel → SG → Runtime ECS」の
/// 経路が繋がったことになる。
/// </summary>
public sealed class SceneDslGeneratorEndToEndTests {
    private const string Components="""
        namespace Sample;

        public struct Transform { public float X; public float Y; }
        public struct Light { public float Intensity; }

        internal struct KeepGeneratorAlive : CSharpGameEngine.Entities.ISystem
        {
            public bool Update(ref Transform transform, ref CSharpGameEngine.Entities.State state) => true;
        }
        """;

    /// <summary>
    /// 生成された<c>Instantiate</c>を呼び、Worldの状態を読み出すための小さなドライバ。テスト側から
    /// <c>World</c>をrefで扱う必要があるため、コンパイル対象の中に置く。
    /// </summary>
    private const string Driver="""
        using CSharpGameEngine.Entities;
        namespace Game;
        public static class Driver {
            public static int InstantiateAndCountEntities(){
                World World=new();
                SceneInstance Instance=World.InstantiateScene<MainScene>();
                var Count=0;
                for(var Index=0;Index<World.EntityArchetypeIndices.Count;Index++){
                    if(World.IsAlive(new Entity(Index))){
                        Count++;
                    }
                }
                return Count;
            }
            public static bool RoundTripsOwnershipAndUnload(){
                World World=new();
                SceneInstance A=World.InstantiateScene<MainScene>();
                SceneInstance B=World.InstantiateScene<MainScene>();
                Entity RootA=World.GetSceneRoot(A);
                Entity RootB=World.GetSceneRoot(B);
                if(RootA==RootB||World.GetOwningScene(RootA)!=A||World.GetOwningScene(RootB)!=B){
                    return false;
                }
                World.UnloadScene(A);
                return !World.IsAlive(RootA)&&World.IsAlive(RootB)&&!World.IsSceneAlive(A)&&World.IsSceneAlive(B);
            }
        }
        """;

    private static Assembly CompileScene(string SceneSource)=>
        GameSystemGeneratorTestHelper.CompileAndLoad(
            [.. GameSystemGeneratorTestHelper.SceneDslSources,Components,SceneSource,Driver]);

    private static object?Invoke(Assembly Assembly,string MethodName)=>
        GameSystemGeneratorTestHelper.Invoke(Assembly,"Game.Driver",MethodName);

    [Fact]
    public void InstantiateScene_CreatesOneEntityPerNode(){
        Assembly Assembly=CompileScene("""
            using CSharpGameEngine.Entities;
            using Sample;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() {
                    Name="Root",
                    Components=[new Transform { X=1f }],
                    Children=[
                        new Node { Name="A",Components=[new Light { Intensity=3f }] },
                        new Node { Name="B",Components=[new Transform { X=2f }] },
                    ],
                };
            }
            """);

        Assert.Equal(3,Invoke(Assembly,"InstantiateAndCountEntities"));
    }

    [Fact]
    public void InstantiateScene_TwiceProducesIndependentSceneInstances(){
        Assembly Assembly=CompileScene("""
            using CSharpGameEngine.Entities;
            using Sample;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() {
                    Components=[new Transform { X=1f }],
                    Children=[new Node { Components=[new Light()] }],
                };
            }
            """);

        Assert.True((bool)Invoke(Assembly,"RoundTripsOwnershipAndUnload")!);
    }

    [Fact]
    public void SceneOnlyShapesBecomeRealArchetypes(){
        // Sceneのノード形状はSystemのUpdate/Spawn形状と同じArchetype空間へ入る。Systemが宣言していない
        // 構成({Light, Parent})でもArchetypeが作られ、Createが解決できなければコンパイルが通らない。
        Assembly Assembly=CompileScene("""
            using CSharpGameEngine.Entities;
            using Sample;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() {
                    Components=[new Transform()],
                    Children=[new Node { Components=[new Light { Intensity=7f }] }],
                };
            }
            """);

        Assert.Equal(2,Invoke(Assembly,"InstantiateAndCountEntities"));
    }

    [Fact]
    public void ComponentlessSceneStillInstantiates(){
        Assembly Assembly=CompileScene("""
            using CSharpGameEngine.Entities;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Name="Empty",Children=[new Node()] };
            }
            """);

        Assert.Equal(2,Invoke(Assembly,"InstantiateAndCountEntities"));
    }
}
