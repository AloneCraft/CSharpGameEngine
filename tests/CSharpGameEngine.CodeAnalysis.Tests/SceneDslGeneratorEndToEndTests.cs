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
        using Sample;
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
            public static bool LooksUpComponentsWithoutReflection(){
                World World=new();
                SceneInstance Instance=World.InstantiateScene<MainScene>();
                Entity Root=World.GetSceneRoot(Instance);

                if(!World.TryGetComponent(Root,out Transform Transform)
                    ||Transform.X!=1f){
                    return false;
                }

                Entity Child=new(Root.Id+1);
                return World.TryGetComponent(Child,out Parent Parent)
                    &&Parent.Value==Root
                    &&!World.TryGetComponent(Child,out Transform _);
            }
            public static bool CellOwnershipUsesPersistentIdsAndUnloadsAsOneUnit(){
                World World=new();
                var Cell=CSharpGameEngine.Contracts.WorldCellId.Parse("9ea8cda6-330c-53c9-909d-e27aedcf7ec2");
                MainScene.InstantiateCell(ref World,Cell);
                if(!World.HasOwnedCell(Cell)){ return false; }
                var Count=0;
                for(var Index=0;Index<World.EntityArchetypeIndices.Count;Index++){
                    var Entity=new Entity(Index);
                    if(!World.IsAlive(Entity)){ continue; }
                    if(World.GetOwningCell(Entity)!=Cell||!World.GetPersistentEntityId(Entity).IsValid){ return false; }
                    Count++;
                }
                if(Count!=3||!World.UnloadCell(Cell)||World.HasOwnedCell(Cell)){ return false; }
                return World.GetStatistics().AliveEntityCount==0;
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
        var Assembly=CompileScene("""
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
        var Assembly=CompileScene("""
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
        var Assembly=CompileScene("""
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
    public void InstantiateScene_ProvidesTypedComponentLookupForSceneEntities(){
        var Assembly=CompileScene("""
                                  using CSharpGameEngine.Entities;
                                  using Sample;
                                  namespace Game;
                                  public partial class MainScene:IScene {
                                      public Node Root=>new() {
                                          Components=[new Transform { X=1f }],
                                          Children=[new Node { Components=[new Light { Intensity=7f }] }],
                                      };
                                  }
                                  """);

        Assert.True((bool)Invoke(Assembly,"LooksUpComponentsWithoutReflection")!);
    }

    [Fact]
    public void InstantiateCell_AssignsDeterministicPersistentOwnershipAndUnloadsAtomically(){
        var Assembly=CompileScene("""
                                  using CSharpGameEngine.Entities;
                                  using Sample;
                                  namespace Game;
                                  public partial class MainScene:IScene {
                                      public Node Root=>new() {
                                          Components=[new Transform { X=1f }],
                                          Children=[
                                              new Node { Components=[new Light()] },
                                              new Node { Components=[new Transform { X=2f }] },
                                          ],
                                      };
                                  }
                                  """);

        Assert.True((bool)Invoke(Assembly,"CellOwnershipUsesPersistentIdsAndUnloadsAsOneUnit")!);
    }

    /// <summary>
    /// 実バグの回帰テスト(ADR-0040 Phase 6): <c>[StartupScene]</c>は複数のシーンが共存できる
    /// (<c>GameSystemGenerator</c>が対象シーンをループしてそれぞれ合成Systemを1つずつ作る)。
    /// 各合成Systemが唯一共有される<c>Bootstrap</c>Entityの<c>Bootstrap.Done</c>フィールドを直接
    /// 読み書きしていた頃は、同一フレーム内で先に実行されたSystemが<c>Done=true</c>にした時点で
    /// 残り全員が「もう実行済み」と誤認し、自分の<c>InstantiateScene</c>を一度も呼ばずに終わっていた
    /// (宣言順が2番目以降の<c>StartupScene</c>が実機で常に無視されるという形で発覚)。生成された
    /// 実際の<c>RunFrame</c>連鎖を通して、2番目のSceneも実際にインスタンス化されることを固定する。
    /// </summary>
    [Fact]
    public void MultipleStartupScenes_BothInstantiateIndependently_ThroughTheGeneratedRunFrameChain(){
        var Assembly=GameSystemGeneratorTestHelper.CompileAndLoad(
                                                                  [.. GameSystemGeneratorTestHelper.SceneDslSources,Components,"""
                                                                      using CSharpGameEngine.Entities;
                                                                      using CSharpGameEngine.Generated;
                                                                      using Sample;

                                                                      namespace Game;

                                                                      [StartupScene(Order = 0)]
                                                                      public partial class SceneA : IScene {
                                                                          public Node Root => new() { Components = [new Light { Intensity = 1f }] };
                                                                      }

                                                                      [StartupScene(Order = 1)]
                                                                      public partial class SceneB : IScene {
                                                                          public Node Root => new() { Components = [new Light { Intensity = 2f }] };
                                                                      }

                                                                      internal static class TestEntry
                                                                      {
                                                                          internal static string RunScenario()
                                                                          {
                                                                              World World = new();
                                                                              World.Create(new Bootstrap());
                                                                              State State = new(World) { Enabled = true };

                                                                               var SceneAStartup = new SceneA__StartupSystem();
                                                                               var SceneBStartup = new SceneB__StartupSystem();
                                                                               var KeepAlive = new KeepGeneratorAlive();
                                                                               var Scheduler = new global::CSharpGameEngine.Runtime.Simulation.FixedTickScheduler();
                                                                               GeneratedGameLoop.RunFrame(ref State, ref KeepAlive, ref SceneAStartup, ref SceneBStartup, ref Scheduler, 1_000, 60_000);

                                                                              var Count = 0;
                                                                              for (var Index = 0; Index < World.EntityArchetypeIndices.Count; Index++)
                                                                              {
                                                                                  if (World.IsAlive(new Entity(Index))) Count++;
                                                                              }

                                                                              // Bootstrapエンティティ自身(1) + SceneA/SceneBそれぞれ1エンティティずつ(2) = 3。
                                                                              // 2番目のStartupSceneが無視されるバグがあれば2のままになる。
                                                                              if (Count != 3) return "FAIL:count=" + Count;

                                                                              return "OK";
                                                                          }
                                                                      }
                                                                      """]);

        var Result=GameSystemGeneratorTestHelper.Invoke(Assembly,"Game.TestEntry","RunScenario");
        Assert.Equal("OK",Result);
    }

    [Fact]
    public void ComponentlessSceneStillInstantiates(){
        var Assembly=CompileScene("""
                                  using CSharpGameEngine.Entities;
                                  namespace Game;
                                  public partial class MainScene:IScene {
                                      public Node Root=>new() { Name="Empty",Children=[new Node()] };
                                  }
                                  """);

        Assert.Equal(2,Invoke(Assembly,"InstantiateAndCountEntities"));
    }
}
