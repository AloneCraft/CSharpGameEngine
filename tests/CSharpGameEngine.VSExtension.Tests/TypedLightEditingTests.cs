using CSharpGameEngine.Lighting;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Tests;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
namespace CSharpGameEngine.VSExtension.Tests;
public sealed class TypedLightEditingTests {
    public static IEnumerable<object[]>Conversions(){
        foreach(var Source in LightComponentContract.Types){
            foreach(var Target in LightComponentContract.Types){
                if(Source!=Target){yield return new object[]{Source,Target};}
            }
        }
    }
    [Theory]
    [MemberData(nameof(Conversions))]
    public void Conversion_PreservesIdentityShadowAndSourceRoundtrip(string SourceName,string TargetName){
        var Data=TypedLightTestScene.Analyze("new "+SourceName+"(),new LightShadow { Priority=7,Bias=new(0.02f) }");
        SceneAnalysisTestHelper.AssertNoDiagnostics(Data.Model);
        var Source=Data.Model.Root!.Components.Single(Component=>LightComponentContract.IsLight(Component.TypeFullyQualifiedName));
        var Target=Data.Compilation.GetTypeByMetadataName(TargetName.Replace("global::",string.Empty))!;
        var Inputs=LightConversionPlanner.Suggest(Source,Target,Data.Compilation);
        var Dimensional=SourceName.EndsWith(".DirectionalLight")||TargetName.EndsWith(".DirectionalLight");
        var Strength=Target.Name=="DirectionalLight"?"Illuminance":Target.Name=="RectAreaLight"?"Luminance":"Flux";
        if(Dimensional){
            Assert.Equal(string.Empty,Inputs[Strength]);
            Assert.False(LightConversionPlanner.TryBuild(Source,Target,Data.Compilation,Inputs,out _));
            Inputs[Strength]="123";
        }
        Assert.True(LightConversionPlanner.TryBuild(Source,Target,Data.Compilation,Inputs,out var Replacement),string.Join("; ",Inputs.Select(Pair=>Pair.Key+"="+Pair.Value)));
        if(!Dimensional){
            Assert.InRange(Math.Abs(Flux(WithDefaults(Source,Data.Compilation))-Flux(Replacement)),0f,0.001f);
        }
        var Reconciler=new SceneProjectionReconciler();
        var Projection=Reconciler.Reconcile(TypedLightTestScene.Key,Data.Model,null);
        var Id=Assert.Single(Projection.Nodes).Id;
        var Operation=new ConvertLightOperation(TypedLightTestScene.Key,Id,1,Source.TypeFullyQualifiedName,Source.EmitExpressionText,Replacement,LightConversionPlanner.ContractFingerprint(Source,Target));
        Assert.True(SceneModelEditApplier.TryApply(Data.Model,Projection,Reconciler,TypedLightTestScene.Key,[Operation],out var Predicted,out var Next));
        Assert.Equal(Id,Assert.Single(Next.Nodes).Id);
        Assert.Equal("Lamp",Predicted.Root!.Name);
        Assert.Same(Data.Model.Root.FindComponent(LightComponentContract.ShadowType),Predicted.Root.FindComponent(LightComponentContract.ShadowType));
        var Truth=SceneJsonRoundTrip.Reanalyze(Predicted,Data.Compilation);
        SceneAnalysisTestHelper.AssertNoDiagnostics(Truth);
        Assert.Equal(
            Predicted.Root.FindComponent(TargetName)!.Members.Select(M=>(M.MemberName,M.Value.ConstantValue)),
            Truth.Root!.FindComponent(TargetName)!.Members.Select(M=>(M.MemberName,M.Value.ConstantValue)));
        Assert.NotNull(Truth.Root.FindComponent(LightComponentContract.ShadowType));
        var Conflicting=new ConvertLightOperation(TypedLightTestScene.Key,Id,1,SourceName,"changed source",Replacement,LightConversionPlanner.ContractFingerprint(Source,Target));
        Assert.False(SceneModelEditApplier.TryApply(Data.Model,Projection,Reconciler,TypedLightTestScene.Key,[Conflicting],out _,out _));
    }
    [Theory]
    [InlineData("new(800f)",true)]
    [InlineData("new LuminousFlux(Lumens:800f)",true)]
    [InlineData("new LuminousFlux(400f+400f)",false)]
    public void PhysicalValue_UsesDeclaredConstructorAndReadOnlyConstantPolicy(string Expression,bool Editable){
        var Data=TypedLightTestScene.Analyze("new PointLight { Flux="+Expression+" }");
        SceneAnalysisTestHelper.AssertNoDiagnostics(Data.Model);
        var Value=Data.Model.Root!.Components.Last().FindMember("Flux")!.Value;
        var Contract=LightValueContract.Find(Value.Type)!;
        Assert.True(Contract.TryRead(Value,out var Numbers,out var CanEdit));
        Assert.Equal(800f,Numbers[0]);
        Assert.Equal(Editable,CanEdit);
    }
    [Fact]
    public void PhysicalEdit_UpdatesWholeValueAndPredictedModelMatchesReparse(){
        var Data=TypedLightTestScene.Analyze("new SpotLight { Cone=new(OuterFullAngleDegrees:60f,InnerFullAngleDegrees:20f) }");
        var Reconciler=new SceneProjectionReconciler();
        var Projection=Reconciler.Reconcile(TypedLightTestScene.Key,Data.Model,null);
        var Id=Assert.Single(Projection.Nodes).Id;
        var Type=LightComponentContract.Prefix+"SpotLight";
        var Operation=new SetComponentMemberOperation(TypedLightTestScene.Key,Id,1,Type,"Cone","new global::CSharpGameEngine.Lighting.SpotConeAngles(25f,70f)");
        Assert.True(SceneModelEditApplier.TryApply(Data.Model,Projection,Reconciler,TypedLightTestScene.Key,[Operation],out var Predicted,out _));
        var Truth=SceneJsonRoundTrip.Reanalyze(Predicted,Data.Compilation);
        SceneAnalysisTestHelper.AssertNoDiagnostics(Truth);
        Assert.Equal(
            SceneJsonRoundTrip.Signature(Predicted.Root!.FindComponent(Type)!.FindMember("Cone")!.Value),
            SceneJsonRoundTrip.Signature(Truth.Root!.FindComponent(Type)!.FindMember("Cone")!.Value));
        var Invalid=new SetComponentMemberOperation(TypedLightTestScene.Key,Id,1,Type,"Cone","new SpotConeAngles(90f,20f)");
        Assert.False(SceneModelEditApplier.TryApply(Data.Model,Projection,Reconciler,TypedLightTestScene.Key,[Invalid],out _,out _));
    }
    [Theory]
    [InlineData("new PointLight { Range=default }")]
    [InlineData("new PointLight { Range=new LightRange() }")]
    [InlineData("new SpotLight { Cone=new(70f,20f) }")]
    [InlineData("new PointLight(),new SpotLight()")]
    [InlineData("new LightShadow()")]
    public void SceneDiagnostics_RejectInvalidDefaultShapeAndComposition(string Expression){
        var Data=TypedLightTestScene.Analyze(Expression);
        Assert.True(Data.Model.HasErrors);
    }
    [Theory]
    [InlineData("new PointLight { Cone=new SpotConeAngles(30f,45f) }")]
    [InlineData("new DirectionalLight { Range=new LightRange(10f) }")]
    [InlineData("new PointLight { Flux=new Illuminance(800f) }")]
    public void PublicContracts_RejectInvalidPropertiesAndUnitAssignmentsAtCompileTime(string Expression){
        var Data=TypedLightTestScene.Analyze(Expression);
        Assert.NotEmpty(Data.Compilation.GetDiagnostics().Where(D=>D.Severity==DiagnosticSeverity.Error));
    }
    [Fact]
    public void RectangleGizmo_IgnoresScaleAndUpdatesSizeAtomically(){
        var Data=TypedLightTestScene.Analyze("new RectAreaLight { Size=new(2f,3f),Range=new(7f) }");
        Assert.True(PropertyGizmoBinding.TryCreate(Data.Model.Root!,System.Numerics.Matrix4x4.CreateScale(5f),1f,out var Frame));
        Assert.Equal(2f,Frame.Width);
        Assert.Equal(3f,Frame.Height);
        Assert.Equal(7f,Frame.Range);
        var Changed=Frame.ApplyPointerDelta(GizmoHandleId.RectWidth,0,-10,false);
        var Operation=Assert.IsType<SetComponentMemberOperation>(Assert.Single(PropertyGizmo.CreateOperations(Changed,GizmoHandleId.RectWidth,default,default,1,Frame)));
        Assert.Equal("Size",Operation.MemberName);
        Assert.Empty(PropertyGizmo.CreateOperations(Frame,GizmoHandleId.RectWidth,default,default,1,Frame));
    }
    [Fact]
    public void ConversionCommit_IsOneNativeWriteAndRejectsChangedContract(){
        var Data=TypedLightTestScene.Analyze("new SpotLight { Flux=new(1234f),Cone=new(20f,70f) },new LightShadow()");
        // ADR-0070: the scene is committed as its scene JSON document.
        var (Json,Context,Base)=SceneJsonRoundTrip.ToJsonScene(Data.Model,Data.Compilation);
        var Reconciler=new SceneProjectionReconciler();
        var Snapshot=SceneDocumentSnapshot.Create(TypedLightTestScene.Key,"scene",1,1,Context.Model,false,null,Reconciler);
        var Source=Context.Model.Root!.FindComponent(LightComponentContract.Prefix+"SpotLight")!;
        var Target=Context.Compilation.GetTypeByMetadataName("CSharpGameEngine.Runtime.Lighting.PointLight")!;
        Assert.True(LightConversionPlanner.TryBuild(Source,Target,Context.Compilation,LightConversionPlanner.Suggest(Source,Target,Context.Compilation),out var Replacement));
        var Id=Assert.Single(Snapshot.Projection!.Nodes).Id;
        var Operation=new ConvertLightOperation(TypedLightTestScene.Key,Id,1,Source.TypeFullyQualifiedName,Source.EmitExpressionText,Replacement,LightConversionPlanner.ContractFingerprint(Source,Target));
        SceneSourceCommitPlan Commit(ConvertLightOperation Convert)=>SceneSourceCommitPlanner.Plan(Context,Snapshot,
            new SceneEditCommitRequest(TypedLightTestScene.Key,1,"Convert light",[],SceneTransformMemberMask.None,DirectOperations:[Convert]),Reconciler,CancellationToken.None);
        var Plan=Commit(Operation);
        Assert.True(Plan.Succeeded,Plan.ErrorMessage);
        Assert.NotNull(Plan.WriteStep);
        var After=Plan.WriteStep!.Before.WithChanges(Plan.WriteStep.Changes).ToString();
        // The native writer owns this single before/after transaction; no light-owned history exists.
        Assert.Equal(Json,Plan.WriteStep.Before.ToString());
        var Undo=SceneJsonRoundTrip.Analyze(Plan.WriteStep.Before.ToString(),"MainScene","Game.MainScene",Base).Model;
        Assert.NotNull(Undo.Root!.FindComponent(Source.TypeFullyQualifiedName));
        var Redo=SceneJsonRoundTrip.Analyze(After,"MainScene","Game.MainScene",Base).Model;
        Assert.NotNull(Redo.Root!.FindComponent(Replacement.TypeFullyQualifiedName));
        Assert.Null(Redo.Root.FindComponent(Source.TypeFullyQualifiedName));
        Assert.False(Commit(Operation with { DefaultsFingerprint="obsolete semantic generation" }).Succeeded);
        Assert.False(LightConversionPlanner.TryBuild(Source,Source.TypeSymbol!,Context.Compilation,new Dictionary<string,string>(),out _));
    }
    private static SceneComponent WithDefaults(SceneComponent Source,Compilation Compilation){
        var Plan=SceneComponentInitializerPolicy.CreatePlan(Source.TypeSymbol!,Source.TypeFullyQualifiedName,Compilation);
        return new SceneComponent(Source.TypeSymbol,Source.TypeFullyQualifiedName,Source.ConstructorArguments,Plan.Members,Plan.Expression,Source.Location,Source.OrderKey);
    }
    private static float Flux(SceneComponent Component){
        float[]Read(string Name){
            var Value=Component.FindMember(Name)!.Value;
            Assert.True(LightValueContract.Find(Value.Type)!.TryRead(Value,out var Values,out _));
            return Values;
        }
        if(Component.TypeFullyQualifiedName.EndsWith(".RectAreaLight")){
            var Size=Read("Size");
            return LightPhotometry.RectangleFlux(new Luminance(Read("Luminance")[0]),new RectangleSize(Size[0],Size[1])).Lumens;
        }
        return Read("Flux")[0];
    }
}
