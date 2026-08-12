namespace CSharpGameEngine.Audio.Abstractions;

/// <summary>
/// Generates PCM sample data entirely in managed code (no file I/O, no asset pipeline). The
/// repository has no imported audio assets yet, so this is the minimal way to get audible output
/// out of any <see cref="IAudioBackend{TEngine, TVoice}"/> implementation. Platform-independent by
/// construction: it only produces <c>short[]</c> samples, never touches a backend.
/// </summary>
public static class ProceduralAudioClip {
    /// <summary>
    /// Generates a mono 16-bit PCM sine wave. <paramref name="AmplitudeScale"/> defaults to leave
    /// headroom below <see cref="short.MaxValue"/> so the signal never clips.
    /// </summary>
    public static short[]GenerateSineWave(float FrequencyHz,float DurationSeconds,int SampleRate,float AmplitudeScale=0.3f){
        if(FrequencyHz<=0f){
            throw new ArgumentOutOfRangeException(nameof(FrequencyHz),"Frequency must be positive.");
        }

        if(DurationSeconds<=0f){
            throw new ArgumentOutOfRangeException(nameof(DurationSeconds),"Duration must be positive.");
        }

        if(SampleRate<=0){
            throw new ArgumentOutOfRangeException(nameof(SampleRate),"Sample rate must be positive.");
        }

        var SampleCount=(int)(DurationSeconds*SampleRate);
        var Samples=new short[SampleCount];

        for(var I=0;I<SampleCount;I++){
            var T=I/(double)SampleRate;
            Samples[I]=(short)(MathF.Sin(2f*MathF.PI*FrequencyHz*(float)T)*short.MaxValue*AmplitudeScale);
        }

        return Samples;
    }
}
