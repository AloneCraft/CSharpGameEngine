// ============================================================================
// CodeFirstAuthoring.NonScene.SingleFileSample.cs
// ============================================================================
//
// このファイルは、Scene以外のゲームエンジン設定・コンテンツを
// 「C#コードを唯一の正本」として定義するための仕様見本である。
//
// 実際のプロジェクトでは、次の領域は別ファイルになる。
//
//   USER-WRITTEN REGION
//       ユーザー、AI、Visual Studio拡張機能が編集する。
//
//   SOURCE-GENERATED REGION
//       Source Generatorが生成する。
//       ユーザーおよびIDEの各編集ウィンドウは直接編集しない。
//
// ----------------------------------------------------------------------------
// 基本思想
// ----------------------------------------------------------------------------
//
// ・ゲームの意味的な構造、設定、関連付けはC# Authoringコードで定義する。
//
// ・各IDEウィンドウは独自の保存データを持たない。
//   同じAuthoringコードを用途別に表示する「別ビュー」である。
//
// ・IDE上の操作はRoslyn Workspaceを使い、元のC#コードへ反映する。
//
// ・ユーザーがコードを直接変更した場合は、各IDEウィンドウを再解析して更新する。
//
// ・Source GeneratorとVisual Studio拡張機能は、同じ解析ライブラリを使用する。
//
// ・RuntimeではAuthoring DSLを解析・実行しない。
//   Source Generatorが生成した直接コード、テーブル、グラフを使用する。
//
// ・ユーザーにはGUID、数値ID、ハッシュ値を直接記述させない。
//   partial propertyの実装と内部IDはSGが生成する。
//
// ・大容量バイナリはC#へ埋め込まない。
//   テクスチャ、音声、メッシュなどは外部Assetファイルとして保存し、
//   C#側では型付きAssetSymbolとパスを定義する。
//
// ----------------------------------------------------------------------------
// IDEウィンドウとの対応
// ----------------------------------------------------------------------------
//
//   AssetCatalogDefinition
//       ↔ Asset Browser
//
//   InputDefinition
//       ↔ Input Map Editor
//
//   UiDefinition
//       ↔ UI Designer / UI Hierarchy / Inspector
//
//   AnimationGraphDefinition
//       ↔ Animation State Machine Graph
//
//   AudioGraphDefinition
//       ↔ Audio Mixer / Audio Routing Graph
//
//   PhysicsDefinition
//       ↔ Physics Layer Matrix
//
//   GameFlowDefinition
//       ↔ Game State Flow Graph
//
//   BuildProfileDefinition
//       ↔ Build Profile Window
//
// ----------------------------------------------------------------------------
// コードとIDEの同期
// ----------------------------------------------------------------------------
//
// 例としてInput Map EditorでJumpを追加すると、IDEは次をRoslynで追加する。
//
//   private static partial InputActionSymbol Jump { get; }
//
//   Input.Action(
//       Jump,
//       Jump =>
//       {
//           Jump.Value(InputValueType.Button);
//           Jump.Bind<SpaceKey>();
//       });
//
// 削除すると、対応する定義呼び出しと未参照propertyを削除する。
//
// RenameではRoslyn Rename APIを使用し、次を一括変更する。
//
//   partial property
//   定義メソッドの第1引数
//   ラムダ引数
//   ラムダ本体内の参照
//   他のAuthoringコードからの参照
//
// 単純な文字列置換は使用しない。
//
// 値の編集では、Inspectorがobject initializerを構文編集する。
//
//   FieldOfView = 60f
//
// をInspectorで70へ変更した場合、コードを次へ更新する。
//
//   FieldOfView = 70f
//
// 対応できない任意式が使われている場合、IDEは勝手に書き換えず、
// 読み取り専用表示またはコード編集への誘導を行う。
//
// ----------------------------------------------------------------------------
// 制約されたC# DSL
// ----------------------------------------------------------------------------
//
// GUIから完全に往復編集するDefine内では、原則として次を禁止する。
//
//   for / foreach / while
//   if / switch
//   try / goto
//   実行時にしか分からない要素数
//   動的なシンボル名
//   定義呼び出しを隠す任意のユーザーメソッド
//
// 動的なゲーム処理は通常のRuntime Systemへ記述する。
//
// ----------------------------------------------------------------------------
// シンボル名とラムダ引数名
// ----------------------------------------------------------------------------
//
// 定義シンボルとラムダ引数は同じ識別子にする。
//
//   Input.Action(
//       Move,
//       Move => { });
//
//   Animation.State(
//       Idle,
//       Idle => { });
//
//   Audio.Create(
//       Master,
//       Master => { });
//
// この規約によってIDEとSGの解析を単純化する。
//
// ただし内部実装では文字列比較だけに依存せず、SemanticModelから
// IPropertySymbol、IParameterSymbol、呼び出し対象を検証する。
//
// ----------------------------------------------------------------------------
// SceneとPrefabについて
// ----------------------------------------------------------------------------
//
// Scene定義は別のScene Authoring DSLで扱う。
//
// Prefabという別定義形式は作らない。
// すべてのSceneは単独ロードと他Sceneへのインスタンス化が可能であり、
// PrefabはSceneの利用形態として扱う。
//
// ----------------------------------------------------------------------------
// 言語要件
// ----------------------------------------------------------------------------
//
// partial propertyを使用するため、C# 13以降を想定する。
//
// ============================================================================

using System;
using System.Collections.Generic;
using System.Numerics;

namespace MyGame.CodeFirstAuthoringSample;

// ============================================================================
// Authoring attributes
// 実際にはEngine.Authoring.Contractsなどへ配置する。
// ============================================================================

[AttributeUsage(AttributeTargets.Class)]
public sealed class AssetCatalogDefinitionAttribute : Attribute;

[AttributeUsage(AttributeTargets.Class)]
public sealed class InputDefinitionAttribute : Attribute;

[AttributeUsage(AttributeTargets.Class)]
public sealed class UiDefinitionAttribute : Attribute;

[AttributeUsage(AttributeTargets.Class)]
public sealed class AnimationGraphDefinitionAttribute : Attribute;

[AttributeUsage(AttributeTargets.Class)]
public sealed class AudioGraphDefinitionAttribute : Attribute;

[AttributeUsage(AttributeTargets.Class)]
public sealed class PhysicsDefinitionAttribute : Attribute;

[AttributeUsage(AttributeTargets.Class)]
public sealed class GameFlowDefinitionAttribute : Attribute;

[AttributeUsage(AttributeTargets.Class)]
public sealed class BuildProfileDefinitionAttribute : Attribute;

