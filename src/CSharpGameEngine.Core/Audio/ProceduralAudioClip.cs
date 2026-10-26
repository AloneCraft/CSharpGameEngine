namespace CSharpGameEngine.Audio.Abstractions;

/// <summary>
/// PCMサンプルデータをマネージドコードだけで生成する（ファイルI/Oやアセットパイプラインは使わない）。
/// リポジトリにはまだインポート済みの音声アセットがないため、あらゆる<see cref="IAudioBackend{TEngine, TVoice}"/>実装から
/// 音を出すための最小限の手段となる。<c>short[]</c>サンプルを生成するだけでバックエンドには触れず、構造上プラットフォームに依存しない。
/// </summary>
public static class ProceduralAudioClip {
    /// <summary>
    /// モノラルの16ビットPCM正弦波を生成する。<paramref name="AmplitudeScale"/>の既定値では
    /// <see cref="short.MaxValue"/>より小さいヘッドルームを確保し、信号がクリッピングしない。
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
            var T=I/(float)SampleRate;
            Samples[I]=(short)(MathF.Sin(2f*MathF.PI*FrequencyHz*T)*short.MaxValue*AmplitudeScale);
        }

        return Samples;
    }
}
