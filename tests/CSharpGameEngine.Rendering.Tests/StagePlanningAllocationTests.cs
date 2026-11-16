using System.Numerics;
using CSharpGameEngine.Rendering.Pipeline;
namespace CSharpGameEngine.Rendering.Tests;
/// <summary>毎フレームの計画の割り当て 0（M0120 設計の目的 3）。使い回し版の結果が元の版と一致し、同じ提出の計画が割り当てをしないことを確かめる。</summary>
public sealed class StagePlanningAllocationTests {
    private static readonly Matrix4x4 ViewProjection=Matrix4x4.CreateLookAt(new Vector3(0f,2f,10f),Vector3.Zero,Vector3.UnitY)
        *Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI/3f,16f/9f,0.1f,1000f);
    [Fact]
    public void ReusableAtlasSelectionMatchesSelect(){
        var Random=new Random(20261116);
        List<ShadowAtlasAllocation>Allocations=[];
        HashSet<int>Scratch=[];
        PhysicalLightKind[]Kinds=[PhysicalLightKind.Directional,PhysicalLightKind.Point,PhysicalLightKind.Spot,PhysicalLightKind.Area,PhysicalLightKind.Emissive];
        for(var Trial=0;Trial<500;Trial++){
            var Count=Random.Next(0,24);
            List<PhysicalLightCandidate>Candidates=[];
            for(var Index=0;Index<Count;Index++){
                // 優先度と寄与の同点を多くして、提出順での決め方を確かめる。
                Candidates.Add(new PhysicalLightCandidate(Index*3+1,Kinds[Random.Next(Kinds.Length)],Random.Next(0,3),Random.Next(0,4)*0.5f,Random.Next(0,4)!=0));
            }
            PhysicalShadowAtlasPlan.SelectAllocationsInto(Candidates,Allocations,Scratch);
            Assert.Equal(PhysicalShadowAtlasPlan.Select(Candidates).Allocations,Allocations);
        }
    }
    [Fact]
    public void ReusableDirectionalShadowSelectionMatchesTheOriginal(){
        var Random=new Random(1116);
        HashSet<int>Scratch=[];
        PhysicalLightKind[]Kinds=[PhysicalLightKind.Directional,PhysicalLightKind.Point];
        for(var Trial=0;Trial<500;Trial++){
            List<PhysicalLightCandidate>Candidates=[];
            for(var Index=0;Index<Random.Next(0,10);Index++){
                Candidates.Add(new PhysicalLightCandidate(Index*2+3,Kinds[Random.Next(Kinds.Length)],Random.Next(0,3),Random.Next(0,3)*0.5f,Random.Next(0,3)!=0));
            }
            Assert.Equal(PhysicalLightBudget.SelectDirectionalShadowIndex((IEnumerable<PhysicalLightCandidate>)Candidates),PhysicalLightBudget.SelectDirectionalShadowIndex(Candidates,Scratch));
        }
    }
    [Fact]
    public void ReusableAtlasSelectionRejectsWhatSelectRejects(){
        List<ShadowAtlasAllocation>Allocations=[];
        HashSet<int>Scratch=[];
        PhysicalLightCandidate[]Duplicate=[new(1,PhysicalLightKind.Spot,0,1f,true),new(1,PhysicalLightKind.Point,0,1f,true)];
        Assert.Throws<ArgumentException>(()=>PhysicalShadowAtlasPlan.SelectAllocationsInto(Duplicate,Allocations,Scratch));
        PhysicalLightCandidate[]NotFinite=[new(1,PhysicalLightKind.Spot,0,float.NaN,true)];
        Assert.Throws<ArgumentOutOfRangeException>(()=>PhysicalShadowAtlasPlan.SelectAllocationsInto(NotFinite,Allocations,Scratch));
    }
    [Fact]
    public void ReusableCascadesMatchBuild(){
        Span<DirectionalShadowCascade>Into=new DirectionalShadowCascade[DirectionalShadowCascades.Count];
        DirectionalShadowCascades.BuildInto(ViewProjection,new Vector3(0f,2f,10f),Vector3.Normalize(new Vector3(0.3f,1f,0.2f)),Into);
        Assert.Equal(DirectionalShadowCascades.Build(ViewProjection,new Vector3(0f,2f,10f),Vector3.Normalize(new Vector3(0.3f,1f,0.2f))),Into.ToArray());
        Assert.Throws<ArgumentException>(()=>DirectionalShadowCascades.BuildInto(ViewProjection,Vector3.Zero,Vector3.UnitY,new DirectionalShadowCascade[1]));
    }
    [Fact]
    public void PlanningTheSameSubmissionsAgainAllocatesNothing(){
        StageLightSubmissions Lights=new();
        var Inputs=new StageLightPlanInputs(ViewProjection,ViewProjection,new Vector3(0f,2f,10f),1280,720,80,45,16);
        var Key=new StagePipelinePlanKey(new StageFrameInputs(RenderPresentationMode.Lit,false,false,true,true),Capture:false);
        long Allocated=0;
        for(var Frame=0;Frame<6;Frame++){
            Submit(Lights);
            var Before=GC.GetAllocatedBytesForCurrentThread();
            _=StagePipelinePlanner.Build(Key);
            Lights.Plan(Inputs);
            var After=GC.GetAllocatedBytesForCurrentThread();
            if(Frame>=2){ Allocated+=After-Before; }
            Assert.Equal(4,Lights.DirectionalLightCount);
            Assert.NotNull(Lights.Cascades);
            Assert.NotEmpty(Lights.ShadowAllocations);
            Lights.EndFrame();
        }
        Assert.Equal(0,Allocated);
    }
    private static void Submit(StageLightSubmissions Lights){
        for(var Index=0;Index<6;Index++){
            Lights.AddDirectionalLightTowardLight(Vector3.Normalize(new Vector3(Index,4f,1f)),Vector3.One,Index+1f,CastsShadows:Index==4);
        }
        for(var Index=0;Index<40;Index++){
            var Position=new Vector3(Index%8-4,1f,Index/8-2);
            if(Index%3==0){
                Lights.AddPointLight(Position,Vector3.One,20f,4f,SelectionPriority:Index%2,CastsShadows:Index==0,ShadowBiasMeters:0.01f);
            } else {
                Lights.AddSpotLight(Position,-Vector3.UnitY,Vector3.One,20f,6f,0.3f,0.5f,SelectionPriority:0,CastsShadows:Index%5==0,ShadowBiasMeters:0.01f);
            }
        }
        Lights.AddAreaLight(new Vector3(0f,3f,0f),-Vector3.UnitY,Vector3.UnitX,Vector3.UnitZ,Vector3.One,50f,1f,1f,6f,0,CastsShadows:true,ShadowBiasMeters:0.01f,PhysicalLightKind.Area);
    }
}
