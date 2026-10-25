using CSharpGameEngine.Rendering;
using CSharpGameEngine.Rendering.Pipeline;
using System.Linq;
using System.Numerics;
namespace CSharpGameEngine.Rendering.Tests;
public class PhysicalLightUnitsTests {
    [Fact]
    public void PointLumensToCandela_FourPiLumens_IsOneCandela(){
        var Candela=PhysicalLightUnits.PointLumensToCandela(4f*MathF.PI);
        Assert.Equal(1f,Candela,precision:5);
    }
    [Fact]
    public void SpotLumensToCandela_UsesConeSolidAngleAndPreservesFlux(){
        const float FluxLumens=800f;
        const float OuterFullAngleDegrees=60f;
        var Candela=PhysicalLightUnits.SpotLumensToCandela(FluxLumens,OuterFullAngleDegrees);
        var SolidAngle=PhysicalLightUnits.ComputeSpotSolidAngle(OuterFullAngleDegrees);
        Assert.Equal(FluxLumens,Candela*SolidAngle,precision:3);
    }
    [Fact]
    public void SpotLumensToCandela_UsesSquaredSmoothConeEffectiveSolidAngle(){
        const float FluxLumens=800f;
        const float InnerFullAngleDegrees=30f;
        const float OuterFullAngleDegrees=60f;

        float EffectiveSolidAngle=PhysicalLightUnits.ComputeSpotEffectiveSolidAngle(
            InnerFullAngleDegrees,OuterFullAngleDegrees);
        float Candela=PhysicalLightUnits.SpotLumensToCandela(
            FluxLumens,InnerFullAngleDegrees,OuterFullAngleDegrees);
        float NumericIntegral=IntegrateSquaredSmoothSpotKernel(
            InnerFullAngleDegrees,OuterFullAngleDegrees);

        Assert.Equal(NumericIntegral,EffectiveSolidAngle,precision:4);
        Assert.Equal(FluxLumens,Candela*EffectiveSolidAngle,precision:3);
        Assert.True(EffectiveSolidAngle<PhysicalLightUnits.ComputeSpotSolidAngle(OuterFullAngleDegrees));
    }

    private static float IntegrateSquaredSmoothSpotKernel(
        float InnerFullAngleDegrees,float OuterFullAngleDegrees){
        float InnerCos=MathF.Cos(InnerFullAngleDegrees*MathF.PI/360f);
        float OuterCos=MathF.Cos(OuterFullAngleDegrees*MathF.PI/360f);
        const int Steps=20_000;
        float Sum=0f;
        for(int Index=0;Index<Steps;Index++){
            float Mu=(Index+0.5f)/Steps;
            float Attenuation=Mu>=InnerCos?1f:Mu<=OuterCos?0f
                :MathF.Pow((Mu-OuterCos)/(InnerCos-OuterCos),2f);
            Sum+=Attenuation;
        }
        return 2f*MathF.PI*Sum/Steps;
    }
    [Fact]
    public void AreaNitsToNormalCandela_UsesPhysicalRectangleArea(){
        var Candela=PhysicalLightUnits.AreaNitsToNormalCandela(100f,WidthMeters:2f,HeightMeters:3f);
        Assert.Equal(600f,Candela,precision:4);
    }
    [Fact]
    public void AreaHemisphereSamples_PreserveOneSidedLambertianFluxAndUseRadianHalfAngles(){
        const float LuminanceNits=100f;
        const float WidthMeters=2f;
        const float HeightMeters=3f;
        const int SampleCount=4;

        float SampleCandela=PhysicalLightUnits.AreaNitsToUniformHemisphereSampleCandela(
            LuminanceNits,WidthMeters,HeightMeters,SampleCount);
        float OneSidedFlux=SampleCount*(2f*MathF.PI)*SampleCandela;
        float ExpectedFlux=MathF.PI*LuminanceNits*WidthMeters*HeightMeters;
        float InnerHalfAngleRadians=PhysicalLightUnits.FullConeAngleDegreesToHalfAngleRadians(179.8f);
        float OuterHalfAngleRadians=PhysicalLightUnits.FullConeAngleDegreesToHalfAngleRadians(180f);

        Assert.Equal(ExpectedFlux,OneSidedFlux,precision:3);
        Assert.True(InnerHalfAngleRadians<OuterHalfAngleRadians);
        Assert.Equal(MathF.PI/2f,OuterHalfAngleRadians,precision:6);
    }
    [Fact]
    public void DirectionalIrradiance_MultipliesLinearColorByLux(){
        var Irradiance=PhysicalLightUnits.DirectionalIrradiance(new Vector3(0.5f,1f,0.25f),1000f);
        Assert.Equal(new Vector3(500f,1000f,250f),Irradiance);
    }
    [Fact]
    public void SpotAngles_RejectsInnerAngleWiderThanOuterAngle(){
        Assert.Throws<ArgumentOutOfRangeException>(()=>PhysicalLightUnits.ValidateSpotAngles(60f,30f));
    }
    [Fact]
    public void Ev100ToExposureScale_ChangesOneStopPerEv(){
        Assert.Equal(1f,PhysicalLightUnits.Ev100ToExposureScale(0f),precision:6);
        Assert.Equal(0.5f,PhysicalLightUnits.Ev100ToExposureScale(1f),precision:6);
        Assert.Equal(2f,PhysicalLightUnits.Ev100ToExposureScale(-1f),precision:6);
    }
    [Fact]
    public void LightBudget_UsesContributionAndStableOrderIndependentlyOfShadowPriority(){
        PhysicalLightBudgetSelection Selection=PhysicalLightBudget.Select([
            new PhysicalLightCandidate(9,PhysicalLightKind.Directional,0,100f,false),
            new PhysicalLightCandidate(2,PhysicalLightKind.Directional,1,1f,false),
            new PhysicalLightCandidate(11,PhysicalLightKind.Point,0,1f,false),
            new PhysicalLightCandidate(12,PhysicalLightKind.Spot,0,1f,false),
            new PhysicalLightCandidate(13,PhysicalLightKind.Area,0,2f,false),
        ]);

        Assert.Equal([9,2],Selection.Directional.Select(static Light=>Light.StableSubmissionOrder));
        Assert.Equal([13,11,12],Selection.Local.Select(static Light=>Light.StableSubmissionOrder));
        Assert.Equal(0,Selection.Diagnostics.DroppedDirectional);
        Assert.Equal(0,Selection.Diagnostics.DroppedLocal);
    }

