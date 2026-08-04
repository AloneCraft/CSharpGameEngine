using System.Numerics;
using CSharpGameEngine.Platform.Windows;

namespace CSharpGameEngine.Graphics.D3D12.Tests;

/// <summary>
/// Exercises the real Windows + D3D12 multi-object stage renderer end to end (ground plane +
/// box "character" placeholder, movable camera, double-buffered per-frame/per-object constant
/// buffers). Runs against the WARP software adapter so it is deterministic in CI without
/// requiring a physical GPU (same rationale as <see cref="ModelGpuIntegrationTests"/>).
/// </summary>
public class StageGpuIntegrationTests
{
    private static readonly string ShaderPath = Path.Combine(AppContext.BaseDirectory, "Shaders", "Stage.hlsl");

    [WindowsOnlyFact]
    [Trait("Category", "GPU")]
    public void Stage_Warp_DebugLayer_Verify_RendersGroundAndCharacter()
    {
        RunWithTimeout(() => RunStageGpuScenario(), TimeSpan.FromSeconds(60));
    }

    [WindowsOnlyFact]
    [Trait("Category", "GPU")]
    public void Stage_Warp_ResizeMidRun_StillRendersAfterResize()
    {
        RunWithTimeout(RunStageResizeScenario, TimeSpan.FromSeconds(60));
    }

    [WindowsOnlyFact]
    [Trait("Category", "GPU")]
    public void Stage_Warp_TwoObjectsWithDifferentMaterials_EachSamplesItsOwnDescriptorTableSlot()
    {
        RunWithTimeout(RunTwoMaterialsScenario, TimeSpan.FromSeconds(60));
    }

    [WindowsOnlyFact]
    [Trait("Category", "GPU")]
    public void Stage_Warp_DirectionalLightAndAmbientChangeRenderedColor()
    {
        RunWithTimeout(RunLightingApiScenario, TimeSpan.FromSeconds(60));
    }

    private static void RunWithTimeout(Action action, TimeSpan timeout)
    {
        Task task = Task.Run(action);
        if (!task.Wait(timeout))
        {
            throw new TimeoutException($"GPU test did not complete within {timeout}.");
        }
    }

    private static void RunStageGpuScenario()
    {
        ProceduralMeshData ground = ProceduralMeshFactory.CreatePlane(width: 20f, depth: 20f);
        ProceduralMeshData character = ProceduralMeshFactory.CreateBox(new Vector3(1f, 2f, 1f));

        using Win32Window window = new(new Win32WindowOptions
        {
            Title = "CSharpGameEngine Stage GPU Test",
            Width = 256,
            Height = 256,
            Visible = false,
        });

        GraphicsDeviceOptions deviceOptions = new()
        {
            UseWarp = true,
            EnableDebugLayer = true,
        };

        using StageRenderer renderer = StageRenderer.Create(
            window.Handle, window.ClientWidth, window.ClientHeight, deviceOptions, ShaderPath,
            [
                new MeshCreateInfo { Positions = ground.Positions, Normals = ground.Normals, Indices = ground.Indices, BaseColorFactor = new Vector4(0.3f, 0.6f, 0.3f, 1f) },
                new MeshCreateInfo { Positions = character.Positions, Normals = character.Normals, Indices = character.Indices, BaseColorFactor = new Vector4(0.8f, 0.2f, 0.2f, 1f) },
            ]);

        Assert.True(renderer.ShaderCompilationSucceeded, string.Join(Environment.NewLine, renderer.ShaderDiagnostics));
        Assert.True(renderer.IsWarp);

        Camera camera = Camera.CreateDefault(new Vector3(0f, 1f, 0f));
        renderer.SetCamera(camera);
        renderer.SetObjectTransform(0, Matrix4x4.Identity);
        renderer.SetObjectTransform(1, Matrix4x4.CreateTranslation(0f, 1f, 0f));

        for (int i = 0; i < 3; i++)
        {
            renderer.RenderFrame(captureForVerification: i == 2);
        }

        Assert.Equal(3u, renderer.FramesRendered);
        Assert.NotNull(renderer.LastCapturedFrame);

        ModelVerificationResult verification = ModelPixelVerifier.Verify(renderer.LastCapturedFrame!, StageRenderer.ClearColor);
        Assert.True(verification.Success, verification.Details);

        Assert.Equal(0, renderer.ErrorOrWorseMessageCount);
    }

