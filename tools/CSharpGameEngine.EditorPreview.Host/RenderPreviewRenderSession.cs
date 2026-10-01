using System.Numerics;
using System.Security.Cryptography;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Rendering;
using CSharpGameEngine.Runtime.Windows;

namespace CSharpGameEngine.EditorPreview.Host;

/// <summary>
/// The isolated host's concrete preview session. It owns exactly one common Render Server and
/// converts only validated wire DTOs into provider-boundary inputs.
/// </summary>
public sealed class RenderPreviewRenderSession:IRenderPreviewSession {
    private readonly nint SurfaceHandle;
    private readonly GraphicsDeviceOptions DeviceOptions;
    private readonly string ShaderArtifactPath;
    private RenderServer?Server;
    private Dictionary<string,int>ObjectIndices;
    private RenderOverlayVertex[]OverlayTriangleBuffer=[];
    private RenderOverlayVertex[]OverlayLineBuffer=[];
    private readonly RenderCameraPreview[]CameraPreviewBuffer=
        new RenderCameraPreview[RenderPreviewHostProtocol.MaximumCameraPreviewCount];
    private RenderPreviewCameraDiagnostics[]CameraPreviewDiagnosticsValue=[];
    private int Width;
    private int Height;

    private RenderPreviewRenderSession(
        nint SurfaceHandle,int Width,int Height,GraphicsDeviceOptions DeviceOptions,
        string ShaderArtifactPath,RenderServer Server,Dictionary<string,int>ObjectIndices){
        this.SurfaceHandle=SurfaceHandle;
        this.Width=Width;
        this.Height=Height;
        this.DeviceOptions=DeviceOptions;
        this.ShaderArtifactPath=ShaderArtifactPath;
        this.Server=Server;
        this.ObjectIndices=ObjectIndices;
    }

    public uint DeviceGeneration=>this.Active.DeviceSnapshot.Generation;
    public ulong CompletedFence=>this.Active.CompletedFence;
    public PixelReadbackResult?LastCapturedFrame=>this.Active.LastCapturedFrame;
    public int ErrorOrWorseMessageCount=>this.Active.ErrorOrWorseMessageCount;
    public string LastFrameHash{get;private set;}=string.Empty;
    public RenderPreviewCameraDiagnostics[]CameraPreviewDiagnostics=>
        this.CameraPreviewDiagnosticsValue;

    private RenderServer Active=>this.Server
        ??throw new ObjectDisposedException(nameof(RenderPreviewRenderSession));

    public static RenderPreviewRenderSession Open(
        RenderPreviewHostRequest Request,GraphicsDeviceOptions?DeviceOptions=null){
        ArgumentNullException.ThrowIfNull(Request);
        string?Error=RenderPreviewHostProtocol.Validate(Request);
        if(Error is not null){ throw new ArgumentException(Error,nameof(Request)); }
        if(Request.Command!=RenderPreviewHostCommand.Open){
            throw new ArgumentException("A render preview session can only be opened by an Open request.",nameof(Request));
        }
        GraphicsDeviceOptions OwnedOptions=CloneOptions(
            DeviceOptions??GraphicsDeviceOptionsResolver.FromEnvironment());
        RenderServer Server=CreateServer(
            (nint)Request.SurfaceHandle,Request.Width,Request.Height,OwnedOptions,
            Request.ShaderArtifactPath,Request.Scene!);
        return new RenderPreviewRenderSession(
            (nint)Request.SurfaceHandle,Request.Width,Request.Height,OwnedOptions,
            Path.GetFullPath(Request.ShaderArtifactPath),Server,
            BuildObjectIndices(Request.Scene!));
    }