// ============================================================================
// Generated internal identifiers
//
// ユーザーはAuthoringIdの値を直接記述しない。
// partial propertyの実装時にSGが埋め込む。
// ============================================================================

public readonly record struct AuthoringId(ulong Value);

public readonly record struct AssetSymbol(
    AuthoringId Id,
    string Name);

public readonly record struct InputActionSymbol(
    AuthoringId Id,
    string Name);

public readonly record struct UiNodeSymbol(
    AuthoringId Id,
    string Name);

public readonly record struct AnimationStateSymbol(
    AuthoringId Id,
    string Name);

public readonly record struct AudioBusSymbol(
    AuthoringId Id,
    string Name);

public readonly record struct PhysicsLayerSymbol(
    AuthoringId Id,
    string Name);

public readonly record struct GameStateSymbol(
    AuthoringId Id,
    string Name);

// ============================================================================
// Authoring DSL contracts
//
// Defineメソッドをコンパイル可能にするためのAPI。
// RuntimeではSG生成済みコードを使用し、通常はこのDSLを実行しない。
// ============================================================================

// ----------------------------------------------------------------------------
// Asset authoring
// ----------------------------------------------------------------------------

public enum AssetKind
{
    Texture,
    Mesh,
    Material,
    AudioClip,
    AnimationClip,
    Font,
    Shader,
    Other
}

public sealed class AssetCatalogBuilder
{
    public void Register(
        AssetSymbol asset,
        string path,
        AssetKind kind)
    {
        // Authoring DSL用。
        // SGとIDEはこの呼び出しの構文とシンボルを解析する。
    }
}

// ----------------------------------------------------------------------------
// Input authoring
// ----------------------------------------------------------------------------

public enum InputValueType
{
    Button,
    Axis1D,
    Vector2,
    Vector3
}

public sealed class InputBuilder
{
    public void Action(
        InputActionSymbol symbol,
        Action<InputActionBuilder> definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        definition(new InputActionBuilder(symbol));
    }
}

public sealed class InputActionBuilder
{
    internal InputActionBuilder(
        InputActionSymbol symbol)
    {
        Symbol = symbol;
    }

    public InputActionSymbol Symbol { get; }

    public void Value(
        InputValueType valueType)
    {
    }

    public void Bind<TBinding>()
        where TBinding : struct
    {
    }
}

// Input binding marker types.
// SGは型シンボルからRuntime用Binding IDへ変換する。

public readonly struct Wasd2D;
public readonly struct GamepadLeftStick;
public readonly struct SpaceKey;
public readonly struct GamepadSouthButton;
public readonly struct EscapeKey;
public readonly struct GamepadStartButton;

// ----------------------------------------------------------------------------
// UI authoring
// ----------------------------------------------------------------------------

public sealed class UiBuilder
{
    public void Create(
        UiNodeSymbol symbol,
        Action<UiNodeBuilder> definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        definition(new UiNodeBuilder(symbol));
    }
}

public sealed class UiNodeBuilder
{
    internal UiNodeBuilder(
        UiNodeSymbol symbol)
    {
        Symbol = symbol;
    }

    public UiNodeSymbol Symbol { get; }

    public void Create(
        UiNodeSymbol symbol,
        Action<UiNodeBuilder> definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        definition(new UiNodeBuilder(symbol));
    }

    public void Set<T>(
        T value)
        where T : struct
    {
    }

    public void OnClick<TCommand>()
        where TCommand : struct
    {
    }
}

// ----------------------------------------------------------------------------
// Animation authoring
// ----------------------------------------------------------------------------

public readonly record struct AnimationCondition(
    AnimationConditionKind Kind,
    string Parameter,
    float FloatValue,
    bool BoolValue);

public enum AnimationConditionKind
{
    FloatGreater,
    FloatLess,
    BoolEquals,
    Trigger
}

public static class When
{
    public static AnimationCondition FloatGreater(
        string parameter,
        float value)
    {
        return new AnimationCondition(
            AnimationConditionKind.FloatGreater,
            parameter,
            value,
            default);
    }

    public static AnimationCondition FloatLess(
        string parameter,
        float value)
    {
        return new AnimationCondition(
            AnimationConditionKind.FloatLess,
            parameter,
            value,
            default);
    }

    public static AnimationCondition BoolEquals(
        string parameter,
        bool value)
    {
        return new AnimationCondition(
            AnimationConditionKind.BoolEquals,
            parameter,
            default,
            value);
    }

    public static AnimationCondition Trigger(
        string parameter)
    {
        return new AnimationCondition(
            AnimationConditionKind.Trigger,
            parameter,
            default,
            default);
    }
}

public sealed class AnimationGraphBuilder
{
    public void State(
        AnimationStateSymbol symbol,
        Action<AnimationStateBuilder> definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        definition(new AnimationStateBuilder(symbol));
    }

    public void Initial(
        AnimationStateSymbol state)
    {
    }
}

public sealed class AnimationStateBuilder
{
    internal AnimationStateBuilder(
        AnimationStateSymbol symbol)
    {
        Symbol = symbol;
    }

    public AnimationStateSymbol Symbol { get; }

    public void Clip(
        AssetSymbol animationClip)
    {
    }

    public void TransitionTo(
        AnimationStateSymbol destination,
        AnimationCondition condition)
    {
    }
}

// ----------------------------------------------------------------------------
// Audio authoring
// ----------------------------------------------------------------------------

public sealed class AudioGraphBuilder
{
    public void Create(
        AudioBusSymbol symbol,
        Action<AudioBusBuilder> definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        definition(new AudioBusBuilder(symbol));
    }
}

public sealed class AudioBusBuilder
{
    internal AudioBusBuilder(
        AudioBusSymbol symbol)
    {
        Symbol = symbol;
    }

    public AudioBusSymbol Symbol { get; }

    public void Create(
        AudioBusSymbol symbol,
        Action<AudioBusBuilder> definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        definition(new AudioBusBuilder(symbol));
    }

    public void Volume(
        float decibels)
    {
    }

    public void Muted(
        bool muted)
    {
    }
}

// ----------------------------------------------------------------------------
// Physics layer authoring
// ----------------------------------------------------------------------------

public sealed class PhysicsBuilder
{
    public void Layer(
        PhysicsLayerSymbol symbol,
        Action<PhysicsLayerBuilder> definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        definition(new PhysicsLayerBuilder(symbol));
    }
}

public sealed class PhysicsLayerBuilder
{
    internal PhysicsLayerBuilder(
        PhysicsLayerSymbol symbol)
    {
        Symbol = symbol;
    }

    public PhysicsLayerSymbol Symbol { get; }

    public void CollidesWith(
        params PhysicsLayerSymbol[] layers)
    {
    }
}

