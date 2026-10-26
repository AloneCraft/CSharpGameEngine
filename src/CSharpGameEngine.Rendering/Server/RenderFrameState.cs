using CSharpGameEngine.Rendering.Pipeline;
using System.Numerics;
using CSharpGameEngine.Rendering.Backend;
using CSharpGameEngine.Rendering;

namespace CSharpGameEngine.Rendering.Server;

/// <summary>
/// Reusable provider-boundary snapshot for the commands that form one complete frame. Two
/// instances are swapped by RenderServer, so completed state survives device replacement without
/// allocating a new command graph every frame.
/// </summary>
internal sealed class RenderFrameState {
    private Matrix4x4[]ObjectTransforms;
    private bool[]ObjectTransformSet;
    private readonly List<int>TouchedObjectIndices=[];
    private float[]ObjectSelectionWeights;
    private bool[]ObjectSelectionWeightSet;
    private readonly List<int>TouchedSelectionIndices=[];
    private bool[]ObjectVisibleInView;
    private bool[]ObjectCastsShadow;
    private bool[]ObjectVisibilitySet;
    private readonly List<int>TouchedVisibilityIndices=[];
    private float[]ObjectEmissiveScales;
    private bool[]ObjectEmissiveScaleSet;
    private readonly List<int>TouchedEmissiveIndices=[];
    private readonly Dictionary<int,Matrix4x4[]>SkinPalettes=[];
    private readonly Dictionary<int,int>SkinPaletteLengths=[];
    private readonly List<int>TouchedSkinMeshes=[];
    private readonly List<DirectionalLightSubmission>DirectionalLights=[];
    private readonly List<PointLightSubmission>PointLights=[];
    private readonly List<SpotLightSubmission>SpotLights=[];
    private readonly List<RectAreaLightSubmission>RectLights=[];
    private readonly List<EmissiveCommand>EmissiveLights=[];
    private readonly List<LightCommandIndex>LightOrder=[];
    private readonly Dictionary<int,Vector3>WorldGridAnchors=[];
    private RenderViewSource ViewSource;
    private Vector3 AmbientLight;
    private FogParams Fog;
    private EnvironmentLightingSettings Environment;
    private float CameraAltitude;
    private float RenderOriginAltitude;
    private bool HasEnvironment;
    private bool HasCameraAltitude;
    private bool HasRenderOriginAltitude;
    private RenderPresentationMode PresentationMode;
    private bool HasAmbientLight;
    private bool HasFog;
    private bool HasPresentationMode;
    private RenderOverlayVertex[]OverlayTriangles=[];
    private RenderOverlayVertex[]OverlayLines=[];
    private int OverlayTriangleCount;
    private int OverlayLineCount;
    private bool HasOverlay;
    private RenderScreenVertex[]ScreenVertices=[];
    private RenderScreenBatch[]ScreenBatches=[];
    private int ScreenVertexCount;
    private int ScreenBatchCount;
    private bool HasScreenLayer;
    private RenderCameraPreview[]CameraPreviews=[];
    private int CameraPreviewCount;
    private bool HasCameraPreviews;

    public RenderFrameState(int ObjectCount){
        ArgumentOutOfRangeException.ThrowIfNegative(ObjectCount);
        this.ObjectTransforms=new Matrix4x4[ObjectCount];
        this.ObjectTransformSet=new bool[ObjectCount];
        this.ObjectSelectionWeights=new float[ObjectCount];
        this.ObjectSelectionWeightSet=new bool[ObjectCount];
        this.ObjectVisibleInView=new bool[ObjectCount];
        this.ObjectCastsShadow=new bool[ObjectCount];
        this.ObjectVisibilitySet=new bool[ObjectCount];
        this.ObjectEmissiveScales=new float[ObjectCount];
        this.ObjectEmissiveScaleSet=new bool[ObjectCount];
    }

