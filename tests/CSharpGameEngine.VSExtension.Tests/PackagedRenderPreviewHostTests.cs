using System.IO.Compression;
using System.Numerics;
using CSharpGameEngine.Contracts;
using static CSharpGameEngine.VSExtension.NativeMethods;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class PackagedRenderPreviewHostTests {
    [Theory]
    [InlineData("Atmosphere")]
    [InlineData("GlobalIllumination")]
    [InlineData("AmbientOcclusion")]
    [InlineData("LuminanceHistogram")]
    [InlineData("Skinning")]
    [InlineData("GlobalDistanceField")]
    [InlineData("Lumen")]
    public void VsixPayload_CarriesEveryComputeShaderArtifactTheHostLoads(string Name){
        string VsixPath=Path.Combine(
            AppContext.BaseDirectory,"TestAssets","CSharpGameEngine.VSExtension.vsix");
        using ZipArchive Archive=ZipFile.OpenRead(VsixPath);

        Assert.Contains(Archive.Entries,Entry=>
            Entry.FullName==$"PreviewHost/Shaders/{Name}.cgeshaderartifact");
    }

    [Fact]
    public void VsixPayload_AppHostRendersWithWarpAndPackagedShader(){
        string Root=Path.Combine(
            Path.GetTempPath(),$"CgePackagedPreview-{Guid.NewGuid():N}");
        string VsixPath=Path.Combine(
            AppContext.BaseDirectory,"TestAssets","CSharpGameEngine.VSExtension.vsix");
        Directory.CreateDirectory(Root);
        ZipFile.ExtractToDirectory(VsixPath,Root);
        string HostPath=Path.Combine(
            Root,"PreviewHost","CSharpGameEngine.EditorPreview.Host.exe");
        string ShaderPath=Path.Combine(
            Root,"PreviewHost","Shaders","Stage.cgeshaderartifact");
        Assert.True(File.Exists(HostPath),HostPath);
        Assert.True(File.Exists(ShaderPath),ShaderPath);

        string?PreviousAdapter=Environment.GetEnvironmentVariable("CGE_GRAPHICS_ADAPTER");
        string?PreviousDebugLayer=Environment.GetEnvironmentVariable("CGE_D3D12_DEBUG_LAYER");
        IntPtr Parent=IntPtr.Zero;
        try {
            Environment.SetEnvironmentVariable("CGE_GRAPHICS_ADAPTER","warp");
            Environment.SetEnvironmentVariable("CGE_D3D12_DEBUG_LAYER","1");
            Parent=CreateWindowEx(
                0,"STATIC",string.Empty,0,
                0,0,128,128,IntPtr.Zero,IntPtr.Zero,
                GetModuleHandle(null),IntPtr.Zero);
            Assert.NotEqual(IntPtr.Zero,Parent);
            using RenderSurfaceHostWindow SurfaceHost=new(
                Parent,128,128,InitiallyVisible:false);
            using RenderPreviewHostClient Client=RenderPreviewHostClient.Start(HostPath);
            RenderPreviewHostResponse Opened=Client.Open(
                SurfaceHost.Surface.Handle.ToInt64(),128,128,ShaderPath,
                RenderPreviewSceneBuilder.FromMesh(CreateMesh(),128,128));
            Assert.Equal(RenderPreviewHostResultState.Succeeded,Opened.State);

            RenderPreviewHostResponse Frame=Client.Render(captureForVerification:true);

            Assert.Equal(RenderPreviewHostResultState.Succeeded,Frame.State);
            Assert.Equal(64,Frame.FrameHash.Length);
            Assert.True(Frame.DeviceGeneration>0);
            Assert.True(Frame.CompletedFence>0);
            Client.Close();
            Assert.Equal(RenderPreviewHostClientState.Disposed,Client.State);
        } finally {
            Environment.SetEnvironmentVariable("CGE_GRAPHICS_ADAPTER",PreviousAdapter);
            Environment.SetEnvironmentVariable("CGE_D3D12_DEBUG_LAYER",PreviousDebugLayer);
            if(Parent!=IntPtr.Zero){ _=DestroyWindow(Parent); }
            Directory.Delete(Root,recursive:true);
        }
    }

    private static SimpleMesh CreateMesh()=>new(){
        Positions=[
            new Vector3(-0.5f,-0.5f,0f),
            new Vector3(0.5f,-0.5f,0f),
            new Vector3(0f,0.5f,0f),
        ],
        Normals=[-Vector3.UnitZ,-Vector3.UnitZ,-Vector3.UnitZ],
        Indices=[0,1,2],
        BaseColorFactor=new Vector4(0.25f,0.5f,0.75f,1f),
        BoundsCenter=Vector3.Zero,
        BoundsRadius=1f,
    };
}