// ----------------------------------------------------------------------------
// Game flow authoring
// ----------------------------------------------------------------------------

public readonly record struct GameTransitionTrigger(
    GameTransitionTriggerKind Kind,
    string Name);

public enum GameTransitionTriggerKind
{
    Completed,
    Command,
    Event
}

public static class On
{
    public static GameTransitionTrigger Completed =>
        new(
            GameTransitionTriggerKind.Completed,
            "Completed");

    public static GameTransitionTrigger Command<TCommand>()
        where TCommand : struct
    {
        return new GameTransitionTrigger(
            GameTransitionTriggerKind.Command,
            typeof(TCommand).FullName ??
            typeof(TCommand).Name);
    }

    public static GameTransitionTrigger Event<TEvent>()
        where TEvent : struct
    {
        return new GameTransitionTrigger(
            GameTransitionTriggerKind.Event,
            typeof(TEvent).FullName ??
            typeof(TEvent).Name);
    }
}

public sealed class GameFlowBuilder
{
    public void State(
        GameStateSymbol symbol,
        Action<GameStateBuilder> definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        definition(new GameStateBuilder(symbol));
    }

    public void Initial(
        GameStateSymbol state)
    {
    }
}

public sealed class GameStateBuilder
{
    internal GameStateBuilder(
        GameStateSymbol symbol)
    {
        Symbol = symbol;
    }

    public GameStateSymbol Symbol { get; }

    public void TransitionTo(
        GameStateSymbol destination,
        GameTransitionTrigger trigger)
    {
    }
}

// ----------------------------------------------------------------------------
// Build profile authoring
// ----------------------------------------------------------------------------

public enum BuildTarget
{
    WindowsX64,
    LinuxX64,
    MacOSArm64
}

public enum BuildConfiguration
{
    Debug,
    Development,
    Release
}

public enum BuildCompression
{
    None,
    Lz4,
    Zstd
}

public sealed class BuildProfileBuilder
{
    public void Target(
        BuildTarget target)
    {
    }

    public void Configuration(
        BuildConfiguration configuration)
    {
    }

    public void Output(
        string path)
    {
    }

    public void Compression(
        BuildCompression compression)
    {
    }

    public void IncludeAllRegisteredAssets()
    {
    }
}

// ============================================================================
// Runtime types
//
// SG生成コードの出力先を示すための最小Runtime API。
// 実際には各SubsystemのRuntimeへ置き換える。
// ============================================================================

public enum InputBindingKind
{
    Wasd2D,
    GamepadLeftStick,
    SpaceKey,
    GamepadSouthButton,
    EscapeKey,
    GamepadStartButton
}

public readonly record struct RuntimeAssetEntry(
    AuthoringId Id,
    string Name,
    string Path,
    AssetKind Kind);

public readonly record struct RuntimeInputAction(
    AuthoringId Id,
    string Name,
    InputValueType ValueType,
    InputBindingKind[] Bindings);

public readonly record struct RuntimeAnimationState(
    AuthoringId Id,
    string Name,
    AuthoringId ClipId,
    bool IsInitial);

public readonly record struct RuntimeAnimationTransition(
    AuthoringId Source,
    AuthoringId Destination,
    AnimationCondition Condition);

public readonly record struct RuntimeAudioBus(
    AuthoringId Id,
    string Name,
    AuthoringId? Parent,
    float VolumeDecibels,
    bool Muted);

public readonly record struct RuntimePhysicsLayer(
    AuthoringId Id,
    string Name,
    int Index,
    ulong CollisionMask);

public readonly record struct RuntimeGameState(
    AuthoringId Id,
    string Name,
    bool IsInitial);

public readonly record struct RuntimeGameTransition(
    AuthoringId Source,
    AuthoringId Destination,
    GameTransitionTrigger Trigger);

public readonly record struct RuntimeBuildProfile(
    BuildTarget Target,
    BuildConfiguration Configuration,
    string OutputPath,
    BuildCompression Compression,
    bool IncludeAllAssets);

public sealed class RuntimeDatabase
{
    public List<RuntimeAssetEntry> Assets { get; } =
        new();

    public List<RuntimeInputAction> InputActions { get; } =
        new();

    public List<RuntimeAnimationState> AnimationStates { get; } =
        new();

    public List<RuntimeAnimationTransition> AnimationTransitions { get; } =
        new();

    public List<RuntimeAudioBus> AudioBuses { get; } =
        new();

    public List<RuntimePhysicsLayer> PhysicsLayers { get; } =
        new();

    public List<RuntimeGameState> GameStates { get; } =
        new();

    public List<RuntimeGameTransition> GameTransitions { get; } =
        new();

    public RuntimeBuildProfile? BuildProfile { get; set; }
}

// UIはSGが直接生成する例を示す。

public readonly record struct UiEntity(int Value);

public sealed class UiWorld
{
    private int _nextEntity;

    public UiEntity CreateNode(
        AuthoringId id,
        string name)
    {
        UiEntity entity =
            new(_nextEntity++);

        Console.WriteLine(
            $"UI Create: {name}, " +
            $"Id={id.Value:X16}, " +
            $"Entity={entity.Value}");

        return entity;
    }

    public void SetParent(
        UiEntity child,
        UiEntity parent)
    {
        Console.WriteLine(
            $"UI Parent: {child.Value} -> {parent.Value}");
    }

    public void Set<T>(
        UiEntity entity,
        in T value)
        where T : struct
    {
        Console.WriteLine(
            $"UI Set<{typeof(T).Name}>: {entity.Value}");
    }

    public void BindClick<TCommand>(
        UiEntity entity)
        where TCommand : struct
    {
        Console.WriteLine(
            $"UI Click<{typeof(TCommand).Name}>: {entity.Value}");
    }
}

// ============================================================================
// Example UI values and commands
// ============================================================================

public enum UiDirection
{
    Row,
    Column
}

public struct UiLayout
{
    public UiDirection Direction;
    public float Gap;
    public Vector2 Padding;
}

public struct UiText
{
    public string Value;
    public float FontSize;
}

public struct UiButton
{
    public bool Interactable;
    public Vector2 MinimumSize;
}

public readonly struct StartGameCommand;
public readonly struct ExitGameCommand;
public readonly struct PauseGameCommand;
public readonly struct ResumeGameCommand;

// ============================================================================
// USER-WRITTEN REGION
//
// この領域だけをユーザー、AI、IDEの各Authoring Windowが編集する。
// ============================================================================

// ----------------------------------------------------------------------------
// Asset Catalog
//
// Asset Browserでは次のように表示する:
//
// GameAssets
// ├ PlayerMesh
// ├ PlayerMaterial
// ├ PlayerIdle
// ├ PlayerRun
// ├ PlayerJump
// └ MainFont
//
// Asset Browserへドラッグ＆ドロップすると、property宣言とRegister呼び出しを
// Roslynで追加する。
// ----------------------------------------------------------------------------

