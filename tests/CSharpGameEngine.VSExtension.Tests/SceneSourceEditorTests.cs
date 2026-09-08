using System.Linq;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Tests;
using Microsoft.CodeAnalysis.CSharp;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>Designerからの編集がSyntaxの対象箇所だけを置換し、comment/triviaを保持することを固定する。</summary>
public sealed class SceneSourceEditorTests {
    private const string TransformTypes="""
        namespace CSharpGameEngine.Runtime.Windows {
            public struct Vector3 { public float R; public float U; public float F; public Vector3(float R,float U,float F){ this.R=R;this.U=U;this.F=F; } }
            public struct Transform {
                public Vector3 Position; public Vector3 Rotation; public Vector3 Scale;
                public float PositionX; public float PositionY; public float PositionZ;
                public float RotationX; public float RotationY; public float RotationZ; public float RotationW;
                public float ScaleX; public float ScaleY; public float ScaleZ;
            }
        }
        """;
    private const string CanonicalTransformTypes="""
        namespace CSharpGameEngine.Contracts {
            public readonly record struct ReferenceFrameId(long Value);
        }
        namespace CSharpGameEngine.Numerics {
            public struct LogicalVector3 {
                public float R; public float U; public float F;
                public LogicalVector3(float R,float U,float F){ this.R=R;this.U=U;this.F=F; }
            }
            public struct LogicalQuaternion {
                public LogicalQuaternion(LogicalVector3 EulerRuf){ }
            }
        }
        namespace CSharpGameEngine.Entities {
            public struct Transform {
                public CSharpGameEngine.Contracts.ReferenceFrameId Frame;
                public CSharpGameEngine.Numerics.LogicalVector3 Position;
                public CSharpGameEngine.Numerics.LogicalQuaternion Rotation;
                public CSharpGameEngine.Numerics.LogicalVector3 Scale;
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
        (var Projection,var Cube)=CreateProjection();
        var Result=SceneSourceEditor.Apply(
                                           CSharpSyntaxTree.ParseText(Source).GetRoot(),
                                           Projection,
                                           new SetComponentMemberOperation(Key,Cube,DocumentVersion:1,"global::Sample.DirectionalLight","Intensity","4f"));

        Assert.True(Result.Succeeded,Result.ErrorMessage);
        var Changed=Result.Root!.ToFullString();
        Assert.Contains("Intensity=4f",Changed);
        Assert.Contains("// 手書きの大事なコメント",Changed);
        Assert.Contains("Name=\"Cube\"",Changed);
    }

    [Fact]
    public void SetComponentMember_RufNestedPath_ReplacesOnlyTheSelectedVectorComponent(){
        const string TransformSource="""
            using CSharpGameEngine.Entities;
            using CSharpGameEngine.Runtime.Windows;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Children=[new Node { Name="Cube", Components=[new Transform {
                    Position=new Vector3(1f,2f,3f), Rotation=new Vector3(0f,0f,0f), Scale=new Vector3(1f,1f,1f),
                }] }] };
            }
            """;
        var Model=SceneAnalysisTestHelper.Analyze([TransformTypes,TransformSource]);
        var Projection=new SceneProjectionReconciler().Reconcile(Key,Model,Previous:null);
        var Cube=Assert.Single(Projection.Nodes,static Node=>Node.Node.Name=="Cube").Id;

        var Result=SceneSourceEditor.Apply(
            CSharpSyntaxTree.ParseText(TransformSource).GetRoot(),Projection,
            new SetComponentMemberOperation(
                Key,Cube,DocumentVersion:1,"global::CSharpGameEngine.Runtime.Windows.Transform","R","9f",
                MemberPath:["Position","R"]));

        Assert.True(Result.Succeeded,Result.ErrorMessage);
        Assert.Matches(@"Position\s*=\s*new\s+Vector3\(\s*9f\s*,\s*2f\s*,\s*3f\s*\)",Result.Root!.ToFullString());
    }
    [Fact]
    public void RenameNode_ChangesOnlyNameLiteral(){
        (var Projection,var Cube)=CreateProjection();
        var Result=SceneSourceEditor.Apply(
                                           CSharpSyntaxTree.ParseText(Source).GetRoot(),
                                           Projection,
                                           new RenameNodeOperation(Key,Cube,DocumentVersion:1,"Renamed"));

        Assert.True(Result.Succeeded,Result.ErrorMessage);
        var Changed=Result.Root!.ToFullString();
        Assert.Contains("Name=\"Renamed\"",Changed);
        Assert.Contains("Intensity=3f",Changed);
        Assert.Contains("// 手書きの大事なコメント",Changed);
    }

    [Fact]
    public void InvalidExpression_DoesNotReturnAMutatedRoot(){
        (var Projection,var Cube)=CreateProjection();
        var Result=SceneSourceEditor.Apply(
                                           CSharpSyntaxTree.ParseText(Source).GetRoot(),
                                           Projection,
                                           new SetComponentMemberOperation(Key,Cube,DocumentVersion:1,"global::Sample.DirectionalLight","Intensity","not valid +"));

        Assert.False(Result.Succeeded);
        Assert.Null(Result.Root);
    }

    [Fact]
    public void ApplyLatest_RebasesOneStaleOperationUsingStableNodeId(){
        SceneProjectionReconciler Reconciler=new();
        var FirstModel=SceneAnalysisTestHelper.Analyze(Source);
        var First=SceneDocumentSnapshot.Create(
                                               Key,"document-a",DocumentVersion:1,AnalysisGeneration:1,FirstModel,HasBlockingErrors:false,Previous:null,Reconciler);
        var LatestSource=Source.Replace("Intensity=3f","Intensity=4f");
        var LatestModel=SceneAnalysisTestHelper.Analyze(LatestSource);
        var Latest=SceneDocumentSnapshot.Create(
                                                Key,"document-a",DocumentVersion:2,AnalysisGeneration:2,LatestModel,HasBlockingErrors:false,Previous:First,Reconciler);
        var Cube=Assert.Single(Latest.Projection!.Nodes,static Node=>Node.Node.Name=="Cube").Id;

        var Result=SceneSourceEditor.ApplyLatest(
                                                 CSharpSyntaxTree.ParseText(LatestSource).GetRoot(),
                                                 Latest,
                                                 new SetComponentMemberOperation(Key,Cube,DocumentVersion:1,"global::Sample.DirectionalLight","Intensity","5f"));

        Assert.True(Result.Succeeded,Result.ErrorMessage);
        Assert.True(Result.WasRebased);
        Assert.Contains("Intensity=5f",Result.Root!.ToFullString());
    }

    [Fact]
    public void OtherSceneKey_IsRejectedBeforeAnySourceChange(){
        (var Projection,var Cube)=CreateProjection();
        var Result=SceneSourceEditor.Apply(
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
        var Model=SceneAnalysisTestHelper.Analyze([TransformTypes,TransformSource]);
        var Projection=new SceneProjectionReconciler().Reconcile(Key,Model,Previous:null);
        var Cube=Assert.Single(Projection.Nodes,static Node=>Node.Node.Name=="Cube").Id;
        SceneTransformValue Value=new(new System.Numerics.Vector3(3f,2f,0f),System.Numerics.Quaternion.Identity,System.Numerics.Vector3.One);

        var Result=SceneSourceEditor.Apply(
                                           CSharpSyntaxTree.ParseText(TransformSource).GetRoot(),Projection,
                                           new SetTransformOperation(Key,Cube,DocumentVersion:1,Value));

        Assert.True(Result.Succeeded,Result.ErrorMessage);
        var Changed=Result.Root!.ToFullString();
        Assert.Contains("Position=new global::CSharpGameEngine.Runtime.Windows.Vector3(3f,2f,0f)",Changed);
        Assert.Contains("// keep",Changed);
    }

    [Fact]
    public void SetTransform_CanonicalRotationWritesNestedQuaternionAuthoringValue(){
        const string TransformSource="""
            using CSharpGameEngine.Entities;
            using CSharpGameEngine.Contracts;
            using CSharpGameEngine.Numerics;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Children=[new Node {
                    Name="Cube",Components=[new Transform {
                        Frame=new ReferenceFrameId(42),
                        Position=new LogicalVector3(0,0,0),
                        Rotation=new LogicalQuaternion(new LogicalVector3(0,0,0)),
                        Scale=new LogicalVector3(1,1,1),
                    }],
                }] };
            }
            """;
        var Model=SceneAnalysisTestHelper.Analyze([CanonicalTransformTypes,TransformSource]);
        var Projection=new SceneProjectionReconciler().Reconcile(Key,Model,Previous:null);
        var Cube=Assert.Single(Projection.Nodes,static Node=>Node.Node.Name=="Cube").Id;
        SceneTransformValue Value=new(
            System.Numerics.Vector3.Zero,
            System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitX,(float)System.Math.PI/2f),
            System.Numerics.Vector3.One);

        var Result=SceneSourceEditor.Apply(
            CSharpSyntaxTree.ParseText(TransformSource).GetRoot(),Projection,
            new SetTransformOperation(Key,Cube,DocumentVersion:1,Value));

        Assert.True(Result.Succeeded,Result.ErrorMessage);
        string Changed=Result.Root!.ToFullString();
        Assert.Contains(
            "Rotation=new global::CSharpGameEngine.Numerics.LogicalQuaternion(new global::CSharpGameEngine.Numerics.LogicalVector3(",
            Changed);
        Assert.Contains("Frame=new ReferenceFrameId(42)",Changed);
        Assert.Equal(1,CountOccurrences(Changed,"Frame=new ReferenceFrameId(42)"));
        var Reanalyzed=SceneAnalysisTestHelper.Analyze([CanonicalTransformTypes,Changed]);
        var Node=Assert.Single(Reanalyzed.EnumerateNodes(),static Node=>Node.Name=="Cube");
        Assert.True(SceneTransformBinding.TryRead(Node,out var RoundTrip));
        Assert.True(System.Math.Abs(System.Numerics.Quaternion.Dot(Value.Rotation,RoundTrip.Rotation))>0.99999f);
    }

    private static int CountOccurrences(string Text,string Value){
        int Count=0;
        for(int Index=Text.IndexOf(Value,System.StringComparison.Ordinal);Index>=0;
            Index=Text.IndexOf(Value,Index+Value.Length,System.StringComparison.Ordinal)){
            Count++;
        }
        return Count;
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
        var Model=SceneAnalysisTestHelper.Analyze([TransformTypes,TransformSource]);
        var Projection=new SceneProjectionReconciler().Reconcile(Key,Model,Previous:null);
        var Cube=Assert.Single(Projection.Nodes,static Node=>Node.Node.Name=="Cube").Id;
        SceneTransformValue Value=new(new System.Numerics.Vector3(1f,4f,0f),System.Numerics.Quaternion.Identity,System.Numerics.Vector3.One);

        var Result=SceneSourceEditor.Apply(
                                           CSharpSyntaxTree.ParseText(TransformSource).GetRoot(),Projection,
                                           new SetTransformOperation(Key,Cube,DocumentVersion:1,Value));

        Assert.True(Result.Succeeded,Result.ErrorMessage);
        Assert.Contains("Position=new global::CSharpGameEngine.Runtime.Windows.Vector3(1f,4f,0f)",Result.Root!.ToFullString());
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
        var Model=SceneAnalysisTestHelper.Analyze([TransformTypes,TransformSource]);
        var Projection=new SceneProjectionReconciler().Reconcile(Key,Model,Previous:null);
        var Cube=Assert.Single(Projection.Nodes,static Node=>Node.Node.Name=="Cube").Id;
        SceneTransformValue Value=new(
            new System.Numerics.Vector3(4f,0f,0f),
            System.Numerics.Quaternion.Identity,
            System.Numerics.Vector3.One);

        var Result=SceneSourceEditor.Apply(
                                           CSharpSyntaxTree.ParseText(TransformSource).GetRoot(),Projection,
                                           new SetTransformOperation(
                                                                     Key,Cube,DocumentVersion:1,Value,Members:SceneTransformMemberMask.Position));

        Assert.True(Result.Succeeded,Result.ErrorMessage);
        var Changed=Result.Root!.ToFullString();
        Assert.Contains("Position=new global::CSharpGameEngine.Runtime.Windows.Vector3(4f,0f,0f)",Changed);
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
        var Model=SceneAnalysisTestHelper.Analyze([TransformTypes,TransformSource]);
        var Projection=new SceneProjectionReconciler().Reconcile(Key,Model,Previous:null);
        var Cube=Assert.Single(Projection.Nodes,static Node=>Node.Node.Name=="Cube").Id;
        SceneTransformValue Value=new(
            System.Numerics.Vector3.Zero,
            System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitZ,(float)(System.Math.PI/2d)),
            System.Numerics.Vector3.One);

        var Result=SceneSourceEditor.Apply(
                                           CSharpSyntaxTree.ParseText(TransformSource).GetRoot(),Projection,
                                           new SetTransformOperation(
                                                                     Key,Cube,DocumentVersion:1,Value,Members:SceneTransformMemberMask.Rotation));

        Assert.True(Result.Succeeded,Result.ErrorMessage);
        var Changed=Result.Root!.ToFullString();
        Assert.Contains("Rotation=new global::CSharpGameEngine.Runtime.Windows.Vector3(",Changed);
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
        var Model=SceneAnalysisTestHelper.Analyze([TransformTypes,TransformSource]);
        var Projection=new SceneProjectionReconciler().Reconcile(Key,Model,Previous:null);
        var Cube=Assert.Single(Projection.Nodes,static Node=>Node.Node.Name=="Cube").Id;
        SceneTransformValue Value=new(
            System.Numerics.Vector3.Zero,System.Numerics.Quaternion.Identity,new System.Numerics.Vector3(2f,3f,4f));

        var Result=SceneSourceEditor.Apply(
                                           CSharpSyntaxTree.ParseText(TransformSource).GetRoot(),Projection,
                                           new SetTransformOperation(
                                                                     Key,Cube,DocumentVersion:1,Value,Members:SceneTransformMemberMask.Scale));

        Assert.True(Result.Succeeded,Result.ErrorMessage);
        var Changed=Result.Root!.ToFullString();
        Assert.Contains("Scale=new global::CSharpGameEngine.Runtime.Windows.Vector3(2f,3f,4f)",Changed);
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
        var Model=SceneAnalysisTestHelper.Analyze([TransformTypes,DuplicateSource]);
        var Projection=new SceneProjectionReconciler().Reconcile(Key,Model,Previous:null);
        var Cube=Assert.Single(Projection.Nodes,static Node=>Node.Node.Name=="Cube").Id;
        SceneTransformValue Transform=new(new System.Numerics.Vector3(5f,0f,0f),
            System.Numerics.Quaternion.Identity,System.Numerics.Vector3.One);
        DuplicateNodeOperation Operation=new(Key,Cube,1,Transform,SceneTransformMemberMask.Position);

        var Result=SceneSourceEditor.Apply(
                                           CSharpSyntaxTree.ParseText(DuplicateSource).GetRoot(),Projection,Operation);

        Assert.True(Result.Succeeded,Result.ErrorMessage);
        var Changed=Result.Root!.ToFullString();
        Assert.Equal(2,System.Text.RegularExpressions.Regex.Matches(Changed,"duplicate-comment").Count);
        Assert.Contains("Cube (2)",Changed);
        Assert.Contains("PositionX=1f",Changed);
        Assert.Contains("Position=new global::CSharpGameEngine.Runtime.Windows.Vector3(5f,0f,0f)",Changed);
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
        var Model=SceneAnalysisTestHelper.Analyze([TransformTypes,RootSource]);
        var Projection=new SceneProjectionReconciler().Reconcile(Key,Model,Previous:null);
        DuplicateNodeOperation Operation=new(
            Key,Projection.Root!.Id,1,SceneTransformValue.Identity,SceneTransformMemberMask.Position);

        var Result=SceneSourceEditor.Apply(
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
        var Model=SceneAnalysisTestHelper.Analyze([TransformTypes,BatchSource]);
        var Projection=new SceneProjectionReconciler().Reconcile(Key,Model,Previous:null);
        var A=Assert.Single(Projection.Nodes,static Node=>Node.Node.Name=="A").Id;
        var B=Assert.Single(Projection.Nodes,static Node=>Node.Node.Name=="B").Id;

        var Result=SceneSourceEditor.ApplyBatch(
                                                CSharpSyntaxTree.ParseText(BatchSource).GetRoot(),Projection,
                                                [
                                                    new DuplicateNodeOperation(
                                                                               Key,A,1,SceneTransformValue.Identity,SceneTransformMemberMask.Position),
                                                    new DuplicateNodeOperation(
                                                                               Key,B,1,SceneTransformValue.Identity,SceneTransformMemberMask.Position),
                                                ]);

        Assert.True(Result.Succeeded,Result.ErrorMessage);
        var Changed=Result.Root!.ToFullString();
        var AClone=Changed.IndexOf("A (2)",System.StringComparison.Ordinal);
        var BClone=Changed.IndexOf("B (1)",System.StringComparison.Ordinal);
        Assert.True(AClone>=0);
        Assert.True(BClone>AClone);
    }

    [Fact]
    public void ApplyBatch_WhenALaterOperationFails_ReturnsNoMutatedRoot(){
        (var Projection,var Cube)=CreateProjection();
        var Result=SceneSourceEditor.ApplyBatch(
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
        var OmittedSource=Source.Replace("Intensity=3f",string.Empty);
        var Model=SceneAnalysisTestHelper.Analyze(OmittedSource);
        var Projection=new SceneProjectionReconciler().Reconcile(Key,Model,Previous:null);
        var Cube=Assert.Single(Projection.Nodes,static Node=>Node.Node.Name=="Cube").Id;
        var Result=SceneSourceEditor.Apply(
                                           CSharpSyntaxTree.ParseText(OmittedSource).GetRoot(),Projection,
                                           new SetComponentMemberOperation(
                                                                           Key,Cube,1,"global::Sample.DirectionalLight","Intensity","12f"));

        Assert.True(Result.Succeeded,Result.ErrorMessage);
        Assert.Contains("Intensity=12f",Result.Root!.ToFullString());
        Assert.Contains("// 手書きの大事なコメント",Result.Root.ToFullString());
    }

    /// <summary>
    /// M0051: <see cref="SceneComponentCatalog"/>がgeneric Component型を「閉じたinstantiation」として
    /// 出す設計の、最終消費者側での裏取り。カタログが返す完全修飾名(`Foo&lt;Bar&gt;`形式)が、
    /// 実際に<c>ApplyAddComponent</c>が受け付ける妥当なobject creation式になることを確認する —
    /// カタログ側のテストだけでは「文字列としてそれらしい」ことしか分からず、構文として本当に
    /// 通るかはここで初めて確認できる。
    /// </summary>
    [Fact]
    public void AddComponent_AcceptsAClosedGenericInstantiationFromTheCatalog(){
        (var Projection,var Cube)=CreateProjection();
        var Root=CSharpSyntaxTree.ParseText(Source).GetRoot();
        AddComponentOperation Add=new(
            Key,Cube,1,
            "global::Sample.StatusEffect<global::Sample.PoisonKind>",
            "new global::Sample.StatusEffect<global::Sample.PoisonKind>()");

        var Added=SceneSourceEditor.Apply(Root,Projection,Add);

        Assert.True(Added.Succeeded,Added.ErrorMessage);
        Assert.Contains(
            "new global::Sample.StatusEffect<global::Sample.PoisonKind>()",Added.Root!.ToFullString());
    }

    [Fact]
    public void AddAndRemoveComponent_EditOnlyTheComponentsCollection(){
        (var Projection,var Cube)=CreateProjection();
        var Root=CSharpSyntaxTree.ParseText(Source).GetRoot();
        AddComponentOperation Add=new(
            Key,Cube,1,"global::Sample.Camera","new global::Sample.Camera { NearZ=0.1f }");

        var Added=SceneSourceEditor.Apply(Root,Projection,Add);

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
        var Model=SceneAnalysisTestHelper.Analyze([
                                                      "namespace Sample { public struct Camera { public float NearZ; } }",
                                                      WithCamera,
                                                  ]);
        var Current=new SceneProjectionReconciler().Reconcile(Key,Model,Previous:null);
        var CurrentCube=Assert.Single(Current.Nodes,static Node=>Node.Node.Name=="Cube").Id;
        var Removed=SceneSourceEditor.Apply(
                                            CSharpSyntaxTree.ParseText(WithCamera).GetRoot(),Current,
                                            new RemoveComponentOperation(Key,CurrentCube,1,"global::Sample.Camera"));

        Assert.True(Removed.Succeeded,Removed.ErrorMessage);
        Assert.DoesNotContain("new Camera",Removed.Root!.ToFullString());
        Assert.Contains("DirectionalLight",Removed.Root.ToFullString());
    }

    [Fact]
    public void AddNode_AppendsAUniqueMinimalNode(){
        const string EmptySource="""
            using CSharpGameEngine.Entities;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Name="Root", Children=[] };
            }
            """;
        var Model=SceneAnalysisTestHelper.Analyze(EmptySource);
        var Projection=new SceneProjectionReconciler().Reconcile(Key,Model,Previous:null);
        var RootNode=Projection.Root!;
        var Result=SceneSourceEditor.Apply(
                                           CSharpSyntaxTree.ParseText(EmptySource).GetRoot(),Projection,
                                           new AddNodeOperation(Key,RootNode.Id,1,"Node"));

        Assert.True(Result.Succeeded,Result.ErrorMessage);
        var Text=Result.Root!.ToFullString();
        Assert.Contains("Node",Text);
        // "new"と"Node"の間にトリビアが無いと"newNode"という1トークンに読めてしまい、
        // 再解析すると追加したNodeが消える(過去に実際に踏んだ回帰)。再解析で復元できることまで確認する。
        var Reanalyzed=SceneAnalysisTestHelper.Analyze(Text);
        Assert.Contains(Reanalyzed.EnumerateNodes(),static Node=>Node.Name=="Node");
    }

    [Fact]
    public void AddNode_CreatesChildrenCollectionWhenItIsOmitted(){
        const string EmptySource="""
            using CSharpGameEngine.Entities;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Name="Root" };
            }
            """;
        var Model=SceneAnalysisTestHelper.Analyze(EmptySource);
        var Projection=new SceneProjectionReconciler().Reconcile(Key,Model,Previous:null);
        var RootNode=Projection.Root!;
        var Result=SceneSourceEditor.Apply(
                                           CSharpSyntaxTree.ParseText(EmptySource).GetRoot(),Projection,
                                           new AddNodeOperation(Key,RootNode.Id,1,"Node"));

        Assert.True(Result.Succeeded,Result.ErrorMessage);
        var Text=Result.Root!.ToFullString();
        Assert.Contains("Children",Text);
        Assert.Contains("Node",Text);
        var Reanalyzed=SceneAnalysisTestHelper.Analyze(Text);
        Assert.Contains(Reanalyzed.EnumerateNodes(),static Node=>Node.Name=="Node");
    }

    [Fact]
    public void RemoveNode_RemovesOnlyTheSelectedChild(){
        const string RemoveSource="""
            using CSharpGameEngine.Entities;
            namespace Game;
            public partial class MainScene:IScene { public Node Root=>new() { Children=[
                new Node { Name="A" },
                new Node { Name="B" },
            ]}; }
            """;
        var Model=SceneAnalysisTestHelper.Analyze(RemoveSource);
        var Projection=new SceneProjectionReconciler().Reconcile(Key,Model,Previous:null);
        var B=Assert.Single(Projection.Nodes,static Node=>Node.Node.Name=="B").Id;
        var Result=SceneSourceEditor.Apply(
                                           CSharpSyntaxTree.ParseText(RemoveSource).GetRoot(),Projection,
                                           new RemoveNodeOperation(Key,B,1));

        Assert.True(Result.Succeeded,Result.ErrorMessage);
        Assert.Contains("Name=\"A\"",Result.Root!.ToFullString());
        Assert.DoesNotContain("Name=\"B\"",Result.Root.ToFullString());
    }

    [Fact]
    public void ReorderNode_MovesTheChildToTheRequestedIndex(){
        const string ReorderSource="""
            using CSharpGameEngine.Entities;
            namespace Game;
            public partial class MainScene:IScene { public Node Root=>new() { Children=[
                new Node { Name="A" },
                new Node { Name="B" },
                new Node { Name="C" },
            ]}; }
            """;
        var Model=SceneAnalysisTestHelper.Analyze(ReorderSource);
        var Projection=new SceneProjectionReconciler().Reconcile(Key,Model,Previous:null);
        var B=Assert.Single(Projection.Nodes,static Node=>Node.Node.Name=="B").Id;
        var Result=SceneSourceEditor.Apply(
                                           CSharpSyntaxTree.ParseText(ReorderSource).GetRoot(),Projection,
                                           new ReorderNodeOperation(Key,B,1,0));

        Assert.True(Result.Succeeded,Result.ErrorMessage);
        var Changed=Result.Root!.ToFullString();
        Assert.True(Changed.IndexOf("Name=\"B\"",StringComparison.Ordinal)
            <Changed.IndexOf("Name=\"A\"",StringComparison.Ordinal));
    }

    [Fact]
    public void ReparentNode_MovesAChildIntoAnotherParent(){
        const string ReparentSource="""
            using CSharpGameEngine.Entities;
            namespace Game;
            public partial class MainScene:IScene { public Node Root=>new() { Children=[
                new Node { Name="A" },
                new Node { Name="B", Children=[] },
            ]}; }
            """;
        var Model=SceneAnalysisTestHelper.Analyze(ReparentSource);
        var Projection=new SceneProjectionReconciler().Reconcile(Key,Model,Previous:null);
        var A=Assert.Single(Projection.Nodes,static Node=>Node.Node.Name=="A").Id;
        var B=Assert.Single(Projection.Nodes,static Node=>Node.Node.Name=="B").Id;
        var Result=SceneSourceEditor.Apply(
                                           CSharpSyntaxTree.ParseText(ReparentSource).GetRoot(),Projection,
                                           new ReparentNodeOperation(Key,A,1,B,0));

        Assert.True(Result.Succeeded,Result.ErrorMessage);
        var Changed=Result.Root!.ToFullString();
        Assert.Contains("Name=\"B\", Children=[",Changed);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(Changed,"Name=\"A\""));
    }
    [Fact]
    public void ApplyBatch_ReparentsMultipleSiblingsWithoutSpanDrift(){
        const string Source="""
            using CSharpGameEngine.Entities;
            namespace Game;
            public partial class MainScene:IScene { public Node Root=>new() { Children=[
                new Node { Name="Target", Children=[] },
                new Node { Name="A" },
                new Node { Name="B" },
            ]}; }
            """;
        var Model=SceneAnalysisTestHelper.Analyze(Source);
        var Projection=new SceneProjectionReconciler().Reconcile(Key,Model,Previous:null);
        var Target=Assert.Single(Projection.Nodes,static Node=>Node.Node.Name=="Target").Id;
        var A=Assert.Single(Projection.Nodes,static Node=>Node.Node.Name=="A").Id;
        var B=Assert.Single(Projection.Nodes,static Node=>Node.Node.Name=="B").Id;
        var Result=SceneSourceEditor.ApplyBatch(
                                                CSharpSyntaxTree.ParseText(Source).GetRoot(),Projection,
                                                [
                                                    new ReparentNodeOperation(Key,A,1,Target,0),
                                                    new ReparentNodeOperation(Key,B,1,Target,1),
                                                ]);
        Assert.True(Result.Succeeded,Result.ErrorMessage);
        var Changed=Result.Root!.ToFullString();
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(Changed,"Name=\"A\""));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(Changed,"Name=\"B\""));
        Assert.True(Changed.IndexOf("Name=\"Target\", Children=[",StringComparison.Ordinal)<Changed.IndexOf("Name=\"A\"",StringComparison.Ordinal));
    }

    private static (SceneProjection Projection,EditorNodeId Cube)CreateProjection(){
        var Model=SceneAnalysisTestHelper.Analyze(Source);
        var Projection=new SceneProjectionReconciler().Reconcile(Key,Model,Previous:null);
        var Cube=Assert.Single(Projection.Nodes,static Node=>Node.Node.Name=="Cube");
        return (Projection,Cube.Id);
    }
}
