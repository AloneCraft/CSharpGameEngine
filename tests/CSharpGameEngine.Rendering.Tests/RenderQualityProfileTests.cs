namespace CSharpGameEngine.Rendering.Tests;

public sealed class RenderQualityProfileTests {
    [Fact]
    public void CompatibilityProfilePreservesTheObservedShadowBloomAndAcesBaseline(){
        RenderQualityPlan Plan=RenderQualityPlan.Build(
            1280,720,RenderQualityProfile.Compatibility);

        Assert.Equal(
            ["DepthPrepass","ShadowCascades","Opaque","Bloom","AcesToneMap","Present"],
            Plan.Graph.Passes.Select(static Pass=>Pass.Name));
        RenderResourcePlan Shadows=Assert.Single(
            Plan.Graph.Resources,static Resource=>Resource.Name=="ShadowCascades");
        Assert.Equal(1024u,Shadows.Descriptor.Width);
        Assert.DoesNotContain(Plan.Graph.Resources,static Resource=>Resource.Name=="Environment");
        Assert.DoesNotContain(Plan.Graph.Passes,static Pass=>Pass.Name=="Fxaa");
    }

    [Fact]
    public void BalancedAndCinematicProfilesScaleOnlyDeclaredQualityAxes(){
        RenderQualityPlan Balanced=RenderQualityPlan.Build(
            1920,1080,RenderQualityProfile.Balanced);
        RenderQualityPlan Cinematic=RenderQualityPlan.Build(
            1920,1080,RenderQualityProfile.Cinematic);

        Assert.Contains(Balanced.Graph.Resources,static Resource=>
            Resource.Name=="ShadowCascades"&&Resource.Descriptor.Width==4096);
        Assert.Contains(Cinematic.Graph.Resources,static Resource=>
            Resource.Name=="ShadowCascades"&&Resource.Descriptor.Width==16384);
        Assert.Contains(Balanced.Graph.Resources,static Resource=>Resource.Name=="Environment");
        Assert.Contains(Cinematic.Graph.Passes,static Pass=>Pass.Name=="Fxaa");
    }

    [Fact]
    public void EveryOptionalFeatureCanBeDisabledWithoutAnImpossibleGraph(){
        RenderQualityProfile Minimal=RenderQualityProfile.Create(
            "Minimal",Shadows:null,ImageBasedLighting:false,
            Bloom:false,AcesToneMapping:false,
            RenderAntiAliasingMode.None,RenderExposurePolicy.Fixed(2f));

        RenderQualityPlan Plan=RenderQualityPlan.Build(1,1,Minimal);

        Assert.Equal(
            ["DepthPrepass","Opaque","LinearComposite","Present"],
            Plan.Graph.Passes.Select(static Pass=>Pass.Name));
        Assert.DoesNotContain(Plan.Graph.Resources,static Resource=>
            Resource.Name is "ShadowCascades" or "Environment" or "Bloom" or "Antialiased");
        Assert.Equal(0.25f,Minimal.Exposure.Resolve(float.NaN),precision:6);
    }

    [Fact]
    public void ProfileGraphIsDeterministicAcrossIndependentBuilds(){
        RenderQualityPlan First=RenderQualityPlan.Build(
            800,600,RenderQualityProfile.Balanced);
        RenderQualityPlan Second=RenderQualityPlan.Build(
            800,600,RenderQualityProfile.Balanced);

        Assert.Equal(
            First.Graph.Passes.Select(static Pass=>(Pass.Name,Pass.Queue,Pass.ExecutionIndex)),
            Second.Graph.Passes.Select(static Pass=>(Pass.Name,Pass.Queue,Pass.ExecutionIndex)));
        Assert.Equal(
            First.Graph.Resources.Select(static Resource=>
                (Resource.Name,Resource.Descriptor,Resource.FirstUse,Resource.LastUse)),
            Second.Graph.Resources.Select(static Resource=>
                (Resource.Name,Resource.Descriptor,Resource.FirstUse,Resource.LastUse)));
    }

    [Fact]
    public void InvalidShadowExposureAndAntialiasingPoliciesAreRejected(){
        Assert.Throws<ArgumentOutOfRangeException>(()=>RenderShadowQuality.Create(3,1024));
        Assert.Throws<ArgumentOutOfRangeException>(()=>RenderShadowQuality.Create(2,1000));
        Assert.Throws<ArgumentOutOfRangeException>(()=>RenderExposurePolicy.Fixed(float.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(()=>RenderQualityProfile.Create(
            "Invalid",null,false,false,false,
            (RenderAntiAliasingMode)byte.MaxValue,RenderExposurePolicy.Camera));
        Assert.Throws<ArgumentOutOfRangeException>(()=>RenderQualityPlan.Build(
            0,1,RenderQualityProfile.Compatibility));
    }

    [Fact]
    public void CameraExposureUsesOneStopPerEvAndRejectsNonFiniteInput(){
        RenderExposurePolicy Exposure=RenderExposurePolicy.Camera;
        Assert.Equal(1f,Exposure.Resolve(0f),precision:6);
        Assert.Equal(0.5f,Exposure.Resolve(1f),precision:6);
        Assert.Equal(2f,Exposure.Resolve(-1f),precision:6);
        Assert.Throws<ArgumentOutOfRangeException>(()=>Exposure.Resolve(float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(()=>Exposure.Resolve(33f));
    }
}