[AssetCatalogDefinition]
public static partial class GameAssets
{
    // 他のAuthoring定義から型安全に参照するAssetはpublicにできる。

    public static partial AssetSymbol PlayerMesh { get; }

    public static partial AssetSymbol PlayerMaterial { get; }

    public static partial AssetSymbol PlayerIdle { get; }

    public static partial AssetSymbol PlayerRun { get; }

    public static partial AssetSymbol PlayerJump { get; }

    public static partial AssetSymbol MainFont { get; }

    public static void Define(
        AssetCatalogBuilder Assets)
    {
        Assets.Register(
            PlayerMesh,
            "Assets/Characters/Player.glb",
            AssetKind.Mesh);

        Assets.Register(
            PlayerMaterial,
            "Assets/Characters/Player.material",
            AssetKind.Material);

        Assets.Register(
            PlayerIdle,
            "Assets/Characters/Animations/Idle.anim",
            AssetKind.AnimationClip);

        Assets.Register(
            PlayerRun,
            "Assets/Characters/Animations/Run.anim",
            AssetKind.AnimationClip);

        Assets.Register(
            PlayerJump,
            "Assets/Characters/Animations/Jump.anim",
            AssetKind.AnimationClip);

        Assets.Register(
            MainFont,
            "Assets/UI/MainFont.ttf",
            AssetKind.Font);
    }
}

// ----------------------------------------------------------------------------
// Input Map
//
// Input Map Editorでは次のように表示する:
//
// PlayerInput
// ├ Move : Vector2
// │  ├ WASD
// │  └ Gamepad Left Stick
// ├ Jump : Button
// │  ├ Space
// │  └ Gamepad South Button
// └ Pause : Button
//    ├ Escape
//    └ Gamepad Start
//
// Binding追加・削除・並べ替えはBind呼び出しを編集する。
// ----------------------------------------------------------------------------

[InputDefinition]
public static partial class PlayerInput
{
    private static partial InputActionSymbol Move { get; }

    private static partial InputActionSymbol Jump { get; }

    private static partial InputActionSymbol Pause { get; }

    public static void Define(
        InputBuilder Input)
    {
        Input.Action(
            Move,
            Move =>
            {
                Move.Value(
                    InputValueType.Vector2);

                Move.Bind<Wasd2D>();
                Move.Bind<GamepadLeftStick>();
            });

        Input.Action(
            Jump,
            Jump =>
            {
                Jump.Value(
                    InputValueType.Button);

                Jump.Bind<SpaceKey>();
                Jump.Bind<GamepadSouthButton>();
            });

        Input.Action(
            Pause,
            Pause =>
            {
                Pause.Value(
                    InputValueType.Button);

                Pause.Bind<EscapeKey>();
                Pause.Bind<GamepadStartButton>();
            });
    }
}

// ----------------------------------------------------------------------------
// UI Definition
//
// UI Designerでは次のように表示する:
//
// MainMenuUi
// └ Root
//    ├ Title
//    ├ StartButton
//    └ ExitButton
//
// HierarchyのAdd/Delete/Rename/ReparentはCreateブロックをRoslynで編集する。
// InspectorはSetのobject initializerを編集する。
// ----------------------------------------------------------------------------

[UiDefinition]
public static partial class MainMenuUi
{
    private static partial UiNodeSymbol Root { get; }

    private static partial UiNodeSymbol Title { get; }

    private static partial UiNodeSymbol StartButton { get; }

    private static partial UiNodeSymbol ExitButton { get; }

    public static void Define(
        UiBuilder UI)
    {
        UI.Create(
            Root,
            Root =>
            {
                Root.Set(
                    new UiLayout
                    {
                        Direction = UiDirection.Column,
                        Gap = 16f,
                        Padding = new Vector2(32f, 32f)
                    });

                Root.Create(
                    Title,
                    Title =>
                    {
                        Title.Set(
                            new UiText
                            {
                                Value = "My Game",
                                FontSize = 48f
                            });
                    });

                Root.Create(
                    StartButton,
                    StartButton =>
                    {
                        StartButton.Set(
                            new UiText
                            {
                                Value = "Start",
                                FontSize = 24f
                            });

                        StartButton.Set(
                            new UiButton
                            {
                                Interactable = true,
                                MinimumSize =
                                    new Vector2(240f, 56f)
                            });

                        StartButton.OnClick<
                            StartGameCommand>();
                    });

                Root.Create(
                    ExitButton,
                    ExitButton =>
                    {
                        ExitButton.Set(
                            new UiText
                            {
                                Value = "Exit",
                                FontSize = 24f
                            });

                        ExitButton.Set(
                            new UiButton
                            {
                                Interactable = true,
                                MinimumSize =
                                    new Vector2(240f, 56f)
                            });

                        ExitButton.OnClick<
                            ExitGameCommand>();
                    });
            });
    }
}

// ----------------------------------------------------------------------------
// Animation State Machine
//
// Animation Graph Editorでは次のように表示する:
//
//     ┌──────┐ Speed > 0.1  ┌─────┐
//     │ Idle │ ───────────→ │ Run │
//     └──────┘ ←─────────── └─────┘
//              Speed < 0.1
//
//        Idle / Run ── Jump trigger ──→ Jump
//
// State追加でpropertyとState呼び出しを追加する。
// Edge追加でTransitionTo呼び出しを追加する。
// ----------------------------------------------------------------------------

[AnimationGraphDefinition]
public static partial class CharacterAnimation
{
    private static partial AnimationStateSymbol Idle { get; }

    private static partial AnimationStateSymbol Run { get; }

    private static partial AnimationStateSymbol Jump { get; }

    public static void Define(
        AnimationGraphBuilder Animation)
    {
        Animation.State(
            Idle,
            Idle =>
            {
                Idle.Clip(
                    GameAssets.PlayerIdle);

                Idle.TransitionTo(
                    Run,
                    When.FloatGreater(
                        "Speed",
                        0.1f));

                Idle.TransitionTo(
                    Jump,
                    When.Trigger(
                        "Jump"));
            });

        Animation.State(
            Run,
            Run =>
            {
                Run.Clip(
                    GameAssets.PlayerRun);

                Run.TransitionTo(
                    Idle,
                    When.FloatLess(
                        "Speed",
                        0.1f));

                Run.TransitionTo(
                    Jump,
                    When.Trigger(
                        "Jump"));
            });

        Animation.State(
            Jump,
            Jump =>
            {
                Jump.Clip(
                    GameAssets.PlayerJump);

                Jump.TransitionTo(
                    Idle,
                    When.BoolEquals(
                        "IsGrounded",
                        true));
            });

        Animation.Initial(
            Idle);
    }
}

