using System.Security.Cryptography;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Graphics.D3D12.Tests;
using CSharpGameEngine.Platform.Windows;
using PreviewBridge=CSharpGameEngine.Generated.GeneratedEditorPreviewBridge;
namespace CSharpGameEngine.Runtime.Windows.Tests;
/// <summary>
/// ADR-0070 B1 step 2: a game's generated bridge renders the game's world (its startup scenes plus
/// the scene being edited) with the game's own Presentation systems into the designer's surface.
/// </summary>
[Collection(D3D12GpuCollection.Name)]
public sealed class EditorRuntimeViewBridgeTests {
    // The edited scene holds a box at (3, 0.5, 0); the front camera looks straight at it.
    private const string EditedScene="UserEcsProject.Scenes.PrimitiveShowcaseBoxScene";

    [WindowsOnlyFact]
    public void RuntimeView_RendersTheGamesWorldFromTheDesignerCamera(){
        using var Surface=new Win32Window(new Win32WindowOptions {
            Title="CSharpGameEngine Editor Runtime View Bridge Test",Width=160,Height=120,Visible=false,
        });
        Assert.False(PreviewBridge.InitializeRuntimeView("UserEcsProject.Scenes.NoSuchScene",AppContext.BaseDirectory,Surface.Handle,160,120,UseWarp:true));
        try {
            Assert.True(PreviewBridge.InitializeRuntimeView(EditedScene,AppContext.BaseDirectory,Surface.Handle,160,120,UseWarp:true));

            Assert.True(PreviewBridge.RenderRuntimeView(3f,0.5f,-4f,0f,0f,0f,1f,MathF.PI/4f,0.1f,300f));
            var Front=PreviewBridge.CaptureRuntimeView();
            Assert.NotNull(Front);
            Assert.Equal((160,120),(Front!.Width,Front.Height));
            var Verification=ModelPixelVerifier.Verify(Front,RenderServer.ClearColor);
            Assert.True(Verification.Success,Verification.Details);

            Assert.True(PreviewBridge.RenderRuntimeView(0f,1.5f,-8f,0f,0f,0f,1f,MathF.PI/4f,0.1f,300f));
            var FrontHash=Convert.ToHexString(SHA256.HashData(Front.Pixels));
            var Side=PreviewBridge.CaptureRuntimeView()!;
            Assert.NotEqual(FrontHash,Convert.ToHexString(SHA256.HashData(Side.Pixels)));

            PreviewBridge.ResizeRuntimeView(96,64);
            Assert.True(PreviewBridge.RenderRuntimeView(0f,1.5f,-8f,0f,0f,0f,1f,MathF.PI/4f,0.1f,300f));
            var Resized=PreviewBridge.CaptureRuntimeView()!;
            Assert.Equal((96,64),(Resized.Width,Resized.Height));
        } finally {
            PreviewBridge.ShutdownRuntimeView();
        }
        Assert.False(PreviewBridge.RenderRuntimeView(0f,1.5f,-8f,0f,0f,0f,1f,MathF.PI/4f,0.1f,300f));
    }
}
