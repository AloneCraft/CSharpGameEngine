namespace CSharpGameEngine.Audio.Abstractions;

/// <summary>
/// Type-based entry point for one selected <typeparamref name="TBackend"/> - every member resolves
/// to <typeparamref name="TBackend"/>'s static members at compile time (no runtime backend
/// dispatch), exactly like <see cref="CSharpGameEngine.Spatial.Abstractions.Spatial{TConvention}"/>
/// does for coordinate conventions. A game project that has already picked exactly one backend
/// should expose its own non-generic facade (e.g. <c>GameAudio</c>) that forwards to
/// <c>AudioBackend&lt;ThatBackend, ...&gt;</c>, so application code never has to spell out
/// <typeparamref name="TBackend"/>/<typeparamref name="TEngine"/>/<typeparamref name="TVoice"/>
/// themselves - see <c>UserEcsProject/GameAudio.cs</c>.
/// </summary>
public static class AudioBackend<TBackend,TEngine,TVoice>
    where TBackend:struct,IAudioBackend<TEngine,TVoice>
    where TEngine:struct
    where TVoice:struct {
    public static TEngine CreateEngine()=>TBackend.CreateEngine();

    public static void DestroyEngine(in TEngine Engine)=>TBackend.DestroyEngine(Engine);

    public static TVoice CreateVoice(in TEngine Engine,int SampleRate,int Channels)=>
        TBackend.CreateVoice(Engine,SampleRate,Channels);

    public static TVoice SubmitBuffer(in TVoice Voice,ReadOnlySpan<short>Samples,bool Loop)=>
        TBackend.SubmitBuffer(Voice,Samples,Loop);

    public static void Start(in TVoice Voice)=>TBackend.Start(Voice);

    public static void Stop(in TVoice Voice)=>TBackend.Stop(Voice);

    public static void SetVolume(in TVoice Voice,float Volume)=>TBackend.SetVolume(Voice,Volume);

    public static void DestroyVoice(in TVoice Voice)=>TBackend.DestroyVoice(Voice);
}