// ----------------------------------------------------------------------------
// Audio Routing
//
// Audio Mixerでは次のように表示する:
//
// Master  0 dB
// ├ Music -6 dB
// └ Sfx   -3 dB
//
// Bus追加・削除・親変更はCreateブロックを編集する。
// VolumeスライダーはVolume引数の数値式を編集する。
// ----------------------------------------------------------------------------

[AudioGraphDefinition]
public static partial class GameAudio
{
    private static partial AudioBusSymbol Master { get; }

    private static partial AudioBusSymbol Music { get; }

    private static partial AudioBusSymbol Sfx { get; }

    public static void Define(
        AudioGraphBuilder Audio)
    {
        Audio.Create(
            Master,
            Master =>
            {
                Master.Volume(0f);
                Master.Muted(false);

                Master.Create(
                    Music,
                    Music =>
                    {
                        Music.Volume(-6f);
                        Music.Muted(false);
                    });

                Master.Create(
                    Sfx,
                    Sfx =>
                    {
                        Sfx.Volume(-3f);
                        Sfx.Muted(false);
                    });
            });
    }
}

// ----------------------------------------------------------------------------
// Physics Layer Matrix
//
// Physics Layer Windowでは次の行列として表示する:
//
//                 Player  Enemy  World
// Player             -      X      X
// Enemy              X      -      X
// World              X      X      -
//
// チェックボックス操作でCollidesWith引数を編集する。
// ----------------------------------------------------------------------------

[PhysicsDefinition]
public static partial class GamePhysics
{
    private static partial PhysicsLayerSymbol Player { get; }

    private static partial PhysicsLayerSymbol Enemy { get; }

    private static partial PhysicsLayerSymbol WorldGeometry { get; }

    public static void Define(
        PhysicsBuilder Physics)
    {
        Physics.Layer(
            Player,
            Player =>
            {
                Player.CollidesWith(
                    Enemy,
                    WorldGeometry);
            });

        Physics.Layer(
            Enemy,
            Enemy =>
            {
                Enemy.CollidesWith(
                    Player,
                    WorldGeometry);
            });

        Physics.Layer(
            WorldGeometry,
            WorldGeometry =>
            {
                WorldGeometry.CollidesWith(
                    Player,
                    Enemy);
            });
    }
}

// ----------------------------------------------------------------------------
// Game Flow
//
// Game Flow Graphでは次のように表示する:
//
// Boot
//   └ Completed → MainMenu
//
// MainMenu
//   └ StartGameCommand → Gameplay
//
// Gameplay
//   └ PauseGameCommand → Pause
//
// Pause
//   └ ResumeGameCommand → Gameplay
//
// GameFlowはSceneそのものではなく、ゲーム全体の状態遷移を定義する。
// 各状態からどのSceneをロードするかは、将来的に型付きScene参照として
// 遷移設定へ追加できる。
// ----------------------------------------------------------------------------

[GameFlowDefinition]
public static partial class GameFlow
{
    private static partial GameStateSymbol Boot { get; }

    private static partial GameStateSymbol MainMenu { get; }

    private static partial GameStateSymbol Gameplay { get; }

    private static partial GameStateSymbol Pause { get; }

    public static void Define(
        GameFlowBuilder Flow)
    {
        Flow.State(
            Boot,
            Boot =>
            {
                Boot.TransitionTo(
                    MainMenu,
                    On.Completed);
            });

        Flow.State(
            MainMenu,
            MainMenu =>
            {
                MainMenu.TransitionTo(
                    Gameplay,
                    On.Command<
                        StartGameCommand>());
            });

        Flow.State(
            Gameplay,
            Gameplay =>
            {
                Gameplay.TransitionTo(
                    Pause,
                    On.Command<
                        PauseGameCommand>());
            });

        Flow.State(
            Pause,
            Pause =>
            {
                Pause.TransitionTo(
                    Gameplay,
                    On.Command<
                        ResumeGameCommand>());
            });

        Flow.Initial(
            Boot);
    }
}

// ----------------------------------------------------------------------------
// Build Profile
//
// Build Profile Windowではフォームとして表示する。
//
// Build Profileはツリーやグラフではないが、同じくコードが正本になる。
// IDEのコンボボックスやテキストボックスがDefine内の引数を編集する。
// ----------------------------------------------------------------------------

[BuildProfileDefinition]
public static partial class DesktopReleaseBuild
{
    public static void Define(
        BuildProfileBuilder Build)
    {
        Build.Target(
            BuildTarget.WindowsX64);

        Build.Configuration(
            BuildConfiguration.Release);

        Build.Output(
            "Build/Desktop");

        Build.Compression(
            BuildCompression.Zstd);

        Build.IncludeAllRegisteredAssets();
    }
}

// ============================================================================
// SOURCE-GENERATED REGION
//
// 実際には複数の.g.csとして出力する。
// このファイルでは仕様を確認しやすいよう、同じファイルへ記載している。
//
// SGはユーザーコードを書き換えない。
// ユーザーコードを書き換えられるのはVisual Studio拡張機能だけである。
// ============================================================================

// ----------------------------------------------------------------------------
// Generated Asset symbols and runtime registration
// ----------------------------------------------------------------------------

public static partial class GameAssets
{
    public static partial AssetSymbol PlayerMesh
    {
        get =>
            new(
                new AuthoringId(
                    0x1000_0000_0000_0001UL),
                nameof(PlayerMesh));
    }

    public static partial AssetSymbol PlayerMaterial
    {
        get =>
            new(
                new AuthoringId(
                    0x1000_0000_0000_0002UL),
                nameof(PlayerMaterial));
    }

    public static partial AssetSymbol PlayerIdle
    {
        get =>
            new(
                new AuthoringId(
                    0x1000_0000_0000_0003UL),
                nameof(PlayerIdle));
    }

    public static partial AssetSymbol PlayerRun
    {
        get =>
            new(
                new AuthoringId(
                    0x1000_0000_0000_0004UL),
                nameof(PlayerRun));
    }

    public static partial AssetSymbol PlayerJump
    {
        get =>
            new(
                new AuthoringId(
                    0x1000_0000_0000_0005UL),
                nameof(PlayerJump));
    }

    public static partial AssetSymbol MainFont
    {
        get =>
            new(
                new AuthoringId(
                    0x1000_0000_0000_0006UL),
                nameof(MainFont));
    }

