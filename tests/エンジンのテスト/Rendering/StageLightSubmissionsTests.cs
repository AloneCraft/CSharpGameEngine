using Xunit;
using System.Numerics;
using CSharpGameEngine.Rendering.Pipeline;
namespace CSharpGameEngine.Rendering.Tests;
/// <summary>L4 の光源の提出と持続の規則（M0120 単位 5b）。L0 から移した規則を、GPU を使わずに確かめる。</summary>
public sealed class StageLightSubmissionsTests {
    private static readonly Matrix4x4 ViewProjection=Matrix4x4.CreateLookAt(new Vector3(0f,0f,10f),Vector3.Zero,Vector3.UnitY)
        *Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI/3f,2f,0.1f,1000f);
    [Fact]
    public void NothingIsLitUntilALightIsSubmitted(){
        StageLightSubmissions Lights=new();
        Lights.PlanDirectional();
        Assert.False(Lights.DirectionalsPlanned);
        Assert.Equal(0,Lights.DirectionalLightCount);
        Assert.Equal(-1,Lights.DirectionalShadowIndex);
    }
    [Fact]
    public void TheSingleDirectionalSetterPersistsAcrossFramesUntilReplaced(){
        StageLightSubmissions Lights=new();
        Lights.SetDirectionalLight(Vector3.UnitY,Vector3.One,2f);
        for(var Frame=0;Frame<3;Frame++){
            Lights.PlanDirectional();
            Assert.True(Lights.DirectionalsPlanned);
            Assert.Equal(1,Lights.DirectionalLightCount);
            Assert.Equal(0,Lights.DirectionalShadowIndex);
            Assert.Equal(Vector3.UnitY,Lights.CascadeDirectionTowardLight);
            Assert.Equal(new Vector3(2f),Lights.Sun!.Value.ColorTimesIntensity);
            Lights.EndFrame();
        }
        Assert.Equal(1,Lights.DirectionalLightCount);
        // 追加の提出に置き換えると、次のフレームからは追加した光源だけになる。
        Lights.AddDirectionalLightTowardLight(Vector3.UnitX,Vector3.One,1f,CastsShadows:false);
        Lights.PlanDirectional();
        Assert.Equal(1,Lights.DirectionalLightCount);
        Assert.Equal(-1,Lights.DirectionalShadowIndex);
        Assert.Null(Lights.CascadeDirectionTowardLight);
        Lights.EndFrame();
        Assert.Equal(0,Lights.DirectionalLightCount);
        Lights.PlanDirectional();
        Assert.False(Lights.DirectionalsPlanned);
    }
    [Fact]
    public void AddedDirectionalsKeepTheStrongestFourAndShadowTheHighestPriorityCaster(){
        StageLightSubmissions Lights=new();
        for(var Index=0;Index<6;Index++){
            Lights.AddDirectionalLightTowardLight(Vector3.UnitY,new Vector3(Index+1),1f,CastsShadows:Index==2,ShadowPriority:Index);
        }
        Lights.PlanDirectional();
        Assert.Equal(StageLightSubmissions.MaxDirectionalLights,Lights.DirectionalLightCount);
        Assert.Equal(2,Lights.DroppedDirectionalLightsThisFrame);
        Assert.Equal([6f,5f,4f,3f],Lights.PlannedDirectionals.Select(static Light=>Light.ColorTimesIntensity.X));
        Assert.Equal(new Vector3(6f),Lights.Sun!.Value.ColorTimesIntensity);
        // 影を落とすのは 3 番目の提出（寄与 3）で、使う順では 4 番目。
        Assert.Equal(3,Lights.DirectionalShadowIndex);
        Lights.EndFrame();
        Assert.Equal(2,Lights.DroppedDirectionalLightsLastFrame);
        Assert.Equal(0,Lights.DroppedDirectionalLightsThisFrame);
    }
    [Fact]
    public void LocalLightsLastOneFrameAndGetTilesWhenOnScreen(){
        StageLightSubmissions Lights=new();
        Lights.AddPointLight(Vector3.Zero,Vector3.One,1f,0.5f);
        Lights.AddPointLight(new Vector3(500f,0f,0f),Vector3.One,1f,1f);
        Lights.AddSpotLight(Vector3.Zero,-Vector3.UnitZ,Vector3.One,1f,5f,0.2f,0.4f,SelectionPriority:1);
        Lights.PlanLocal(ViewProjection,160,80,10,5,16);
        Assert.Equal(3,Lights.LocalLightCount);
        Assert.Equal(StageLocalLightType.Spot,Lights.SelectedLocals[0].Type);
        Assert.NotNull(Lights.SelectedLocalTiles[0]);
        var Offscreen=Lights.SelectedLocals.Select(static (Light,Index)=>(Light,Index)).Single(static Entry=>Entry.Light.Position.X>100f).Index;
        Assert.Null(Lights.SelectedLocalTiles[Offscreen]);
        Assert.Equal(MathF.Cos(0.4f),Lights.SelectedLocals[0].OuterConeCos);
        Lights.EndFrame();
        Lights.PlanLocal(ViewProjection,160,80,10,5,16);
        Assert.Equal(0,Lights.LocalLightCount);
    }
    [Fact]
    public void ShadowCastingLocalLightsGetAtlasProjections(){
        StageLightSubmissions Lights=new();
        Lights.SetDirectionalLight(Vector3.UnitY,Vector3.One,1f);
        Lights.AddPointLight(Vector3.Zero,Vector3.One,10f,5f,CastsShadows:true,ShadowBiasMeters:0.01f);
        Lights.AddSpotLight(new Vector3(1f,2f,0f),-Vector3.UnitY,Vector3.One,10f,5f,0.2f,0.4f,CastsShadows:true,ShadowBiasMeters:0.01f);
        Lights.AddAreaLight(new Vector3(0f,3f,0f),-Vector3.UnitY,Vector3.UnitX,Vector3.UnitZ,Vector3.One,100f,1f,1f,5f,0,CastsShadows:true,ShadowBiasMeters:0.01f,PhysicalLightKind.Area);
        Lights.PlanDirectional();
        Lights.PlanLocal(ViewProjection,160,80,10,5,16);
        Lights.PlanLocalShadows(ViewProjection);
        Assert.Equal(StageLightSubmissions.MaxPointShadowProjections,Lights.PointShadowProjectionCount);
        Assert.Equal(1,Lights.SpotShadowProjectionCount);
        Assert.Equal(1,Lights.AreaShadowProjectionCount);
        Assert.Equal(3,Lights.ShadowAllocations.Count);
        Assert.All(Lights.ShadowAllocations,Allocation=>Assert.Equal(Lights.SelectedLocals[Allocation.LocalIndex].Type,Allocation.Kind));
        Lights.EndFrame();
        Assert.Empty(Lights.ShadowAllocations);
        Assert.Equal(0,Lights.SpotShadowProjectionCount);
    }
    [Fact]
    public void InvalidSubmissionsAreRejected(){
        StageLightSubmissions Lights=new();
        Assert.Throws<ArgumentOutOfRangeException>(()=>Lights.SetDirectionalLight(Vector3.UnitY,Vector3.One,1f,ShadowBiasMeters:-1f));
        Assert.Throws<ArgumentOutOfRangeException>(()=>Lights.AddPointLight(Vector3.Zero,Vector3.One,1f,1f,ShadowBiasMeters:float.NaN));
        Assert.Throws<ArgumentException>(()=>Lights.AddAreaLight(Vector3.Zero,Vector3.UnitZ,Vector3.UnitZ,Vector3.UnitY,Vector3.One,1f,1f,1f,1f,0,false,0f,PhysicalLightKind.Area));
        Assert.Throws<ArgumentOutOfRangeException>(()=>Lights.AddAreaLight(Vector3.Zero,-Vector3.UnitY,Vector3.UnitX,Vector3.UnitZ,Vector3.One,1f,1f,1f,1f,0,false,0f,PhysicalLightKind.Point));
    }
}