    [Fact]
    public void LightBudget_ReportsGlobalAndTileOverflow(){
        List<PhysicalLightCandidate> Candidates=[];
        for(int Index=0;Index<PhysicalLightBudget.MaxLocalLights+1;Index++){
            Candidates.Add(new PhysicalLightCandidate(
                Index,PhysicalLightKind.Point,0,1f,false));
        }

        PhysicalLightBudgetSelection Global=PhysicalLightBudget.Select(Candidates);
        PhysicalLightTileSelection Tile=PhysicalLightBudget.SelectTile(Global.Local);

        Assert.Equal(PhysicalLightBudget.MaxLocalLights,Global.Local.Count);
        Assert.Equal(1,Global.Diagnostics.DroppedLocal);
        Assert.Equal(PhysicalLightBudget.MaxLightsPerTile,Tile.Lights.Count);
        Assert.Equal(PhysicalLightBudget.MaxLocalLights-PhysicalLightBudget.MaxLightsPerTile,Tile.DroppedCount);
    }

    [Fact]
    public void DirectionalLightBudget_RetainsAtMostFourHighestContributingLights(){
        PhysicalLightBudgetSelection Selection=PhysicalLightBudget.Select([
            new PhysicalLightCandidate(0,PhysicalLightKind.Directional,0,0f,false),
            new PhysicalLightCandidate(1,PhysicalLightKind.Directional,1,0f,false),
            new PhysicalLightCandidate(2,PhysicalLightKind.Directional,2,0f,false),
            new PhysicalLightCandidate(3,PhysicalLightKind.Directional,3,0f,false),
            new PhysicalLightCandidate(4,PhysicalLightKind.Directional,4,0f,false),
        ]);

        Assert.Equal(4,PhysicalLightBudget.MaxDirectionalLights);
        Assert.Equal([0,1,2,3],Selection.Directional.Select(static Light=>Light.StableSubmissionOrder));
        Assert.Equal(1,Selection.Diagnostics.DroppedDirectional);
    }
    [Fact]
    public void DirectLightBudget_UsesContributionWhileShadowAtlasUsesShadowPriority(){
        PhysicalLightCandidate[]Candidates=[
            new(0,PhysicalLightKind.Directional,100,1f,true),
            new(1,PhysicalLightKind.Directional,0,100f,true),
            new(2,PhysicalLightKind.Directional,0,90f,true),
            new(3,PhysicalLightKind.Directional,0,80f,true),
            new(4,PhysicalLightKind.Directional,0,70f,true),
        ];

        PhysicalLightBudgetSelection Direct=PhysicalLightBudget.Select(Candidates);
        PhysicalShadowAtlasPlan Shadows=PhysicalShadowAtlasPlan.Select(Candidates);

        Assert.Equal(
            [1,2,3,4],Direct.Directional.Select(static Candidate=>Candidate.StableSubmissionOrder));
        Assert.Equal(1,Direct.Diagnostics.DroppedDirectional);
        Assert.Equal(
            [0],Shadows.Allocations.Select(static Allocation=>Allocation.Candidate.StableSubmissionOrder));
        Assert.Equal(
            [1,2,3,4],Shadows.ShadowCastersWithoutAtlasTiles
                .Select(static Candidate=>Candidate.StableSubmissionOrder));
    }