    internal static void RegisterRuntime(
        RuntimeDatabase Runtime)
    {
        Runtime.Assets.Add(
            new RuntimeAssetEntry(
                PlayerMesh.Id,
                PlayerMesh.Name,
                "Assets/Characters/Player.glb",
                AssetKind.Mesh));

        Runtime.Assets.Add(
            new RuntimeAssetEntry(
                PlayerMaterial.Id,
                PlayerMaterial.Name,
                "Assets/Characters/Player.material",
                AssetKind.Material));

        Runtime.Assets.Add(
            new RuntimeAssetEntry(
                PlayerIdle.Id,
                PlayerIdle.Name,
                "Assets/Characters/Animations/Idle.anim",
                AssetKind.AnimationClip));

        Runtime.Assets.Add(
            new RuntimeAssetEntry(
                PlayerRun.Id,
                PlayerRun.Name,
                "Assets/Characters/Animations/Run.anim",
                AssetKind.AnimationClip));

        Runtime.Assets.Add(
            new RuntimeAssetEntry(
                PlayerJump.Id,
                PlayerJump.Name,
                "Assets/Characters/Animations/Jump.anim",
                AssetKind.AnimationClip));

        Runtime.Assets.Add(
            new RuntimeAssetEntry(
                MainFont.Id,
                MainFont.Name,
                "Assets/UI/MainFont.ttf",
                AssetKind.Font));
    }
}

// ----------------------------------------------------------------------------
// Generated Input symbols and runtime input table
// ----------------------------------------------------------------------------

public static partial class PlayerInput
{
    private static partial InputActionSymbol Move
    {
        get =>
            new(
                new AuthoringId(
                    0x2000_0000_0000_0001UL),
                nameof(Move));
    }

    private static partial InputActionSymbol Jump
    {
        get =>
            new(
                new AuthoringId(
                    0x2000_0000_0000_0002UL),
                nameof(Jump));
    }

    private static partial InputActionSymbol Pause
    {
        get =>
            new(
                new AuthoringId(
                    0x2000_0000_0000_0003UL),
                nameof(Pause));
    }

    internal static void RegisterRuntime(
        RuntimeDatabase Runtime)
    {
        Runtime.InputActions.Add(
            new RuntimeInputAction(
                Move.Id,
                Move.Name,
                InputValueType.Vector2,
                new[]
                {
                    InputBindingKind.Wasd2D,
                    InputBindingKind.GamepadLeftStick
                }));

        Runtime.InputActions.Add(
            new RuntimeInputAction(
                Jump.Id,
                Jump.Name,
                InputValueType.Button,
                new[]
                {
                    InputBindingKind.SpaceKey,
                    InputBindingKind.GamepadSouthButton
                }));

        Runtime.InputActions.Add(
            new RuntimeInputAction(
                Pause.Id,
                Pause.Name,
                InputValueType.Button,
                new[]
                {
                    InputBindingKind.EscapeKey,
                    InputBindingKind.GamepadStartButton
                }));
    }
}

// ----------------------------------------------------------------------------
// Generated UI symbols and direct Runtime construction
//
// RuntimeはUiBuilderやDefineを実行しない。
// SGが生成した直接的なUiWorld呼び出しだけを実行する。
// ----------------------------------------------------------------------------

public static partial class MainMenuUi
{
    private static partial UiNodeSymbol Root
    {
        get =>
            new(
                new AuthoringId(
                    0x3000_0000_0000_0001UL),
                nameof(Root));
    }

    private static partial UiNodeSymbol Title
    {
        get =>
            new(
                new AuthoringId(
                    0x3000_0000_0000_0002UL),
                nameof(Title));
    }

    private static partial UiNodeSymbol StartButton
    {
        get =>
            new(
                new AuthoringId(
                    0x3000_0000_0000_0003UL),
                nameof(StartButton));
    }

    private static partial UiNodeSymbol ExitButton
    {
        get =>
            new(
                new AuthoringId(
                    0x3000_0000_0000_0004UL),
                nameof(ExitButton));
    }

    public static UiEntity CreateRuntime(
        UiWorld Runtime)
    {
        ArgumentNullException.ThrowIfNull(Runtime);

        UiEntity Root =
            Runtime.CreateNode(
                MainMenuUi.Root.Id,
                MainMenuUi.Root.Name);

        UiLayout RootLayout =
            new()
            {
                Direction = UiDirection.Column,
                Gap = 16f,
                Padding = new Vector2(32f, 32f)
            };

        Runtime.Set(
            Root,
            in RootLayout);

        UiEntity Title =
            Runtime.CreateNode(
                MainMenuUi.Title.Id,
                MainMenuUi.Title.Name);

        Runtime.SetParent(
            Title,
            Root);

        UiText TitleText =
            new()
            {
                Value = "My Game",
                FontSize = 48f
            };

        Runtime.Set(
            Title,
            in TitleText);

        UiEntity StartButton =
            Runtime.CreateNode(
                MainMenuUi.StartButton.Id,
                MainMenuUi.StartButton.Name);

        Runtime.SetParent(
            StartButton,
            Root);

        UiText StartButtonText =
            new()
            {
                Value = "Start",
                FontSize = 24f
            };

        Runtime.Set(
            StartButton,
            in StartButtonText);

        UiButton StartButtonValue =
            new()
            {
                Interactable = true,
                MinimumSize =
                    new Vector2(240f, 56f)
            };

        Runtime.Set(
            StartButton,
            in StartButtonValue);

        Runtime.BindClick<
            StartGameCommand>(
            StartButton);

        UiEntity ExitButton =
            Runtime.CreateNode(
                MainMenuUi.ExitButton.Id,
                MainMenuUi.ExitButton.Name);

        Runtime.SetParent(
            ExitButton,
            Root);

        UiText ExitButtonText =
            new()
            {
                Value = "Exit",
                FontSize = 24f
            };

        Runtime.Set(
            ExitButton,
            in ExitButtonText);

        UiButton ExitButtonValue =
            new()
            {
                Interactable = true,
                MinimumSize =
                    new Vector2(240f, 56f)
            };

        Runtime.Set(
            ExitButton,
            in ExitButtonValue);

        Runtime.BindClick<
            ExitGameCommand>(
            ExitButton);

        return Root;
    }
}

// ----------------------------------------------------------------------------
// Generated Animation graph
// ----------------------------------------------------------------------------

public static partial class CharacterAnimation
{
    private static partial AnimationStateSymbol Idle
    {
        get =>
            new(
                new AuthoringId(
                    0x4000_0000_0000_0001UL),
                nameof(Idle));
    }

    private static partial AnimationStateSymbol Run
    {
        get =>
            new(
                new AuthoringId(
                    0x4000_0000_0000_0002UL),
                nameof(Run));
    }

