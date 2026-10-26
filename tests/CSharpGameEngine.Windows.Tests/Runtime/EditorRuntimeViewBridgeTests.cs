using CSharpGameEngine.Rendering.Server;
using System.Security.Cryptography;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Graphics.D3D12.Tests;
using CSharpGameEngine.Platform.Windows;
using PreviewBridge=CSharpGameEngine.Generated.GeneratedEditorPreviewBridge;
namespace CSharpGameEngine.Runtime.Windows.Tests;
/// <summary>
/// ADR-0070 B1 step 2：ゲームが生成したブリッジは、ゲーム自身のPresentationシステムでゲームのワールドを描画する。
/// 対象はスタートアップシーンと編集中のシーンで、描画先はデザイナーのサーフェス。
/// </summary>
[Collection(D3D12GpuCollection.Name)]
public sealed class EditorRuntimeViewBridgeTests {
    // 編集中のシーンには(3, 0.5, 0)にボックスがあり、正面カメラがそれを正対して見る。
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

            // 値の編集は再ビルドせずに実行中のワールドへ反映する。ノードにないコンポーネントの編集は反映しない。
            Assert.Equal(1,PreviewBridge.RuntimeViewNodeCount());
            Assert.True(PreviewBridge.RenderRuntimeView(3f,0.5f,-4f,0f,0f,0f,1f,MathF.PI/4f,0.1f,300f));
            var BeforeMove=Convert.ToHexString(SHA256.HashData(PreviewBridge.CaptureRuntimeView()!.Pixels));
            Assert.True(PreviewBridge.TrySetRuntimeViewComponent(0,new CSharpGameEngine.Entities.Transform { Position=new LogicalVector3(3f,0.5f,3f) }));
            Assert.True(PreviewBridge.RenderRuntimeView(3f,0.5f,-4f,0f,0f,0f,1f,MathF.PI/4f,0.1f,300f));
            Assert.NotEqual(BeforeMove,Convert.ToHexString(SHA256.HashData(PreviewBridge.CaptureRuntimeView()!.Pixels)));
            Assert.False(PreviewBridge.TrySetRuntimeViewComponent(0,new Camera()));
            Assert.False(PreviewBridge.TrySetRuntimeViewComponent(1,new CSharpGameEngine.Entities.Transform()));

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
