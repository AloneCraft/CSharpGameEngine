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
    [Fact]
    public void VolumetricCloudAndSkySphere_RoundTripThroughTheSnapshotCodec(){
        AssertRoundTrip(new VolumetricCloud { LayerBottomAltitudeKm=2f,Density=0.02f,Coverage=0.3f,WindDirectionDegrees=180f });
        AssertRoundTrip(new SkySphere { RadiusMeters=5000f });
    }
    [Fact]
    public void VolumetricCloud_DefaultsAreUnrealsTemplateValues(){
        // M0110: the four UE 5.8 template levels use the VolumetricCloud component's defaults with m_SimpleVolumetricCloud_Inst.
        var Settings=EnvironmentCollection.ToCloudSettings(new VolumetricCloud(),Vector2.Zero,0f);
        Assert.Equal(VolumetricCloudSettings.Unreal,Settings);
        Assert.True(Settings.IsValid);
    }
    [Fact]
    public void VolumetricCloud_CarriesTheCameraPositionAndTheWindInKilometres(){
        var Settings=EnvironmentCollection.ToCloudSettings(new VolumetricCloud { WindSpeedMetersPerSecond=20f,WindDirectionDegrees=90f },new Vector2(1500f,-250f),100f);
        Assert.Equal(1.5f,Settings.CameraPositionKm.X,5);
        Assert.Equal(-0.25f,Settings.CameraPositionKm.Y,5);
        // 20 m/s for 100 s toward +R (90 degrees clockwise from +F) is 2 km.
        Assert.Equal(2f,Settings.WindOffsetKm.X,4);
        Assert.Equal(0f,Settings.WindOffsetKm.Y,4);
    }
    [Fact]
    public void VolumetricCloud_UnusableValuesDisableOnlyTheClouds(){
        var Collection=new EnvironmentCollection {
            Atmosphere=new SkyAtmosphere(),
            SkyLight=new SkyLight(),
            Clouds=new VolumetricCloud { Density=float.NaN },
        };
        var Settings=Collection.Build();
        Assert.True(Settings.IsValid);
        Assert.Null(Settings.Clouds);
        Assert.NotNull(Settings.Atmosphere);
        Assert.Equal(nameof(VolumetricCloud),Collection.InvalidReason);
    }
    [Fact]
    public void SkySphere_BuildsACameraRelativeSphereAndRejectsAZeroRadius(){
        var Collection=new EnvironmentCollection { Atmosphere=new SkyAtmosphere(),SkySphere=new SkySphere(),SkySphereCenter=new Vector3(0f,-1.7f,0f) };
        Assert.Equal(new SkySphereSettings(new Vector3(0f,-1.7f,0f),16384f),Collection.Build().SkySphere);
        var Broken=new EnvironmentCollection { Atmosphere=new SkyAtmosphere(),SkySphere=new SkySphere { RadiusMeters=0f } };
        var Settings=Broken.Build();
        Assert.True(Settings.IsValid);
        Assert.Null(Settings.SkySphere);
        Assert.Equal(nameof(SkySphere),Broken.InvalidReason);
    }
    [Fact]
    public void EnvironmentClock_SumsElapsedCountersWithoutLosingTheRemainder(){
        var Clock=new EnvironmentClock();
        for(var Frame=0;Frame<600;Frame++){
            Clock.Advance(8,60);
        }
        Assert.Equal(80f,Clock.Seconds);
        Clock.Advance(1,3);
        Assert.Equal(80f+1f/3f,Clock.Seconds,4);
        Clock.Advance(0,60);
        Clock.Advance(-5,60);
        Assert.Equal(80f+1f/3f,Clock.Seconds,4);
    }
    [Fact]
    public void PostProcessVolume_LumenDefaultsAreUnrealsAndMapToTheRenderer(){
        // ADR-0078/M0111: the four UE 5.8 templates use Lumen GI and reflections with the PostProcessVolume defaults (probe_lumen.py).
        var Volume=new PostProcessVolume();
        Assert.Equal(GlobalIlluminationMethod.Lumen,Volume.GlobalIlluminationMethod);
        Assert.Equal(ReflectionMethod.Lumen,Volume.ReflectionMethod);
        Assert.Equal(LumenSettings.Unreal,EnvironmentCollection.ToLumenSettings(Volume));
        Assert.Equal(LumenSettings.Unreal,new EnvironmentCollection { PostProcess=Volume }.Build().Lumen);
        AssertRoundTrip(new PostProcessVolume { GlobalIlluminationMethod=GlobalIlluminationMethod.None,ReflectionMethod=ReflectionMethod.None,LumenFinalGatherQuality=2f,LumenFinalGatherScreenTraces=false,LumenMaxTraceDistanceMeters=50f });
        var Tuned=EnvironmentCollection.ToLumenSettings(new PostProcessVolume { ReflectionMethod=ReflectionMethod.None,LumenFinalGatherQuality=2f,LumenMaxRoughnessToTraceReflections=0.6f,LumenReflectionsScreenTraces=false });
        Assert.Equal(LumenSettings.Unreal with { Reflections=false,FinalGatherQuality=2f,MaxRoughnessToTraceReflections=0.6f,ReflectionScreenTraces=false },Tuned);
    }
    [Fact]
    public void PostProcessVolume_WithoutLumenOrWithUnusableValuesKeepsTheProbeVolume(){
        Assert.Null(EnvironmentCollection.ToLumenSettings(new PostProcessVolume { GlobalIlluminationMethod=GlobalIlluminationMethod.None }));
        // Lumen reflections need Lumen's GI (their rough lobes read its probes): reflections alone give neither.
        Assert.Null(EnvironmentCollection.ToLumenSettings(new PostProcessVolume { GlobalIlluminationMethod=GlobalIlluminationMethod.None,ReflectionMethod=ReflectionMethod.Lumen }));
        Assert.Null(new EnvironmentCollection().Build().Lumen);
        foreach(var Broken in new[]{
            new PostProcessVolume { LumenFinalGatherQuality=float.NaN },
            new PostProcessVolume { LumenSceneViewDistanceMeters=0f },
            new PostProcessVolume { LumenMaxRoughnessToTraceReflections=1.5f },
            new PostProcessVolume { LumenSkylightLeaking=-0.1f },
        }){
            var Collection=new EnvironmentCollection { Atmosphere=new SkyAtmosphere(),SkyLight=new SkyLight(),PostProcess=Broken };
            var Settings=Collection.Build();
            Assert.True(Settings.IsValid);
            Assert.Null(Settings.Lumen);
            Assert.NotNull(Settings.SkyLight);
            Assert.Equal(nameof(LumenSettings),Collection.InvalidReason);
        }
    }
    private static void AssertRoundTrip<T>(T Value)where T:struct{
        Assert.False(EcsSnapshotComponentCodec<T>.IsTransient);
        var Payload=EcsSnapshotComponentCodec<T>.Serialize(Value);
        Assert.True(EcsSnapshotComponentCodec<T>.TryDeserialize(Payload,out var Restored,out var Error),$"{typeof(T).Name}: {Error}");
        Assert.Equal(Value,Restored);
    }
}