    [Fact]
    public void DirectionalShadowSelection_UsesPriorityThenStableSubmissionOrder(){
        int Selected=PhysicalLightBudget.SelectDirectionalShadowIndex([
            new PhysicalLightCandidate(0,PhysicalLightKind.Directional,100,0f,false),
            new PhysicalLightCandidate(1,PhysicalLightKind.Directional,4,0f,true),
            new PhysicalLightCandidate(7,PhysicalLightKind.Directional,9,0f,true),
            new PhysicalLightCandidate(3,PhysicalLightKind.Directional,9,0f,true),
            new PhysicalLightCandidate(4,PhysicalLightKind.Point,99,0f,true),
        ]);

        Assert.Equal(3,Selected);
        Assert.Equal(-1,PhysicalLightBudget.SelectDirectionalShadowIndex([
            new PhysicalLightCandidate(0,PhysicalLightKind.Directional,1,0f,false),
        ]));
    }

    [Fact]
    public void ShadowAtlas_SelectsConfiguredBudgetsDeterministically(){
        PhysicalLightCandidate Candidate(
            int Order,PhysicalLightKind Kind,int Priority)=>
            new(Order,Kind,Priority,1f,true);
        PhysicalShadowAtlasPlan Plan=PhysicalShadowAtlasPlan.Select([
            Candidate(0,PhysicalLightKind.Directional,0),
            Candidate(1,PhysicalLightKind.Directional,10),
            Candidate(2,PhysicalLightKind.Point,0),
            Candidate(3,PhysicalLightKind.Point,9),
            Candidate(4,PhysicalLightKind.Spot,0),
            Candidate(5,PhysicalLightKind.Spot,1),
            Candidate(6,PhysicalLightKind.Spot,2),
            Candidate(7,PhysicalLightKind.Spot,3),
            Candidate(8,PhysicalLightKind.Spot,4),
            Candidate(9,PhysicalLightKind.Area,0),
            Candidate(10,PhysicalLightKind.Emissive,1),
            Candidate(11,PhysicalLightKind.Area,2),
            Candidate(12,PhysicalLightKind.Emissive,3),
            Candidate(13,PhysicalLightKind.Area,4),
        ]);

        Assert.Equal(PhysicalShadowAtlasPlan.TotalDefaultAllocatedTiles,Plan.Diagnostics.AllocatedTiles);
        Assert.Equal(PhysicalShadowAtlasPlan.ReservedTileCount,Plan.Diagnostics.ReservedTiles);
        Assert.Equal(10,Plan.Diagnostics.SelectedCasters);
        Assert.Equal(4,Plan.Diagnostics.DroppedCasters);
        Assert.Equal(
            [1,3,8,7,6,5,13,12,11,10],
            Plan.Allocations.Select(static Allocation=>Allocation.Candidate.StableSubmissionOrder));
        Assert.Equal(
            [0,1,7,8,9,10,11,12,13,14],
            Plan.Allocations.Select(static Allocation=>Allocation.FirstTile));
    }

    [Fact]
    public void ShadowAtlas_MapsPointCubemapTilesToAlignedPixelAndUvRectangles(){
        PhysicalShadowAtlasPlan Plan=PhysicalShadowAtlasPlan.Select([
            new PhysicalLightCandidate(0,PhysicalLightKind.Directional,0,1f,true),
            new PhysicalLightCandidate(1,PhysicalLightKind.Point,0,1f,true),
        ]);

        ShadowAtlasAllocation Point=Assert.Single(Plan.Allocations,
            static Allocation=>Allocation.Candidate.Kind==PhysicalLightKind.Point);
        Assert.Equal(6,Point.TileCount);
        Assert.Equal([1,2,3,4,5,6],Point.Tiles.Select(static Tile=>Tile.Index));
        Assert.Equal(new ShadowAtlasTile(6,2048,1024,1024,1024),Point.GetTile(5));
        Assert.Equal(new Vector4(0.25f,0.25f,0.5f,0.25f),Point.GetTile(5).UvScaleOffset);
        Assert.Equal(new ShadowAtlasTile(15,3072,3072,1024,1024),ShadowAtlasTile.FromIndex(15));
        Assert.Throws<ArgumentOutOfRangeException>(()=>ShadowAtlasTile.FromIndex(16));
    }

