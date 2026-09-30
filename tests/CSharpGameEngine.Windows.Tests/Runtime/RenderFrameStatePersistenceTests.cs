using System.Numerics;
using CSharpGameEngine.Graphics.D3D12;
namespace CSharpGameEngine.Runtime.Windows.Tests;
/// <summary>
/// StageRenderer keeps ambient and fog until they change, so the recovery replay state must keep a
/// value that was set once and then left alone, not only what the last frame recorded.
/// </summary>
public sealed class RenderFrameStatePersistenceTests {
    [Fact]
    public void FrameWithoutPersistentSettings_InheritsThemFromThePreviousFrame(){
        var First=new RenderFrameState(0);
        First.RecordAmbient(new Vector3(0.4f));
        First.RecordFog(new FogParams(0.05f,0.2f,Vector3.One));
        var Second=new RenderFrameState(0);
        Second.RecordCameraAltitude(1.5f);
        Second.InheritPersistentSettings(First);
        Assert.True(Second.TryGetAmbientLight(out var Ambient));
        Assert.Equal(new Vector3(0.4f),Ambient);
        Assert.True(Second.TryGetFog(out var Fog));
        Assert.Equal(new FogParams(0.05f,0.2f,Vector3.One),Fog);
    }
    [Fact]
    public void FrameThatSetsAPersistentValue_KeepsItsOwnValue(){
        var First=new RenderFrameState(0);
        First.RecordAmbient(new Vector3(0.4f));
        var Second=new RenderFrameState(0);
        Second.RecordAmbient(Vector3.Zero);
        Second.InheritPersistentSettings(First);
        Assert.True(Second.TryGetAmbientLight(out var Ambient));
        Assert.Equal(Vector3.Zero,Ambient);
        Assert.False(Second.TryGetFog(out _));
    }
}
