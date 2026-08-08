// ============================================================================
// SceneAuthoring.SingleFileSample.cs
// ============================================================================
//
// このファイルは、次の2領域を1ファイルにまとめた仕様見本です。
//
// 1. USER-WRITTEN REGION
//    ユーザーおよびHierarchy Docking Windowが編集する正本
//
// 2. SOURCE-GENERATED REGION
//    Source Generatorが生成するRuntime用コード
//
// ----------------------------------------------------------------------------
// コードとIDEヒエラルキーを同期する仕様
// ----------------------------------------------------------------------------
//
// ・[SceneDefinition]が付いたstatic partial classを1つのScene定義として扱う。
//
// ・static partial SceneNodeSymbolプロパティを、Scene Nodeの宣言として扱う。
//
//      private static partial SceneNodeSymbol MainCamera { get; }
//
//   このプロパティ名が、コード上のノード識別子になる。
//
// ・Create呼び出しによって親子関係を定義する。
//
//      Root.Create(
//          MainCamera,
//          MainCamera => { ... });
//
//   第1引数のノードシンボル名と、ラムダ引数名は必ず同じにする。
//
//      MainCamera, MainCamera => ...
//
//   この規則により、SGとIDEは単純かつ確実にノードを対応付けられる。
//
// ・ラムダ本体内のSet呼び出しを、そのノードが持つComponentとして扱う。
//
//      MainCamera.Set(new Camera { ... });
//
// ・Hierarchy Docking WindowはRoslynを使用して、ユーザー領域を解析する。
//
//      partial property  -> ノード一覧
//      Create呼び出し    -> 親子関係
//      Set呼び出し       -> Component一覧
//
// ・HierarchyでAddした場合、IDEは次の2か所をコードへ追加する。
//
//      1. partial property宣言
//      2. 親ノード内のCreate呼び出し
//
// ・HierarchyでDeleteした場合、IDEは対応するCreateブロックを削除する。
//   他から参照されていなければpartial property宣言も削除する。
//
// ・HierarchyでReparentした場合、IDEはCreate呼び出し全体を、
//   移動先の親ノードのラムダ本体へ移動する。
//
// ・HierarchyでRenameした場合、Roslyn Renameを使って次を一括変更する。
//
//      partial property名
//      Create第1引数
//      ラムダ引数
//      ラムダ内部の参照
//      他コードからの型付き参照
//
// ・ユーザーがコードを直接編集した場合、IDEはDocument変更を検知し、
//   Hierarchy Treeを再解析して更新する。
//
// ・IDEはSG生成領域を直接編集しない。
//   生成領域はユーザーコードから常に再生成される。
//
// ・静的なHierarchyとして表示するDefine内では、原則として
//   条件分岐、ループ、動的なノード名、任意の外部メソッド呼び出しを禁止する。
//   動的スポーンは通常のRuntime Systemへ分離する。
//
// ・このサンプルではSceneNodeIdを、完全修飾されたproperty symbolから
//   SGが決定論的に生成している。
//   Rename後も同じIDを維持する必要がある場合は、将来的にIDEが管理する
//   hidden annotationまたは.scene.metaを使用する。
//   ユーザー自身には生のGUIDやIDを書かせない。
//
// ・Requires C# 13 or later for partial properties.
//
// ============================================================================

using System;
using System.Numerics;

namespace MyGame.SceneSample;

// ============================================================================
// Minimal authoring/runtime contracts
// 実際にはEngine側の別Assemblyに置く。
// ============================================================================

[AttributeUsage(AttributeTargets.Class)]
public sealed class SceneDefinitionAttribute : Attribute
{
}

/// <summary>
/// ユーザーコード上でScene Nodeを識別するシンボル。
/// 実体のSceneObjectやEntityではない。
/// </summary>
public readonly record struct SceneNodeSymbol(
    SceneNodeId Id,
    string Name);

/// <summary>
/// RuntimeおよびEditor内部で使用するノードID。
/// ユーザーには直接記述させない。
/// </summary>
public readonly record struct SceneNodeId(ulong Value);

/// <summary>
/// Scene定義の最上位Builder。
/// Defineメソッドをコンパイル可能にするAuthoring API。
/// RuntimeではSG生成済みコードを直接実行するため、
/// 必ずしもDefineを実行する必要はない。
/// </summary>
public sealed class SceneBuilder
{
    public void Create(
        SceneNodeSymbol symbol,
        Action<SceneNodeBuilder> definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        // Editor Previewなどでインタープリター実行する場合に使用できる。
        // 通常のRuntime BuildではSGが直接生成したコードを実行する。
        SceneNodeBuilder builder =
            new(symbol);

        definition(builder);
    }
}

/// <summary>
/// 現在定義中の1ノードを操作するBuilder。
/// </summary>
public sealed class SceneNodeBuilder
{
    internal SceneNodeBuilder(
        SceneNodeSymbol symbol)
    {
        Symbol = symbol;
    }

    public SceneNodeSymbol Symbol { get; }

