using CSharpGameEngine.Contracts;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class RuntimeDebugSnapshotPresentationTests {
    [Fact]
    public void Create_ProjectsEveryBoundedGraphSectionWithoutInventingTimings(){
        RenderPreviewDebugSnapshot Snapshot=CreateSnapshot();

        RuntimeDebugSnapshotPresentation Presentation=
            RuntimeDebugSnapshotPresentation.Create(Snapshot);

        Assert.Contains("7",Presentation.Summary,StringComparison.Ordinal);
        Assert.Contains(Presentation.Rows,Row=>
            Row.Contains("Present",StringComparison.Ordinal));
        Assert.Contains(Presentation.Rows,Row=>
            Row.Contains("BackBuffer",StringComparison.Ordinal));
        Assert.Contains(Presentation.Rows,Row=>
            Row.Contains("Example",StringComparison.Ordinal));
        Assert.Contains(Presentation.Rows,Row=>
            Row.Contains("Windows",StringComparison.Ordinal));
        Assert.Contains(Presentation.Rows,Row=>
            Row.Contains("jump",StringComparison.Ordinal));
        Assert.Contains(Presentation.Rows,Row=>
            Row.Contains("Silent",StringComparison.Ordinal));
        Assert.DoesNotContain(Presentation.Rows,Row=>
            Row.Contains(" ms",StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(Presentation.Rows,Row=>
            Row.Contains("handle",StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Create_RejectsMalformedHostDataBeforePresentation(){
        RenderPreviewDebugSnapshot Snapshot=CreateSnapshot();
        Snapshot.SurfaceWidth=0;

        Assert.Throws<ArgumentException>(
            ()=>RuntimeDebugSnapshotPresentation.Create(Snapshot));
    }

    private static RenderPreviewDebugSnapshot CreateSnapshot()=>new(){
        DeviceState=RenderPreviewDebugDeviceState.Ready,
        DeviceGeneration=7,
        CompletedFence=11,
        FramesRendered=3,
        SurfaceWidth=64,
        SurfaceHeight=48,
        SceneObjectCount=1,
        PrimaryObjectDrawCount=1,
        ResidentMaterialTextureCount=1,
        AdapterDescription="WARP",
        IsWarp=true,
        QualityProfile="Compatibility",
        Passes=[new RenderPreviewDebugPass {
            Index=0,Name="Present",Queue=RenderPreviewDebugQueue.Graphics,
            ExecutionIndex=0,
        }],
        Resources=[new RenderPreviewDebugResource {
            Index=0,Name="BackBuffer",Kind=RenderPreviewDebugResourceKind.Texture,
            Width=64,Height=48,Format="rgba8",Imported=true,
            FirstUse=0,LastUse=0,AliasSlot=-1,
        }],
        Transitions=[new RenderPreviewDebugTransition {
            ResourceIndex=0,PassIndex=0,
            Before=RenderPreviewDebugResourceState.External,
            After=RenderPreviewDebugResourceState.Write,
            SourceQueue=RenderPreviewDebugQueue.Graphics,
            DestinationQueue=RenderPreviewDebugQueue.Graphics,
        }],
        Diagnostics=[new RenderPreviewDebugDiagnostic {
            Severity=RenderPreviewDebugSeverity.Warning,
            Category="Execution",Id="Example",Description="Warning details",
        }],
        Platform=new PlatformDebugSnapshot {
            CapturedCounter=900,
            CounterFrequency=10_000_000,
            OsDescription="Windows",
            OsArchitecture="X64",
            ProcessArchitecture="X64",
            RuntimeDescription=".NET 10",
            ProcessId=42,
            Input=new PlatformDebugInputSnapshot {
                State=PlatformDebugSubsystemState.Ready,
                ActiveContextCount=1,
                HeldControlCount=1,
                ActiveActionCount=1,
                Actions=[new PlatformDebugInputAction {
                    TimestampTicks=800,WindowId="window",DeviceId="keyboard",
                    Sequence=3,ContextId="gameplay",ActionId="jump",
                    Phase=PlatformDebugInputActionPhase.Started,
                    RawX=1f,ValueX=1f,
                }],
            },
            Audio=new PlatformDebugAudioSnapshot {
                State=PlatformDebugSubsystemState.Ready,
                Mode=PlatformDebugAudioBackendMode.Silent,
                Generation=1,MaximumVoiceCount=128,BusCount=1,
            },
        },
    };
}