    /// <summary>Objects added after creation (M0105) get indices past the first capacity, and a rebuilt renderer may hold more slots than the server first did.</summary>
    private void EnsureObjectCapacity(int Required){
        if(this.ObjectTransforms.Length>=Required){ return; }
        var Length=Math.Max(Required,this.ObjectTransforms.Length*2);
        Array.Resize(ref this.ObjectTransforms,Length);
        Array.Resize(ref this.ObjectTransformSet,Length);
        Array.Resize(ref this.ObjectSelectionWeights,Length);
        Array.Resize(ref this.ObjectSelectionWeightSet,Length);
        Array.Resize(ref this.ObjectVisibleInView,Length);
        Array.Resize(ref this.ObjectCastsShadow,Length);
        Array.Resize(ref this.ObjectVisibilitySet,Length);
        Array.Resize(ref this.ObjectEmissiveScales,Length);
        Array.Resize(ref this.ObjectEmissiveScaleSet,Length);
    }

    public bool IsEmpty=>
        !this.ViewSource.HasValue&&!this.HasAmbientLight&&!this.HasFog
        &&!this.HasEnvironment&&!this.HasCameraAltitude&&!this.HasRenderOriginAltitude
        &&this.WorldGridAnchors.Count==0
        &&!this.HasPresentationMode
        &&!this.HasOverlay
        &&!this.HasScreenLayer
        &&!this.HasCameraPreviews
        &&this.LightOrder.Count==0
        &&this.TouchedObjectIndices.Count==0&&this.TouchedSelectionIndices.Count==0&&this.TouchedVisibilityIndices.Count==0&&this.TouchedEmissiveIndices.Count==0
        &&this.TouchedSkinMeshes.Count==0;

    public void RecordViewSource(in RenderViewSource Source){
        if(!Source.HasValue){ throw new ArgumentException("A recorded view source must be initialized.",nameof(Source)); }
        this.ViewSource=Source;
    }

    public void RecordSetDirectional(in DirectionalLightSubmission Value){
        this.LightOrder.Add(new(LightCommandKind.SetDirectional,this.DirectionalLights.Count));
        this.DirectionalLights.Add(Value);
    }
    public void RecordAddDirectional(in DirectionalLightSubmission Value){
        this.LightOrder.Add(new(LightCommandKind.Directional,this.DirectionalLights.Count));
        this.DirectionalLights.Add(Value);
    }
    public void RecordAmbient(Vector3 Color){
        this.AmbientLight=Color;
        this.HasAmbientLight=true;
    }

    public void RecordFog(FogParams Fog){
        this.Fog=Fog;
        this.HasFog=true;
    }

    public void RecordEnvironment(in EnvironmentLightingSettings Settings){
        this.Environment=Settings;
        this.HasEnvironment=true;
    }

    public void RecordCameraAltitude(float Meters){
        this.CameraAltitude=Meters;
        this.HasCameraAltitude=true;
    }

    public void RecordRenderOriginAltitude(float Meters){
        this.RenderOriginAltitude=Meters;
        this.HasRenderOriginAltitude=true;
    }

    public void RecordWorldGridAnchor(int MaterialIndex,Vector3 Anchor){
        this.WorldGridAnchors[MaterialIndex]=Anchor;
    }

    public void RecordPoint(in PointLightSubmission Value){
        this.LightOrder.Add(new(LightCommandKind.Point,this.PointLights.Count));
        this.PointLights.Add(Value);
    }
    public void RecordSpot(in SpotLightSubmission Value){
        this.LightOrder.Add(new(LightCommandKind.Spot,this.SpotLights.Count));
        this.SpotLights.Add(Value);
    }
    public void RecordArea(in RectAreaLightSubmission Value){
        this.LightOrder.Add(new(LightCommandKind.Rect,this.RectLights.Count));
        this.RectLights.Add(Value);
    }
    public void RecordEmissive(IReadOnlyList<EmissivePatch>Patches,float RangeMeters,bool CastsShadows,int ShadowPriority,float ShadowBiasMeters){
        for(var Index=0;Index<Patches.Count;Index++){
            var Patch=Patches[Index];
            this.LightOrder.Add(new(LightCommandKind.Emissive,this.EmissiveLights.Count));
            this.EmissiveLights.Add(new(Patch,RangeMeters,CastsShadows,ShadowPriority,ShadowBiasMeters));
        }
    }
    public void RecordObjectTransform(int ObjectIndex,in Matrix4x4 Transform){
        ArgumentOutOfRangeException.ThrowIfNegative(ObjectIndex);
        this.EnsureObjectCapacity(ObjectIndex+1);
        this.ObjectTransforms[ObjectIndex]=Transform;
        if(this.ObjectTransformSet[ObjectIndex]){ return; }
        this.ObjectTransformSet[ObjectIndex]=true;
        this.TouchedObjectIndices.Add(ObjectIndex);
    }

