using System.Linq;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Tests;
using Microsoft.CodeAnalysis.CSharp;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>Designerからの編集がSyntaxの対象箇所だけを置換し、comment/triviaを保持することを固定する。</summary>
public sealed class SceneSourceEditorTests {
    private const string TransformTypes="""
        namespace CSharpGameEngine.Runtime.Windows {
            public struct Transform {
                public float PositionX; public float PositionY; public float PositionZ;
                public float RotationX; public float RotationY; public float RotationZ; public float RotationW;
                public float ScaleX; public float ScaleY; public float ScaleZ;
            }
        }
        """;
    private const string Source="""
        using CSharpGameEngine.Entities;
        using Sample;
        namespace Game;
        public partial class MainScene:IScene {
            public Node Root=>new() {
                Children=[
                    new Node {
                        Name="Cube", // 手書きの大事なコメント
                        Components=[new DirectionalLight { Intensity=3f }],
                    },
                ],
            };
        }
        """;

    private static readonly SceneKey Key=new("project-a","global::Game.MainScene");

    [Fact]
    public void SetComponentMember_ReplacesOnlyTheValueExpressionAndPreservesTrivia(){
        (SceneProjection Projection,EditorNodeId Cube)=CreateProjection();
        SceneSourceEditResult Result=SceneSourceEditor.Apply(
            CSharpSyntaxTree.ParseText(Source).GetRoot(),
            Projection,
            new SetComponentMemberOperation(Key,Cube,DocumentVersion:1,"global::Sample.DirectionalLight","Intensity","4f"));

        Assert.True(Result.Succeeded,Result.ErrorMessage);
        string Changed=Result.Root!.ToFullString();
        Assert.Contains("Intensity=4f",Changed);
        Assert.Contains("// 手書きの大事なコメント",Changed);
        Assert.Contains("Name=\"Cube\"",Changed);
    }

    [Fact]
    public void RenameNode_ChangesOnlyNameLiteral(){
        (SceneProjection Projection,EditorNodeId Cube)=CreateProjection();
        SceneSourceEditResult Result=SceneSourceEditor.Apply(
            CSharpSyntaxTree.ParseText(Source).GetRoot(),
            Projection,
            new RenameNodeOperation(Key,Cube,DocumentVersion:1,"Renamed"));

        Assert.True(Result.Succeeded,Result.ErrorMessage);
        string Changed=Result.Root!.ToFullString();
        Assert.Contains("Name=\"Renamed\"",Changed);
        Assert.Contains("Intensity=3f",Changed);
        Assert.Contains("// 手書きの大事なコメント",Changed);
    }

    [Fact]
    public void InvalidExpression_DoesNotReturnAMutatedRoot(){
        (SceneProjection Projection,EditorNodeId Cube)=CreateProjection();
        SceneSourceEditResult Result=SceneSourceEditor.Apply(
            CSharpSyntaxTree.ParseText(Source).GetRoot(),
            Projection,
            new SetComponentMemberOperation(Key,Cube,DocumentVersion:1,"global::Sample.DirectionalLight","Intensity","not valid +"));

        Assert.False(Result.Succeeded);
        Assert.Null(Result.Root);
    }

    [Fact]
    public void ApplyLatest_RebasesOneStaleOperationUsingStableNodeId(){
        SceneProjectionReconciler Reconciler=new();
        SceneModel FirstModel=SceneAnalysisTestHelper.Analyze(Source);
        SceneDocumentSnapshot First=SceneDocumentSnapshot.Create(
            Key,"document-a",DocumentVersion:1,AnalysisGeneration:1,FirstModel,HasBlockingErrors:false,Previous:null,Reconciler);
        string LatestSource=Source.Replace("Intensity=3f","Intensity=4f");
        SceneModel LatestModel=SceneAnalysisTestHelper.Analyze(LatestSource);
        SceneDocumentSnapshot Latest=SceneDocumentSnapshot.Create(
            Key,"document-a",DocumentVersion:2,AnalysisGeneration:2,LatestModel,HasBlockingErrors:false,Previous:First,Reconciler);
        EditorNodeId Cube=Assert.Single(Latest.Projection!.Nodes,static Node=>Node.Node.Name=="Cube").Id;

        SceneSourceEditResult Result=SceneSourceEditor.ApplyLatest(
            CSharpSyntaxTree.ParseText(LatestSource).GetRoot(),
            Latest,
            new SetComponentMemberOperation(Key,Cube,DocumentVersion:1,"global::Sample.DirectionalLight","Intensity","5f"));

        Assert.True(Result.Succeeded,Result.ErrorMessage);
        Assert.True(Result.WasRebased);
        Assert.Contains("Intensity=5f",Result.Root!.ToFullString());
    }

