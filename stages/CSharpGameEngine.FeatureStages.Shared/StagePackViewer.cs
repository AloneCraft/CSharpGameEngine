using System.IO.Compression;
using System.Numerics;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Platform.Windows;
using CSharpGameEngine.Rendering;
using CSharpGameEngine.Rendering.Server;
using CSharpGameEngine.Rendering.View;
namespace CSharpGameEngine.FeatureStages;
/// <summary>`show` で窓に出す内容の指定。<see cref="Frames"/> が null なら窓が閉じられるまで描き続ける。</summary>
public sealed record StageViewOptions(int Width,int Height,int? Frames,bool Hidden,bool UseWarp,string? CapturePath);
/// <summary>
/// ステージのシーンに入っている <see cref="StageVisual"/> を、実際の窓に描くビューア。形の対応は次のとおり（シーンの部品名から決めた表示用の取り決めで、物理の形ではない）。
/// 0 = カメラの目印（小さい箱）、1 = 箱、2 = 球、3 = 三角形、4 = 平たい板（布・波の格子）、5 = 細い棒（ロープ・軸）。
/// </summary>
internal static class StagePackViewer {
    public static int Show(string Pack,string SceneId,IReadOnlyList<StageVisual>Visuals,StageViewOptions Options){
        if(Visuals.Count==0){
            Console.Error.WriteLine($"Scene {SceneId} has no StageVisual to draw.");
            return 1;
        }
        var ShaderArtifactPath=Path.Combine(AppContext.BaseDirectory,"Shaders","Stage.cgeshaderartifact");
        using var Window=new Win32Window(new Win32WindowOptions {
            Title=$"{Pack} / {SceneId}",Width=Options.Width,Height=Options.Height,Visible=!Options.Hidden,
        });
        var (Meshes,Materials,Objects,Placements)=BuildRoster(Visuals);
        RenderServer Renderer;
        try {
            Renderer=D3D12RenderServer.Create(Window.Handle,Window.ClientWidth,Window.ClientHeight,
                new GraphicsDeviceOptions { UseWarp=Options.UseWarp },ShaderArtifactPath,Meshes,Materials,Objects);
        } catch(Exception Ex){
            Console.Error.WriteLine($"Could not start the D3D12 renderer: {Ex.Message}");
            return 1;
        }
        var ExitCode=0;
        try {
            Window.Resized+=(Width,Height)=>Renderer.Resize(Width,Height);
            for(var Index=0;Index<Placements.Length;Index++){ Renderer.SetObjectTransform(Index,Placements[Index]); }
            Renderer.SetAmbientLight(new Vector3(0.9f));
            Renderer.SetViewSource(FrameAll(Visuals));
            Console.WriteLine($"Adapter: {Renderer.AdapterDescription} ({(Renderer.IsWarp?"WARP":"Hardware")}), visuals={Visuals.Count}, shader={(Renderer.ShaderArtifactLoaded?"OK":"FAILED")}");
            var Rendered=0;
            while(!Window.IsClosed&&(Options.Frames is not int Limit||Rendered<Limit)){
                Window.PumpEvents();
                if(Window.IsClosed||Window.IsMinimized){ continue; }
                var Last=Options.Frames is int Count&&Rendered==Count-1;
                Renderer.RenderFrame(CaptureForVerification:Last&&Options.CapturePath is not null);
                Rendered++;
            }
            if(Options.CapturePath is not null){
                if(Renderer.LastCapturedFrame is null){
                    Console.Error.WriteLine("No frame was captured. --capture needs --frames.");
                    ExitCode=1;
                } else {
                    WritePng(Options.CapturePath,Renderer.LastCapturedFrame);
                    Console.WriteLine($"Captured: {Options.CapturePath}");
                }
            }
            Console.WriteLine($"Frames rendered: {Renderer.FramesRendered}, D3D12 errors: {Renderer.ErrorOrWorseMessageCount}");
            if(Renderer.ErrorOrWorseMessageCount>0){ ExitCode=1; }
        } finally {
            Renderer.Dispose();
        }
        return ExitCode;
    }
    private const int CubeMesh=0;
    private const int SphereMesh=1;
    private const int TriangleMesh=2;
    private static (StageMeshCreateInfo[]Meshes,StageMaterialCreateInfo[]Materials,StageObjectCreateInfo[]Objects,Matrix4x4[]Placements)BuildRoster(IReadOnlyList<StageVisual>Visuals){
        StageMeshCreateInfo[]Meshes=[BuildCube(),BuildSphere(),BuildTriangle()];
        var Materials=new StageMaterialCreateInfo[Visuals.Count];
        var Objects=new StageObjectCreateInfo[Visuals.Count];
        var Placements=new Matrix4x4[Visuals.Count];
        for(var Index=0;Index<Visuals.Count;Index++){
            var Visual=Visuals[Index];
            var Argb=Visual.Color;
            Materials[Index]=new StageMaterialCreateInfo {
                BaseColorFactor=new Vector4(((Argb>>16)&0xFF)/255f,((Argb>>8)&0xFF)/255f,(Argb&0xFF)/255f,1f),
                RoughnessFactor=0.6f,MetallicFactor=0f,
            };
            Objects[Index]=new StageObjectCreateInfo { MeshIndex=Visual.Primitive switch { 2=>SphereMesh,3=>TriangleMesh,_=>CubeMesh },MaterialIndex=Index };
            var Scale=new Vector3(Visual.ScaleX,Visual.ScaleY,Visual.ScaleZ);
            if(Visual.Primitive==4){ Scale=new Vector3(Scale.X,Scale.Y*0.05f,Scale.Z); }
            if(Visual.Primitive==5){ Scale=new Vector3(Scale.X*0.1f,Scale.Y*0.1f,Scale.Z*0.1f); }
            Placements[Index]=Matrix4x4.CreateScale(Scale)*Matrix4x4.CreateTranslation(Visual.X,Visual.Y,Visual.Z);
        }
        return (Meshes,Materials,Objects,Placements);
    }
    /// <summary>全部の見た目が入るように、中心を後ろ上から見下ろすカメラ。</summary>
    private static RenderViewSource FrameAll(IReadOnlyList<StageVisual>Visuals){
        var Min=new Vector3(float.MaxValue);
        var Max=new Vector3(float.MinValue);
        foreach(var Visual in Visuals){
            var Center=new Vector3(Visual.X,Visual.Y,Visual.Z);
            var Half=new Vector3(Visual.ScaleX,Visual.ScaleY,Visual.ScaleZ)*0.5f;
            Min=Vector3.Min(Min,Center-Half);
            Max=Vector3.Max(Max,Center+Half);
        }
        var Middle=(Min+Max)*0.5f;
        var Radius=MathF.Max((Max-Min).Length()*0.5f,1f);
        var Eye=Middle+new Vector3(0f,Radius*0.7f,-Radius*2.4f);
        return RenderViewSource.Perspective(Matrix4x4.CreateLookAt(Eye,Middle,Vector3.UnitY),Eye,
            new RadialViewParameters(MathF.PI/4f,0.1f,MathF.Max(1000f,Radius*20f),RadialDepthCurve.DefaultTransitionMeters),0f);
    }
    private static StageMeshCreateInfo BuildMesh(List<Vector3>Positions,List<Vector3>Normals,List<Vector2>Uvs,List<uint>Indices){
        var Tangents=new Vector4[Positions.Count];
        for(var Index=0;Index<Tangents.Length;Index++){ Tangents[Index]=StageMeshGeometry.ArbitraryTangentFor(Normals[Index]); }
        return new StageMeshCreateInfo { Positions=Positions,Normals=Normals,Tangents=Tangents,Uvs=Uvs,Indices=Indices };
    }
    private static StageMeshCreateInfo BuildTriangle(){
        List<Vector3>Positions=[new(0f,0.5f,0f),new(-0.5f,-0.5f,0f),new(0.5f,-0.5f,0f)];
        List<Vector3>Normals=[-Vector3.UnitZ,-Vector3.UnitZ,-Vector3.UnitZ];
        List<Vector2>Uvs=[new(0.5f,0f),new(0f,1f),new(1f,1f)];
        // 面の向きはカメラ側（-Z）に向ける。巻き方向は TestHost の三角形と同じにそろえ、裏面で消えないよう両面を出す。
        return BuildMesh(Positions,Normals,Uvs,[0,1,2,0,2,1]);
    }
    private static StageMeshCreateInfo BuildCube(){
        List<Vector3>Positions=[];List<Vector3>Normals=[];List<Vector2>Uvs=[];List<uint>Indices=[];
        Vector3[]Axes=[Vector3.UnitX,-Vector3.UnitX,Vector3.UnitY,-Vector3.UnitY,Vector3.UnitZ,-Vector3.UnitZ];
        foreach(var Normal in Axes){
            var Side=MathF.Abs(Normal.Y)>0.5f?Vector3.UnitX:Vector3.UnitY;
            var Tangent=Vector3.Cross(Side,Normal);
            var Base=(uint)Positions.Count;
            foreach(var (U,V) in new[]{(-0.5f,-0.5f),(0.5f,-0.5f),(0.5f,0.5f),(-0.5f,0.5f)}){
                Positions.Add(Normal*0.5f+Side*V+Tangent*U);
                Normals.Add(Normal);
                Uvs.Add(new Vector2(U+0.5f,V+0.5f));
            }
            // 巻き方向は向きによって変わるため、法線と一致する側が表になるように決める。
            var Winding=Vector3.Dot(Vector3.Cross(Positions[(int)Base+1]-Positions[(int)Base],Positions[(int)Base+2]-Positions[(int)Base]),Normal)>0f;
            uint[]Order=Winding?[0,1,2,0,2,3]:[0,2,1,0,3,2];
            foreach(var Offset in Order){ Indices.Add(Base+Offset); }
        }
        return BuildMesh(Positions,Normals,Uvs,Indices);
    }
    private static StageMeshCreateInfo BuildSphere(){
        const int Rings=16;const int Segments=24;
        List<Vector3>Positions=[];List<Vector3>Normals=[];List<Vector2>Uvs=[];List<uint>Indices=[];
        for(var Ring=0;Ring<=Rings;Ring++){
            var Phi=MathF.PI*Ring/Rings;
            for(var Segment=0;Segment<=Segments;Segment++){
                var Theta=2f*MathF.PI*Segment/Segments;
                var Normal=new Vector3(MathF.Sin(Phi)*MathF.Cos(Theta),MathF.Cos(Phi),MathF.Sin(Phi)*MathF.Sin(Theta));
                Positions.Add(Normal*0.5f);Normals.Add(Normal);Uvs.Add(new Vector2((float)Segment/Segments,(float)Ring/Rings));
            }
        }
        for(var Ring=0;Ring<Rings;Ring++){
            for(var Segment=0;Segment<Segments;Segment++){
                var A=(uint)(Ring*(Segments+1)+Segment);var B=A+(uint)Segments+1;
                Indices.AddRange([A,A+1,B]);Indices.AddRange([A+1,B+1,B]);
            }
        }
        // 巻き方向を、最初の三角形の外向きの法線から確かめて、逆なら全体を反転する。
        var Probe=Vector3.Cross(Positions[(int)Indices[1]]-Positions[(int)Indices[0]],Positions[(int)Indices[2]]-Positions[(int)Indices[0]]);
        if(Vector3.Dot(Probe,Normals[(int)Indices[0]]+Normals[(int)Indices[1]]+Normals[(int)Indices[2]])<0f){
            for(var Index=0;Index<Indices.Count;Index+=3){ (Indices[Index+1],Indices[Index+2])=(Indices[Index+2],Indices[Index+1]); }
        }
        return BuildMesh(Positions,Normals,Uvs,Indices);
    }
    private static void WritePng(string Path,PixelReadbackResult Frame){
        var Bgra=Frame.Format.ToString().StartsWith("B",StringComparison.OrdinalIgnoreCase);
        var Raw=new byte[Frame.Height*(1+Frame.Width*4)];
        for(var Y=0;Y<Frame.Height;Y++){
            var Row=Y*(1+Frame.Width*4);
            for(var X=0;X<Frame.Width;X++){
                var Source=(int)(Y*Frame.RowPitch)+X*4;
                var Target=Row+1+X*4;
                Raw[Target]=Frame.Pixels[Source+(Bgra?2:0)];
                Raw[Target+1]=Frame.Pixels[Source+1];
                Raw[Target+2]=Frame.Pixels[Source+(Bgra?0:2)];
                Raw[Target+3]=255;
            }
        }
        using var Output=File.Create(Path);
        Output.Write([0x89,0x50,0x4E,0x47,0x0D,0x0A,0x1A,0x0A]);
        var Header=new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(Header,Frame.Width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(Header.AsSpan(4),Frame.Height);
        Header[8]=8;Header[9]=6;
        WriteChunk(Output,"IHDR",Header);
        using var Compressed=new MemoryStream();
        using(var Zlib=new ZLibStream(Compressed,CompressionLevel.Fastest,true)){ Zlib.Write(Raw); }
        WriteChunk(Output,"IDAT",Compressed.ToArray());
        WriteChunk(Output,"IEND",[]);
    }
    private static void WriteChunk(Stream Output,string Type,byte[]Data){
        var Length=new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(Length,Data.Length);
        Output.Write(Length);
        var Body=new byte[4+Data.Length];
        System.Text.Encoding.ASCII.GetBytes(Type).CopyTo(Body,0);
        Data.CopyTo(Body,4);
        Output.Write(Body);
        var Crc=new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(Crc,Crc32(Body));
        Output.Write(Crc);
    }
    private static uint Crc32(byte[]Data){
        var Crc=0xFFFFFFFFu;
        foreach(var Byte in Data){
            Crc^=Byte;
            for(var Bit=0;Bit<8;Bit++){ Crc=(Crc&1)!=0?(Crc>>1)^0xEDB88320u:Crc>>1; }
        }
        return ~Crc;
    }
}
