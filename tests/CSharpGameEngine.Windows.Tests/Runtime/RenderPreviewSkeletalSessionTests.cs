extern alias PreviewHost;
using System.Numerics;
using System.Security.Cryptography;
using CSharpGameEngine.Contracts;
using PreviewHost::CSharpGameEngine.EditorPreview.Host;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Platform.Windows;
using CSharpGameEngine.Rendering;
using CSharpGameEngine.Rendering.View;
namespace CSharpGameEngine.Windows.Tests.Runtime;
/// <summary>
/// プレビューホストのスケルタル glTF（ADR-0090、M0127）。テンプレートのマネキン（スキン付き .glb）とアニメーションだけの .glb を、
/// ホストが自分で読み込んで WARP で描き、要約・姿勢・ボーンの線が画素として現れることを確かめる。
/// </summary>
[Collection(CSharpGameEngine.Graphics.D3D12.Tests.D3D12GpuCollection.Name)]
public sealed class RenderPreviewSkeletalSessionTests {
    private static string ShaderArtifactPath=>Path.Combine(AppContext.BaseDirectory,"Shaders","Stage.cgeshaderartifact");
    private const int Size=160;
    private static readonly GraphicsDeviceOptions Options=new(){ UseWarp=true,EnableDebugLayer=true };
    private static string Mannequins=>Path.Combine(RepositoryRoot(),"Templates","CSharpGameEngine.Templates.FirstPerson","Assets","Characters","Mannequins");
    private static string Mesh=>Path.Combine(Mannequins,"SKM_Manny_Simple.glb");
    private static string Idle=>Path.Combine(Mannequins,"Anims","MM_Idle.glb");
    private static string RepositoryRoot(){
        for(var Directory=new DirectoryInfo(AppContext.BaseDirectory);Directory is not null;Directory=Directory.Parent){
            if(File.Exists(Path.Combine(Directory.FullName,"CSharpGameEngine.slnx"))){ return Directory.FullName; }
        }
        throw new InvalidOperationException("Repository root not found.");
    }
    private sealed class Preview:IDisposable {
        private readonly Win32Window Window;
        internal readonly RenderPreviewRenderSession Session;
        internal readonly RenderPreviewCamera Camera;
        internal Preview(RenderPreviewAssetSource?Source,string?StaticAssetPath=null){
            this.Window=new Win32Window(new Win32WindowOptions { Title="CSharpGameEngine Preview Skeletal Test",Width=Size,Height=Size,Visible=false });
            var Fit=RenderCamera.CreateBoundsFit(new Vector3(-1f,0f,-1f),new Vector3(1f,1.8f,1f));
            var ViewProjection=Fit.GetView()*Fit.GetProjection(1f);
            var Eye=Fit.GetEyePosition();
            this.Camera=new RenderPreviewCamera {
                ViewProjection=ToWire(ViewProjection),Position=[Eye.X,Eye.Y,Eye.Z],FovYRadians=Fit.FovYRadians,NearZ=Fit.NearZ,FarZ=Fit.FarZ,
            };
            var Open=new RenderPreviewHostRequest {
                RequestId=Guid.NewGuid(),SessionId=Guid.NewGuid(),Sequence=1,Command=RenderPreviewHostCommand.Open,
                SurfaceHandle=this.Window.Handle,Width=this.Window.ClientWidth,Height=this.Window.ClientHeight,ShaderArtifactPath=ShaderArtifactPath,
                Scene=new RenderPreviewScene { Camera=this.Camera,Asset=Source,StaticAssetPath=StaticAssetPath??string.Empty },
            };
            Assert.Null(RenderPreviewHostProtocol.Validate(Open));
            this.Session=RenderPreviewRenderSession.Open(Open,Options);
        }
        internal string Render(RenderPreviewAnimation?Animation,bool ShowSkeleton=false,int SelectedJoint=-1,string?Snapshot=null,RenderPreviewObjectTransform[]?Objects=null,RenderPreviewPresentationMode Mode=RenderPreviewPresentationMode.Lit){
            var Frame=new RenderPreviewFrame { Camera=this.Camera,Animation=Animation,ShowSkeleton=ShowSkeleton,SelectedJoint=SelectedJoint,Objects=Objects??[],PresentationMode=Mode };
            Assert.Null(RenderPreviewHostProtocol.Validate(new RenderPreviewHostRequest {
                RequestId=Guid.NewGuid(),SessionId=Guid.NewGuid(),Sequence=2,Command=RenderPreviewHostCommand.UpdateFrame,Frame=Frame,
            }));
            this.Session.UpdateFrame(Frame);
            this.Session.RenderForVerification();
            Assert.Equal(0,this.Session.ErrorOrWorseMessageCount);
            if(Snapshot is not null){ PreviewFramePng.SaveIfRequested(this.Session.LastCapturedFrame!,Snapshot); }
            return Convert.ToHexString(SHA256.HashData(this.Session.LastCapturedFrame!.Pixels));
        }
        public void Dispose(){
            this.Session.Dispose();
            this.Window.Dispose();
        }
    }
    private static RenderPreviewMatrix4x4 ToWire(in Matrix4x4 Value)=>new(){ Values=[Value.M11,Value.M12,Value.M13,Value.M14,Value.M21,Value.M22,Value.M23,Value.M24,Value.M31,Value.M32,Value.M33,Value.M34,Value.M41,Value.M42,Value.M43,Value.M44] };
    [Fact]
    public void WARP_TheHostLoadsTheSkinAndReportsJointsAndClips(){
        if(!OperatingSystem.IsWindows()){ return; }
        using var Test=new Preview(new RenderPreviewAssetSource { MeshPath=Mesh,AnimationPath=Idle });
        var Summary=Test.Session.AssetSummary!;
        Assert.NotNull(Summary);
        Assert.True(Summary.JointNames.Length>20,"the mannequin rig has many joints");
        Assert.Equal(Summary.JointNames.Length,Summary.JointParents.Length);
        Assert.Contains(-1,Summary.JointParents);
        Assert.True(Summary.SkinnedMeshCount>0);
        var Bounds=Assert.IsType<RenderPreviewAssetBounds>(Summary.RestBounds);
        Assert.Equal(3,Bounds.Min.Length);
        Assert.Equal(3,Bounds.Max.Length);
        Assert.True(Bounds.Max[1]>Bounds.Min[1]);
        Assert.True(Bounds.VertexCount>100);
        Assert.True(Bounds.TriangleCount>100);
        var Clip=Assert.Single(Summary.Clips);
        Assert.True(Clip.DurationSeconds>0f);
        Assert.True(Clip.BoundTrackCount>0,"the animation-only file animates this skeleton's joints");
    }
    [Fact]
    public void WARP_AnimationTimeChangesThePoseAndTheRestPoseIsStable(){
        if(!OperatingSystem.IsWindows()){ return; }
        using var Test=new Preview(new RenderPreviewAssetSource { MeshPath=Mesh,AnimationPath=Idle });
        var Rest=Test.Render(new RenderPreviewAnimation { ClipIndex=-1 },Snapshot:"skeletal-rest");
        var Start=Test.Render(new RenderPreviewAnimation { ClipIndex=0,TimeSeconds=0f },Snapshot:"skeletal-idle-0");
        var Later=Test.Render(new RenderPreviewAnimation { ClipIndex=0,TimeSeconds=1.0f },Snapshot:"skeletal-idle-1");
        var RestAgain=Test.Render(new RenderPreviewAnimation { ClipIndex=-1 });
        Assert.Equal(Rest,RestAgain);
        Assert.NotEqual(Start,Later);
    }
    [Fact]
    public void WARP_TheSkeletonLinesAndTheSelectedJointChangeTheFrame(){
        if(!OperatingSystem.IsWindows()){ return; }
        using var Test=new Preview(new RenderPreviewAssetSource { MeshPath=Mesh });
        Assert.Empty(Test.Session.AssetSummary!.Clips);
        var Without=Test.Render(new RenderPreviewAnimation { ClipIndex=-1 });
        var With=Test.Render(new RenderPreviewAnimation { ClipIndex=-1 },ShowSkeleton:true,Snapshot:"skeletal-bones");
        var Selected=Test.Render(new RenderPreviewAnimation { ClipIndex=-1 },ShowSkeleton:true,SelectedJoint:5,Snapshot:"skeletal-bones-selected");
        Assert.NotEqual(Without,With);
        Assert.NotEqual(With,Selected);
    }
    [Fact]
    public void WARP_ReplacingThePreviewFloorKeepsTheLoadedAssetWithoutReadingItsSourceAgain(){
        if(!OperatingSystem.IsWindows()){ return; }
        var DirectoryPath=Path.Combine(Path.GetTempPath(),"CgePreviewReuse-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);
        try {
            var MeshPath=Path.Combine(DirectoryPath,"Mesh.glb");
            var AnimationPath=Path.Combine(DirectoryPath,"Idle.glb");
            File.Copy(Mesh,MeshPath);
            File.Copy(Idle,AnimationPath);
            using var Test=new Preview(new RenderPreviewAssetSource { MeshPath=MeshPath,AnimationPath=AnimationPath });
            var Animation=new RenderPreviewAnimation { ClipIndex=0,TimeSeconds=1f };
            var Before=Test.Render(Animation,ShowSkeleton:true,SelectedJoint:5);
            var Summary=Test.Session.AssetSummary;
            File.Delete(MeshPath);
            File.Delete(AnimationPath);
            var Scene=new RenderPreviewScene {
                Camera=Test.Camera,Asset=new RenderPreviewAssetSource { MeshPath=MeshPath,AnimationPath=AnimationPath },
                Meshes=[new RenderPreviewMesh {
                    Positions=[1000f,0f,0f,1001f,0f,0f,1000f,0f,1f],
                    Normals=[0f,1f,0f,0f,1f,0f,0f,1f,0f],Indices=[0,1,2],
                }],
                Materials=[new RenderPreviewMaterial { BaseColor=[0.4f,0.4f,0.4f,1f] }],
                Objects=[new RenderPreviewObject { ObjectId="Floor",MeshIndex=0,MaterialIndex=0 }],
            };
            Test.Session.ReplaceScene(Scene,string.Empty);
            Assert.Same(Summary,Test.Session.AssetSummary);
            Assert.Equal(Before,Test.Render(Animation,ShowSkeleton:true,SelectedJoint:5,Objects:[new RenderPreviewObjectTransform { ObjectId="Floor" }]));
            Test.Session.ReplaceScene(new RenderPreviewScene { Camera=Test.Camera,Asset=Scene.Asset },string.Empty);
            Assert.Equal(Before,Test.Render(Animation,ShowSkeleton:true,SelectedJoint:5));
        } finally {
            Directory.Delete(DirectoryPath,true);
        }
    }
    [Fact]
    public void WARP_SkeletalMeshDoesNotDependOnTheFirstPrimitiveBeingRenderable(){
        if(!OperatingSystem.IsWindows()){ return; }
        var Folder=Path.Combine(Path.GetTempPath(),"CgeSkeletalFirstPrimitive-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Folder);
        try {
            var Source=Path.Combine(Folder,"Mannequin.glb");
            File.WriteAllBytes(Source,WithLeadingPointPrimitive(File.ReadAllBytes(Mesh)));
            Assert.False(CSharpGameEngine.Import.Gltf.GltfImporter.Import(Source).Success);
            using var Test=new Preview(new RenderPreviewAssetSource { MeshPath=Source });
            Assert.True(Test.Session.AssetSummary!.SkinnedMeshCount>0);
            Assert.True(Test.Session.AssetSummary.RestBounds!.VertexCount>100);
            Test.Render(new RenderPreviewAnimation { ClipIndex=-1 });
        } finally { Directory.Delete(Folder,true); }
    }
    private static byte[]WithLeadingPointPrimitive(byte[]Source){
        var JsonLength=System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(Source.AsSpan(12,4));
        var Document=System.Text.Json.Nodes.JsonNode.Parse(Source.AsSpan(20,JsonLength))!;
        var Primitives=Document["meshes"]![0]!["primitives"]!.AsArray();
        var Point=Primitives[0]!.DeepClone();
        Point["mode"]=0;
        Primitives.Insert(0,Point);
        var Json=System.Text.Encoding.UTF8.GetBytes(Document.ToJsonString());
        var Padded=(Json.Length+3)&~3;
        var BinaryOffset=20+JsonLength;
        var Result=new byte[20+Padded+Source.Length-BinaryOffset];
        Array.Copy(Source,Result,20);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(Result.AsSpan(8,4),Result.Length);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(Result.AsSpan(12,4),Padded);
        Array.Copy(Json,0,Result,20,Json.Length);
        Result.AsSpan(20+Json.Length,Padded-Json.Length).Fill(0x20);
        Array.Copy(Source,BinaryOffset,Result,20+Padded,Source.Length-BinaryOffset);
        return Result;
    }
    [Fact]
    public void WARP_StaticBoundsIncludeEveryTransformedNode(){
        if(!OperatingSystem.IsWindows()){ return; }
        var Folder=Path.Combine(Path.GetTempPath(),"CgeStaticBounds-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Folder);
        try {
            var Bytes=new byte[42];
            float[]Positions=[0f,0f,0f,1f,0f,0f,0f,1f,0f];
            for(var Index=0;Index<Positions.Length;Index++){ System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(Bytes.AsSpan(Index*4,4),Positions[Index]); }
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(Bytes.AsSpan(36,2),0);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(Bytes.AsSpan(38,2),1);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(Bytes.AsSpan(40,2),2);
            var PathValue=Path.Combine(Folder,"TwoNodes.gltf");
            File.WriteAllText(PathValue,$$"""
                {"asset":{"version":"2.0"},"scene":0,"scenes":[{"nodes":[0,1]}],
                "nodes":[{"mesh":0,"translation":[-4,0,0]},{"mesh":0,"translation":[3,2,0],"scale":[2,2,1]}],
                "meshes":[{"primitives":[{"attributes":{"POSITION":0},"indices":1,"mode":0},{"attributes":{"POSITION":0},"indices":1},{"attributes":{"POSITION":0},"indices":1}]}],
                "buffers":[{"uri":"data:application/octet-stream;base64,{{Convert.ToBase64String(Bytes)}}","byteLength":42}],
                "bufferViews":[{"buffer":0,"byteOffset":0,"byteLength":36},{"buffer":0,"byteOffset":36,"byteLength":6}],
                "accessors":[{"bufferView":0,"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[1,1,0]},{"bufferView":1,"componentType":5123,"count":3,"type":"SCALAR"}]}
                """);
            Assert.False(CSharpGameEngine.Import.Gltf.GltfImporter.Import(PathValue).Success);
            using var Test=new Preview(null,PathValue);
            var Bounds=Assert.IsType<RenderPreviewAssetBounds>(Test.Session.StaticBounds);
            Assert.Equal(new[]{ -4f,0f,0f },Bounds.Min);
            Assert.Equal(new[]{ 5f,4f,0f },Bounds.Max);
            Assert.Equal(6,Bounds.VertexCount);
            Assert.Equal(2,Bounds.TriangleCount);
            Test.Session.ReplaceScene(new RenderPreviewScene { Camera=Test.Camera,StaticAssetPath=PathValue },string.Empty);
            Assert.Equal(Bounds.Min,Test.Session.StaticBounds!.Min);
            Assert.Equal(Bounds.Max,Test.Session.StaticBounds!.Max);
        } finally { Directory.Delete(Folder,true); }
    }
    [Fact]
    public void WARP_StaticTexturedAssetRendersAndSurvivesFloorReplacementWithoutReloading(){
        if(!OperatingSystem.IsWindows()){ return; }
        var DirectoryPath=Path.Combine(Path.GetTempPath(),"CgeStaticPreview-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);
        try {
            var Source=Path.Combine(DirectoryPath,"BoxTextured.glb");
            File.Copy(Path.Combine(RepositoryRoot(),"assets","TestFixtures","Gltf","BoxTextured.glb"),Source);
            using var Test=new Preview(null,Source);
            var Before=Test.Render(null,Mode:RenderPreviewPresentationMode.BaseColor);
            var Pixels=Test.Session.LastCapturedFrame!.Pixels;
            var Colors=new HashSet<uint>();
            for(var Index=0;Index+3<Pixels.Length;Index+=4){ Colors.Add(BitConverter.ToUInt32(Pixels,Index)); }
            Assert.True(Colors.Count>10,"the textured mesh must render more than a flat material and background");
            File.Delete(Source);
            var Scene=new RenderPreviewScene {
                Camera=Test.Camera,StaticAssetPath=Source,
                Meshes=[new RenderPreviewMesh { Positions=[1000f,0f,0f,1001f,0f,0f,1000f,0f,1f],Normals=[0f,1f,0f,0f,1f,0f,0f,1f,0f],Indices=[0,1,2] }],
                Materials=[new RenderPreviewMaterial { BaseColor=[0.4f,0.4f,0.4f,1f] }],
                Objects=[new RenderPreviewObject { ObjectId="Floor",MeshIndex=0,MaterialIndex=0 }],
            };
            Test.Session.ReplaceScene(Scene,string.Empty);
            Assert.Equal(Before,Test.Render(null,Objects:[new RenderPreviewObjectTransform { ObjectId="Floor" }],Mode:RenderPreviewPresentationMode.BaseColor));
            Test.Session.ReplaceScene(new RenderPreviewScene { Camera=Test.Camera,StaticAssetPath=Source },string.Empty);
            Assert.Equal(Before,Test.Render(null,Mode:RenderPreviewPresentationMode.BaseColor));
        } finally { Directory.Delete(DirectoryPath,true); }
    }
    [Fact]
    public void WARP_AMeshWithoutASkinIsRejectedWithAReason(){
        if(!OperatingSystem.IsWindows()){ return; }
        var Error=Assert.ThrowsAny<Exception>(()=>{ using var _=new Preview(new RenderPreviewAssetSource { MeshPath=Idle }); });
        Assert.Contains("skin",Error.Message,StringComparison.OrdinalIgnoreCase);
    }
}