    public void RecordSkinPalette(int MeshIndex,ReadOnlySpan<Matrix4x4>Palette){
        if(!this.SkinPalettes.TryGetValue(MeshIndex,out var Stored)||Stored.Length<Palette.Length){
            Stored=new Matrix4x4[Palette.Length];
            this.SkinPalettes[MeshIndex]=Stored;
        }
        Palette.CopyTo(Stored);
        if(!this.SkinPaletteLengths.ContainsKey(MeshIndex)){
            this.TouchedSkinMeshes.Add(MeshIndex);
        }
        this.SkinPaletteLengths[MeshIndex]=Palette.Length;
    }

    public void RecordPresentationMode(RenderPresentationMode Mode){
        this.PresentationMode=Mode;
        this.HasPresentationMode=true;
    }

    public void RecordObjectSelectionWeight(int ObjectIndex,float Weight){
        ArgumentOutOfRangeException.ThrowIfNegative(ObjectIndex);
        this.EnsureObjectCapacity(ObjectIndex+1);
        this.ObjectSelectionWeights[ObjectIndex]=Weight;
        if(this.ObjectSelectionWeightSet[ObjectIndex]){ return; }
        this.ObjectSelectionWeightSet[ObjectIndex]=true;
        this.TouchedSelectionIndices.Add(ObjectIndex);
    }

    public void RecordObjectVisibility(int ObjectIndex,bool VisibleInView,bool CastsShadow){
        ArgumentOutOfRangeException.ThrowIfNegative(ObjectIndex);
        this.EnsureObjectCapacity(ObjectIndex+1);
        this.ObjectVisibleInView[ObjectIndex]=VisibleInView;
        this.ObjectCastsShadow[ObjectIndex]=CastsShadow;
        if(this.ObjectVisibilitySet[ObjectIndex]){ return; }
        this.ObjectVisibilitySet[ObjectIndex]=true;
        this.TouchedVisibilityIndices.Add(ObjectIndex);
    }

    /// <summary>ADR-0078/M0109: the factor on one object's material emission (<see cref="IStageRenderBackend.SetObjectEmissiveScale"/>).</summary>
    public void RecordObjectEmissiveScale(int ObjectIndex,float Scale){
        ArgumentOutOfRangeException.ThrowIfNegative(ObjectIndex);
        this.EnsureObjectCapacity(ObjectIndex+1);
        this.ObjectEmissiveScales[ObjectIndex]=Scale;
        if(this.ObjectEmissiveScaleSet[ObjectIndex]){ return; }
        this.ObjectEmissiveScaleSet[ObjectIndex]=true;
        this.TouchedEmissiveIndices.Add(ObjectIndex);
    }

    public void RecordOverlay(
        ReadOnlySpan<RenderOverlayVertex>Triangles,
        ReadOnlySpan<RenderOverlayVertex>Lines){
        if(this.OverlayTriangles.Length<Triangles.Length){
            this.OverlayTriangles=new RenderOverlayVertex[Triangles.Length];
        }
        if(this.OverlayLines.Length<Lines.Length){
            this.OverlayLines=new RenderOverlayVertex[Lines.Length];
        }
        Triangles.CopyTo(this.OverlayTriangles);
        Lines.CopyTo(this.OverlayLines);
        this.OverlayTriangleCount=Triangles.Length;
        this.OverlayLineCount=Lines.Length;
        this.HasOverlay=true;
    }

    public void RecordScreenLayer(ReadOnlySpan<RenderScreenVertex>Vertices,ReadOnlySpan<RenderScreenBatch>Batches){
        if(this.ScreenVertices.Length<Vertices.Length){
            this.ScreenVertices=new RenderScreenVertex[Vertices.Length];
        }
        if(this.ScreenBatches.Length<Batches.Length){
            this.ScreenBatches=new RenderScreenBatch[Batches.Length];
        }
        Vertices.CopyTo(this.ScreenVertices);
        Batches.CopyTo(this.ScreenBatches);
        this.ScreenVertexCount=Vertices.Length;
        this.ScreenBatchCount=Batches.Length;
        this.HasScreenLayer=true;
    }

