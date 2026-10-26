using System.Buffers.Binary;
using System.Security.Cryptography;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Graphics.D3D12.Tests;
using CSharpGameEngine.Platform.Windows;
namespace CSharpGameEngine.Runtime.Windows.Tests;
/// <summary>
/// M0099 Phase 0：ランタイムはカメラ相対（カメラがレンダリング空間の原点）で描画するため、ワールド空間グリッド（ADR-0075）と簡易 height fog には render origin を加え戻す必要があります。EngineHost はこれを、各グリッドマテリアルには正確な Q64.64 の剰余として、カメラの高度として渡します。
/// </summary>
[Collection(D3D12GpuCollection.Name)]
public sealed class RenderOriginTests {
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void WorldGrid_StaysOnTheWorldLatticeWhenTheCameraMoves(){
        // 2台のカメラは壁の原点から右へ0.75 m、セル数が整数となる10 000 m離れています。線は同じピクセルに描画されなければなりません。
        // 横方向へ半セル移動すると、線も移動しなければなりません。
        var Here=Render(new LogicalVector3(0.75f,0.4f,0f),new LogicalVector3(0f,0f,5f),Fog:null);
        var FarAway=Render(new LogicalVector3(10000.75f,0.4f,0f),new LogicalVector3(10000f,0f,5f),Fog:null);
        var HalfACellAside=Render(new LogicalVector3(0.25f,0.4f,0f),new LogicalVector3(0f,0f,5f),Fog:null);
        Assert.Equal(Here,FarAway);
        Assert.NotEqual(Here,HalfACellAside);
    }
    [WindowsOnlyFact]
    [Trait("Category","GPU")]
    public void HeightFog_ThinsWithTheWorldAltitudeOfTheView(){
        // 同じビューでワールド内を3 m上昇します。フォグは高度とともに薄くなるため、フレームが変化するはずです。
        var Fog=new HeightFog { Density=0.2f,HeightFalloff=0.5f,ColorR=0.8f,ColorG=0.8f,ColorB=0.8f };
        var Low=Render(new LogicalVector3(0.75f,0.4f,0f),new LogicalVector3(0f,0f,5f),Fog);
        var High=Render(new LogicalVector3(0.75f,3.4f,0f),new LogicalVector3(0f,3f,5f),Fog);
        Assert.NotEqual(Low,High);
    }
    [Theory]
    [InlineData("0.75","1","0.75")]
    [InlineData("-0.25","1","0.75")]
    [InlineData("10000.75","1","0.75")]
    [InlineData("1000000000000000.375","0.25","0.125")]
    [InlineData("-1000000000000000.375","0.25","0.125")]
    [InlineData("3","1","0")]
    public void ReduceToGridCell_IsExactFarFromTheWorldOrigin(string Position,string Cell,string Expected){
        var Reduced=EngineHost.ReduceToGridCell(LogicalScalar.ParseInvariant(Position),LogicalScalar.ParseInvariant(Cell));
        Assert.Equal(float.Parse(Expected,System.Globalization.CultureInfo.InvariantCulture),Reduced);
    }
    private static GraphicsDeviceOptions Warp=>new() { UseWarp=true,EnableDebugLayer=true };
    /// <summary>カメラ向きのグリッド壁（指定された場合は height fog も含む）をロードし、1フレームをキャプチャします。</summary>
    private static string Render(LogicalVector3 CameraPosition,LogicalVector3 WallPosition,HeightFog?Fog){
        var Directory=Path.Combine(Path.GetTempPath(),$"render-origin-{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(Directory);
        try {
            var AssetPath=Path.Combine(Directory,"Wall.gltf");
            File.WriteAllText(AssetPath,CreateGridWallGltf());
            using var Surface=new Win32Window(new Win32WindowOptions { Title="CSharpGameEngine Render Origin Test",Width=128,Height=128,Visible=false });
            using var Host=new EngineHost();
            Host.UseExternalSurface(Surface.Handle,Surface.ClientWidth,Surface.ClientHeight,Warp);
            ref var World=ref Host.World;
            var App=new AppHost();
            var AppRuntime=new AppHostRuntime();
            var Level=new Level { ShaderArtifactPath=Path.Combine("Shaders","Stage.cgeshaderartifact") };
            var LevelRuntime=new LevelRuntime();
            var LevelEntity=World.Create(App,AppRuntime,Level,LevelRuntime);
            var WallTransform=new Transform { Position=WallPosition };
            var Wall=new StaticMesh { AssetPath=AssetPath,Mobility=MeshMobility.Static };
            var WallEntity=World.Create(WallTransform,Wall);
            var CameraTransform=new Transform { Position=CameraPosition };
            var Camera=new Camera();
            var CameraEntity=World.Create(CameraTransform,Camera);
            var State=Host.CreateState();
            var Load=new LevelLoad();
            Load.Update(WallEntity,ref WallTransform,ref Wall,ref State);
            Load.Update(CameraEntity,ref CameraTransform,ref Camera,ref State);
            Load.Update(LevelEntity,ref App,ref AppRuntime,ref Level,ref LevelRuntime,Host,ref State);
            Assert.True(LevelRuntime.Loaded);
            if(Fog is{ }HeightFog){
                new HeightFogSync().Update(ref HeightFog,Host,ref State);
            }
            Host.Present();
            Host.Renderer!.RenderFrame(CaptureForVerification:true);
            Assert.Equal(0,Host.Renderer.ErrorOrWorseMessageCount);
            return Convert.ToHexString(SHA256.HashData(Host.Renderer.LastCapturedFrame!.Pixels));
        } finally {
            System.IO.Directory.Delete(Directory,true);
        }
    }
    /// <summary>XY 平面上で -Z を向く40 m四方の面に、ADR-0075 の1 m プロトタイプグリッドを配置します。</summary>
    private static string CreateGridWallGltf(){
        float[]Positions=[-20f,-20f,0f,20f,-20f,0f,20f,20f,0f,-20f,20f,0f];
        float[]Normals=[0f,0f,-1f,0f,0f,-1f,0f,0f,-1f,0f,0f,-1f];
        ushort[]Indices=[0,1,2,0,2,3];
        var Geometry=new byte[(Positions.Length+Normals.Length)*4+Indices.Length*2];
        for(var Index=0;Index<Positions.Length;Index++){
            BinaryPrimitives.WriteSingleLittleEndian(Geometry.AsSpan(Index*4,4),Positions[Index]);
            BinaryPrimitives.WriteSingleLittleEndian(Geometry.AsSpan(48+Index*4,4),Normals[Index]);
        }
        for(var Index=0;Index<Indices.Length;Index++){
            BinaryPrimitives.WriteUInt16LittleEndian(Geometry.AsSpan(96+Index*2,2),Indices[Index]);
        }
        return $$"""
            {
              "asset": { "version": "2.0" },
              "scene": 0,
              "scenes": [ { "nodes": [ 0 ] } ],
              "nodes": [ { "mesh": 0 } ],
              "meshes": [ { "primitives": [ { "attributes": { "POSITION": 0, "NORMAL": 1 }, "indices": 2, "material": 0 } ] } ],
              "materials": [ {
                "pbrMetallicRoughness": { "baseColorFactor": [0.18, 0.18, 0.18, 1.0], "metallicFactor": 0 },
                "extras": { "CGE_world_grid": { "gridSizeMeters": 1.0, "subGridDivisions": 5, "wireHalfWidth": 0.009765625, "gridColor": [0.026, 0.026, 0.026], "subGridColor": [0.088, 0.088, 0.088] } }
              } ],
              "buffers": [ { "uri": "data:application/octet-stream;base64,{{Convert.ToBase64String(Geometry)}}", "byteLength": {{Geometry.Length}} } ],
              "bufferViews": [
                { "buffer": 0, "byteOffset": 0, "byteLength": 48 },
                { "buffer": 0, "byteOffset": 48, "byteLength": 48 },
                { "buffer": 0, "byteOffset": 96, "byteLength": 12 }
              ],
              "accessors": [
                { "bufferView": 0, "componentType": 5126, "count": 4, "type": "VEC3", "min": [-20, -20, 0], "max": [20, 20, 0] },
                { "bufferView": 1, "componentType": 5126, "count": 4, "type": "VEC3" },
                { "bufferView": 2, "componentType": 5123, "count": 6, "type": "SCALAR" }
              ]
            }
            """;
    }
}
