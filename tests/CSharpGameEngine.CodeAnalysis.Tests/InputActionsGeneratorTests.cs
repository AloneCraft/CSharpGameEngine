using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.CodeAnalysis.Tests;

/// <summary>ADR-0077 stage 4: <c>[InputActions]</c> structs become an input map and a per-frame <c>Read</c>; a system reads one with <c>in</c>.</summary>
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

        // The keys of a stick: Up swizzles, Down swizzles and negates, Left negates, Right is plain.
        Assert.Contains("KeyCode(0x57)), Modifiers: new global::CSharpGameEngine.Input.InputModifier[] { new global::CSharpGameEngine.Input.InputSwizzleModifier() })",Source);
        Assert.Contains("KeyCode(0x53)), Modifiers: new global::CSharpGameEngine.Input.InputModifier[] { new global::CSharpGameEngine.Input.InputSwizzleModifier(), new global::CSharpGameEngine.Input.InputNegateModifier() })",Source);
        Assert.Contains("KeyCode(0x41)), Modifiers: new global::CSharpGameEngine.Input.InputModifier[] { new global::CSharpGameEngine.Input.InputNegateModifier() })",Source);
        Assert.Contains("Id_Move, global::CSharpGameEngine.Platform.Windows.Win32InputEventAdapter.GamepadLeftStick, DeadZone: 0.2f)",Source);
        // The mouse delta is negated in Y so that moving up looks up; the stick is a separate action scaled by the frame time.
        Assert.Contains("\"Delta\", global::CSharpGameEngine.Core.InputControlKind.RelativeAxis2D), Modifiers: new global::CSharpGameEngine.Input.InputModifier[] { new global::CSharpGameEngine.Input.InputNegateModifier(X: false, Y: true) })",Source);
        Assert.Contains("S.X * global::CSharpGameEngine.Input.Look.StickTurnsPerSecond * DeltaSeconds",Source);
        Assert.Contains("\"Gamepad_FaceButton_Bottom\"",Source);
        // The IDs come from the type and member names; no GUID is written by hand.
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
        // No [SystemPhase] was written: reading input puts the system in FrameInput (phase 0).
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
}
