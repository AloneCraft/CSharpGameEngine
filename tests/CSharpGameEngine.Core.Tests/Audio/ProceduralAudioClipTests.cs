using CSharpGameEngine.Audio.Abstractions;

namespace CSharpGameEngine.Audio.Tests;

public class ProceduralAudioClipTests {
    [Fact]
    public void GenerateSineWave_ReturnsExpectedSampleCount(){
        var Samples=ProceduralAudioClip.GenerateSineWave(440f,1f,44100);

        Assert.Equal(44100,Samples.Length);
    }

    [Fact]
    public void GenerateSineWave_StaysWithinAmplitudeScale(){
        var Samples=ProceduralAudioClip.GenerateSineWave(440f,0.5f,44100,AmplitudeScale:0.3f);

        var Limit=(short)(short.MaxValue*0.3f+1);

        Assert.All(Samples,Sample=>Assert.InRange(Sample,(short)-Limit,Limit));
    }

    [Fact]
    public void GenerateSineWave_IsNotSilent(){
        var Samples=ProceduralAudioClip.GenerateSineWave(440f,0.1f,44100);

        Assert.Contains(Samples,Sample=>Sample!=0);
    }

    [Theory]
    [InlineData(0f,1f,44100)]
    [InlineData(440f,0f,44100)]
    [InlineData(440f,1f,0)]
    public void GenerateSineWave_RejectsNonPositiveArguments(float FrequencyHz,float DurationSeconds,int SampleRate){
        Assert.Throws<ArgumentOutOfRangeException>(()=>ProceduralAudioClip.GenerateSineWave(FrequencyHz,DurationSeconds,SampleRate));
    }
}
