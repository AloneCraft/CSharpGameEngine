using System.Linq;
using System.Numerics;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Tests;
using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class SceneSaveVerificationTests {
    private const string TransformTypes="""
        namespace CSharpGameEngine.Runtime.Windows {
            public struct LogicalVector3 {
                public float R; public float U; public float F;
                public LogicalVector3(float R,float U,float F){ this.R=R;this.U=U;this.F=F; }
            }
            public struct Transform {
                public LogicalVector3 Position;
                public float PositionX; public float PositionY; public float PositionZ;
                public float RotationX; public float RotationY; public float RotationZ; public float RotationW;
                public float ScaleX; public float ScaleY; public float ScaleZ;
            }
        }
        """;

    private const string Source="""
        using CSharpGameEngine.Entities;
        using CSharpGameEngine.Runtime.Windows;
        namespace Game;
        public partial class MainScene:IScene {
            public Node Root=>new() { Children=[new Node {
                Name="Cube", // hand-written comment
                Components=[new Transform { PositionX=1f,RotationW=1f,ScaleX=1f,ScaleY=1f,ScaleZ=1f }],
            }] };
        }
        """;

    private static readonly SceneKey Key=new("project-a","Game.MainScene");

    [Fact]
    public void VerifiedBatchMatchesPredictedModelAndPreservesUnrelatedText(){
        Seed Seed=CreateSeed();
        SetTransformOperation Operation=new(
            Key,Seed.Cube,DocumentVersion:1,
            new SceneTransformValue(new Vector3(9f,0f,0f),Quaternion.Identity,Vector3.One));
        Assert.True(SceneModelEditApplier.TryApply(
            Seed.Model,Seed.Projection,Seed.Reconciler,Key,[Operation],
            out SceneModel Predicted,out _));
        SceneSourceEditResult Edited=SceneSourceEditor.ApplyBatch(
            Seed.Root,Seed.Projection,[Operation]);

        Assert.True(Edited.Succeeded,Edited.ErrorMessage);
        Assert.True(SceneSaveVerification.Verify(
            Edited.Root!,Seed.Root,Seed.Compilation,Predicted,Key.ClassFullyQualifiedName,
            CancellationToken.None,out string?Error),Error);
        Assert.Contains("hand-written comment",Edited.Root!.ToFullString());
    }

    [Fact]
    public void VerificationRejectsSourceThatDoesNotMatchPredictedModel(){
        Seed Seed=CreateSeed();
        SetTransformOperation Operation=new(
            Key,Seed.Cube,DocumentVersion:1,
            new SceneTransformValue(new Vector3(9f,0f,0f),Quaternion.Identity,Vector3.One));
        Assert.True(SceneModelEditApplier.TryApply(
            Seed.Model,Seed.Projection,Seed.Reconciler,Key,[Operation],
            out SceneModel Predicted,out _));

        Assert.False(SceneSaveVerification.Verify(
            Seed.Root,Seed.Root,Seed.Compilation,Predicted,Key.ClassFullyQualifiedName,
            CancellationToken.None,out string?Error));
        Assert.Contains(SceneModelStructuralSignature.Compute(Predicted),Error);
    }

    private static Seed CreateSeed(){
        var Compilation=SceneAnalysisTestHelper.CreateCompilation(TransformTypes,Source);
        SyntaxNode Root=Compilation.SyntaxTrees
            .Single(Tree=>Tree.GetRoot().ToFullString().Contains("class MainScene"))
            .GetRoot();
        var SceneClass=SceneAnalysisTestHelper.FindSceneClass(Compilation,"MainScene")!;
        SceneModel Model=SceneDeclarationAnalyzer.Analyze(
            SceneClass,Compilation,CancellationToken.None);
        SceneProjectionReconciler Reconciler=new();
        SceneProjection Projection=Reconciler.Reconcile(Key,Model,Previous:null);
        EditorNodeId Cube=Assert.Single(
            Projection.Nodes,static Node=>Node.Node.Name=="Cube").Id;
        return new Seed(Compilation,Root,Model,Projection,Reconciler,Cube);
    }

    private sealed record Seed(
        Microsoft.CodeAnalysis.CSharp.CSharpCompilation Compilation,SyntaxNode Root,
        SceneModel Model,SceneProjection Projection,SceneProjectionReconciler Reconciler,
        EditorNodeId Cube);
}
