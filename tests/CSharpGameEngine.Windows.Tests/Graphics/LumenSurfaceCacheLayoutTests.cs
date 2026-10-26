using CSharpGameEngine.Rendering;
using System.Numerics;
namespace CSharpGameEngine.Graphics.D3D12.Tests;
/// <summary>ADR-0078/M0111: the surface cache's card atlas and card geometry, the tier budgets and the scalability resolution (no GPU).</summary>
public sealed class LumenSurfaceCacheLayoutTests {
    [Fact]
    public void Atlas_PacksCardsOfOneSizeIntoAPageAndGivesThePageBackWhenEmpty(){
        var Atlas=new LumenCardAtlas(256);
        Assert.Equal(4,Atlas.PageCount);
        List<(int X,int Y)>Cards=[];
        for(var Index=0;Index<16;Index++){
            Assert.True(Atlas.TryAllocate(32,32,out var X,out var Y));
            Cards.Add((X,Y));
        }
        // Sixteen 32 x 32 cards fill one 128 x 128 page exactly, without overlap.
        Assert.Equal(3,Atlas.FreePageCount);
        Assert.Equal(16,Cards.Distinct().Count());
        Assert.All(Cards,Card=>Assert.True(Card.X%32==0&&Card.Y%32==0&&Card.X<128&&Card.Y<128));
        Assert.True(Atlas.TryAllocate(32,32,out _,out _));
        Assert.Equal(2,Atlas.FreePageCount);
        foreach(var (X,Y) in Cards){
            Atlas.Free(X,Y,32,32);
        }
        Assert.Equal(3,Atlas.FreePageCount);
        Assert.Throws<InvalidOperationException>(()=>Atlas.Free(Cards[0].X,Cards[0].Y,32,32));
    }
    [Fact]
    public void Atlas_RectangularCardsAndAFullAtlas(){
        var Atlas=new LumenCardAtlas(128);
        List<(int X,int Y)>Cards=[];
        while(Atlas.TryAllocate(128,8,out var X,out var Y)){
            Cards.Add((X,Y));
        }
        Assert.Equal(16,Cards.Count);
        Assert.Equal(16,Cards.Select(Card=>Card.Y).Distinct().Count());
        // Full: another size finds no page; freeing one card of the full page still leaves no free page.
        Assert.False(Atlas.TryAllocate(8,8,out _,out _));
        Atlas.Free(Cards[3].X,Cards[3].Y,128,8);
        Assert.False(Atlas.TryAllocate(8,8,out _,out _));
        Assert.True(Atlas.TryAllocate(128,8,out var AgainX,out var AgainY));
        Assert.Equal(Cards[3],(AgainX,AgainY));
        Assert.Throws<ArgumentOutOfRangeException>(()=>Atlas.TryAllocate(12,8,out _,out _));
        Assert.Throws<ArgumentOutOfRangeException>(()=>Atlas.TryAllocate(256,8,out _,out _));
        Assert.Throws<ArgumentOutOfRangeException>(()=>new LumenCardAtlas(100));
    }
    [Fact]
    public void CaptureProjection_MapsTheBoxOntoTheCardWithDepthFromTheFace(){
        var Minimum=new Vector3(-1f,0f,-2f);
        var Maximum=new Vector3(3f,1f,2f);
        for(var Face=0;Face<LumenCards.FaceCount;Face++){
            var Projection=LumenCards.CaptureProjection(Face,Minimum,Maximum);
            var Axis=LumenCards.Axis(Face);
            Assert.Equal(Axis,Vector3.Cross(LumenCards.U(Face),LumenCards.V(Face)));
            var Centre=(Minimum+Maximum)*0.5f;
            var Half=(Maximum-Minimum)*0.5f;
            // The face's centre is at depth 0, the opposite face's at depth 1, and every corner of the box inside the clip square.
            var Near=Vector4.Transform(new Vector4(Centre+Axis*Vector3.Dot(Vector3.Abs(Axis),Half),1f),Projection);
            var Far=Vector4.Transform(new Vector4(Centre-Axis*Vector3.Dot(Vector3.Abs(Axis),Half),1f),Projection);
            Assert.Equal(0f,Near.Z,5);
            Assert.Equal(1f,Far.Z,5);
            Assert.Equal(0f,Near.X,5);
            Assert.Equal(0f,Near.Y,5);
            for(var Corner=0;Corner<8;Corner++){
                var Point=new Vector3((Corner&1)==0?Minimum.X:Maximum.X,(Corner&2)==0?Minimum.Y:Maximum.Y,(Corner&4)==0?Minimum.Z:Maximum.Z);
                var Clip=Vector4.Transform(new Vector4(Point,1f),Projection);
                Assert.Equal(1f,MathF.Abs(Clip.X),5);
                Assert.Equal(1f,MathF.Abs(Clip.Y),5);
                Assert.InRange(Clip.Z,-1e-5f,1f+1e-5f);
                Assert.Equal(1f,Clip.W);
            }
            // +U is the card's +x (right), +V its +y (up).
            Assert.True(Vector4.Transform(new Vector4(Centre+LumenCards.U(Face),1f),Projection).X>0f);
            Assert.True(Vector4.Transform(new Vector4(Centre+LumenCards.V(Face),1f),Projection).Y>0f);
        }
    }
    [Fact]
    public void CardBoxAndResolution_ThickenFlatMeshesAndClampToTheTier(){
        var (Minimum,Maximum)=LumenCards.CardBox(new Vector3(-0.5f,0f,-0.5f),new Vector3(0.5f,0f,0.5f));
        Assert.True(Maximum.Y>Minimum.Y);
        Assert.True(Minimum.X< -0.5f&&Maximum.X>0.5f);
        // A 127 m floor reaches the tier's largest card, a 1 m side the smallest, at 4 texels per metre.
        Assert.Equal((128,128),LumenCards.Resolution(2,Minimum,Maximum,new Vector3(127f,1f,127f),4f,128));
        Assert.Equal((64,64),LumenCards.Resolution(2,Minimum,Maximum,new Vector3(127f,1f,127f),4f,64));
        Assert.Equal((8,8),LumenCards.Resolution(0,Minimum,Maximum,Vector3.One,4f,128));
        Assert.Equal((16,8),LumenCards.Resolution(2,new Vector3(-1f),new Vector3(1f),new Vector3(2f,1f,0.5f),4f,128));
        Assert.Equal((8,8),LumenCards.Resolution(2,new Vector3(-1f),new Vector3(1f),new Vector3(float.NaN),4f,128));
    }
    [Fact]
    public void Tier_FollowsUnrealsScalabilityAndThePostProcessVolume(){
        var Epic=StageLumen.ResolveTier(LumenSettings.Unreal,RenderScalability.Epic);
        var High=StageLumen.ResolveTier(LumenSettings.Unreal,new RenderScalability(ScalabilityLevel.High,ScalabilityLevel.High));
        // BaseScalability.ini: screen probes every 16 pixels at Epic and 32 at High; reflections full resolution at Epic and half at High.
        Assert.Equal(16,Epic.ProbeTexels);
        Assert.Equal(32,High.ProbeTexels);
        Assert.Equal(1,Epic.ReflectionDownsample);
        Assert.Equal(2,High.ReflectionDownsample);
        Assert.True(Epic.Reflections);
        Assert.Equal(8,StageLumen.ResolveTier(LumenSettings.Unreal with { FinalGatherQuality=2f },RenderScalability.Epic).ProbeTexels);
        Assert.False(StageLumen.ResolveTier(LumenSettings.Unreal with { Reflections=false },RenderScalability.Epic).Reflections);
        Assert.False(StageLumen.ResolveTier(LumenSettings.Unreal,new RenderScalability(ScalabilityLevel.Epic,ScalabilityLevel.Medium)).Reflections);
        Assert.True(Epic.CardTexelsPerFrame>High.CardTexelsPerFrame);
        Assert.True(Epic.CacheProbesPerFrame>High.CacheProbesPerFrame);
    }
    [Fact]
    public void Scalability_DefaultsPerAdapterAndReadsTheVariables(){
        // A hardware adapter defaults to High (Epic is opt-in, M0111), WARP to Medium (the probe volume).
        Assert.Equal(RenderScalability.High,RenderScalabilityResolver.Resolve(false,null,null));
        Assert.Equal(RenderScalability.Medium,RenderScalabilityResolver.Resolve(true,null,null));
        Assert.Equal(RenderScalability.Epic,RenderScalabilityResolver.Resolve(true,"epic",null));
        Assert.Equal(new RenderScalability(ScalabilityLevel.High,ScalabilityLevel.Low),RenderScalabilityResolver.Resolve(true," High ","0"));
        Assert.Equal(new RenderScalability(ScalabilityLevel.Medium,ScalabilityLevel.Epic),RenderScalabilityResolver.Resolve(true,null,"epic"));
        Assert.Throws<InvalidOperationException>(()=>RenderScalabilityResolver.Resolve(false,"ultra",null));
        Assert.False(RenderScalability.Medium.AllowsLumenGlobalIllumination);
        Assert.True(new RenderScalability(ScalabilityLevel.High,ScalabilityLevel.Low).AllowsLumenGlobalIllumination);
        Assert.False(new RenderScalability(ScalabilityLevel.High,ScalabilityLevel.Low).AllowsLumenReflections);
        Assert.False(new RenderScalability(ScalabilityLevel.Low,ScalabilityLevel.Epic).AllowsLumenReflections);
    }
    [Fact]
    public void Settings_RejectUnusableValues(){
        Assert.True(LumenSettings.Unreal.IsValid);
        Assert.False((LumenSettings.Unreal with { FinalGatherQuality=0f }).IsValid);
        Assert.False((LumenSettings.Unreal with { MaxTraceDistanceMeters=float.PositiveInfinity }).IsValid);
        Assert.False((LumenSettings.Unreal with { SurfaceCacheResolution=2f }).IsValid);
        Assert.False((EnvironmentLightingSettings.Legacy with { Lumen=LumenSettings.Unreal with { SkylightLeaking=float.NaN } }).IsValid);
    }
}
