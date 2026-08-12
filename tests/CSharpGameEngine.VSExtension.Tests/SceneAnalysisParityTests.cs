using System.Linq;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Tests;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// Proves the shared Scene analysis behaves identically on <b>net472</b> - the framework this VSIX
/// runs on - as it does on net10.0 where the Source Generator runs (ADR-0040).
///
/// <para>
/// This is the payoff of sharing the analyser as source rather than reimplementing it per host. The
/// assertions below are deliberately the same facts <c>CSharpGameEngine.SceneModel.Tests</c> pins on
/// net10.0; if the two hosts ever diverge - a language feature that behaves differently, a BCL API
/// missing on net472, a Roslyn version skew - one of these fails instead of the editor quietly
/// disagreeing with the generated code.
/// </para>
/// </summary>
public sealed class SceneAnalysisParityTests {
    private const string CanonicalScene="""
        using CSharpGameEngine.Entities;
        using Sample;
        using System;
        using System.Numerics;
        namespace Game;
        public partial class MainScene:IScene {
            private const string Prefix="C# Game Engine";
            public Node Root=>new() {
                Name="Root",
                Components=[
                    new Transform { Position=new Vector3(1f,2f,3f) },
                    new PrimitiveMesh { Type=PrimitiveType.Cube },
                ],
                Children=[
                    new Node {
                        Name="Sun",
                        // Deliberately spells out pi instead of using MathF: the shared test helper
                        // builds the analysed compilation from the *host process's* assemblies, and
                        // MathF does not exist on net472. That is a property of this fixture, not of
                        // the analyser - in Visual Studio the VSIX analyses the user's project with
                        // the user's own references, where MathF resolves normally. Keeping the
                        // arithmetic identical still pins the folding and G9 formatting behaviour,
                        // which is what this test is actually about.
                        Components=[new SpotLight { Intensity=3f,InnerConeAngleRadians=12f*3.14159274f/180f }],
                    },
                ],
            };
        }
        """;

    private static SceneModel Analyze()=>SceneAnalysisTestHelper.Analyze(CanonicalScene);

    [Fact]
    public void CanonicalSceneAnalysesCleanlyOnNet472(){
        SceneAnalysisTestHelper.AssertNoDiagnostics(Analyze());
    }

    [Fact]
    public void NodePathsMatch(){
        SceneModel Model=Analyze();

        Assert.Equal("(root)",Model.Root!.Path.ToDisplayString());
        Assert.Equal("0",Model.Root.Children[0].Path.ToDisplayString());
        Assert.Equal(2,Model.TotalNodeCount);
    }

    [Fact]
    public void ComponentTypesAreFullyQualifiedIdentically(){
        SceneModel Model=Analyze();

        Assert.Equal(
            ["global::Sample.Transform","global::Sample.PrimitiveMesh"],
            Model.Root!.Components.Select(static C=>C.TypeFullyQualifiedName));
    }

    [Fact]
    public void EmittedExpressionsAreIdenticalToTheGeneratorHost(){
        // If this text differed between hosts, the Inspector would be editing something other than
        // what the Source Generator compiles.
        SceneModel Model=Analyze();

        Assert.Equal(
            "new global::Sample.Transform() { Position = new global::System.Numerics.Vector3(1f, 2f, 3f) }",
            Model.Root!.Components[0].EmitExpressionText);
        Assert.Equal(
            "new global::Sample.PrimitiveMesh() { Type = global::Sample.PrimitiveType.Cube }",
            Model.Root.Components[1].EmitExpressionText);
    }

    [Fact]
    public void ConstantFoldingBehavesTheSameOnNet472(){
        // Constant folding is the compiler's, not ours - but the float formatting on the way out is
        // ours, and "R" round-trips differently on .NET Framework. This pins the G9 choice.
        SceneModel Model=Analyze();
        SceneValue Angle=Model.Root!.Children[0].Components[0].Members
            .Single(static M=>M.MemberName=="InnerConeAngleRadians").Value;

        Assert.Equal(SceneValueKind.ConstantExpression,Angle.Kind);
        Assert.Equal("0.209439516f",Angle.EmitExpressionText);
        Assert.Equal("12f*3.14159274f/180f",Angle.SourceExpressionText);
    }

    [Fact]
    public void WhitelistViolationsAreRejectedTheSameWay(){
        SceneModel Model=SceneAnalysisTestHelper.Analyze("""
            using CSharpGameEngine.Entities;
            using Sample;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Components=[new Label { Text=System.IO.Path.Combine("a","b") }] };
            }
            """);

        SceneAnalysisTestHelper.AssertContainsDiagnostic(Model,"SCENE111");
    }

    [Fact]
    public void IncompleteSyntaxDoesNotThrowOnNet472(){
        // The VSIX calls the analyser while the user is mid-keystroke; throwing here would surface
        // as an exception dialog inside Visual Studio.
        for(var Length=CanonicalScene.Length;Length>=0;Length-=7){
            var Truncated=CanonicalScene.Substring(0,Length);
            Microsoft.CodeAnalysis.CSharp.CSharpCompilation Compilation=
                SceneAnalysisTestHelper.CreateCompilation(SceneAnalysisTestHelper.CommonComponents,Truncated);
            Microsoft.CodeAnalysis.INamedTypeSymbol?SceneClass=
                SceneAnalysisTestHelper.FindSceneClass(Compilation,SceneClassName:null);

            if(SceneClass is null){
                continue;
            }

            SceneDeclarationAnalyzer.Analyze(SceneClass,Compilation,System.Threading.CancellationToken.None);
        }
    }
}
