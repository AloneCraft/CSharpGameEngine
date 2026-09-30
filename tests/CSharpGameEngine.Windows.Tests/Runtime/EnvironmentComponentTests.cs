using System.Numerics;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Runtime.Lighting;
namespace CSharpGameEngine.Runtime.Windows.Tests;
/// <summary>ADR-0069 environment Components: snapshot round trip (the AUD-001 contract) and renderer conversion.</summary>
public sealed class EnvironmentComponentTests {
    [Fact]
    public void EnvironmentComponents_RoundTripThroughTheSnapshotCodec(){
        AssertRoundTrip(new SkyAtmosphere { BottomRadiusKm=6000f,MieAnisotropy=0.7f,SunDiskAngleDegrees=0.7357f });
        AssertRoundTrip(new SkyLight { Intensity=2f,LowerHemisphereG=0.25f });
        AssertRoundTrip(new PostProcessVolume { ExposureMethod=ExposureMethod.Legacy,MinEv100=3f,ToneMapper=ToneMapperKind.Legacy });
        AssertRoundTrip(new PostProcessVolume { ExposureMethod=ExposureMethod.AutoHistogram,MinEv100=-2f,MaxEv100=12f,AutoExposureSpeedUp=2f,AutoExposureSpeedDown=0.5f });
        AssertRoundTrip(new ExponentialHeightFog { FogDensity=0.0436f,FogHeight=-68.5f,InscatteringLuminanceB=0.5f });
        AssertRoundTrip(new GlobalIlluminationVolume { HalfExtentX=20f,ProbeSpacingMeters=3f,ProbesPerFrame=48,Hysteresis=0.75f });
    }
    [Fact]
    public void PostProcessVolume_MapsEachExposureMethod(){
        ExposureSettings Build(PostProcessVolume Volume)=>new EnvironmentCollection { PostProcess=Volume }.Build().Exposure;
        Assert.Equal(new ExposureSettings(ExposureMode.PhysicalCamera,2f,1f),Build(new PostProcessVolume { ExposureMethod=ExposureMethod.Manual,MinEv100=2f,MaxEv100=9f }));
        Assert.Equal(new ExposureSettings(ExposureMode.AutoHistogram,2f,1f,9f,3f,1f),Build(new PostProcessVolume { ExposureMethod=ExposureMethod.AutoHistogram,MinEv100=2f,MaxEv100=9f }));
        Assert.Equal(ExposureSettings.Legacy,Build(new PostProcessVolume { ExposureMethod=ExposureMethod.Legacy }));
        Assert.False(new EnvironmentCollection { PostProcess=new PostProcessVolume { ExposureMethod=ExposureMethod.AutoHistogram,MinEv100=9f,MaxEv100=2f } }.Build().IsValid);
    }
    [Fact]
    public void GlobalIlluminationVolume_BuildsCameraRelativeProbeSettings(){
        var Collection=new EnvironmentCollection {
            GlobalIllumination=new GlobalIlluminationVolume { HalfExtentX=8f,HalfExtentY=2f,HalfExtentZ=6f,ProbeSpacingMeters=2f,ProbesPerFrame=16,Hysteresis=0.25f,Intensity=1.5f },
            GlobalIlluminationCenter=new Vector3(1f,2f,-3f),
        };
        var Settings=Collection.Build();
        Assert.True(Settings.IsValid);
        Assert.Equal(new GlobalIlluminationSettings(new Vector3(1f,2f,-3f),new Vector3(8f,2f,6f),2f,16,0.25f,1.5f),Settings.GlobalIllumination);
    }
    [Fact]
    public void GlobalIlluminationVolume_OversizedVolumeDisablesOnlyGlobalIllumination(){
        var Collection=new EnvironmentCollection {
            Atmosphere=new SkyAtmosphere(),
            SkyLight=new SkyLight(),
            GlobalIllumination=new GlobalIlluminationVolume { HalfExtentX=200f,HalfExtentZ=200f,ProbeSpacingMeters=1f },
        };
        var Settings=Collection.Build();
        Assert.True(Settings.IsValid);
        Assert.Null(Settings.GlobalIllumination);
        Assert.NotNull(Settings.SkyLight);
        Assert.Equal("GlobalIlluminationVolume",Collection.InvalidReason);
    }
    private static void AssertRoundTrip<T>(T Value)where T:struct{
        Assert.False(EcsSnapshotComponentCodec<T>.IsTransient);
        var Payload=EcsSnapshotComponentCodec<T>.Serialize(Value);
        Assert.True(EcsSnapshotComponentCodec<T>.TryDeserialize(Payload,out var Restored,out var Error),$"{typeof(T).Name}: {Error}");
        Assert.Equal(Value,Restored);
    }
}