    [Fact]
    public void ShadowAtlas_LeavesOverflowCastersAndNonCastersInDirectSubmission(){
        PhysicalLightCandidate HigherContributionDirectional=
            new(6,PhysicalLightKind.Directional,2,2f,true);
        PhysicalLightCandidate OverflowDirectional=
            new(7,PhysicalLightKind.Directional,2,0f,true);
        PhysicalLightCandidate HigherContributionPoint=
            new(4,PhysicalLightKind.Point,0,4f,true);
        PhysicalLightCandidate OverflowPoint=
            new(5,PhysicalLightKind.Point,0,1f,true);
        PhysicalLightCandidate DirectOnly=
            new(99,PhysicalLightKind.Area,0,0f,false);

        PhysicalShadowAtlasPlan Plan=PhysicalShadowAtlasPlan.Select([
            OverflowDirectional,HigherContributionDirectional,
            OverflowPoint,HigherContributionPoint,DirectOnly,
        ]);

        Assert.Equal(
            [7,6,5,4,99],Plan.SubmittedLights.Select(static Candidate=>Candidate.StableSubmissionOrder));
        Assert.Equal(
            [6,4],Plan.Allocations.Select(static Allocation=>Allocation.Candidate.StableSubmissionOrder));
        Assert.Equal(
            [7,5],Plan.ShadowCastersWithoutAtlasTiles.Select(static Candidate=>Candidate.StableSubmissionOrder));
        Assert.True(Plan.TryGetAllocation(6,out _));
        Assert.True(Plan.TryGetAllocation(4,out _));
        Assert.False(Plan.TryGetAllocation(7,out _));
        Assert.False(Plan.TryGetAllocation(5,out _));
        Assert.False(Plan.TryGetAllocation(99,out _));
        Assert.Equal(2,Plan.Diagnostics.DroppedCasters);
    }

    [Fact]
    public void ShadowAtlas_RejectsDuplicateSubmissionOrderEvenForNonCasters(){
        Assert.Throws<ArgumentException>(()=>PhysicalShadowAtlasPlan.Select([
            new PhysicalLightCandidate(4,PhysicalLightKind.Point,0,1f,false),
            new PhysicalLightCandidate(4,PhysicalLightKind.Area,0,1f,false),
        ]));
    }

    [Fact]
    public void EmissivePatchBuilder_UsesNitsAndActualObjectTransform(){
        IReadOnlyList<EmissivePatch> Patches=EmissivePatchBuilder.Build(new EmissivePatchBuildInput {
            Positions=[Vector3.Zero,new Vector3(2f,0f,0f),new Vector3(0f,1f,0f)],
            Indices=[0,1,2],
            ObjectWorld=Matrix4x4.CreateTranslation(10f,0f,0f),
            EmissiveNits=new Vector3(100f,50f,0f),
        });

        EmissivePatch Patch=Assert.Single(Patches);
        Assert.Equal(new Vector3(11f,0.5f,0f),Patch.Center);
        Assert.Equal(Vector3.UnitZ,Patch.Forward);
        Assert.Equal(new Vector3(1f,0.5f,0f),Patch.LinearRgb);
        Assert.Equal(100f,Patch.LuminanceNits);
        Assert.Equal(2f,Patch.WidthMeters);
        Assert.Equal(1f,Patch.HeightMeters);
    }

    [Fact]
    public void EmissivePatchBuilder_ClampsToSixteenAndIsDeterministic(){
        List<Vector3> Positions=[];
        List<uint> Indices=[];
        for(var Triangle=0;Triangle<17;Triangle++){
            uint BaseIndex=(uint)Positions.Count;
            float X=Triangle*2f;
            Positions.Add(new Vector3(X,0f,0f));
            Positions.Add(new Vector3(X+1f,0f,0f));
            Positions.Add(new Vector3(X,1f,0f));
            Indices.Add(BaseIndex);
            Indices.Add(BaseIndex+1);
            Indices.Add(BaseIndex+2);
        }
        EmissivePatchBuildInput Input=new() {
            Positions=Positions,
            Indices=Indices,
            ObjectWorld=Matrix4x4.Identity,
            EmissiveNits=new Vector3(20f),
        };

        IReadOnlyList<EmissivePatch> First=EmissivePatchBuilder.Build(Input);
        IReadOnlyList<EmissivePatch> Second=EmissivePatchBuilder.Build(Input);

        Assert.Equal(16,First.Count);
        Assert.Equal(First,Second);
        Assert.Equal(17,First.Sum(static Patch=>Patch.SourceTriangleCount));
    }

    [Fact]
    public void EmissivePatchBuilder_ExcludesZeroEmission(){
        IReadOnlyList<EmissivePatch> Patches=EmissivePatchBuilder.Build(new EmissivePatchBuildInput {
            Positions=[Vector3.Zero,Vector3.UnitX,Vector3.UnitY],
            Indices=[0,1,2],
            ObjectWorld=Matrix4x4.Identity,
            EmissiveNits=Vector3.Zero,
        });

        Assert.Empty(Patches);
    }

}