    [Fact]
    public void OtherSceneKey_IsRejectedBeforeAnySourceChange(){
        (SceneProjection Projection,EditorNodeId Cube)=CreateProjection();
        SceneSourceEditResult Result=SceneSourceEditor.Apply(
            CSharpSyntaxTree.ParseText(Source).GetRoot(),
            Projection,
            new RenameNodeOperation(new SceneKey("project-b","global::Game.MainScene"),Cube,DocumentVersion:1,"Renamed"));

        Assert.False(Result.Succeeded);
        Assert.Null(Result.Root);
    }

    [Fact]
    public void SetTransform_ReplacesOnlyChangedMembersAndPreservesComments(){
        const string TransformSource="""
            using CSharpGameEngine.Entities;
            using CSharpGameEngine.Runtime.Windows;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Children=[new Node {
                    Name="Cube", // keep
                    Components=[new Transform { PositionX=1f,PositionY=2f,RotationW=1f,ScaleX=1f,ScaleY=1f,ScaleZ=1f }],
                }] };
            }
            """;
        SceneModel Model=SceneAnalysisTestHelper.Analyze([TransformTypes,TransformSource]);
        SceneProjection Projection=new SceneProjectionReconciler().Reconcile(Key,Model,Previous:null);
        EditorNodeId Cube=Assert.Single(Projection.Nodes,static Node=>Node.Node.Name=="Cube").Id;
        SceneTransformValue Value=new(new System.Numerics.Vector3(3f,2f,0f),System.Numerics.Quaternion.Identity,System.Numerics.Vector3.One);

        SceneSourceEditResult Result=SceneSourceEditor.Apply(
            CSharpSyntaxTree.ParseText(TransformSource).GetRoot(),Projection,
            new SetTransformOperation(Key,Cube,DocumentVersion:1,Value));

        Assert.True(Result.Succeeded,Result.ErrorMessage);
        string Changed=Result.Root!.ToFullString();
        Assert.Contains("PositionX=3f",Changed);
        Assert.Contains("PositionY=2f",Changed);
        Assert.Contains("// keep",Changed);
    }

    [Fact]
    public void SetTransform_MissingMember_AppendsItToTheTransformInitializer(){
        const string TransformSource="""
            using CSharpGameEngine.Entities;
            using CSharpGameEngine.Runtime.Windows;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Children=[new Node {
                    Name="Cube",Components=[new Transform { PositionX=1f }],
                }] };
            }
            """;
        SceneModel Model=SceneAnalysisTestHelper.Analyze([TransformTypes,TransformSource]);
        SceneProjection Projection=new SceneProjectionReconciler().Reconcile(Key,Model,Previous:null);
        EditorNodeId Cube=Assert.Single(Projection.Nodes,static Node=>Node.Node.Name=="Cube").Id;
        SceneTransformValue Value=new(new System.Numerics.Vector3(1f,4f,0f),System.Numerics.Quaternion.Identity,System.Numerics.Vector3.One);

        SceneSourceEditResult Result=SceneSourceEditor.Apply(
            CSharpSyntaxTree.ParseText(TransformSource).GetRoot(),Projection,
            new SetTransformOperation(Key,Cube,DocumentVersion:1,Value));

        Assert.True(Result.Succeeded,Result.ErrorMessage);
        Assert.Contains("PositionY=4f",Result.Root!.ToFullString());
    }