    public void Create(
        SceneNodeSymbol symbol,
        Action<SceneNodeBuilder> definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        SceneNodeBuilder child =
            new(symbol);

        definition(child);
    }

    public void Set<T>(
        T component)
        where T : struct
    {
        // Authoring時の記述用API。
        // SGは呼び出し構文を解析してRuntimeコードへ展開する。
    }
}

// ============================================================================
// Runtime placeholder types
// 実際にはEngine Runtimeに存在する。
// ============================================================================

public readonly record struct EntityId(
    int Index,
    int Generation);

public readonly record struct SceneHandle(
    EntityId Root);

public sealed class SceneWorld
{
    public EntityId CreateSceneObject(
        SceneNodeId nodeId,
        string name)
    {
        Console.WriteLine(
            $"Create: {name}, NodeId={nodeId.Value:X16}");

        return new EntityId(
            Index: _nextIndex++,
            Generation: 1);
    }

    public void SetParent(
        EntityId child,
        EntityId parent)
    {
        Console.WriteLine(
            $"SetParent: {child.Index} -> {parent.Index}");
    }

    public void Set<T>(
        EntityId entity,
        in T component)
        where T : struct
    {
        Console.WriteLine(
            $"Set<{typeof(T).Name}>: Entity={entity.Index}");
    }

    private int _nextIndex;
}

// ============================================================================
// Components
// ============================================================================

public struct LocalTransform
{
    public Vector3 Position;
    public Quaternion Rotation;
    public Vector3 Scale;
}

public struct Camera
{
    public float FieldOfView;
}

public readonly struct PlayerTag
{
}

public struct Health
{
    public float Value;
}

// ============================================================================
// USER-WRITTEN REGION
//
// この領域がSceneの正本。
// ユーザーとHierarchy Docking Windowが共同で編集する。
// ============================================================================

[SceneDefinition]
public static partial class MainScene
{
    // ------------------------------------------------------------------------
    // Scene Node declarations
    //
    // プロパティ本体とSceneNodeIdはSGが生成する。
    // ユーザーは生のGUIDや数値IDを書かない。
    // ------------------------------------------------------------------------

    private static partial SceneNodeSymbol Root { get; }

    private static partial SceneNodeSymbol MainCamera { get; }

    private static partial SceneNodeSymbol Player { get; }

    // ------------------------------------------------------------------------
    // Scene hierarchy definition
    //
    // partial property名とラムダ引数名を同一にする。
    //
    // Root, Root => ...
    // MainCamera, MainCamera => ...
    // Player, Player => ...
    //
    // IDEとAnalyzerはこの一致を必須ルールとして検証する。
    // ------------------------------------------------------------------------

    public static void Define(
        SceneBuilder Scene)
    {
        Scene.Create(
            Root,
            Root =>
            {
                Root.Create(
                    MainCamera,
                    MainCamera =>
                    {
                        MainCamera.Set(
                            new LocalTransform
                            {
                                Position =
                                    new Vector3(
                                        0f,
                                        2f,
                                        -10f),

                                Rotation =
                                    Quaternion.Identity,

                                Scale =
                                    Vector3.One
                            });

                        MainCamera.Set(
                            new Camera
                            {
                                FieldOfView = 60f
                            });
                    });

                Root.Create(
                    Player,
                    Player =>
                    {
                        Player.Set(
                            new LocalTransform
                            {
                                Position =
                                    Vector3.Zero,

                                Rotation =
                                    Quaternion.Identity,

                                Scale =
                                    Vector3.One
                            });

                        Player.Set(
                            new PlayerTag());

                        Player.Set(
                            new Health
                            {
                                Value = 100f
                            });
                    });
            });
    }
}

// ============================================================================
// SOURCE-GENERATED REGION
//
// 実際にはMainScene.Scene.g.csなどへSGが出力する。
// この見本では理解しやすいよう、同じファイルに記載する。
//
// IDEはこの領域を編集しない。
// ============================================================================

public static partial class MainScene
{
    // ------------------------------------------------------------------------
    // Generated SceneNodeSymbol implementations
    //
    // SceneNodeIdはSGが完全修飾シンボル名などから生成する。
    //
    // 例:
//  MyGame.SceneSample.MainScene.Root
//  MyGame.SceneSample.MainScene.MainCamera
//  MyGame.SceneSample.MainScene.Player
    //
    // このIDはユーザーに書かせない。
    // ------------------------------------------------------------------------

    private static partial SceneNodeSymbol Root
    {
        get =>
            new(
                new SceneNodeId(
                    0x5BD2_1D39_12A4_0001UL),

                nameof(Root));
    }

    private static partial SceneNodeSymbol MainCamera
    {
        get =>
            new(
                new SceneNodeId(
                    0x5BD2_1D39_12A4_0002UL),

                nameof(MainCamera));
    }

    private static partial SceneNodeSymbol Player
    {
        get =>
            new(
                new SceneNodeId(
                    0x5BD2_1D39_12A4_0003UL),

                nameof(Player));
    }