    private static partial AnimationStateSymbol Jump
    {
        get =>
            new(
                new AuthoringId(
                    0x4000_0000_0000_0003UL),
                nameof(Jump));
    }

    internal static void RegisterRuntime(
        RuntimeDatabase Runtime)
    {
        Runtime.AnimationStates.Add(
            new RuntimeAnimationState(
                Idle.Id,
                Idle.Name,
                GameAssets.PlayerIdle.Id,
                IsInitial: true));

        Runtime.AnimationStates.Add(
            new RuntimeAnimationState(
                Run.Id,
                Run.Name,
                GameAssets.PlayerRun.Id,
                IsInitial: false));

        Runtime.AnimationStates.Add(
            new RuntimeAnimationState(
                Jump.Id,
                Jump.Name,
                GameAssets.PlayerJump.Id,
                IsInitial: false));

        Runtime.AnimationTransitions.Add(
            new RuntimeAnimationTransition(
                Idle.Id,
                Run.Id,
                When.FloatGreater(
                    "Speed",
                    0.1f)));

        Runtime.AnimationTransitions.Add(
            new RuntimeAnimationTransition(
                Idle.Id,
                Jump.Id,
                When.Trigger(
                    "Jump")));

        Runtime.AnimationTransitions.Add(
            new RuntimeAnimationTransition(
                Run.Id,
                Idle.Id,
                When.FloatLess(
                    "Speed",
                    0.1f)));

        Runtime.AnimationTransitions.Add(
            new RuntimeAnimationTransition(
                Run.Id,
                Jump.Id,
                When.Trigger(
                    "Jump")));

        Runtime.AnimationTransitions.Add(
            new RuntimeAnimationTransition(
                Jump.Id,
                Idle.Id,
                When.BoolEquals(
                    "IsGrounded",
                    true)));
    }
}

// ----------------------------------------------------------------------------
// Generated Audio graph
// ----------------------------------------------------------------------------

public static partial class GameAudio
{
    private static partial AudioBusSymbol Master
    {
        get =>
            new(
                new AuthoringId(
                    0x5000_0000_0000_0001UL),
                nameof(Master));
    }

    private static partial AudioBusSymbol Music
    {
        get =>
            new(
                new AuthoringId(
                    0x5000_0000_0000_0002UL),
                nameof(Music));
    }

    private static partial AudioBusSymbol Sfx
    {
        get =>
            new(
                new AuthoringId(
                    0x5000_0000_0000_0003UL),
                nameof(Sfx));
    }

    internal static void RegisterRuntime(
        RuntimeDatabase Runtime)
    {
        Runtime.AudioBuses.Add(
            new RuntimeAudioBus(
                Master.Id,
                Master.Name,
                Parent: null,
                VolumeDecibels: 0f,
                Muted: false));

        Runtime.AudioBuses.Add(
            new RuntimeAudioBus(
                Music.Id,
                Music.Name,
                Parent: Master.Id,
                VolumeDecibels: -6f,
                Muted: false));

        Runtime.AudioBuses.Add(
            new RuntimeAudioBus(
                Sfx.Id,
                Sfx.Name,
                Parent: Master.Id,
                VolumeDecibels: -3f,
                Muted: false));
    }
}

// ----------------------------------------------------------------------------
// Generated Physics collision table
//
// SGはPhysicsLayerSymbolを密なindexへ割り当て、
// Runtimeではbit maskだけを参照できる。
// ----------------------------------------------------------------------------

public static partial class GamePhysics
{
    private static partial PhysicsLayerSymbol Player
    {
        get =>
            new(
                new AuthoringId(
                    0x6000_0000_0000_0001UL),
                nameof(Player));
    }

    private static partial PhysicsLayerSymbol Enemy
    {
        get =>
            new(
                new AuthoringId(
                    0x6000_0000_0000_0002UL),
                nameof(Enemy));
    }

    private static partial PhysicsLayerSymbol WorldGeometry
    {
        get =>
            new(
                new AuthoringId(
                    0x6000_0000_0000_0003UL),
                nameof(WorldGeometry));
    }

    internal static void RegisterRuntime(
        RuntimeDatabase Runtime)
    {
        const int PlayerIndex = 0;
        const int EnemyIndex = 1;
        const int WorldGeometryIndex = 2;

        Runtime.PhysicsLayers.Add(
            new RuntimePhysicsLayer(
                Player.Id,
                Player.Name,
                PlayerIndex,
                CollisionMask:
                    (1UL << EnemyIndex) |
                    (1UL << WorldGeometryIndex)));

        Runtime.PhysicsLayers.Add(
            new RuntimePhysicsLayer(
                Enemy.Id,
                Enemy.Name,
                EnemyIndex,
                CollisionMask:
                    (1UL << PlayerIndex) |
                    (1UL << WorldGeometryIndex)));

        Runtime.PhysicsLayers.Add(
            new RuntimePhysicsLayer(
                WorldGeometry.Id,
                WorldGeometry.Name,
                WorldGeometryIndex,
                CollisionMask:
                    (1UL << PlayerIndex) |
                    (1UL << EnemyIndex)));
    }
}

// ----------------------------------------------------------------------------
// Generated Game Flow graph
// ----------------------------------------------------------------------------

public static partial class GameFlow
{
    private static partial GameStateSymbol Boot
    {
        get =>
            new(
                new AuthoringId(
                    0x7000_0000_0000_0001UL),
                nameof(Boot));
    }

    private static partial GameStateSymbol MainMenu
    {
        get =>
            new(
                new AuthoringId(
                    0x7000_0000_0000_0002UL),
                nameof(MainMenu));
    }

    private static partial GameStateSymbol Gameplay
    {
        get =>
            new(
                new AuthoringId(
                    0x7000_0000_0000_0003UL),
                nameof(Gameplay));
    }

    private static partial GameStateSymbol Pause
    {
        get =>
            new(
                new AuthoringId(
                    0x7000_0000_0000_0004UL),
                nameof(Pause));
    }