    [Fact]
    public void SetTransform_PositionMask_DoesNotRewriteRotationOrScale(){
        const string TransformSource="""
            using CSharpGameEngine.Entities;
            using CSharpGameEngine.Runtime.Windows;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Children=[new Node {
                    Name="Cube",Components=[new Transform {
                        PositionX=1f,RotationW=2f,ScaleX=3f,ScaleY=3f,ScaleZ=3f,
                    }],
                }] };
            }
            """;
        SceneModel Model=SceneAnalysisTestHelper.Analyze([TransformTypes,TransformSource]);
        SceneProjection Projection=new SceneProjectionReconciler().Reconcile(Key,Model,Previous:null);
        EditorNodeId Cube=Assert.Single(Projection.Nodes,static Node=>Node.Node.Name=="Cube").Id;
        SceneTransformValue Value=new(
            new System.Numerics.Vector3(4f,0f,0f),
            System.Numerics.Quaternion.Identity,
            System.Numerics.Vector3.One);

        SceneSourceEditResult Result=SceneSourceEditor.Apply(
            CSharpSyntaxTree.ParseText(TransformSource).GetRoot(),Projection,
            new SetTransformOperation(
                Key,Cube,DocumentVersion:1,Value,Members:SceneTransformMemberMask.Position));

        Assert.True(Result.Succeeded,Result.ErrorMessage);
        string Changed=Result.Root!.ToFullString();
        Assert.Contains("PositionX=4f",Changed);
        Assert.Contains("RotationW=2f",Changed);
        Assert.Contains("ScaleX=3f",Changed);
        Assert.DoesNotContain("RotationW=1f",Changed);
        Assert.DoesNotContain("ScaleX=1f",Changed);
    }

    [Fact]
    public void SetTransform_RotationMask_DoesNotRewriteScale(){
        const string TransformSource="""
            using CSharpGameEngine.Entities;
            using CSharpGameEngine.Runtime.Windows;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Children=[new Node {
                    Name="Cube",Components=[new Transform { RotationW=1f,ScaleX=3f,ScaleY=3f,ScaleZ=3f }],
                }] };
            }
            """;
        SceneModel Model=SceneAnalysisTestHelper.Analyze([TransformTypes,TransformSource]);
        SceneProjection Projection=new SceneProjectionReconciler().Reconcile(Key,Model,Previous:null);
        EditorNodeId Cube=Assert.Single(Projection.Nodes,static Node=>Node.Node.Name=="Cube").Id;
        SceneTransformValue Value=new(
            System.Numerics.Vector3.Zero,
            System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitZ,(float)(System.Math.PI/2d)),
            System.Numerics.Vector3.One);

        SceneSourceEditResult Result=SceneSourceEditor.Apply(
            CSharpSyntaxTree.ParseText(TransformSource).GetRoot(),Projection,
            new SetTransformOperation(
                Key,Cube,DocumentVersion:1,Value,Members:SceneTransformMemberMask.Rotation));

