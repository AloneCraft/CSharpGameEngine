using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.CodeAnalysis.Tests;

/// <summary>ADR-0077 stage 4：<c>[InputActions]</c>構造体から入力マップとフレームごとの<c>Read</c>を生成し、システムは<c>in</c>で読み取る。</summary>
public sealed class InputActionsGeneratorTests {
    private static readonly string Declarations=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"TestAssets","Core","InputActionDeclarations.cs"));

    private const string Actions="""
        using CSharpGameEngine.Input;

        namespace Game;

        [InputActions]
        public partial struct PlayerActions
        {
            [Keys(Up: Key.W, Down: Key.S, Left: Key.A, Right: Key.D)]
            [Gamepad(Pad.LeftStick)]
            public Stick Move;
            [Mouse(MouseAxis.Delta)]
            [Gamepad(Pad.RightStick)]
            public Look Look;
            [Keys(Key.Space)]
            [Gamepad(Pad.South)]
            public Button Jump;
        }
        """;

    private const string Reader="""
        using CSharpGameEngine.Entities;
        using Sample;

        namespace Game;

        internal struct Controls : ISystem
        {
            public void Update(ref Position position, in PlayerActions input) { }
        }
        """;

    private static GeneratorDriverRunResult Run(params string[] Sources)=>
        GameSystemGeneratorTestHelper.RunGenerator([Declarations,GameSystemGeneratorTestHelper.CommonComponents,.. Sources]);

    [Fact]
    public void Declaration_GeneratesTheContextTheIdsAndThePerFrameRead(){
        var Result=Run(Actions);

        Assert.Empty(Result.Diagnostics);
        var Source=GameSystemGeneratorTestHelper.GeneratedText(Result,"GameSystem.InputActions.Game_PlayerActions.g.cs");

        // スティックの各方向：Upは軸を入れ替え、Downは入れ替えて反転、Leftは反転、Rightはそのまま。
        Assert.Contains("KeyCode(0x57)), Modifiers: new global::CSharpGameEngine.Input.InputModifier[] { new global::CSharpGameEngine.Input.InputSwizzleModifier() })",Source);
        Assert.Contains("KeyCode(0x53)), Modifiers: new global::CSharpGameEngine.Input.InputModifier[] { new global::CSharpGameEngine.Input.InputSwizzleModifier(), new global::CSharpGameEngine.Input.InputNegateModifier() })",Source);
        Assert.Contains("KeyCode(0x41)), Modifiers: new global::CSharpGameEngine.Input.InputModifier[] { new global::CSharpGameEngine.Input.InputNegateModifier() })",Source);
        Assert.Contains("Id_Move, global::CSharpGameEngine.Platform.Windows.Win32InputEventAdapter.GamepadLeftStick, DeadZone: 0.2f)",Source);
        // マウス移動量はY方向を反転して上へ動かすと上を見るようにする。スティックは別のアクションで、フレーム時間に応じてスケーリングする。
        Assert.Contains("\"Delta\", global::CSharpGameEngine.Core.InputControlKind.RelativeAxis2D), Modifiers: new global::CSharpGameEngine.Input.InputModifier[] { new global::CSharpGameEngine.Input.InputNegateModifier(X: false, Y: true) })",Source);
        Assert.Contains("S.X * global::CSharpGameEngine.Input.Look.StickTurnsPerSecond * DeltaSeconds",Source);
        Assert.Contains("\"Gamepad_FaceButton_Bottom\"",Source);
        // IDは型名とメンバー名から生成する。GUIDを手書きしない。
        Assert.Contains("StableId.FromName(IdNamespace, \"Game.PlayerActions.Jump\")",Source);
        Assert.Contains("Action.Phase == global::CSharpGameEngine.Core.InputActionPhase.Started",Source);
    }

    [Fact]
    public void AMouseButtonAndAPointer_AreBoundAndTheCursorIsPassedThrough(){
        const string Click="""
            using CSharpGameEngine.Input;

            namespace Game;

            [InputActions]
            public partial struct ClickActions
            {
                [Mouse(MouseButton.Left)]
                public Button SetDestination;
                [Pointer]
                public Pointer Cursor;
            }
            """;
        var Result=Run(Click);

        Assert.Empty(Result.Diagnostics);
        var Source=GameSystemGeneratorTestHelper.GeneratedText(Result,"GameSystem.InputActions.Game_ClickActions.g.cs");

        Assert.Contains("new global::CSharpGameEngine.Core.InputControl(global::CSharpGameEngine.Core.InputDeviceKind.Mouse, \"Left\")",Source);
        Assert.Contains("float DeltaSeconds, global::CSharpGameEngine.Input.Pointer CursorPointer)",Source);
        Assert.Contains("Result.Cursor = CursorPointer;",Source);
    }

    [Fact]
    public void Declarations_AreAggregatedIntoOneInputServer(){
        var Result=Run(Actions);
        var Aggregate=GameSystemGeneratorTestHelper.GeneratedText(Result,"GameSystem.InputActions.g.cs");

        Assert.Contains("global::Game.PlayerActions.CreateContext(),",Aggregate);
        Assert.Contains("Server.SetContextActive(Context.Id, true, default)",Aggregate);
    }

    [Fact]
    public void ASystemThatTakesTheStructByIn_ReadsItBeforeItsDispatchAndRunsInFrameInput(){
        var Result=Run(Actions,Reader);

        Assert.Empty(Result.Diagnostics);
        var Loop=GameSystemGeneratorTestHelper.GeneratedText(Result,"GameSystem.Loop.g.cs");

        Assert.Contains("internal global::Game.PlayerActions Input0;",Loop);
        Assert.Contains("internal bool InputInstalled;",Loop);
        Assert.Contains("Systems.Input0 = global::Game.PlayerActions.Read(EngineHost.Input, global::System.Math.Min(State.FrameDeltaSeconds, 0.1f), EngineHost.Cursor);",Loop);
        Assert.Contains("Controls.Update(ref State.World.Archetype0.GetPositionRefUnchecked(Index), in Systems.Input0)",Loop);
        Assert.Contains("SetInput(global::CSharpGameEngine.Generated.GeneratedInputActions.CreateServer())",Loop);
        // [SystemPhase]の記述はない。入力を読むことで、システムはFrameInput（フェーズ0）へ入る。
        Assert.Contains("if (Phase == (global::CSharpGameEngine.Entities.SystemPhase)0 &&",Loop.Substring(Loop.IndexOf("private static void RunPhase",StringComparison.Ordinal)));
    }

    [Fact]
    public void ASystemThatReadsInput_RunsAfterTheEnginesWindowSystemWithoutSayingSo(){
        const string Window="""
            using CSharpGameEngine.Entities;
            using Sample;

            namespace CSharpGameEngine.Runtime.Windows;

            [SystemPhase(SystemPhase.FrameInput)]
            internal struct Window : ISystem
            {
                public void Update(ref Health health) { }
            }
            """;
        var Result=Run(Actions,Reader,Window);
        var Loop=GameSystemGeneratorTestHelper.GeneratedText(Result,"GameSystem.Loop.g.cs");

        Assert.True(Loop.IndexOf("Systems.Window.Update(",StringComparison.Ordinal)<Loop.IndexOf("Systems.Controls.Update(",StringComparison.Ordinal));
    }

    [Fact]
    public void AnExplicitSystemPhase_StillWins(){
        const string Explicit="""
            using CSharpGameEngine.Entities;
            using Sample;

            namespace Game;

            [SystemPhase(SystemPhase.Presentation)]
            internal struct Late : ISystem
            {
                public void Update(ref Position position, in PlayerActions input) { }
            }
            """;
        var Loop=GameSystemGeneratorTestHelper.GeneratedText(Run(Actions,Explicit),"GameSystem.Loop.g.cs");

        Assert.Contains("if (Phase == (global::CSharpGameEngine.Entities.SystemPhase)4 &&",Loop);
    }

    [Fact]
    public void InputActionsPassedByRef_ReportsSysgen038(){
        const string Bad="""
            using CSharpGameEngine.Entities;
            using Sample;

            namespace Game;

            internal struct Bad : ISystem
            {
                public void Update(ref Position position, ref PlayerActions input) { }
            }
            """;

        Assert.Contains(Run(Actions,Bad).Diagnostics,D=>D.Id=="SYSGEN038");
    }

    [Fact]
    public void ANonPartialStruct_ReportsInput001(){
        const string NotPartial="""
            using CSharpGameEngine.Input;

            namespace Game;

            [InputActions]
            public struct Closed { [Keys(Key.Space)] public Button Jump; }
            """;

        Assert.Contains(Run(NotPartial).Diagnostics,D=>D.Id=="INPUT001");
    }

    [Fact]
    public void AMemberThatIsNotAStickLookOrButton_ReportsInput002(){
        const string Wrong="""
            using CSharpGameEngine.Input;

            namespace Game;

            [InputActions]
            public partial struct Wrong { public int Count; }
            """;

        Assert.Contains(Run(Wrong).Diagnostics,D=>D.Id=="INPUT002");
    }

    [Fact]
    public void ABindingThatDoesNotFitTheMemberType_ReportsInput003(){
        const string Misfit="""
            using CSharpGameEngine.Input;

            namespace Game;

            [InputActions]
            public partial struct Misfit
            {
                [Mouse(MouseAxis.Delta)] public Button Fire;
                [Gamepad(Pad.South)] public Stick Move;
                [Keys(Key.W, Key.S)] public Stick Walk;
            }
            """;
        var Diagnostics=Run(Misfit).Diagnostics.Where(static D=>D.Id=="INPUT003").ToArray();

        Assert.Equal(3,Diagnostics.Length);
    }

    [Fact]
    public void AMemberWithoutABinding_ReportsInput004(){
        const string Unbound="""
            using CSharpGameEngine.Input;

            namespace Game;

            [InputActions]
            public partial struct Unbound { public Button Jump; }
            """;

        Assert.Contains(Run(Unbound).Diagnostics,D=>D.Id=="INPUT004");
    }

    private const string Driving="""
        using CSharpGameEngine.Input;

        namespace Game;

        [InputActions]
        public partial struct DriveActions
        {
            [Keys(Key.W, Key.Up)]
            [Gamepad(Pad.RightTrigger)]
            [Gamepad(Pad.West)]
            public Axis Throttle;
            [Keys(Key.S)]
            [Gamepad(Pad.LeftTrigger, DeadZone = 0.05f, Scale = 1.5f)]
            public Axis Brake;
            [Keys(Key.D)]
            [NegativeKeys(Key.A, Key.Left)]
            [Gamepad(Pad.LeftStick, StickAxis.X, Invert = true)]
            public Axis Steering;
            [Mouse(MouseAxis.DeltaX)]
            [Gamepad(Pad.RightStick, StickAxis.X)]
            public Look Around;
            [Mouse(MouseAxis.DeltaY)]
            [Gamepad(Pad.RightStick, StickAxis.Y)]
            public Look Tilt;
            [Keys(Key.Space)]
            [Gamepad(Pad.LeftShoulder)]
            public Button Handbrake;
        }
        """;

    [Fact]
    public void AnAxis_BindsKeysNegativeKeysTriggersStickAxesAndButtonsAndReadsAClampedValue(){
        var Result=Run(Driving);

        Assert.Empty(Result.Diagnostics);
        var Source=GameSystemGeneratorTestHelper.GeneratedText(Result,"GameSystem.InputActions.Game_DriveActions.g.cs");

        // キーは+1、NegativeKeysは-1。トリガーとスティック軸は独立したコントロールで、既定のデッドゾーンはそれぞれ0.1、0.2。
        Assert.Contains("Id_Throttle, new global::CSharpGameEngine.Core.InputControl(global::CSharpGameEngine.Core.InputDeviceKind.Keyboard, global::CSharpGameEngine.Platform.Windows.Win32InputEventAdapter.KeyCode(0x57)))",Source);
        Assert.Contains("Id_Steering, new global::CSharpGameEngine.Core.InputControl(global::CSharpGameEngine.Core.InputDeviceKind.Keyboard, global::CSharpGameEngine.Platform.Windows.Win32InputEventAdapter.KeyCode(0x41)), Modifiers: new global::CSharpGameEngine.Input.InputModifier[] { new global::CSharpGameEngine.Input.InputNegateModifier() })",Source);
        Assert.Contains("Id_Steering, new global::CSharpGameEngine.Core.InputControl(global::CSharpGameEngine.Core.InputDeviceKind.Keyboard, global::CSharpGameEngine.Platform.Windows.Win32InputEventAdapter.KeyCode(0x25)), Modifiers:",Source);
        Assert.Contains("Id_Throttle, global::CSharpGameEngine.Platform.Windows.Win32InputEventAdapter.GamepadRightTrigger, DeadZone: 0.1f)",Source);
        Assert.Contains("Id_Brake, global::CSharpGameEngine.Platform.Windows.Win32InputEventAdapter.GamepadLeftTrigger, DeadZone: 0.05f, Scale: 1.5f)",Source);
        Assert.Contains("Id_Steering, global::CSharpGameEngine.Platform.Windows.Win32InputEventAdapter.GamepadLeftX, DeadZone: 0.2f, Modifiers: new global::CSharpGameEngine.Input.InputModifier[] { new global::CSharpGameEngine.Input.InputNegateModifier() })",Source);
        Assert.Contains("\"Gamepad_FaceButton_Left\"",Source);
        // 複数のバインディング値を加算し、Axis.FromSumが-1～1に制限してNaNを0に変換する。
        Assert.Contains("Result.Throttle = global::CSharpGameEngine.Input.Axis.FromSum(V.X);",Source);
        Assert.Contains("StableId.FromName(IdNamespace, \"Game.DriveActions.Brake\")",Source);
    }

    [Fact]
    public void ALookCanTakeOneAxisOfTheMouseAndOfAStick(){
        var Result=Run(Driving);
        var Source=GameSystemGeneratorTestHelper.GeneratedText(Result,"GameSystem.InputActions.Game_DriveActions.g.cs");

        // MouseXだけならYをマスクする。MouseYだけならXをマスクし、上へ動かすと上を見る符号反転を維持する。
        Assert.Contains("\"Delta\", global::CSharpGameEngine.Core.InputControlKind.RelativeAxis2D), Modifiers: new global::CSharpGameEngine.Input.InputModifier[] { new global::CSharpGameEngine.Input.InputMaskModifier(X: false, Y: true) })",Source);
        Assert.Contains("{ new global::CSharpGameEngine.Input.InputMaskModifier(X: true, Y: false), new global::CSharpGameEngine.Input.InputNegateModifier(X: false, Y: true) })",Source);
        // スティックのY軸はswizzleによって視点入力のY軸へ対応する。X軸に変換は不要。
        Assert.Contains("Id_Around_Stick, global::CSharpGameEngine.Platform.Windows.Win32InputEventAdapter.GamepadRightX, DeadZone: 0.2f)",Source);
        Assert.Contains("Id_Tilt_Stick, global::CSharpGameEngine.Platform.Windows.Win32InputEventAdapter.GamepadRightY, DeadZone: 0.2f, Modifiers: new global::CSharpGameEngine.Input.InputModifier[] { new global::CSharpGameEngine.Input.InputSwizzleModifier() })",Source);
    }

    [Fact]
    public void AButton_ReadsItsHeldSecondsFromTheServerWithTheFrameTime(){
        var Result=Run(Actions);
        var Source=GameSystemGeneratorTestHelper.GeneratedText(Result,"GameSystem.InputActions.Game_PlayerActions.g.cs");

        Assert.Contains("var Held = !Server.ReadValue(Id_Jump).IsZero;",Source);
        Assert.Contains("Result.Jump = new global::CSharpGameEngine.Input.Button(Held, Pressed, Released, Server.TrackHold(Id_Jump, Held, Pressed, Released, DeltaSeconds));",Source);
        // 読み取り処理の引数は引き続き3つなので、生成ループは変更されない。
        Assert.Contains("Read(global::CSharpGameEngine.Input.InputServer Server, float DeltaSeconds, global::CSharpGameEngine.Input.Pointer CursorPointer)",Source);
    }

    [Fact]
    public void AGamepadButtonOnAnAxisAndAShoulderOnAButton_UseUnrealsKeyNames(){
        var Result=Run(Driving);
        var Source=GameSystemGeneratorTestHelper.GeneratedText(Result,"GameSystem.InputActions.Game_DriveActions.g.cs");

        Assert.Contains("\"Gamepad_LeftShoulder\"",Source);
    }

    [Theory]
    [InlineData("[Gamepad(Pad.LeftStick)] public Axis A;")]
    [InlineData("[Gamepad(Pad.LeftStick, StickAxis.X)] public Stick A;")]
    [InlineData("[Gamepad(Pad.LeftTrigger)] public Stick A;")]
    [InlineData("[Gamepad(Pad.LeftTrigger)] public Look A;")]
    [InlineData("[Gamepad(Pad.LeftTrigger, StickAxis.X)] public Axis A;")]
    [InlineData("[Gamepad(Pad.South, StickAxis.X)] public Button A;")]
    [InlineData("[Gamepad(Pad.LeftTrigger)] public Button A;")]
    [InlineData("[Mouse(MouseAxis.DeltaX)] public Axis A;")]
    [InlineData("[Mouse(MouseAxis.DeltaY)] public Button A;")]
    [InlineData("[Mouse(MouseButton.Left)] public Axis A;")]
    [InlineData("[NegativeKeys(Key.A)] public Button A;")]
    [InlineData("[NegativeKeys(Key.A)] public Stick A;")]
    [InlineData("[NegativeKeys()] public Axis A;")]
    [InlineData("[Keys()] public Axis A;")]
    [InlineData("[Keys(Key.W)] public Look A;")]
    [InlineData("[Pointer] public Axis A;")]
    public void ABindingThatDoesNotFitTheNewMemberTypes_ReportsInput003(string Member){
        var Source=$$"""
            using CSharpGameEngine.Input;

            namespace Game;

            [InputActions]
            public partial struct Misfit
            {
                {{Member}}
            }
            """;

        Assert.Contains(Run(Source).Diagnostics,D=>D.Id=="INPUT003");
    }

    [Theory]
    [InlineData("DeadZone = 1f")]
    [InlineData("DeadZone = 1.5f")]
    [InlineData("DeadZone = -0.1f")]
    [InlineData("DeadZone = float.NaN")]
    [InlineData("DeadZone = float.PositiveInfinity")]
    [InlineData("Scale = 0f")]
    [InlineData("Scale = -1f")]
    [InlineData("Scale = float.NaN")]
    [InlineData("Scale = float.PositiveInfinity")]
    public void ADeadZoneOrScaleOutOfRange_ReportsInput005(string Argument){
        var Source=$$"""
            using CSharpGameEngine.Input;

            namespace Game;

            [InputActions]
            public partial struct Bad
            {
                [Gamepad(Pad.RightTrigger, {{Argument}})] public Axis A;
            }
            """;
        var Result=Run(Source);

        Assert.Contains(Result.Diagnostics,D=>D.Id=="INPUT005");
        Assert.DoesNotContain(Result.GeneratedTrees,Tree=>Tree.FilePath.Contains("Game_Bad",StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("DeadZone = 0f")]
    [InlineData("DeadZone = 0.999f")]
    [InlineData("Scale = 0.001f")]
    [InlineData("Scale = 100f")]
    [InlineData("Invert = true")]
    public void TheEdgesOfTheValidRange_AreAccepted(string Argument){
        var Source=$$"""
            using CSharpGameEngine.Input;

            namespace Game;

            [InputActions]
            public partial struct Edge
            {
                [Gamepad(Pad.RightTrigger, {{Argument}})] public Axis A;
            }
            """;

        Assert.Empty(Run(Source).Diagnostics);
    }

    [Fact]
    public void ADeadZoneOrScaleOnAGamepadButton_ReportsInput005(){
        const string Button="""
            using CSharpGameEngine.Input;

            namespace Game;

            [InputActions]
            public partial struct Pad1
            {
                [Gamepad(Pad.South, DeadZone = 0.1f)] public Button Jump;
                [Gamepad(Pad.West, Invert = true)] public Axis Fire;
            }
            """;
        var Diagnostics=Run(Button).Diagnostics.Where(static D=>D.Id=="INPUT005").ToArray();

        Assert.Equal(2,Diagnostics.Length);
    }
}