    public void ReplaceScene(RenderPreviewScene Scene,string SceneJson){
        ArgumentNullException.ThrowIfNull(Scene);
        RenderPreviewHostRequest ValidationRequest=new(){
            RequestId=Guid.NewGuid(),
            SessionId=Guid.NewGuid(),
            Sequence=1,
            Command=RenderPreviewHostCommand.Open,
            SurfaceHandle=this.SurfaceHandle,
            Width=this.Width,
            Height=this.Height,
            ShaderArtifactPath=this.ShaderArtifactPath,
            Scene=Scene,
        };
        string?Error=RenderPreviewHostProtocol.Validate(ValidationRequest);
        if(Error is not null){ throw new ArgumentException(Error,nameof(Scene)); }

        Dictionary<string,int>ReplacementIndices=BuildObjectIndices(Scene);
        RenderServer Prior=this.Active;
        this.Server=null;
        Prior.Dispose();
        this.LastFrameHash=string.Empty;
        this.CameraPreviewDiagnosticsValue=[];
        RenderServer Replacement=CreateServer(
            this.SurfaceHandle,this.Width,this.Height,this.DeviceOptions,
            this.ShaderArtifactPath,Scene);
        try {
            // StageRenderer construction may clear its swap chain. Present the complete replacement
            // before publishing it to the session so the HWND never waits a later WPF render tick
            // while showing that initialization clear.
            Replacement.RenderFrame(CaptureForVerification:false);
            this.Server=Replacement;
        } catch {
            Replacement.Dispose();
            throw;
        }
        this.ObjectIndices=ReplacementIndices;
    }

    public void UpdateFrame(RenderPreviewFrame Frame){
        ArgumentNullException.ThrowIfNull(Frame);
        RenderPreviewHostRequest ValidationRequest=new(){
            RequestId=Guid.NewGuid(),
            SessionId=Guid.NewGuid(),
            Sequence=1,
            Command=RenderPreviewHostCommand.UpdateFrame,
            Frame=Frame,
        };
        string?Error=RenderPreviewHostProtocol.Validate(ValidationRequest);
        if(Error is not null){ throw new ArgumentException(Error,nameof(Frame)); }
        if(Frame.Objects.Length!=this.ObjectIndices.Count){
            throw new InvalidOperationException(
                $"Frame contains {Frame.Objects.Length} objects; session roster contains {this.ObjectIndices.Count}.");
        }
        RenderServer Active=this.Active;
        Active.SetViewProjection(
            ToMatrix(Frame.Camera!.ViewProjection),ToVector3(Frame.Camera.Position));
        Active.SetPresentationMode(Frame.PresentationMode switch {
            RenderPreviewPresentationMode.Lit=>RenderPresentationMode.Lit,
            RenderPreviewPresentationMode.BaseColor=>RenderPresentationMode.BaseColor,
            RenderPreviewPresentationMode.Normal=>RenderPresentationMode.Normal,
            RenderPreviewPresentationMode.Wireframe=>RenderPresentationMode.Wireframe,
            _=>throw new ArgumentOutOfRangeException(nameof(Frame)),
        });
        foreach(RenderPreviewObjectTransform Object in Frame.Objects){
            if(!this.ObjectIndices.TryGetValue(Object.ObjectId,out int ObjectIndex)){
                throw new InvalidOperationException($"Frame object is not in the session roster: {Object.ObjectId}.");
            }
            Active.SetObjectTransform(ObjectIndex,ToMatrix(Object.World));
            Active.SetObjectSelectionWeight(ObjectIndex,Object.SelectionWeight);
        }
        ConvertOverlay(Frame.OverlayTriangles,ref this.OverlayTriangleBuffer);
        ConvertOverlay(Frame.OverlayLines,ref this.OverlayLineBuffer);
        Active.SetOverlay(
            this.OverlayTriangleBuffer.AsSpan(0,Frame.OverlayTriangles.Length),
            this.OverlayLineBuffer.AsSpan(0,Frame.OverlayLines.Length));
        for(int Index=0;Index<Frame.CameraPreviews.Length;Index++){
            RenderPreviewCameraPass Source=Frame.CameraPreviews[Index];
            int ExcludedObjectIndex=-1;
            if(Source.ExcludedObjectId.Length>0
                &&!this.ObjectIndices.TryGetValue(
                    Source.ExcludedObjectId,out ExcludedObjectIndex)){
                throw new InvalidOperationException(
                    $"Camera preview excluded object is not in the session roster: {Source.ExcludedObjectId}.");
            }
            this.CameraPreviewBuffer[Index]=new RenderCameraPreview(
                Source.X,Source.Y,Source.Width,Source.Height,
                ToMatrix(Source.Camera!.ViewProjection),
                ToVector3(Source.Camera.Position),ExcludedObjectIndex);
        }
        Active.SetCameraPreviews(
            this.CameraPreviewBuffer.AsSpan(0,Frame.CameraPreviews.Length));
        RenderCameraPreviewDiagnostics[]Diagnostics=
            Active.GetCameraPreviewDiagnostics();
        if(this.CameraPreviewDiagnosticsValue.Length!=Diagnostics.Length){
            this.CameraPreviewDiagnosticsValue=
                new RenderPreviewCameraDiagnostics[Diagnostics.Length];
        }
        for(int Index=0;Index<Diagnostics.Length;Index++){
            RenderCameraPreviewDiagnostics Source=Diagnostics[Index];
            this.CameraPreviewDiagnosticsValue[Index]=
                new RenderPreviewCameraDiagnostics {
                    SceneObjectCount=Source.SceneObjectCount,
                    FrustumCandidateCount=Source.FrustumCandidateCount,
                    DrawCallCount=Source.DrawCallCount,
                    ViewProjectionHash=Source.ViewProjectionHash,
                };
        }
        this.LastFrameHash=string.Empty;
    }