    private static void RunStageResizeScenario()
    {
        ProceduralMeshData ground = ProceduralMeshFactory.CreatePlane(width: 10f, depth: 10f);

        using Win32Window window = new(new Win32WindowOptions
        {
            Title = "CSharpGameEngine Stage Resize GPU Test",
            Width = 128,
            Height = 128,
            Visible = false,
        });

        GraphicsDeviceOptions deviceOptions = new()
        {
            UseWarp = true,
            EnableDebugLayer = true,
        };

        using StageRenderer renderer = StageRenderer.Create(
            window.Handle, window.ClientWidth, window.ClientHeight, deviceOptions, ShaderPath,
            [new MeshCreateInfo { Positions = ground.Positions, Normals = ground.Normals, Indices = ground.Indices, BaseColorFactor = Vector4.One }]);

        Camera camera = Camera.CreateDefault(Vector3.Zero);
        renderer.SetCamera(camera);
        renderer.SetObjectTransform(0, Matrix4x4.Identity);
        renderer.RenderFrame();

        renderer.Resize(256, 256);
        renderer.SetCamera(camera);
        renderer.SetObjectTransform(0, Matrix4x4.Identity);
        renderer.RenderFrame(captureForVerification: true);

        Assert.NotNull(renderer.LastCapturedFrame);
        Assert.Equal(256, renderer.LastCapturedFrame!.Width);
        Assert.Equal(256, renderer.LastCapturedFrame.Height);
        Assert.Equal(0, renderer.ErrorOrWorseMessageCount);
    }

    private static void RunTwoMaterialsScenario()
    {
        ProceduralMeshData box = ProceduralMeshFactory.CreateBox(new Vector3(1.2f, 1.2f, 1.2f));

        using Win32Window window = new(new Win32WindowOptions
        {
            Title = "CSharpGameEngine Stage Multi-Material GPU Test",
            Width = 256,
            Height = 256,
            Visible = false,
        });

        GraphicsDeviceOptions deviceOptions = new() { UseWarp = true, EnableDebugLayer = true };

        var uvs = new Vector2[box.Positions.Length];
        var tangents = new Vector4[box.Positions.Length];
        for (int i = 0; i < box.Positions.Length; i++)
        {
            tangents[i] = new Vector4(1, 0, 0, 1);
        }

        StageMeshCreateInfo mesh = new() { Positions = box.Positions, Normals = box.Normals, Tangents = tangents, Uvs = uvs, Indices = box.Indices };
        StageMaterialCreateInfo redMaterial = new() { BaseColorFactor = new Vector4(1, 0, 0, 1), RoughnessFactor = 1f, MetallicFactor = 0f };
        StageMaterialCreateInfo blueMaterial = new() { BaseColorFactor = new Vector4(0, 0, 1, 1), RoughnessFactor = 1f, MetallicFactor = 0f };

        using StageRenderer renderer = StageRenderer.Create(
            window.Handle, window.ClientWidth, window.ClientHeight, deviceOptions, ShaderPath,
            [mesh],
            [redMaterial, blueMaterial],
            [
                new StageObjectCreateInfo { MeshIndex = 0, MaterialIndex = 0 },
                new StageObjectCreateInfo { MeshIndex = 0, MaterialIndex = 1 },
            ]);

        Assert.True(renderer.ShaderCompilationSucceeded, string.Join(Environment.NewLine, renderer.ShaderDiagnostics));

        Camera camera = Camera.CreateDefault(Vector3.Zero);
        renderer.SetCamera(camera);
        renderer.SetAmbientLight(new Vector3(0.4f, 0.4f, 0.4f));
        renderer.SetDirectionalLight(new Vector3(0.3f, 0.8f, 0.4f), Vector3.One, 2f);
        renderer.SetObjectTransform(0, Matrix4x4.CreateTranslation(-1.0f, 0f, 0f));
        renderer.SetObjectTransform(1, Matrix4x4.CreateTranslation(1.0f, 0f, 0f));

        for (int i = 0; i < 3; i++)
        {
            renderer.RenderFrame(captureForVerification: i == 2);
        }

        Assert.NotNull(renderer.LastCapturedFrame);
        PixelReadbackResult frame = renderer.LastCapturedFrame!;

        bool foundReddish = false;
        bool foundBluish = false;
        for (int y = 0; y < frame.Height && !(foundReddish && foundBluish); y++)
        {
            for (int x = 0; x < frame.Width; x++)
            {
                PixelColor pixel = ReadPixel(frame, x, y);
                if (pixel.R > pixel.G + 15 && pixel.R > pixel.B + 15)
                {
                    foundReddish = true;
                }
                if (pixel.B > pixel.R + 15 && pixel.B > pixel.G + 15)
                {
                    foundBluish = true;
                }
            }
        }

        Assert.True(foundReddish, "Expected at least one red-dominant pixel from the object using materialIndex 0.");
        Assert.True(foundBluish, "Expected at least one blue-dominant pixel from the object using materialIndex 1.");
        Assert.Equal(0, renderer.ErrorOrWorseMessageCount);
    }