        Assert.True(Result.Succeeded,Result.ErrorMessage);
        string Changed=Result.Root!.ToFullString();
        Assert.Contains("RotationZ=",Changed);
        Assert.Contains("ScaleX=3f",Changed);
        Assert.DoesNotContain("ScaleX=1f",Changed);
    }

    [Fact]
    public void SetTransform_ScaleMask_DoesNotRewriteRotation(){
        const string TransformSource="""
            using CSharpGameEngine.Entities;
            using CSharpGameEngine.Runtime.Windows;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Children=[new Node {
                    Name="Cube",Components=[new Transform { RotationW=2f,ScaleX=1f,ScaleY=1f,ScaleZ=1f }],
                }] };
            }
            """;
        SceneModel Model=SceneAnalysisTestHelper.Analyze([TransformTypes,TransformSource]);
        SceneProjection Projection=new SceneProjectionReconciler().Reconcile(Key,Model,Previous:null);
        EditorNodeId Cube=Assert.Single(Projection.Nodes,static Node=>Node.Node.Name=="Cube").Id;
        SceneTransformValue Value=new(
            System.Numerics.Vector3.Zero,System.Numerics.Quaternion.Identity,new System.Numerics.Vector3(2f,3f,4f));

        SceneSourceEditResult Result=SceneSourceEditor.Apply(
            CSharpSyntaxTree.ParseText(TransformSource).GetRoot(),Projection,
            new SetTransformOperation(
                Key,Cube,DocumentVersion:1,Value,Members:SceneTransformMemberMask.Scale));

        Assert.True(Result.Succeeded,Result.ErrorMessage);
        string Changed=Result.Root!.ToFullString();
        Assert.Contains("ScaleX=2f",Changed);
        Assert.Contains("ScaleY=3f",Changed);
        Assert.Contains("ScaleZ=4f",Changed);
        Assert.Contains("RotationW=2f",Changed);
        Assert.DoesNotContain("RotationW=1f",Changed);
    }

    [Fact]
    public void DuplicateNode_ClonesTrivia_AppendsUniqueName_AndTransformsCloneOnly(){
        const string DuplicateSource="""
            using CSharpGameEngine.Entities;
            using CSharpGameEngine.Runtime.Windows;
            namespace Game;
            public partial class MainScene:IScene { public Node Root=>new() { Children=[
                // duplicate-comment
                new Node { Name="Cube",Components=[new Transform { PositionX=1f }] },
                new Node { Name="Cube (1)",Components=[new Transform()] },
            ]}; }
            """;
        SceneModel Model=SceneAnalysisTestHelper.Analyze([TransformTypes,DuplicateSource]);
        SceneProjection Projection=new SceneProjectionReconciler().Reconcile(Key,Model,Previous:null);
        EditorNodeId Cube=Assert.Single(Projection.Nodes,static Node=>Node.Node.Name=="Cube").Id;
        SceneTransformValue Transform=new(new System.Numerics.Vector3(5f,0f,0f),
            System.Numerics.Quaternion.Identity,System.Numerics.Vector3.One);
        DuplicateNodeOperation Operation=new(Key,Cube,1,Transform,SceneTransformMemberMask.Position);

        SceneSourceEditResult Result=SceneSourceEditor.Apply(
            CSharpSyntaxTree.ParseText(DuplicateSource).GetRoot(),Projection,Operation);

        Assert.True(Result.Succeeded,Result.ErrorMessage);
        string Changed=Result.Root!.ToFullString();
        Assert.Equal(2,System.Text.RegularExpressions.Regex.Matches(Changed,"duplicate-comment").Count);
        Assert.Contains("Cube (2)",Changed);
        Assert.Contains("PositionX=1f",Changed);
        Assert.Contains("PositionX=5f",Changed);
    }

    [Fact]
    public void DuplicateNode_RootWithoutSiblingCollection_FailsWithoutMutation(){
        const string RootSource="""
            using CSharpGameEngine.Entities;
            using CSharpGameEngine.Runtime.Windows;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Name="Root",Components=[new Transform()] };
            }
            """;
        SceneModel Model=SceneAnalysisTestHelper.Analyze([TransformTypes,RootSource]);
        SceneProjection Projection=new SceneProjectionReconciler().Reconcile(Key,Model,Previous:null);
        DuplicateNodeOperation Operation=new(
            Key,Projection.Root!.Id,1,SceneTransformValue.Identity,SceneTransformMemberMask.Position);

        SceneSourceEditResult Result=SceneSourceEditor.Apply(
            CSharpSyntaxTree.ParseText(RootSource).GetRoot(),Projection,Operation);

        Assert.False(Result.Succeeded);
        Assert.Null(Result.Root);
    }

    [Fact]
    public void DuplicateNodeBatch_AppendsSameParentClonesInRequestedPathOrder(){
        const string BatchSource="""
            using CSharpGameEngine.Entities;
            using CSharpGameEngine.Runtime.Windows;
            namespace Game;
            public partial class MainScene:IScene { public Node Root=>new() { Children=[
                new Node { Name="A",Components=[new Transform()] },
                new Node { Name="A (1)",Components=[new Transform()] },
                new Node { Name="B",Components=[new Transform()] },
            ]}; }
            """;
        SceneModel Model=SceneAnalysisTestHelper.Analyze([TransformTypes,BatchSource]);
        SceneProjection Projection=new SceneProjectionReconciler().Reconcile(Key,Model,Previous:null);
        EditorNodeId A=Assert.Single(Projection.Nodes,static Node=>Node.Node.Name=="A").Id;
        EditorNodeId B=Assert.Single(Projection.Nodes,static Node=>Node.Node.Name=="B").Id;

        SceneSourceEditResult Result=SceneSourceEditor.ApplyBatch(
            CSharpSyntaxTree.ParseText(BatchSource).GetRoot(),Projection,
            [
                new DuplicateNodeOperation(
                    Key,A,1,SceneTransformValue.Identity,SceneTransformMemberMask.Position),
                new DuplicateNodeOperation(
                    Key,B,1,SceneTransformValue.Identity,SceneTransformMemberMask.Position),
            ]);

        Assert.True(Result.Succeeded,Result.ErrorMessage);
        string Changed=Result.Root!.ToFullString();
        int AClone=Changed.IndexOf("A (2)",System.StringComparison.Ordinal);
        int BClone=Changed.IndexOf("B (1)",System.StringComparison.Ordinal);
        Assert.True(AClone>=0);
        Assert.True(BClone>AClone);
    }

    [Fact]
    public void ApplyBatch_WhenALaterOperationFails_ReturnsNoMutatedRoot(){
        (SceneProjection Projection,EditorNodeId Cube)=CreateProjection();
        SceneSourceEditResult Result=SceneSourceEditor.ApplyBatch(
            CSharpSyntaxTree.ParseText(Source).GetRoot(),Projection,
            [
                new RenameNodeOperation(Key,Cube,DocumentVersion:1,"Renamed"),
                new SetComponentMemberOperation(Key,Cube,DocumentVersion:1,"global::Sample.DirectionalLight","Missing","4f"),
            ]);

        Assert.False(Result.Succeeded);
        Assert.Null(Result.Root);
    }

    [Fact]
    public void SetComponentMember_InsertsAnOmittedMemberIntoTheExistingInitializer(){
        string OmittedSource=Source.Replace("Intensity=3f",string.Empty);
        SceneModel Model=SceneAnalysisTestHelper.Analyze(OmittedSource);
        SceneProjection Projection=new SceneProjectionReconciler().Reconcile(Key,Model,Previous:null);
        EditorNodeId Cube=Assert.Single(Projection.Nodes,static Node=>Node.Node.Name=="Cube").Id;
        SceneSourceEditResult Result=SceneSourceEditor.Apply(
            CSharpSyntaxTree.ParseText(OmittedSource).GetRoot(),Projection,
            new SetComponentMemberOperation(
                Key,Cube,1,"global::Sample.DirectionalLight","Intensity","12f"));

        Assert.True(Result.Succeeded,Result.ErrorMessage);
        Assert.Contains("Intensity=12f",Result.Root!.ToFullString());
        Assert.Contains("// 手書きの大事なコメント",Result.Root.ToFullString());
    }

    [Fact]
    public void AddAndRemoveComponent_EditOnlyTheComponentsCollection(){
        (SceneProjection Projection,EditorNodeId Cube)=CreateProjection();
        var Root=CSharpSyntaxTree.ParseText(Source).GetRoot();
        AddComponentOperation Add=new(
            Key,Cube,1,"global::Sample.Camera","new global::Sample.Camera { NearZ=0.1f }");

        SceneSourceEditResult Added=SceneSourceEditor.Apply(Root,Projection,Add);

        Assert.True(Added.Succeeded,Added.ErrorMessage);
        Assert.Contains("global::Sample.Camera",Added.Root!.ToFullString());

        const string WithCamera="""
            using CSharpGameEngine.Entities;
            using Sample;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Children=[new Node {
                    Name="Cube", Components=[
                        new DirectionalLight { Intensity=3f },
                        new Camera { NearZ=0.1f },
                    ],
                }] };
            }
            """;
        SceneModel Model=SceneAnalysisTestHelper.Analyze([
            "namespace Sample { public struct Camera { public float NearZ; } }",
            WithCamera,
        ]);
        SceneProjection Current=new SceneProjectionReconciler().Reconcile(Key,Model,Previous:null);
        EditorNodeId CurrentCube=Assert.Single(Current.Nodes,static Node=>Node.Node.Name=="Cube").Id;
        SceneSourceEditResult Removed=SceneSourceEditor.Apply(
            CSharpSyntaxTree.ParseText(WithCamera).GetRoot(),Current,
            new RemoveComponentOperation(Key,CurrentCube,1,"global::Sample.Camera"));

        Assert.True(Removed.Succeeded,Removed.ErrorMessage);
        Assert.DoesNotContain("new Camera",Removed.Root!.ToFullString());
        Assert.Contains("DirectionalLight",Removed.Root.ToFullString());
    }

    private static (SceneProjection Projection,EditorNodeId Cube)CreateProjection(){
        SceneModel Model=SceneAnalysisTestHelper.Analyze(Source);
        SceneProjection Projection=new SceneProjectionReconciler().Reconcile(Key,Model,Previous:null);
        SceneProjectionNode Cube=Assert.Single(Projection.Nodes,static Node=>Node.Node.Name=="Cube");
        return (Projection,Cube.Id);
    }
}
