using System.Reflection;
using CSharpGameEngine.Audio.XAudio2;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Input;
using CSharpGameEngine.Platform.Windows;
using CSharpGameEngine.Runtime.Windows;
using CSharpGameEngine.World;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Content;

namespace CSharpGameEngine.Windows.Tests.Runtime;

public sealed class RuntimeOwnershipTests {
    [Fact]
    public void EngineHost_OwnsOneWorldAndFixedTickClock(){
        using EngineHost Host=new();
        State First=Host.CreateState();
        State Second=Host.CreateState();
        Entity Entity=First.World.Create(new Bootstrap());

        Assert.True(Second.World.IsAlive(Entity));
        Assert.Same(Host.FixedTicks,Host.FixedTicks);
        Assert.Equal(1,Host.World.GetStatistics().AliveEntityCount);
    }

    [Fact]
    public void EngineHost_DisposeInvalidatesWorldLease(){
        EngineHost Host=new();
        State State=Host.CreateState();
        Host.Dispose();

        Assert.Throws<ObjectDisposedException>(()=>State.World.GetStatistics());
    }

    [Fact]
    public async Task EngineHost_DisposesOwnedWorldStreamingService(){
        Assert.True(WorldPartitionManifest.TryCreate([],out var Manifest,out var Error),Error.Message);
        WorldCellStreamingService Streaming=new(new WorldCellRuntime(Manifest),new EmptyArtifactSource(),new EmptyActivationSink());
        EngineHost Host=new();
        Host.OwnWorldStreaming(Streaming);

        Host.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(async()=>
            await Streaming.RequestAsync([],Host.FrameSnapshot,WorldCellStreamingBudget.Unlimited));
    }

    [Fact]
    public void EcsRuntimeComponents_DoNotOwnHostOrNativeResources(){
        Type[]Forbidden=[
            typeof(Win32Window),typeof(RenderServer),typeof(StageRenderer),
            typeof(GraphicsDevice),typeof(InputServer),
            typeof(XAudio2EngineHandle),typeof(XAudio2VoiceHandle),
        ];
        var Violations=typeof(AppHostRuntime).Assembly.GetTypes()
            .Where(static Type=>Type.IsValueType&&Type.GetInterfaces().Any(IsRuntimeComponent))
            .SelectMany(static Type=>Type.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
                .Select(Field=>(Component:Type,Field)))
            .Where(Pair=>Forbidden.Contains(Nullable.GetUnderlyingType(Pair.Field.FieldType)??Pair.Field.FieldType))
            .Select(static Pair=>$"{Pair.Component.FullName}.{Pair.Field.Name}: {Pair.Field.FieldType.FullName}")
            .OrderBy(static Value=>Value,StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(Violations);
    }

    [Fact]
    public void EngineHost_OwnsRenderServerInsteadOfNativeStageRenderer(){
        PropertyInfo RendererProperty=typeof(EngineHost).GetProperty(
            "Renderer",BindingFlags.Instance|BindingFlags.NonPublic)
            ??throw new InvalidOperationException("EngineHost.Renderer was not found.");

        Assert.Equal(typeof(RenderServer),RendererProperty.PropertyType);
        Assert.NotEqual(typeof(StageRenderer),RendererProperty.PropertyType);
    }

    private static bool IsRuntimeComponent(Type Interface)=>
        Interface==typeof(IRuntimeComponent)
        ||Interface.IsGenericType&&Interface.GetGenericTypeDefinition()==typeof(IRuntimeComponent<>);

    private sealed class EmptyArtifactSource:IWorldCellArtifactSource {
        public ValueTask<ReadOnlyMemory<byte>>ReadAsync(AssetArtifactReference Reference,CancellationToken CancellationToken)=>
            throw new InvalidOperationException("Empty manifest has no artifacts.");
        public ValueTask ReleaseAsync(AssetArtifactReference Reference)=>ValueTask.CompletedTask;
    }
    private sealed class EmptyActivationSink:IWorldCellActivationSink {
        public bool TryApply(WorldCellTransition Transition,IReadOnlyDictionary<WorldCellId,LoadedWorldCell>LoadedCells,out string Error){ Error=string.Empty;return true; }
    }
}
