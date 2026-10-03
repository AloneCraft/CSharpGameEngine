using System.Numerics;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Rendering;
using RenderCamera=CSharpGameEngine.Graphics.D3D12.Camera;

namespace CSharpGameEngine.Runtime.Windows;

/// <summary>
/// Reusable provider-boundary snapshot for the commands that form one complete frame. Two
/// instances are swapped by RenderServer, so completed state survives device replacement without
/// allocating a new command graph every frame.
/// </summary>
internal sealed class RenderFrameState {
    private readonly Matrix4x4[]ObjectTransforms;
    private readonly bool[]ObjectTransformSet;
    private readonly List<int>TouchedObjectIndices=[];
    private readonly float[]ObjectSelectionWeights;
    private readonly bool[]ObjectSelectionWeightSet;
    private readonly List<int>TouchedSelectionIndices=[];
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
    private RenderCamera Camera;
    private Matrix4x4 ViewProjection;
    private Vector3 ViewProjectionCameraPosition;
    private RadialViewParameters?ViewProjectionRadial;
    private float CameraAspectRatioOverride;
    private Vector3 AmbientLight;
    private FogParams Fog;
    private EnvironmentLightingSettings Environment;
    private float CameraAltitude;
    private float RenderOriginAltitude;
    private bool HasEnvironment;
    private bool HasCameraAltitude;
    private bool HasRenderOriginAltitude;
    private RenderPresentationMode PresentationMode;
    private bool HasCamera;
    private bool HasViewProjection;
    private bool HasAmbientLight;
    private bool HasFog;
    private bool HasPresentationMode;
    private RenderOverlayVertex[]OverlayTriangles=[];
    private RenderOverlayVertex[]OverlayLines=[];
    private int OverlayTriangleCount;
    private int OverlayLineCount;
    private bool HasOverlay;
    private RenderCameraPreview[]CameraPreviews=[];
    private int CameraPreviewCount;
    private bool HasCameraPreviews;

    public RenderFrameState(int ObjectCount){
        ArgumentOutOfRangeException.ThrowIfNegative(ObjectCount);
        this.ObjectTransforms=new Matrix4x4[ObjectCount];
        this.ObjectTransformSet=new bool[ObjectCount];
        this.ObjectSelectionWeights=new float[ObjectCount];
        this.ObjectSelectionWeightSet=new bool[ObjectCount];
    }

    public bool IsEmpty=>
        !this.HasCamera&&!this.HasViewProjection&&!this.HasAmbientLight&&!this.HasFog
        &&!this.HasEnvironment&&!this.HasCameraAltitude&&!this.HasRenderOriginAltitude
        &&this.WorldGridAnchors.Count==0
        &&!this.HasPresentationMode
        &&!this.HasOverlay
        &&!this.HasCameraPreviews
        &&this.LightOrder.Count==0
        &&this.TouchedObjectIndices.Count==0&&this.TouchedSelectionIndices.Count==0
        &&this.TouchedSkinMeshes.Count==0;

    public void RecordCamera(in RenderCamera Camera,float AspectRatioOverride){
        this.Camera=Camera;
        this.CameraAspectRatioOverride=AspectRatioOverride;
        this.HasCamera=true;
        this.HasViewProjection=false;
    }

    public void RecordViewProjection(
        in Matrix4x4 ViewProjection,in Vector3 CameraPosition,RadialViewParameters?Radial=null){
        this.ViewProjection=ViewProjection;
        this.ViewProjectionCameraPosition=CameraPosition;
        this.ViewProjectionRadial=Radial;
        this.HasViewProjection=true;
        this.HasCamera=false;
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
        if((uint)ObjectIndex>=(uint)this.ObjectTransforms.Length){
            throw new ArgumentOutOfRangeException(nameof(ObjectIndex));
        }
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
        if((uint)ObjectIndex>=(uint)this.ObjectSelectionWeights.Length){
            throw new ArgumentOutOfRangeException(nameof(ObjectIndex));
        }
        this.ObjectSelectionWeights[ObjectIndex]=Weight;
        if(this.ObjectSelectionWeightSet[ObjectIndex]){ return; }
        this.ObjectSelectionWeightSet[ObjectIndex]=true;
        this.TouchedSelectionIndices.Add(ObjectIndex);
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
    /// frame did not set them. StageRenderer keeps ambient, fog, environment, camera and render origin
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

    public void Replay(StageRenderer Renderer){
        ArgumentNullException.ThrowIfNull(Renderer);
        if(this.HasViewProjection&&this.ViewProjectionRadial is { }Radial){
            Renderer.SetViewProjection(
                this.ViewProjection,this.ViewProjectionCameraPosition,Radial);
        } else if(this.HasViewProjection){
            Renderer.SetViewProjection(
                this.ViewProjection,this.ViewProjectionCameraPosition);
        } else if(this.HasCamera){
            Renderer.SetCamera(this.Camera,this.CameraAspectRatioOverride);
        }
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
        if(this.HasOverlay){
            Renderer.SetOverlay(
                this.OverlayTriangles.AsSpan(0,this.OverlayTriangleCount),
                this.OverlayLines.AsSpan(0,this.OverlayLineCount));
        }
        if(this.HasCameraPreviews){
            Renderer.SetCameraPreviews(
                this.CameraPreviews.AsSpan(0,this.CameraPreviewCount));
        }
    }

    public void Reset(){
        this.TouchedSkinMeshes.Clear();
        this.SkinPaletteLengths.Clear();
        this.HasCamera=false;
        this.HasViewProjection=false;
        this.HasAmbientLight=false;
        this.HasFog=false;
        this.HasEnvironment=false;
        this.HasCameraAltitude=false;
        this.HasRenderOriginAltitude=false;
        this.WorldGridAnchors.Clear();
        this.HasPresentationMode=false;
        this.HasOverlay=false;
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
    }

    private enum LightCommandKind:byte {SetDirectional,Directional,Point,Spot,Rect,Emissive}
    private readonly record struct LightCommandIndex(LightCommandKind Kind,int Index);
    private readonly record struct EmissiveCommand(EmissivePatch Patch,float RangeMeters,bool CastsShadows,int ShadowPriority,float ShadowBiasMeters){
        public void Replay(StageRenderer Renderer){
            Renderer.AddAreaLight(this.Patch.Center,this.Patch.Forward,this.Patch.Right,this.Patch.Up,
                this.Patch.LinearRgb,this.Patch.LuminanceNits,this.Patch.WidthMeters,this.Patch.HeightMeters,
                this.RangeMeters,this.ShadowPriority,this.CastsShadows,this.ShadowBiasMeters,PhysicalLightKind.Emissive);
        }
    }
}