    public void Resize(int Width,int Height){
        this.Active.Resize(Width,Height);
        this.Width=Width;
        this.Height=Height;
        this.LastFrameHash=string.Empty;
    }

    public void Render(bool CaptureForVerification){
        this.Active.RenderFrame(CaptureForVerification);
        if(CaptureForVerification&&this.Active.LastCapturedFrame is { }Frame){
            this.LastFrameHash=Convert.ToHexString(SHA256.HashData(Frame.Pixels));
        }
    }

    public void RenderForVerification()=>this.Render(CaptureForVerification:true);

    public void Dispose(){
        RenderServer?Owned=Interlocked.Exchange(ref this.Server,null);
        Owned?.Dispose();
    }

    private static RenderServer CreateServer(
        nint SurfaceHandle,int Width,int Height,GraphicsDeviceOptions DeviceOptions,
        string ShaderArtifactPath,RenderPreviewScene Scene){
        bool EmptyScene=Scene.Objects.Length==0;
        StageMeshCreateInfo[]Meshes=EmptyScene
            ?[CreateEmptySentinelMesh()]
            :new StageMeshCreateInfo[Scene.Meshes.Length];
        StageMaterialCreateInfo[]Materials=EmptyScene
            ?[new StageMaterialCreateInfo {
                BaseColorFactor=Vector4.Zero,
                RoughnessFactor=1f,
                MetallicFactor=0f,
            }]
            :new StageMaterialCreateInfo[Scene.Materials.Length];
        StageObjectCreateInfo[]Objects=EmptyScene
            ?[new StageObjectCreateInfo { MeshIndex=0,MaterialIndex=0 }]
            :new StageObjectCreateInfo[Scene.Objects.Length];
        if(!EmptyScene){
            for(int Index=0;Index<Meshes.Length;Index++){
                Meshes[Index]=ConvertMesh(Scene.Meshes[Index]);
            }
            for(int Index=0;Index<Materials.Length;Index++){
                float[]Color=Scene.Materials[Index].BaseColor;
                Materials[Index]=new StageMaterialCreateInfo {
                    BaseColorFactor=new Vector4(
                        Color[0],Color[1],Color[2],Color[3]),
                    RoughnessFactor=1f,
                    MetallicFactor=0f,
                };
            }
            for(int Index=0;Index<Objects.Length;Index++){
                RenderPreviewObject Source=Scene.Objects[Index];
                Objects[Index]=new StageObjectCreateInfo {
                    MeshIndex=Source.MeshIndex,
                    MaterialIndex=Source.MaterialIndex,
                };
            }
        }

        RenderServer Server=RenderServer.Create(
            SurfaceHandle,Width,Height,DeviceOptions,ShaderArtifactPath,
            Meshes,Materials,Objects);
        try {
            Server.SetViewProjection(
                ToMatrix(Scene.Camera!.ViewProjection),ToVector3(Scene.Camera.Position));
            Server.SetAmbientLight(new Vector3(0.4f));
            Server.SetDirectionalLight(new CSharpGameEngine.Rendering.DirectionalLightSubmission(Vector3.UnitY,new CSharpGameEngine.Lighting.LinearLightColor(1f,1f,1f),new CSharpGameEngine.Lighting.Illuminance(2f),new CSharpGameEngine.Lighting.LightShadowSettings(0,new CSharpGameEngine.Lighting.ShadowBias(0.0015f))));
            for(int Index=0;Index<Scene.Objects.Length;Index++){
                Server.SetObjectTransform(Index,ToMatrix(Scene.Objects[Index].World));
            }
            return Server;
        } catch {
            Server.Dispose();
            throw;
        }
    }