    public void RecordCameraPreviews(ReadOnlySpan<RenderCameraPreview>Previews){
        if(this.CameraPreviews.Length<Previews.Length){
            this.CameraPreviews=new RenderCameraPreview[Previews.Length];
        }
        Previews.CopyTo(this.CameraPreviews);
        this.CameraPreviewCount=Previews.Length;
        this.HasCameraPreviews=true;
    }

    /// <summary>
    /// Carries the renderer's persistent settings forward from the previous completed frame when this
    /// frame did not set them. The backend keeps ambient, fog, environment, camera and render origin
    /// altitudes, world grid anchors and presentation mode until they change, so a recovery replay must
    /// restore a value set once (the Scene view sets its ambient only when the preview is created) and
    /// not only the last frame's.
    /// </summary>
    public void InheritPersistentSettings(RenderFrameState Previous){
        ArgumentNullException.ThrowIfNull(Previous);
        if(!this.HasAmbientLight&&Previous.HasAmbientLight){ this.RecordAmbient(Previous.AmbientLight); }
        if(!this.HasFog&&Previous.HasFog){ this.RecordFog(Previous.Fog); }
        if(!this.HasEnvironment&&Previous.HasEnvironment){ this.RecordEnvironment(Previous.Environment); }
        if(!this.HasCameraAltitude&&Previous.HasCameraAltitude){ this.RecordCameraAltitude(Previous.CameraAltitude); }
        if(!this.HasRenderOriginAltitude&&Previous.HasRenderOriginAltitude){ this.RecordRenderOriginAltitude(Previous.RenderOriginAltitude); }
        foreach(var (MaterialIndex,Anchor) in Previous.WorldGridAnchors){
            this.WorldGridAnchors.TryAdd(MaterialIndex,Anchor);
        }
        if(!this.HasPresentationMode&&Previous.HasPresentationMode){ this.RecordPresentationMode(Previous.PresentationMode); }
    }

    internal bool TryGetAmbientLight(out Vector3 Color){
        Color=this.AmbientLight;
        return this.HasAmbientLight;
    }

    internal bool TryGetFog(out FogParams Fog){
        Fog=this.Fog;
        return this.HasFog;
    }

    internal bool TryGetRenderOriginAltitude(out float Meters){
        Meters=this.RenderOriginAltitude;
        return this.HasRenderOriginAltitude;
    }

    internal bool TryGetWorldGridAnchor(int MaterialIndex,out Vector3 Anchor)=>
        this.WorldGridAnchors.TryGetValue(MaterialIndex,out Anchor);

