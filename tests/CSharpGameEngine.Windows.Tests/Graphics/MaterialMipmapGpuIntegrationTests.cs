using System.Numerics;
using CSharpGameEngine.Geometry;
using CSharpGameEngine.Lighting;
using CSharpGameEngine.Platform.Windows;
using CSharpGameEngine.Rendering;
using Xunit.Abstractions;
namespace CSharpGameEngine.Graphics.D3D12.Tests;
/// <summary>
/// M0110: material textures are uploaded with their whole mip chain and sampled anisotropically. A fine checkerboard floor seen toward the
/// horizon is rendered without mips (the renderer's upload limited to level 0, the state before M0110) and with them; far from the camera the
/// mips must turn the aliasing noise into the texture's average and stop it from changing when the camera moves a little.
/// </summary>
[Collection(D3D12GpuCollection.Name)]
public sealed class MaterialMipmapGpuIntegrationTests(ITestOutputHelper Output) {
    private static readonly string ShaderArtifactPath=Path.Combine(AppContext.BaseDirectory,"Shaders","Stage.cgeshaderartifact");
    private const int Width=256;
    private const int Height=144;
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Mipmaps_Warp_RemoveFarAliasingAndItsFlicker(){
        var Without=RenderPair(MipLevelLimit:1);
        var With=RenderPair(MipLevelLimit:0);
        var Horizon=FirstGroundRow(With.First);
        Assert.True(Horizon>=8&&Horizon<=Height/2,$"horizon row {Horizon}; centre column {string.Join(" ",Enumerable.Range(0,Height).Where(Y=>Y%6==0).Select(Y=>ReadPixel(With.First,Width/2,Y).G))}; without mips {string.Join(" ",Enumerable.Range(0,Height).Where(Y=>Y%6==0).Select(Y=>ReadPixel(Without.First,Width/2,Y).G))}; bottom row {string.Join(" ",Enumerable.Range(0,64).Select(X=>ReadPixel(With.First,X,Height-1).G))}");
        // Far: the ten rows just under the horizon, tens of metres away. Near: the bottom ten rows, about two metres away.
        var FarWithout=Statistics(Without.First,Without.Second,Horizon+1,Horizon+11);
        var FarWith=Statistics(With.First,With.Second,Horizon+1,Horizon+11);
        var NearWithout=Statistics(Without.First,Without.Second,Height-10,Height);
        var NearWith=Statistics(With.First,With.Second,Height-10,Height);
        var Diagnostic=$"horizon row {Horizon}; far variance {FarWithout.Variance:F1} -> {FarWith.Variance:F1}, far frame difference {FarWithout.FrameDifference:F2} -> {FarWith.FrameDifference:F2}; near variance {NearWithout.Variance:F1} -> {NearWith.Variance:F1}";
        Output.WriteLine(Diagnostic);
        Assert.True(FarWith.Variance*8.0<FarWithout.Variance,Diagnostic);
        Assert.True(FarWith.FrameDifference*4.0<FarWithout.FrameDifference,Diagnostic);
        // Close to the camera the cells are several pixels wide: the mips must not blur them away.
        Assert.True(NearWith.Variance>NearWithout.Variance*0.5,Diagnostic);
        Assert.True(NearWith.Variance>500.0,Diagnostic);
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void Mipmaps_Warp_UploadOneTexelAndNonPowerOfTwoTexturesWithoutDebugLayerErrors(){
        // 1 x 1 (one level), 3 x 5 (three levels, odd sizes) and 300 x 7 (nine levels): every footprint copies at its own row pitch.
        RenderTextureResource[]Textures=[
            RenderArtifactCodec.CreateProceduralTexture(1,1,[200,30,30,255]),
            RenderArtifactCodec.CreateProceduralTexture(3,5,Enumerable.Range(0,3*5*4).Select(Index=>(byte)(Index*7)).ToArray()),
            RenderArtifactCodec.CreateProceduralTexture(300,7,Enumerable.Range(0,300*7*4).Select(Index=>(byte)(Index*13)).ToArray()),
        ];
        var Plane=ToMesh(ProceduralMeshFactory.CreatePlane(Width:4f,Depth:4f),1f);
        using var Window=new Win32Window(new Win32WindowOptions { Title="CSharpGameEngine Mipmap Sizes",Width=Width,Height=Height,Visible=false });
        using var Renderer=StageRenderer.Create(Window.Handle,Window.ClientWidth,Window.ClientHeight,new GraphicsDeviceOptions { UseWarp=true,EnableDebugLayer=true },ShaderArtifactPath,
            [Plane],
            Textures.Select(Texture=>new StageMaterialCreateInfo { BaseColorFactor=Vector4.One,RoughnessFactor=1f,MetallicFactor=0f,BaseColorTexture=Texture,EmissiveTexture=Texture }).ToArray(),
            [new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 },new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=1 },new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=2 }]);
        var View=Camera.CreateFirstPerson(new Vector3(0f,3f,-6f));
        View.Pitch=-0.4f;
        View.DepthMapping=RenderDepthMapping.Standard;
        for(var Frame=0;Frame<2;Frame++){
            Renderer.SetCamera(View);
            Renderer.SetAmbientLight(new Vector3(0.5f));
            for(var Object=0;Object<3;Object++){
                Renderer.SetObjectTransform(Object,Matrix4x4.CreateTranslation((Object-1)*4.2f,0f,0f));
            }
            Renderer.RenderFrame(CaptureForVerification:Frame==1);
        }
        Assert.Equal(0,Renderer.ErrorOrWorseMessageCount);
        // One texture used as base colour (sRGB) and emissive (sRGB) is resident once per colour space, not once per slot.
        Assert.Equal(Textures.Length+2,Renderer.ResidentMaterialTextureCount);
    }
    private static (PixelReadbackResult First,PixelReadbackResult Second) RenderPair(int MipLevelLimit){
        StageRenderer.MaterialMipLevelLimitForTesting=MipLevelLimit;
        try {
            return (Render(0f),Render(0.37f));
        } finally {
            StageRenderer.MaterialMipLevelLimitForTesting=0;
        }
    }
    private static PixelReadbackResult Render(float ForwardOffset){
        // 64 x 64 texels of 4 x 4 black and white cells, repeated once per metre: cells of 6.25 cm.
        var Pixels=new byte[64*64*4];
        for(var Y=0;Y<64;Y++){
            for(var X=0;X<64;X++){
                var Value=(byte)((((X>>2)+(Y>>2))&1)==0?0:255);
                var Offset=(Y*64+X)*4;
                Pixels[Offset]=Value;
                Pixels[Offset+1]=Value;
                Pixels[Offset+2]=Value;
                Pixels[Offset+3]=255;
            }
        }
        var Checker=RenderArtifactCodec.CreateProceduralTexture(64,64,Pixels);
        var Floor=ToMesh(ProceduralMeshFactory.CreatePlane(Width:400f,Depth:400f),1f);
        using var Window=new Win32Window(new Win32WindowOptions { Title="CSharpGameEngine Mipmap Checker",Width=Width,Height=Height,Visible=false });
        using var Renderer=StageRenderer.Create(Window.Handle,Window.ClientWidth,Window.ClientHeight,new GraphicsDeviceOptions { UseWarp=true,EnableDebugLayer=true },ShaderArtifactPath,
            [Floor],[new StageMaterialCreateInfo { BaseColorFactor=Vector4.One,RoughnessFactor=1f,MetallicFactor=0f,BaseColorTexture=Checker }],[new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 }]);
        var View=Camera.CreateFirstPerson(new Vector3(0.013f,1.6f,ForwardOffset));
        View.Pitch=-0.12f;
        View.DepthMapping=RenderDepthMapping.Standard;
        for(var Frame=0;Frame<2;Frame++){
            Renderer.SetCamera(View);
            Renderer.SetAmbientLight(new Vector3(0.6f));
            Renderer.SetObjectTransform(0,Matrix4x4.Identity);
            Renderer.RenderFrame(CaptureForVerification:Frame==1);
        }
        Assert.Equal(0,Renderer.ErrorOrWorseMessageCount);
        return Renderer.LastCapturedFrame!;
    }
    /// <summary>The first row of the centre column that is not the clear colour.</summary>
    private static int FirstGroundRow(PixelReadbackResult Frame){
        for(var Y=0;Y<Frame.Height;Y++){
            var Pixel=ReadPixel(Frame,Frame.Width/2,Y);
            var Clear=StageRenderer.ClearColor;
            if(Math.Abs(Pixel.R-Clear.R)+Math.Abs(Pixel.G-Clear.G)+Math.Abs(Pixel.B-Clear.B)>6){
                return Y;
            }
        }
        return -1;
    }
    /// <summary>The variance of the green channel over the rows of the first frame, and the mean absolute difference between the two frames.</summary>
    private static (double Variance,double FrameDifference) Statistics(PixelReadbackResult First,PixelReadbackResult Second,int FromRow,int ToRow){
        List<double>Values=[];
        var Difference=0.0;
        for(var Y=FromRow;Y<ToRow;Y++){
            for(var X=0;X<First.Width;X++){
                var A=ReadPixel(First,X,Y).G;
                var B=ReadPixel(Second,X,Y).G;
                Values.Add(A);
                Difference+=Math.Abs(A-B);
            }
        }
        var Mean=Values.Average();
        return (Values.Average(Value=>(Value-Mean)*(Value-Mean)),Difference/Values.Count);
    }
    private static StageMeshCreateInfo ToMesh(ProceduralMeshData Data,float UvPerMeter){
        var Tangents=new Vector4[Data.Positions.Length];
        var Uvs=new Vector2[Data.Positions.Length];
        for(var Index=0;Index<Tangents.Length;Index++){
            Tangents[Index]=StageRenderer.ArbitraryTangentFor(Data.Normals[Index]);
            Uvs[Index]=new Vector2(Data.Positions[Index].X,Data.Positions[Index].Z)*UvPerMeter;
        }
        return new StageMeshCreateInfo { Positions=Data.Positions,Normals=Data.Normals,Tangents=Tangents,Uvs=Uvs,Indices=Data.Indices };
    }
    private static PixelColor ReadPixel(PixelReadbackResult Readback,int X,int Y){
        var Offset=((long)Y*Readback.RowPitch)+(X*4);
        return new PixelColor(Readback.Pixels[Offset],Readback.Pixels[Offset+1],Readback.Pixels[Offset+2],Readback.Pixels[Offset+3]);
    }
}
