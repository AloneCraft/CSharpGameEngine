using Xunit;
using CSharpGameEngine.Numerics;
using CSharpGameEngine.Rendering.Server;
using CSharpGameEngine.Rendering;
using System.Numerics;
using CSharpGameEngine.Graphics.D3D12;
namespace CSharpGameEngine.Runtime.Windows.Tests;
/// <summary>
/// StageRenderer はアンビエントとフォグを変更があるまで保持します。そのため復旧時のリプレイ状態にも、直前のフレームで記録された値だけでなく、一度設定されてから変更されていない値を保持する必要があります。
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
    [Fact]
    public void RenderOriginSettings_AreInheritedUntilTheyChange(){
        var First=new RenderFrameState(0);
        First.RecordRenderOriginAltitude(12.5f);
        First.RecordWorldGridAnchor(2,new Vector3(0.25f,0.5f,0.75f));
        First.RecordWorldGridAnchor(3,new Vector3(0.1f));
        var Second=new RenderFrameState(0);
        Second.RecordWorldGridAnchor(3,new Vector3(0.2f));
        Second.InheritPersistentSettings(First);
        Assert.True(Second.TryGetRenderOriginAltitude(out var Altitude));
        Assert.Equal(12.5f,Altitude);
        Assert.True(Second.TryGetWorldGridAnchor(2,out var Inherited));
        Assert.Equal(new Vector3(0.25f,0.5f,0.75f),Inherited);
        Assert.True(Second.TryGetWorldGridAnchor(3,out var Own));
        Assert.Equal(new Vector3(0.2f),Own);
    }
    [Fact]
    public void VirtualShadowAnchor_IsInheritedUntilItChanges(){
        var First=new RenderFrameState(0);
        First.RecordVirtualShadowAnchor(new VirtualShadowAnchor(2,0,-1,new Vector3(52.3f,1.7f,1018f)));
        var Second=new RenderFrameState(0);
        Second.InheritPersistentSettings(First);
        Assert.True(Second.TryGetVirtualShadowAnchor(out var Inherited));
        Assert.Equal(new VirtualShadowAnchor(2,0,-1,new Vector3(52.3f,1.7f,1018f)),Inherited);
        var Third=new RenderFrameState(0);
        Third.RecordVirtualShadowAnchor(new VirtualShadowAnchor(3,0,-1,Vector3.Zero));
        Third.InheritPersistentSettings(Second);
        Assert.True(Third.TryGetVirtualShadowAnchor(out var Own));
        Assert.Equal(3,Own.CellX);
        Assert.False(new RenderFrameState(0).TryGetVirtualShadowAnchor(out _));
        // 空に戻すと区画も消える（残ると記録が空にならず、描画サーバーが前のフレームを再生しなくなる）。
        Third.Reset();
        Assert.True(Third.IsEmpty);
        Assert.False(Third.TryGetVirtualShadowAnchor(out _));
    }
}
