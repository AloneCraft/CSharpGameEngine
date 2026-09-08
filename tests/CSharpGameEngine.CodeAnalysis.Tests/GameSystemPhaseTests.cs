namespace CSharpGameEngine.CodeAnalysis.Tests;

public sealed class GameSystemPhaseTests {


    [Fact]
    public void EmitsSystemMajorLoopsAndWorldSystemOnce(){
        var Result=GameSystemGeneratorTestHelper.RunGenerator(
            GameSystemGeneratorTestHelper.CommonComponents,
            """
            using CSharpGameEngine.Entities;
            using Sample;
            namespace Game;

            internal struct PerEntity : ISystem {
                public bool Update(ref Position position, ref State state) => true;
            }

            [SystemPhase(SystemPhase.PostPhysics)]
            internal struct OnceAfterPhysics : IWorldSystem {
                public bool Update(ref State state) => true;
            }
            """);

        Assert.Empty(Result.Diagnostics);
        string Text=GameSystemGeneratorTestHelper.GeneratedText(Result,"GameSystem.Loop.g.cs");
        Assert.Contains("internal struct GeneratedSystemSet",Text);
        Assert.Contains("RunPhase(global::CSharpGameEngine.Entities.SystemPhase Phase",Text);
        Assert.Contains("Systems.OnceAfterPhysics.Update(ref State)",Text);
        Assert.Contains("Phase == (global::CSharpGameEngine.Entities.SystemPhase)3",Text);
        Assert.Equal(1,Count(Text,"OnceAfterPhysics.Update(ref State)"));

        int PerEntity=Text.IndexOf("Systems.PerEntity.Update(",StringComparison.Ordinal);
        int PostPhysics=Text.IndexOf("Systems.OnceAfterPhysics.Update(",StringComparison.Ordinal);
        Assert.True(PerEntity>=0&&PostPhysics>PerEntity);
        Assert.Contains("RunPhase(global::CSharpGameEngine.Entities.SystemPhase.Presentation",Text);
        Assert.Contains("SystemPhase.Presentation, ref State, ref Systems, false, false);\n        State.World.PlaybackStructuralChanges(ref State);",Text);
        Assert.Contains("internal static void Shutdown",Text);
        Assert.Contains("State.World.BeginSystemExecution();",Text);
        Assert.Contains("State.World.EndSystemExecution();",Text);
    }

    [Fact]
    public void EditorMethodsReuseCallerOwnedSystemSet(){
        var Result=GameSystemGeneratorTestHelper.RunGenerator(
            GameSystemGeneratorTestHelper.CommonComponents,
            """
            using CSharpGameEngine.Entities;
            using Sample;
            namespace Game;
            [ExecuteInEditor(Realtime=true)]
            internal struct Stateful : ISystem {
                private int count;
                public bool Update(ref Position position, ref State state) { count++; return true; }
            }
            """);

        Assert.Empty(Result.Diagnostics);
        string Text=GameSystemGeneratorTestHelper.GeneratedText(Result,"GameSystem.Loop.g.cs");
        Assert.Contains("RunEditorFrame(ref global::CSharpGameEngine.Entities.State State, ref GeneratedSystemSet Systems)",Text);
        Assert.DoesNotContain("var Stateful = new global::Game.Stateful();",Text);
    }

    private static int Count(string Text,string Value){
        int Count=0;
        for(int Index=0;(Index=Text.IndexOf(Value,Index,StringComparison.Ordinal))>=0;Index+=Value.Length){ Count++; }
        return Count;
    }
}
