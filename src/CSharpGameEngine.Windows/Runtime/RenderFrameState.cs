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
    private readonly List<DirectionalLightSubmission>DirectionalLights=[];
    private readonly List<PointLightSubmission>PointLights=[];
    private readonly List<SpotLightSubmission>SpotLights=[];
    private readonly List<RectAreaLightSubmission>RectLights=[];
    private readonly List<EmissiveCommand>EmissiveLights=[];
    private readonly List<LightCommandIndex>LightOrder=[];
    private RenderCamera Camera;
    private Matrix4x4 ViewProjection;
    private Vector3 ViewProjectionCameraPosition;
    private float CameraAspectRatioOverride;
    private Vector3 AmbientLight;
    private FogParams Fog;
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
        &&!this.HasPresentationMode
        &&!this.HasOverlay
        &&!this.HasCameraPreviews
        &&this.LightOrder.Count==0
        &&this.TouchedObjectIndices.Count==0&&this.TouchedSelectionIndices.Count==0;

    public void RecordCamera(in RenderCamera Camera,float AspectRatioOverride){
        this.Camera=Camera;
        this.CameraAspectRatioOverride=AspectRatioOverride;
        this.HasCamera=true;
        this.HasViewProjection=false;
    }

    public void RecordViewProjection(
        in Matrix4x4 ViewProjection,in Vector3 CameraPosition){
        this.ViewProjection=ViewProjection;
        this.ViewProjectionCameraPosition=CameraPosition;
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

    public void Replay(StageRenderer Renderer){
        ArgumentNullException.ThrowIfNull(Renderer);
        if(this.HasViewProjection){
            Renderer.SetViewProjection(
                this.ViewProjection,this.ViewProjectionCameraPosition);
        } else if(this.HasCamera){
            Renderer.SetCamera(this.Camera,this.CameraAspectRatioOverride);
        }
        if(this.HasAmbientLight){ Renderer.SetAmbientLight(this.AmbientLight); }
        if(this.HasFog){ Renderer.SetFog(this.Fog); }
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
        this.HasCamera=false;
        this.HasViewProjection=false;
        this.HasAmbientLight=false;
        this.HasFog=false;
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
