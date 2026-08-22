using System;
using System.Collections.Immutable;
using System.Linq;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Tests;
using Microsoft.CodeAnalysis.CSharp;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// Regression coverage for component drops that mutate hierarchy source. These tests keep the
/// source writer and its predicted scene model on the same first-wins ordering contract.
/// </summary>
public sealed class HierarchyComponentMutationTests {
    private const string TransformTypes="""
        namespace CSharpGameEngine.Runtime.Windows {
            public struct Transform {
                public float PositionX; public float PositionY; public float PositionZ;
                public float RotationX; public float RotationY; public float RotationZ; public float RotationW;
                public float ScaleX; public float ScaleY; public float ScaleZ;
            }
        }
        """;

    private static readonly SceneKey Key=new("project-a","global::Game.MainScene");

    [Fact]
    public void ComponentDropPolicy_BlankHierarchyCreatesARootChildAndDuplicateExplainsWhyItCannotDrop(){
        var BlankHierarchy=ComponentDropPolicy.Evaluate(
            HasScene:true,TargetResolved:false,TargetHasComponentsList:false,TargetAlreadyHasType:false,
            CanCreateRootChild:true);
        var Duplicate=ComponentDropPolicy.Evaluate(
            HasScene:true,TargetResolved:true,TargetHasComponentsList:true,TargetAlreadyHasType:true);

        Assert.Equal(ComponentDropVerdict.AcceptCreateRootChild,BlankHierarchy);
        Assert.True(ComponentDropPolicy.Accepts(BlankHierarchy));
        Assert.Equal(ComponentDropVerdict.RejectDuplicate,Duplicate);
        var Message=ComponentDropPolicy.Describe(Duplicate,"PointLight");
        Assert.Contains("PointLight",Message);
        Assert.Contains("\u65E2\u306B",Message);
    }

    [Fact]
    public void DownwardLightTransform_UsesAQuarterTurnWhenTheFixedTransformUsesItsIdentityConstant(){
        const string FixedTransformExpression="new global::CSharpGameEngine.Runtime.Windows.Transform { RotationX=default, RotationY=default, RotationZ=default, RotationW=FixedRotationScalar.One, ScaleX=FixedScalar.One }";

        string Downward=CSharpGameEngineViewerPackage.CreateInitialTransformExpression(
            FixedTransformExpression,FacesDown:true);
        string Point=CSharpGameEngineViewerPackage.CreateInitialTransformExpression(
            FixedTransformExpression,FacesDown:false);

        Assert.Contains("RotationX=0.70710678f",Downward);
        Assert.Contains("RotationW=0.70710678f",Downward);
        Assert.Equal(FixedTransformExpression,Point);
    }

    [Fact]
    public void DownwardLightTransform_UsesAPositiveRQuarterTurnForRufEulerRotation(){
        const string RufTransformExpression="new global::CSharpGameEngine.Entities.Transform { Position=new global::CSharpGameEngine.Numerics.Logical256.Vector3(0, 0, 0), Rotation=new global::CSharpGameEngine.Numerics.Logical256.Vector3(0, 0, 0), Scale=new global::CSharpGameEngine.Numerics.Logical256.Vector3(1, 1, 1) }";

        string Downward=CSharpGameEngineViewerPackage.CreateInitialTransformExpression(
            RufTransformExpression,FacesDown:true);
        string Point=CSharpGameEngineViewerPackage.CreateInitialTransformExpression(
            RufTransformExpression,FacesDown:false);

        Assert.Contains("Rotation=new global::CSharpGameEngine.Numerics.Logical256.Vector3(1.5707964f, 0, 0)",Downward);
        Assert.Equal(RufTransformExpression,Point);
    }
    [Fact]
    public void AddComponent_CreatesAnOmittedComponentsCollectionOnTheTargetNode(){
        const string LightTypes="""
            namespace Sample { public struct DirectionalLight { } }
            """;

        const string Source="""
            using CSharpGameEngine.Entities;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Children=[new Node { Name="Empty" }] };
            }
            """;
        var Model=SceneAnalysisTestHelper.Analyze([TransformTypes,LightTypes,Source]);
        var Projection=new SceneProjectionReconciler().Reconcile(Key,Model,Previous:null);
        var Empty=Assert.Single(Projection.Nodes,static Node=>Node.Node.Name=="Empty").Id;

        var Result=SceneSourceEditor.Apply(
            CSharpSyntaxTree.ParseText(Source).GetRoot(),Projection,
            new AddComponentOperation(
                Key,Empty,DocumentVersion:1,"global::Sample.DirectionalLight",
                "new global::Sample.DirectionalLight()"));

        Assert.True(Result.Succeeded,Result.ErrorMessage);
        var Text=Result.Root!.ToFullString();
        Assert.Contains("Components",Text);
        Assert.Contains("new global::Sample.DirectionalLight()",Text);
    }

