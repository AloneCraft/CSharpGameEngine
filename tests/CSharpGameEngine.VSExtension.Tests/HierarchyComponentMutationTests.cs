using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Tests;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// Regression coverage for component drops that mutate the hierarchy. The predicted scene model follows
/// the first-wins ordering contract, and the scene JSON written from it reads back the same (ADR-0070).
/// </summary>
[Collection(VsixLocalizationTestCollection.Name)]
public sealed class HierarchyComponentMutationTests {
    private const string TransformTypes="""
        namespace CSharpGameEngine.Entities {
            public sealed class SceneComponentOrderAttribute(int Category,int Order):System.Attribute { }
        }
        namespace CSharpGameEngine.Runtime.Windows {
            [CSharpGameEngine.Entities.SceneComponentOrder(0,0)]
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
        CultureInfo Original=VsixLocalization.UICulture;
        try {
            VsixLocalization.Capture(CultureInfo.GetCultureInfo("ja-JP"));
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
        } finally {
            VsixLocalization.Capture(Original);
        }
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
        const string RufTransformExpression="new global::CSharpGameEngine.Entities.Transform { Position=new global::CSharpGameEngine.Numerics.LogicalVector3(0, 0, 0), Rotation=new global::CSharpGameEngine.Numerics.LogicalVector3(0, 0, 0), Scale=new global::CSharpGameEngine.Numerics.LogicalVector3(1, 1, 1) }";

        string Downward=CSharpGameEngineViewerPackage.CreateInitialTransformExpression(
            RufTransformExpression,FacesDown:true);
        string Point=CSharpGameEngineViewerPackage.CreateInitialTransformExpression(
            RufTransformExpression,FacesDown:false);

        Assert.Contains("Rotation=new global::CSharpGameEngine.Numerics.LogicalVector3(1.5707964f, 0, 0)",Downward);
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
        var Compilation=SceneAnalysisTestHelper.CreateCompilation(TransformTypes,LightTypes,Source);
        var Model=SceneAnalysisTestHelper.Analyze([TransformTypes,LightTypes,Source]);
        var Reconciler=new SceneProjectionReconciler();
        var Projection=Reconciler.Reconcile(Key,Model,Previous:null);
        var Empty=Assert.Single(Projection.Nodes,static Node=>Node.Node.Name=="Empty").Id;

        Assert.True(SceneModelEditApplier.TryApply(Model,Projection,Reconciler,Key,
            [new AddComponentOperation(Key,Empty,DocumentVersion:1,"global::Sample.DirectionalLight","new global::Sample.DirectionalLight()")],
            out var Predicted,out _));

        var Truth=SceneJsonRoundTrip.Reanalyze(Predicted,Compilation);
        Assert.NotNull(Assert.Single(Truth.EnumerateNodes(),static Node=>Node.Name=="Empty").FindComponent("global::Sample.DirectionalLight"));
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
        var Compilation=SceneAnalysisTestHelper.CreateCompilation(TransformTypes,LightTypes,Source);
        var Model=SceneAnalysisTestHelper.Analyze([TransformTypes,LightTypes,Source]);
        var Reconciler=new SceneProjectionReconciler();
        var Projection=Reconciler.Reconcile(Key,Model,Previous:null);
        ImmutableArray<NewNodeComponent> Components=ImmutableArray.Create(
            new NewNodeComponent(
                "global::Sample.DirectionalLight","new global::Sample.DirectionalLight()"),
            new NewNodeComponent(
                "global::CSharpGameEngine.Runtime.Windows.Transform",
                "new global::CSharpGameEngine.Runtime.Windows.Transform()"),
            new NewNodeComponent(
                "global::Sample.DirectionalLight","new global::Sample.DirectionalLight()"));

        Assert.True(SceneModelEditApplier.TryApply(Model,Projection,Reconciler,Key,
            [new AddNodeOperation(Key,Projection.Root!.Id,DocumentVersion:1,"Lamp",Components)],
            out var Predicted,out _));

        var Lamp=Assert.Single(SceneJsonRoundTrip.Reanalyze(Predicted,Compilation).EnumerateNodes(),static Node=>Node.Name=="Lamp");
        Assert.Equal(
            ["global::CSharpGameEngine.Runtime.Windows.Transform","global::Sample.DirectionalLight"],
            Lamp.Components.Select(static Component=>Component.TypeFullyQualifiedName));
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
                    Components=[new Second.Shared(),new First.Shared()],
                }] };
            }
            """;
        var Compilation=SceneAnalysisTestHelper.CreateCompilation(TypeDefinitions,Source);
        var Model=SceneAnalysisTestHelper.Analyze([TypeDefinitions,Source]);
        var Reconciler=new SceneProjectionReconciler();
        var Projection=Reconciler.Reconcile(Key,Model,Previous:null);
        var Cube=Assert.Single(Projection.Nodes,static Node=>Node.Node.Name=="Cube").Id;

        Assert.True(SceneModelEditApplier.TryApply(Model,Projection,Reconciler,Key,
            [new NormalizeComponentsOperation(Key,Cube,DocumentVersion:1)],
            out var Predicted,out _));

        string[]Expected=["global::First.Shared","global::Second.Shared"];
        Assert.Equal(Expected,Assert.Single(Predicted.EnumerateNodes(),static Node=>Node.Name=="Cube").Components.Select(static Component=>Component.TypeFullyQualifiedName));
        Assert.Equal(Expected,Assert.Single(SceneJsonRoundTrip.Reanalyze(Predicted,Compilation).EnumerateNodes(),static Node=>Node.Name=="Cube").Components.Select(static Component=>Component.TypeFullyQualifiedName));
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