    public void Replay(IStageRenderBackend Renderer,int Width,int Height){
        ArgumentNullException.ThrowIfNull(Renderer);
        if(this.ViewSource.HasValue){ Renderer.SetView(this.ViewSource.Resolve(Width,Height)); }
        if(this.HasAmbientLight){ Renderer.SetAmbientLight(this.AmbientLight); }
        if(this.HasFog){ Renderer.SetFog(this.Fog); }
        if(this.HasEnvironment){ Renderer.SetEnvironmentLighting(this.Environment); }
        if(this.HasCameraAltitude){ Renderer.SetCameraAltitude(this.CameraAltitude); }
        if(this.HasRenderOriginAltitude){ Renderer.SetRenderOriginAltitude(this.RenderOriginAltitude); }
        foreach(var (MaterialIndex,Anchor) in this.WorldGridAnchors){
            Renderer.SetWorldGridAnchor(MaterialIndex,Anchor);
        }
        foreach(var Command in this.LightOrder){
            switch(Command.Kind){
                case LightCommandKind.SetDirectional:Renderer.SetLight(this.DirectionalLights[Command.Index]);break;
                case LightCommandKind.Directional:Renderer.Submit(this.DirectionalLights[Command.Index]);break;
                case LightCommandKind.Point:Renderer.Submit(this.PointLights[Command.Index]);break;
                case LightCommandKind.Spot:Renderer.Submit(this.SpotLights[Command.Index]);break;
                case LightCommandKind.Rect:Renderer.Submit(this.RectLights[Command.Index]);break;
                case LightCommandKind.Emissive:this.EmissiveLights[Command.Index].Replay(Renderer);break;
                default:throw new InvalidOperationException("Unknown light command.");
            }
        }
        foreach(int ObjectIndex in this.TouchedObjectIndices){
            Renderer.SetObjectTransform(ObjectIndex,this.ObjectTransforms[ObjectIndex]);
        }
        foreach(int MeshIndex in this.TouchedSkinMeshes){
            Renderer.SetSkinPalette(MeshIndex,this.SkinPalettes[MeshIndex].AsSpan(0,this.SkinPaletteLengths[MeshIndex]));
        }
        if(this.HasPresentationMode){
            Renderer.SetPresentationMode(this.PresentationMode);
        }
        foreach(int ObjectIndex in this.TouchedSelectionIndices){
            Renderer.SetObjectSelectionWeight(
                ObjectIndex,this.ObjectSelectionWeights[ObjectIndex]);
        }
        foreach(int ObjectIndex in this.TouchedVisibilityIndices){
            Renderer.SetObjectVisibility(ObjectIndex,this.ObjectVisibleInView[ObjectIndex],this.ObjectCastsShadow[ObjectIndex]);
        }
        foreach(int ObjectIndex in this.TouchedEmissiveIndices){
            Renderer.SetObjectEmissiveScale(ObjectIndex,this.ObjectEmissiveScales[ObjectIndex]);
        }
        if(this.HasOverlay){
            Renderer.SetOverlay(
                this.OverlayTriangles.AsSpan(0,this.OverlayTriangleCount),
                this.OverlayLines.AsSpan(0,this.OverlayLineCount));
        }
        if(this.HasScreenLayer){
            Renderer.SetScreenLayer(
                this.ScreenVertices.AsSpan(0,this.ScreenVertexCount),
                this.ScreenBatches.AsSpan(0,this.ScreenBatchCount));
        }
        if(this.HasCameraPreviews){
            Renderer.SetCameraPreviews(
                this.CameraPreviews.AsSpan(0,this.CameraPreviewCount));
        }
    }

    public void Reset(){
        this.TouchedSkinMeshes.Clear();
        this.SkinPaletteLengths.Clear();
        this.ViewSource=default;
        this.HasAmbientLight=false;
        this.HasFog=false;
        this.HasEnvironment=false;
        this.HasCameraAltitude=false;
        this.HasRenderOriginAltitude=false;
        this.WorldGridAnchors.Clear();
        this.HasPresentationMode=false;
        this.HasOverlay=false;
        this.HasScreenLayer=false;
        this.ScreenVertexCount=0;
        this.ScreenBatchCount=0;
        this.HasCameraPreviews=false;
        this.OverlayTriangleCount=0;
        this.OverlayLineCount=0;
        this.CameraPreviewCount=0;
        this.DirectionalLights.Clear();
        this.PointLights.Clear();
        this.SpotLights.Clear();
        this.RectLights.Clear();
        this.EmissiveLights.Clear();
        this.LightOrder.Clear();
        foreach(int ObjectIndex in this.TouchedObjectIndices){
            this.ObjectTransformSet[ObjectIndex]=false;
        }
        this.TouchedObjectIndices.Clear();
        foreach(int ObjectIndex in this.TouchedSelectionIndices){
            this.ObjectSelectionWeightSet[ObjectIndex]=false;
        }
        this.TouchedSelectionIndices.Clear();
        foreach(int ObjectIndex in this.TouchedVisibilityIndices){
            this.ObjectVisibilitySet[ObjectIndex]=false;
        }
        this.TouchedVisibilityIndices.Clear();
        foreach(int ObjectIndex in this.TouchedEmissiveIndices){
            this.ObjectEmissiveScaleSet[ObjectIndex]=false;
        }
        this.TouchedEmissiveIndices.Clear();
    }

    private enum LightCommandKind:byte {SetDirectional,Directional,Point,Spot,Rect,Emissive}
    private readonly record struct LightCommandIndex(LightCommandKind Kind,int Index);
    private readonly record struct EmissiveCommand(EmissivePatch Patch,float RangeMeters,bool CastsShadows,int ShadowPriority,float ShadowBiasMeters){
        public void Replay(IStageRenderBackend Renderer){
            Renderer.SubmitEmissivePatch(this.Patch,this.RangeMeters,this.CastsShadows,this.ShadowPriority,this.ShadowBiasMeters);
        }
    }
}
