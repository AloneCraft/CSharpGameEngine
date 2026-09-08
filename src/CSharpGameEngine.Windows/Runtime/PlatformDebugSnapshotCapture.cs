using System.Diagnostics;
using System.Runtime.InteropServices;
using CSharpGameEngine.Contracts;

namespace CSharpGameEngine.Runtime.Windows;

/// <summary>
/// Copies platform diagnostics from subsystem-owned truth into the bounded provider-neutral
/// Preview Host contract. The copy owns no native handle and cannot mutate the source services.
/// </summary>
public static class PlatformDebugSnapshotCapture {
    public static PlatformDebugSnapshot Capture(
        PlatformDebugInputSnapshot Input,PlatformDebugAudioSnapshot Audio){
        ArgumentNullException.ThrowIfNull(Input);
        ArgumentNullException.ThrowIfNull(Audio);
        return new PlatformDebugSnapshot {
            CapturedCounter=Stopwatch.GetTimestamp(),
            CounterFrequency=Stopwatch.Frequency,
            OsDescription=RuntimeInformation.OSDescription,
            OsArchitecture=RuntimeInformation.OSArchitecture.ToString(),
            ProcessArchitecture=RuntimeInformation.ProcessArchitecture.ToString(),
            RuntimeDescription=RuntimeInformation.FrameworkDescription,
            ProcessId=Environment.ProcessId,
            Input=Input,
            Audio=Audio,
        };
    }

    public static PlatformDebugInputSnapshot NotOwnedInput(string Detail)=>new(){
        State=PlatformDebugSubsystemState.NotOwned,
        Detail=BoundDetail(Detail),
    };

    public static PlatformDebugAudioSnapshot NotOwnedAudio(string Detail)=>new(){
        State=PlatformDebugSubsystemState.NotOwned,
        Mode=PlatformDebugAudioBackendMode.NotOwned,
        Detail=BoundDetail(Detail),
    };

    public static PlatformDebugAudioSnapshot UnavailableAudio(string Detail)=>new(){
        State=PlatformDebugSubsystemState.Unavailable,
        Mode=PlatformDebugAudioBackendMode.None,
        Detail=BoundDetail(Detail),
    };

    private static string BoundDetail(string Value){
        ArgumentException.ThrowIfNullOrWhiteSpace(Value);
        return Value.Length<=RenderPreviewHostProtocol.MaximumPlatformDebugTextLength
            ?Value:Value.Substring(0,RenderPreviewHostProtocol.MaximumPlatformDebugTextLength);
    }
}