    private static StageMeshCreateInfo CreateEmptySentinelMesh()=>new(){
        Positions=[Vector3.Zero,Vector3.Zero,Vector3.Zero],
        Normals=[Vector3.UnitY,Vector3.UnitY,Vector3.UnitY],
        Tangents=[
            new Vector4(Vector3.UnitX,1f),
            new Vector4(Vector3.UnitX,1f),
            new Vector4(Vector3.UnitX,1f),
        ],
        Uvs=[Vector2.Zero,Vector2.Zero,Vector2.Zero],
        Indices=[0,1,2],
    };

    private static StageMeshCreateInfo ConvertMesh(RenderPreviewMesh Source){
        int VertexCount=Source.Positions.Length/3;
        Vector3[]Positions=new Vector3[VertexCount];
        Vector3[]Normals=new Vector3[VertexCount];
        Vector4[]Tangents=new Vector4[VertexCount];
        Vector2[]Uvs=new Vector2[VertexCount];
        for(int Index=0;Index<VertexCount;Index++){
            Positions[Index]=Vector3From(Source.Positions,Index);
            Vector3 Normal=Vector3From(Source.Normals,Index);
            Normals[Index]=Normal;
            Vector3 Axis=MathF.Abs(Normal.Y)<0.999f?Vector3.UnitY:Vector3.UnitX;
            Vector3 Tangent=Vector3.Normalize(Vector3.Cross(Axis,Normal));
            Tangents[Index]=new Vector4(Tangent,1f);
        }
        return new StageMeshCreateInfo {
            Positions=Positions,
            Normals=Normals,
            Tangents=Tangents,
            Uvs=Uvs,
            Indices=[..Source.Indices],
        };
    }

    private static Vector3 Vector3From(float[]Values,int Index){
        int Offset=Index*3;
        return new Vector3(Values[Offset],Values[Offset+1],Values[Offset+2]);
    }

    private static void ConvertOverlay(
        RenderPreviewOverlayVertex[]Source,ref RenderOverlayVertex[]Destination){
        if(Destination.Length<Source.Length){
            Array.Resize(ref Destination,Source.Length);
        }
        for(int Index=0;Index<Source.Length;Index++){
            RenderPreviewOverlayVertex Vertex=Source[Index];
            Destination[Index]=new RenderOverlayVertex(
                new Vector3(
                    Vertex.Position[0],Vertex.Position[1],Vertex.Position[2]),
                new Vector4(
                    Vertex.Color[0],Vertex.Color[1],Vertex.Color[2],Vertex.Color[3]));
        }
    }

    private static Matrix4x4 ToMatrix(RenderPreviewMatrix4x4 Source){
        float[]V=Source.Values;
        return new Matrix4x4(
            V[0],V[1],V[2],V[3],
            V[4],V[5],V[6],V[7],
            V[8],V[9],V[10],V[11],
            V[12],V[13],V[14],V[15]);
    }

    private static Vector3 ToVector3(float[]Values)=>
        new(Values[0],Values[1],Values[2]);

    private static GraphicsDeviceOptions CloneOptions(GraphicsDeviceOptions Source)=>new(){
        UseWarp=Source.UseWarp,
        EnableDebugLayer=Source.EnableDebugLayer,
        EnableGpuBasedValidation=Source.EnableGpuBasedValidation,
    };

    private static Dictionary<string,int>BuildObjectIndices(RenderPreviewScene Scene){
        Dictionary<string,int>Result=new(StringComparer.Ordinal);
        for(int Index=0;Index<Scene.Objects.Length;Index++){
            Result.Add(Scene.Objects[Index].ObjectId,Index);
        }
        return Result;
    }
}
