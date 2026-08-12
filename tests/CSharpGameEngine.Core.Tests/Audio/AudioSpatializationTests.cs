using System.Numerics;
using CSharpGameEngine.Audio.Abstractions;

namespace CSharpGameEngine.Audio.Tests;

public class AudioSpatializationTests {
    [Fact]
    public void ComputeAttenuatedVolume_IsFullWithinMinimumDistance(){
        var Volume=AudioSpatialization.ComputeAttenuatedVolume(
            0.75f,Vector3.Zero,new Vector3(1f,0f,0f),MinDistance:2f,MaxDistance:10f);

        Assert.Equal(0.75f,Volume,precision:5);
    }

    [Fact]
    public void ComputeAttenuatedVolume_FadesMonotonicallyToZero(){
        var Near=AudioSpatialization.ComputeAttenuatedVolume(1f,Vector3.Zero,new Vector3(2f,0f,0f),1f,10f);
        var Far=AudioSpatialization.ComputeAttenuatedVolume(1f,Vector3.Zero,new Vector3(6f,0f,0f),1f,10f);
        var Silent=AudioSpatialization.ComputeAttenuatedVolume(1f,Vector3.Zero,new Vector3(11f,0f,0f),1f,10f);

        Assert.True(Near>Far);
        Assert.Equal(0f,Silent);
    }

    [Fact]
    public void ComputeAttenuatedVolume_ClampsInvalidVolume(){
        Assert.Equal(0f,AudioSpatialization.ComputeAttenuatedVolume(-1f,Vector3.Zero,Vector3.Zero,1f,10f));
        Assert.Equal(1f,AudioSpatialization.ComputeAttenuatedVolume(2f,Vector3.Zero,Vector3.Zero,1f,10f));
    }
}
