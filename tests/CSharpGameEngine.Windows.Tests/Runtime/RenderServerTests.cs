using System.Numerics;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Graphics.D3D12.Tests;
using CSharpGameEngine.Platform.Windows;

namespace CSharpGameEngine.Runtime.Windows.Tests;

/// <summary>
/// Exercises <see cref="RenderServer.BuildPrimitiveRoster"/> - both as a pure data transform
/// (dedup, per-<see cref="PrimitiveType"/> validity) and end to end through the real D3D12 pipeline
/// (WARP adapter, same rationale as <see cref="StageGpuIntegrationTests"/>) to prove a
/// <see cref="PrimitiveMesh"/> actually renders a recognizable shape, not just that it produces
/// well-formed <see cref="StageMeshCreateInfo"/> data.
/// </summary>
public class RenderServerTests {
    private static readonly string ShaderArtifactPath=Path.Combine(AppContext.BaseDirectory,"Shaders","Stage.cgeshaderartifact");

    [WindowsOnlyFact]
    public void BuildPrimitiveRoster_SamePrimitiveTypeRepeated_DedupesToOneMeshEntry(){
        (var Meshes,var Materials,var Objects)=
            RenderServer.BuildPrimitiveRoster([PrimitiveType.Box,PrimitiveType.Box,PrimitiveType.Box],MeshIndexOffset:0,MaterialIndexOffset:0);

        Assert.Single(Meshes);
        Assert.Single(Materials);
        Assert.Equal(3,Objects.Length);
        Assert.All(Objects,Obj=>Assert.Equal(0,Obj.MeshIndex));
        Assert.All(Objects,Obj=>Assert.Equal(0,Obj.MaterialIndex));
    }

    [WindowsOnlyFact]
    public void BuildPrimitiveRoster_DistinctPrimitiveTypes_EachGetsItsOwnMeshIndexOffsetCorrectly(){
        (var Meshes,var Materials,var Objects)=
            RenderServer.BuildPrimitiveRoster([PrimitiveType.Box,PrimitiveType.Sphere,PrimitiveType.Box],MeshIndexOffset:5,MaterialIndexOffset:2);

        Assert.Equal(2,Meshes.Length);
        Assert.Equal(3,Objects.Length);
        Assert.Equal(5,Objects[0].MeshIndex); // Box, first seen -> offset+0
        Assert.Equal(6,Objects[1].MeshIndex); // Sphere, second seen -> offset+1
        Assert.Equal(5,Objects[2].MeshIndex); // Box again -> dedups to the same index as Objects[0]
        Assert.All(Objects,Obj=>Assert.Equal(2,Obj.MaterialIndex));
        Assert.Single(Materials);
    }

    [WindowsOnlyFact]
    public void BuildPrimitiveRoster_EveryPrimitiveType_ProducesAStageRendererValidMesh(){
        foreach(var Type in Enum.GetValues<PrimitiveType>()){
            (var Meshes,_,_)=RenderServer.BuildPrimitiveRoster([Type],0,0);
            var Mesh=Assert.Single(Meshes);

            Assert.NotEmpty(Mesh.Positions);
            Assert.Equal(Mesh.Positions.Count,Mesh.Normals.Count);
            Assert.Equal(Mesh.Positions.Count,Mesh.Tangents.Count);
            Assert.Equal(Mesh.Positions.Count,Mesh.Uvs.Count);
            Assert.NotEmpty(Mesh.Indices);
        }
    }

    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void BuildPrimitiveRoster_Box_RendersARecognizableSilhouetteAgainstTheClearColor(){
        RunWithTimeout(RunBoxRenderScenario,TimeSpan.FromSeconds(60));
    }

    private static void RunWithTimeout(Action Action,TimeSpan Timeout){
        Task Task=Task.Run(Action);
        if(!Task.Wait(Timeout)){
            throw new TimeoutException($"GPU test did not complete within {Timeout}.");
        }
    }

    private static void RunBoxRenderScenario(){
        (var Meshes,var Materials,var Objects)=
            RenderServer.BuildPrimitiveRoster([PrimitiveType.Box],0,0);

        using Win32Window Window=new(new Win32WindowOptions {
            Title="CSharpGameEngine RenderServer GPU Test",
            Width=128,
            Height=128,
            Visible=false,
        });

        GraphicsDeviceOptions DeviceOptions=new(){ UseWarp=true,EnableDebugLayer=true };

        using var Renderer=StageRenderer.Create(
                                                Window.Handle,Window.ClientWidth,Window.ClientHeight,DeviceOptions,ShaderArtifactPath,
                                                Meshes,Materials,Objects);

        Assert.True(Renderer.ShaderArtifactLoaded,string.Join(Environment.NewLine,Renderer.ShaderArtifactDiagnostics));

        var Camera=CSharpGameEngine.Graphics.D3D12.Camera.CreateDefault(Vector3.Zero);
        Renderer.SetCamera(Camera);
        Renderer.SetAmbientLight(new Vector3(0.4f,0.4f,0.4f));
        Renderer.SetDirectionalLight(Vector3.UnitY,Vector3.One,2f);
        Renderer.SetObjectTransform(0,Matrix4x4.Identity);

        for(var I=0;I<3;I++){
            Renderer.RenderFrame(CaptureForVerification:I==2);
        }

        Assert.NotNull(Renderer.LastCapturedFrame);
        var Verification=ModelPixelVerifier.Verify(Renderer.LastCapturedFrame!,StageRenderer.ClearColor);
        Assert.True(Verification.Success,Verification.Details);

        Assert.Equal(0,Renderer.ErrorOrWorseMessageCount);
    }
}