    internal static void RegisterRuntime(
        RuntimeDatabase Runtime)
    {
        Runtime.GameStates.Add(
            new RuntimeGameState(
                Boot.Id,
                Boot.Name,
                IsInitial: true));

        Runtime.GameStates.Add(
            new RuntimeGameState(
                MainMenu.Id,
                MainMenu.Name,
                IsInitial: false));

        Runtime.GameStates.Add(
            new RuntimeGameState(
                Gameplay.Id,
                Gameplay.Name,
                IsInitial: false));

        Runtime.GameStates.Add(
            new RuntimeGameState(
                Pause.Id,
                Pause.Name,
                IsInitial: false));

        Runtime.GameTransitions.Add(
            new RuntimeGameTransition(
                Boot.Id,
                MainMenu.Id,
                new GameTransitionTrigger(
                    GameTransitionTriggerKind.Completed,
                    "Completed")));

        Runtime.GameTransitions.Add(
            new RuntimeGameTransition(
                MainMenu.Id,
                Gameplay.Id,
                new GameTransitionTrigger(
                    GameTransitionTriggerKind.Command,
                    typeof(StartGameCommand).FullName ??
                    nameof(StartGameCommand))));

        Runtime.GameTransitions.Add(
            new RuntimeGameTransition(
                Gameplay.Id,
                Pause.Id,
                new GameTransitionTrigger(
                    GameTransitionTriggerKind.Command,
                    typeof(PauseGameCommand).FullName ??
                    nameof(PauseGameCommand))));

        Runtime.GameTransitions.Add(
            new RuntimeGameTransition(
                Pause.Id,
                Gameplay.Id,
                new GameTransitionTrigger(
                    GameTransitionTriggerKind.Command,
                    typeof(ResumeGameCommand).FullName ??
                    nameof(ResumeGameCommand))));
    }
}

// ----------------------------------------------------------------------------
// Generated Build Profile
// ----------------------------------------------------------------------------

public static partial class DesktopReleaseBuild
{
    internal static void RegisterRuntime(
        RuntimeDatabase Runtime)
    {
        Runtime.BuildProfile =
            new RuntimeBuildProfile(
                BuildTarget.WindowsX64,
                BuildConfiguration.Release,
                "Build/Desktop",
                BuildCompression.Zstd,
                IncludeAllAssets: true);
    }
}

// ============================================================================
// Generated bootstrap
//
// SGが全Authoring定義を検出し、決定的な順序で登録コードを生成する。
// Runtime側は各Defineメソッドを呼び出さない。
// ============================================================================

public static class GeneratedAuthoringBootstrap
{
    public static RuntimeDatabase CreateRuntimeDatabase()
    {
        RuntimeDatabase Runtime =
            new();

        // Asset参照を他定義が使用するため、Assetを先に登録する。
        GameAssets.RegisterRuntime(
            Runtime);

        PlayerInput.RegisterRuntime(
            Runtime);

        CharacterAnimation.RegisterRuntime(
            Runtime);

        GameAudio.RegisterRuntime(
            Runtime);

        GamePhysics.RegisterRuntime(
            Runtime);

        GameFlow.RegisterRuntime(
            Runtime);

        DesktopReleaseBuild.RegisterRuntime(
            Runtime);

        return Runtime;
    }
}

// ============================================================================
// Analyzer specifications
// 実際にはDiagnosticAnalyzerとCode Fixとして実装する。
// ============================================================================
//
// AUTH001
// Definition型はstatic partial classでなければならない。
//
// AUTH002
// Authoring Symbolはstatic partial get-only propertyでなければならない。
//
// AUTH003
// 定義呼び出しの第1引数名とラムダ引数名が一致しない。
//
// AUTH004
// 宣言されたSymbolがDefine内で使用されていない。
//
// AUTH005
// 同じSymbolが複数回定義されている。
//
// AUTH006
// Define内にサポートされない制御構文が存在する。
//
// AUTH007
// Runtimeコードへ安全に変換できない式が存在する。
//
// AUTH008
// ラムダ本体内の呼び出し対象が現在のラムダ引数ではない。
//
// AUTH009
// 他のDefinitionから参照されているSymbolを削除しようとしている。
//
// AUTH010
// Asset pathが重複している、または存在しない。
//
// AUTH011
// Input ActionにValueTypeが指定されていない。
//
// AUTH012
// Animation GraphにInitial Stateが存在しない、または複数存在する。
//
// AUTH013
// Animation Transitionの参照先Stateが存在しない。
//
// AUTH014
// Audio GraphにRoot Busが存在しない、または複数存在する。
//
// AUTH015
// Physics Layer数がRuntime bit maskの上限を超えている。
//
// AUTH016
// Physics Collision設定が非対称である。
// 設計により、警告または自動修正を選択できる。
//
// AUTH017
// Game FlowにInitial Stateが存在しない、または複数存在する。
//
// AUTH018
// Game Flowから到達不能なStateが存在する。
//
// AUTH019
// UI Nodeに親が存在しない、または親子関係が循環している。
//
// AUTH020
// 同じUI Nodeに同じComponentが複数Setされている。
//
// ============================================================================
// 共通解析モデル
// ============================================================================
//
// SGとVisual Studio拡張機能で解析コードを重複させない。
//
// 推奨構成:
//
//   Engine.Authoring.Contracts
//       Attribute、Symbol、Builder API
//
//   Engine.Authoring.Analysis
//       RoslynからAuthoring Modelを構築する純粋な解析処理
//
//   Engine.Authoring.Generator
//       Authoring ModelからRuntimeコードを生成
//
//   Engine.Authoring.Analyzers
//       Authoring Modelの診断とCode Fix
//
//   Engine.Authoring.VisualStudio
//       各Docking WindowとRoslynコード編集
//
//   Engine.Runtime
//       生成済みテーブルと直接コードを実行
//
// 共通モデルの概念:
//
//   AuthoringDocumentModel
//   ├ AssetCatalogModel
//   ├ InputMapModel
//   ├ UiDefinitionModel
//   ├ AnimationGraphModel
//   ├ AudioGraphModel
//   ├ PhysicsDefinitionModel
//   ├ GameFlowModel
//   └ BuildProfileModel
//
// 各モデルは最低限、次を保持する:
//
//   Definition Type Symbol
//   Node / Item Symbol
//   AuthoringId
//   Source Location
//   Parent / Child Relation
//   Property Values
//   External References
//   Diagnostics
//
// ============================================================================
// Example runtime entry point
// ============================================================================

public static class Program
{
    public static void aMain()
    {
        RuntimeDatabase Runtime =
            GeneratedAuthoringBootstrap
                .CreateRuntimeDatabase();

        UiWorld Ui =
            new();

        UiEntity MainMenuRoot =
            MainMenuUi.CreateRuntime(
                Ui);

        Console.WriteLine(
            $"Assets: {Runtime.Assets.Count}");

        Console.WriteLine(
            $"Input Actions: {Runtime.InputActions.Count}");

        Console.WriteLine(
            $"Animation States: {Runtime.AnimationStates.Count}");

        Console.WriteLine(
            $"Audio Buses: {Runtime.AudioBuses.Count}");

        Console.WriteLine(
            $"Physics Layers: {Runtime.PhysicsLayers.Count}");

        Console.WriteLine(
            $"Game States: {Runtime.GameStates.Count}");

        Console.WriteLine(
            $"Main Menu Root: {MainMenuRoot.Value}");
    }
}