    private static void RunLightingApiScenario()
    {
        ProceduralMeshData ground = ProceduralMeshFactory.CreatePlane(width: 20f, depth: 20f);

        using Win32Window window = new(new Win32WindowOptions
        {
            Title = "CSharpGameEngine Stage Lighting API GPU Test",
            Width = 128,
            Height = 128,
            Visible = false,
        });

        GraphicsDeviceOptions deviceOptions = new() { UseWarp = true, EnableDebugLayer = true };

        var uvs = new Vector2[ground.Positions.Length];
        var tangents = new Vector4[ground.Positions.Length];
        for (int i = 0; i < ground.Positions.Length; i++)
        {
            tangents[i] = new Vector4(1, 0, 0, 1);
        }

        StageMeshCreateInfo mesh = new() { Positions = ground.Positions, Normals = ground.Normals, Tangents = tangents, Uvs = uvs, Indices = ground.Indices };
        StageMaterialCreateInfo material = new() { BaseColorFactor = Vector4.One, RoughnessFactor = 1f, MetallicFactor = 0f };

        using StageRenderer renderer = StageRenderer.Create(
            window.Handle, window.ClientWidth, window.ClientHeight, deviceOptions, ShaderPath,
            [mesh], [material], [new StageObjectCreateInfo { MeshIndex = 0, MaterialIndex = 0 }]);

        Camera camera = Camera.CreateDefault(Vector3.Zero);
        renderer.SetCamera(camera);
        renderer.SetObjectTransform(0, Matrix4x4.Identity);

        renderer.SetAmbientLight(Vector3.Zero);
        renderer.SetDirectionalLight(Vector3.UnitY, Vector3.Zero, 0f);
        renderer.RenderFrame();
        renderer.RenderFrame();
        renderer.RenderFrame(captureForVerification: true);
        PixelColor darkCenter = ReadPixel(renderer.LastCapturedFrame!, renderer.LastCapturedFrame!.Width / 2, renderer.LastCapturedFrame!.Height / 2);

        renderer.SetCamera(camera);
        renderer.SetAmbientLight(new Vector3(0.3f, 0.3f, 0.3f));
        renderer.SetDirectionalLight(Vector3.UnitY, Vector3.One, 3f);
        renderer.SetObjectTransform(0, Matrix4x4.Identity);
        renderer.RenderFrame();
        renderer.RenderFrame();
        renderer.RenderFrame(captureForVerification: true);
        PixelColor brightCenter = ReadPixel(renderer.LastCapturedFrame!, renderer.LastCapturedFrame!.Width / 2, renderer.LastCapturedFrame!.Height / 2);

        int darkSum = darkCenter.R + darkCenter.G + darkCenter.B;
        int brightSum = brightCenter.R + brightCenter.G + brightCenter.B;
        Assert.True(darkSum < 15, $"Expected a near-black center pixel with zero light/ambient, got {darkCenter} (sum={darkSum}).");
        Assert.True(brightSum > darkSum + 200, $"Expected SetDirectionalLight/SetAmbientLight to meaningfully brighten the center pixel: dark={darkCenter}, bright={brightCenter}.");
        Assert.Equal(0, renderer.ErrorOrWorseMessageCount);
    }

    private static PixelColor ReadPixel(PixelReadbackResult readback, int x, int y)
    {
        const int bytesPerPixel = 4;
        long offset = ((long)y * readback.RowPitch) + (x * bytesPerPixel);
        return new PixelColor(readback.Pixels[offset], readback.Pixels[offset + 1], readback.Pixels[offset + 2], readback.Pixels[offset + 3]);
    }
}