    // ------------------------------------------------------------------------
    // Generated Runtime scene construction
    //
    // Defineの構文木をSGが解析し、SceneWorldへの直接呼び出しへ展開する。
    //
    // Runtimeでは次を行わない。
    //
    // ・Reflection
    // ・ラムダ式の解析
    // ・SceneBuilderの動的解釈
    // ・文字列によるComponent検索
    // ・Hierarchy Treeの再構築
    //
    // コンパイル時に確定した直接コードだけを実行する。
    // ------------------------------------------------------------------------

    public static SceneHandle CreateRuntime(
        SceneWorld World)
    {
        ArgumentNullException.ThrowIfNull(World);

        // --------------------------------------------------------------------
        // Root
        // --------------------------------------------------------------------

        EntityId Root =
            World.CreateSceneObject(
                MainScene.Root.Id,
                MainScene.Root.Name);

        // --------------------------------------------------------------------
        // MainCamera
        // Parent: Root
        // --------------------------------------------------------------------

        EntityId MainCamera =
            World.CreateSceneObject(
                MainScene.MainCamera.Id,
                MainScene.MainCamera.Name);

        World.SetParent(
            MainCamera,
            Root);

        LocalTransform MainCamera_LocalTransform =
            new()
            {
                Position =
                    new Vector3(
                        0f,
                        2f,
                        -10f),

                Rotation =
                    Quaternion.Identity,

                Scale =
                    Vector3.One
            };

        World.Set(
            MainCamera,
            in MainCamera_LocalTransform);

        Camera MainCamera_Camera =
            new()
            {
                FieldOfView = 60f
            };

        World.Set(
            MainCamera,
            in MainCamera_Camera);

        // --------------------------------------------------------------------
        // Player
        // Parent: Root
        // --------------------------------------------------------------------

        EntityId Player =
            World.CreateSceneObject(
                MainScene.Player.Id,
                MainScene.Player.Name);

        World.SetParent(
            Player,
            Root);

        LocalTransform Player_LocalTransform =
            new()
            {
                Position =
                    Vector3.Zero,

                Rotation =
                    Quaternion.Identity,

                Scale =
                    Vector3.One
            };

        World.Set(
            Player,
            in Player_LocalTransform);

        PlayerTag Player_PlayerTag =
            new();

        World.Set(
            Player,
            in Player_PlayerTag);

        Health Player_Health =
            new()
            {
                Value = 100f
            };

        World.Set(
            Player,
            in Player_Health);

        return new SceneHandle(
            Root);
    }
}

// ============================================================================
// Generated analyzer rules represented as documentation
// 実際にはDiagnosticAnalyzerとして実装する。
// ============================================================================
//
// SCENE001
// [SceneDefinition]型はstatic partial classでなければならない。
//
// SCENE002
// SceneNodeSymbolの宣言はstatic partial get-only propertyでなければならない。
//
// SCENE003
// Create第1引数のノード名とラムダ引数名が一致していない。
//
// 例:
//
//     Root.Create(
//         MainCamera,
//         Camera => { });
//
// 正:
//
//     Root.Create(
//         MainCamera,
//         MainCamera => { });
//
// SCENE004
// 宣言されたSceneNodeSymbolがDefine内で作成されていない。
//
// SCENE005
// 同じSceneNodeSymbolが複数回作成されている。
//
// SCENE006
// Root以外のノードに親が存在しない。
//
// SCENE007
// 循環した親子関係が検出された。
//
// SCENE008
// Define内に静的解析できないループまたは条件分岐がある。
//
// SCENE009
// Setへ渡されたComponent初期化式をコンパイル時に展開できない。
//
// SCENE010
// Hierarchyから削除しようとしたノードが他コードから参照されている。
//
// ============================================================================
// IDE Hierarchy synchronization example
// ============================================================================
//
// このユーザーコード:
//
//     Root.Create(
//         MainCamera,
//         MainCamera =>
//         {
//             MainCamera.Set(new Camera());
//         });
//
// はIDEで次のように表示される:
//
//     MainScene
//     └─ Root
//        ├─ MainCamera
//        │  ├─ LocalTransform
//        │  └─ Camera
//        └─ Player
//           ├─ LocalTransform
//           ├─ PlayerTag
//           └─ Health
//
// HierarchyでDirectionalLightをRootへ追加した場合、IDEは概念的に
// 次のコード編集を行う:
//
// 1. propertyを追加:
//
//     private static partial SceneNodeSymbol DirectionalLight { get; }
//
// 2. Rootのラムダ内部へCreateを追加:
//
//     Root.Create(
//         DirectionalLight,
//         DirectionalLight =>
//         {
//             DirectionalLight.Set(
//                 new DirectionalLightComponent());
//         });
//
// SGの次回実行時に、SceneNodeSymbol実装とCreateRuntimeコードが生成される。
//
// ============================================================================
// Example runtime entry point
// ============================================================================

public static class Program
{
    public static void _Main()
    {
        SceneWorld World =
            new();

        SceneHandle Scene =
            MainScene.CreateRuntime(
                World);

        Console.WriteLine(
            $"Scene root entity: {Scene.Root.Index}");
    }
}
