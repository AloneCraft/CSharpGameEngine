namespace CSharpGameEngine.Audio.Abstractions;

/// <summary>
/// Generates PCM sample data entirely in managed code (no file I/O, no asset pipeline). The
/// repository has no imported audio assets yet, so this is the minimal way to get audible output
/// out of any <see cref="IAudioBackend{TEngine, TVoice}"/> implementation. Platform-independent by
/// construction: it only produces <c>short[]</c> samples, never touches a backend.
/// </summary>
public static class ProceduralAudioClip
{
    /// <summary>
    /// Generates a mono 16-bit PCM sine wave. <paramref name="amplitudeScale"/> defaults to leave
    /// headroom below <see cref="short.MaxValue"/> so the signal never clips.
    /// </summary>
    public static short[] GenerateSineWave(float frequencyHz, float durationSeconds, int sampleRate, float amplitudeScale = 0.3f)
    {
        if (frequencyHz <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(frequencyHz), "Frequency must be positive.");
        }

        if (durationSeconds <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(durationSeconds), "Duration must be positive.");
        }

        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate), "Sample rate must be positive.");
        }

        int sampleCount = (int)(durationSeconds * sampleRate);
        short[] samples = new short[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            double t = i / (double)sampleRate;
            samples[i] = (short)(MathF.Sin(2f * MathF.PI * frequencyHz * (float)t) * short.MaxValue * amplitudeScale);
        }

        return samples;
    }
}
