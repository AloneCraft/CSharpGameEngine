using CSharpGameEngine.Audio.Abstractions;

namespace CSharpGameEngine.Audio.Tests;

public class ProceduralAudioClipTests
{
    [Fact]
    public void GenerateSineWave_ReturnsExpectedSampleCount()
    {
        short[] samples = ProceduralAudioClip.GenerateSineWave(440f, 1f, 44100);

        Assert.Equal(44100, samples.Length);
    }

    [Fact]
    public void GenerateSineWave_StaysWithinAmplitudeScale()
    {
        short[] samples = ProceduralAudioClip.GenerateSineWave(440f, 0.5f, 44100, amplitudeScale: 0.3f);

        short limit = (short)(short.MaxValue * 0.3f + 1);

        Assert.All(samples, sample => Assert.InRange(sample, (short)-limit, limit));
    }

    [Fact]
    public void GenerateSineWave_IsNotSilent()
    {
        short[] samples = ProceduralAudioClip.GenerateSineWave(440f, 0.1f, 44100);

        Assert.Contains(samples, sample => sample != 0);
    }

    [Theory]
    [InlineData(0f, 1f, 44100)]
    [InlineData(440f, 0f, 44100)]
    [InlineData(440f, 1f, 0)]
    public void GenerateSineWave_RejectsNonPositiveArguments(float frequencyHz, float durationSeconds, int sampleRate)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ProceduralAudioClip.GenerateSineWave(frequencyHz, durationSeconds, sampleRate));
    }
}
