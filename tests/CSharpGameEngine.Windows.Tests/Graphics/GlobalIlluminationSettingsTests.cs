using CSharpGameEngine.Rendering;
using System.Numerics;
namespace CSharpGameEngine.Graphics.D3D12.Tests;
public sealed class GlobalIlluminationSettingsTests {
    private static GlobalIlluminationSettings Volume(Vector3 HalfExtent,float Spacing,int ProbesPerFrame=16)=>
        new(new Vector3(1f,2f,3f),HalfExtent,Spacing,ProbesPerFrame,0.5f,1f);
    [Fact]
    public void CaptureFaces_ProjectTheDirectionsTheGiShaderReadsBack(){
        // GlobalIllumination.hlsl turns capture texel NDC (x, y) back into the direction Forward + Right*x + Up*y; the
        // capture's view-projection must put that direction at the same NDC, or bounced light lands mirrored.
        var Probe=new Vector3(3f,-2f,7f);
        for(var Face=0;Face<6;Face++){
            var (Forward,Right,Up)=StageGlobalIllumination.FaceBasis(Face);
            foreach(var Ndc in new[]{new Vector2(0.5f,0.25f),new Vector2(-0.75f,0.6f),new Vector2(0.9f,-0.9f)}){
                var World=Probe+(Forward+Right*Ndc.X+Up*Ndc.Y)*10f;
                var Clip=Vector4.Transform(new Vector4(World,1f),StageGlobalIllumination.FaceViewProjection(Probe,Face));
                Assert.Equal(Ndc.X,Clip.X/Clip.W,4);
                Assert.Equal(Ndc.Y,Clip.Y/Clip.W,4);
                Assert.InRange(Clip.Z/Clip.W,0f,1f);
            }
        }
    }
    [Fact]
    public void Grid_SpansTheBoxExactlyWithAtLeastTwoProbesPerAxis(){
        var Settings=Volume(new Vector3(4f,0.5f,3.5f),2f);
        Assert.Equal((5,2,4),Settings.ProbeCounts);
        Assert.Equal(40,Settings.ProbeCount);
        Assert.Equal(Settings.Center-Settings.HalfExtent,Settings.ProbePosition(0));
        var Last=Settings.ProbePosition(Settings.ProbeCount-1);
        Assert.True(Vector3.Distance(Settings.Center+Settings.HalfExtent,Last)<1e-5f,$"last probe {Last}");
        Assert.Equal(2f,Settings.GridSpacing.X,5);
        Assert.Equal(1f,Settings.GridSpacing.Y,5);
        Assert.Equal(7f/3f,Settings.GridSpacing.Z,5);
    }
    [Fact]
    public void ProbePosition_IsXMajorThenYThenZ(){
        var Settings=Volume(new Vector3(1f,1f,1f),1f);
        Assert.Equal((3,3,3),Settings.ProbeCounts);
        Assert.Equal(Settings.GridOrigin+new Vector3(1f,0f,0f),Settings.ProbePosition(1));
        Assert.Equal(Settings.GridOrigin+new Vector3(0f,1f,0f),Settings.ProbePosition(3));
        Assert.Equal(Settings.GridOrigin+new Vector3(0f,0f,1f),Settings.ProbePosition(9));
    }
    [Fact]
    public void IsValid_RejectsVolumesBeyondTheAtlasAndOutOfRangeParameters(){
        Assert.True(Volume(new Vector3(24f,4f,24f),3f).IsValid);
        Assert.False(Volume(new Vector3(100f,10f,100f),1f).IsValid);
        Assert.False(Volume(new Vector3(4f,4f,4f),0f).IsValid);
        Assert.False(Volume(new Vector3(4f,0f,4f),1f).IsValid);
        Assert.False(Volume(new Vector3(4f,4f,4f),1f,ProbesPerFrame:0).IsValid);
        Assert.False(Volume(new Vector3(4f,4f,4f),1f,ProbesPerFrame:GlobalIlluminationSettings.MaxProbesPerFrame+1).IsValid);
        Assert.False((Volume(new Vector3(4f,4f,4f),1f) with { Hysteresis=1f }).IsValid);
        Assert.False((Volume(new Vector3(4f,4f,4f),1f) with { Center=new Vector3(float.NaN,0f,0f) }).IsValid);
        Assert.False(new EnvironmentLightingSettings(null,null,null,ExposureSettings.Legacy,ToneMapper.LegacyAces,1f,0.005f,Volume(new Vector3(4f,4f,4f),0f)).IsValid);
    }
    [Fact]
    public void OctahedralMapping_RoundTripsEveryTexelCentre(){
        for(var Y=0;Y<StageGlobalIllumination.DistanceTexels;Y++){
            for(var X=0;X<StageGlobalIllumination.DistanceTexels;X++){
                var Direction=StageGlobalIllumination.TexelDirection(X,Y,StageGlobalIllumination.DistanceTexels);
                var Encoded=StageGlobalIllumination.OctahedralEncode(Direction);
                Assert.Equal((X+0.5f)/StageGlobalIllumination.DistanceTexels*2f-1f,Encoded.X,4);
                Assert.Equal((Y+0.5f)/StageGlobalIllumination.DistanceTexels*2f-1f,Encoded.Y,4);
            }
        }
    }
}