    [Fact]
    public void AddNode_WithInitialComponents_IsAtomicAndNormalizesFirstDeclarations(){
        const string LightTypes="""
            namespace Sample { public struct DirectionalLight { } }
            """;
        const string Source="""
            using CSharpGameEngine.Entities;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Name="Root", Children=[] };
            }
            """;
        var Model=SceneAnalysisTestHelper.Analyze([TransformTypes,LightTypes,Source]);
        var Projection=new SceneProjectionReconciler().Reconcile(Key,Model,Previous:null);
        ImmutableArray<NewNodeComponent> Components=ImmutableArray.Create(
            new NewNodeComponent(
                "global::Sample.DirectionalLight","new global::Sample.DirectionalLight()"),
            new NewNodeComponent(
                "global::CSharpGameEngine.Runtime.Windows.Transform",
                "new global::CSharpGameEngine.Runtime.Windows.Transform()"),
            new NewNodeComponent(
                "global::Sample.DirectionalLight","new global::Sample.DirectionalLight()"));

        var Result=SceneSourceEditor.Apply(
            CSharpSyntaxTree.ParseText(Source).GetRoot(),Projection,
            new AddNodeOperation(Key,Projection.Root!.Id,DocumentVersion:1,"Lamp",Components));

        Assert.True(Result.Succeeded,Result.ErrorMessage);
        var Text=Result.Root!.ToFullString();
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(
            Text,"new global::Sample.DirectionalLight")
            .Cast<System.Text.RegularExpressions.Match>());
        Assert.True(Text.IndexOf("new global::CSharpGameEngine.Runtime.Windows.Transform",StringComparison.Ordinal)
            <Text.IndexOf("new global::Sample.DirectionalLight",StringComparison.Ordinal));
        var Reanalyzed=SceneAnalysisTestHelper.Analyze([TransformTypes,LightTypes,Text]);
        var Lamp=Assert.Single(Reanalyzed.EnumerateNodes(),static Node=>Node.Name=="Lamp");
        Assert.Collection(Lamp.Components,
            static _=>{},
            static _=>{});
    }

    [Fact]
    public void AddNode_WithAnInvalidInitialComponent_DoesNotMutateTheParentSource(){
        const string Source="""
            using CSharpGameEngine.Entities;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Name="Root", Children=[] };
            }
            """;
        var Model=SceneAnalysisTestHelper.Analyze(Source);
        var Projection=new SceneProjectionReconciler().Reconcile(Key,Model,Previous:null);
        ImmutableArray<NewNodeComponent> Components=ImmutableArray.Create(
            new NewNodeComponent("global::Sample.DirectionalLight","not valid +"),
            new NewNodeComponent(
                "global::CSharpGameEngine.Runtime.Windows.Transform",
                "new global::CSharpGameEngine.Runtime.Windows.Transform()"));

        var Result=SceneSourceEditor.Apply(
            CSharpSyntaxTree.ParseText(Source).GetRoot(),Projection,
            new AddNodeOperation(Key,Projection.Root!.Id,DocumentVersion:1,"Lamp",Components));

        Assert.False(Result.Succeeded);
        Assert.Null(Result.Root);
    }

    [Fact]
    public void NormalizeComponents_UsesFullyQualifiedIdentityAndFirstWins(){
        const string TypeDefinitions="""
            namespace First { public struct Shared { } }
            namespace Second { public struct Shared { } }
            """;
        const string Source="""
            using CSharpGameEngine.Entities;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Children=[new Node {
                    Name="Cube",
                    Components=[new Second.Shared(),new First.Shared(),new Second.Shared()],
                }] };
            }
            """;
        var Model=SceneAnalysisTestHelper.Analyze([TypeDefinitions,Source]);
        var Projection=new SceneProjectionReconciler().Reconcile(Key,Model,Previous:null);
        var Cube=Assert.Single(Projection.Nodes,static Node=>Node.Node.Name=="Cube").Id;

        var Result=SceneSourceEditor.Apply(
            CSharpSyntaxTree.ParseText(Source).GetRoot(),Projection,
            new NormalizeComponentsOperation(Key,Cube,DocumentVersion:1));

        Assert.True(Result.Succeeded,Result.ErrorMessage);
        var Text=Result.Root!.ToFullString();
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(Text,"new First.Shared")
            .Cast<System.Text.RegularExpressions.Match>());
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(Text,"new Second.Shared")
            .Cast<System.Text.RegularExpressions.Match>());
        Assert.True(Text.IndexOf("new First.Shared",StringComparison.Ordinal)
            <Text.IndexOf("new Second.Shared",StringComparison.Ordinal));
    }

    [Fact]
    public void ModelApply_AddNode_InitialComponentsAreNormalizedInThePredictedModel(){
        const string Source="""
            using CSharpGameEngine.Entities;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Name="Root", Children=[] };
            }
            """;
        var Base=SceneAnalysisTestHelper.Analyze([TransformTypes,Source]);
        var Reconciler=new SceneProjectionReconciler();
        var Projection=Reconciler.Reconcile(Key,Base,Previous:null);
        var Components=ImmutableArray.Create(
            new NewNodeComponent(
                "global::Sample.DirectionalLight","new global::Sample.DirectionalLight()"),
            new NewNodeComponent(
                "global::CSharpGameEngine.Runtime.Windows.Transform",
                "new global::CSharpGameEngine.Runtime.Windows.Transform()"),
            new NewNodeComponent(
                "global::Sample.DirectionalLight","new global::Sample.DirectionalLight()"));

        Assert.True(SceneModelEditApplier.TryApply(
            Base,Projection,Reconciler,Key,
            [new AddNodeOperation(Key,Projection.Root!.Id,DocumentVersion:1,"Lamp",Components)],
            out var Predicted,out var PredictedProjection));

        var Lamp=Assert.Single(PredictedProjection.Nodes,static Node=>Node.Node.Name=="Lamp").Node;
        Assert.Equal(
            ["global::CSharpGameEngine.Runtime.Windows.Transform","global::Sample.DirectionalLight"],
            Lamp.Components.Select(static Component=>Component.TypeFullyQualifiedName));
        Assert.NotNull(Lamp.ComponentsListLocation);
        Assert.Single(Predicted.Root!.Children);
    }
}
