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
    private readonly List<DirectionalCommand>DirectionalLights=[];
    private readonly List<LocalLightCommand>LocalLights=[];
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
        &&this.DirectionalLights.Count==0&&this.LocalLights.Count==0
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

    public void RecordSetDirectional(
        Vector3 LightForward,Vector3 LinearRgb,float IlluminanceLux,
        bool CastsShadows,int ShadowPriority,float ShadowBiasMeters){
        this.DirectionalLights.Clear();
        this.DirectionalLights.Add(new DirectionalCommand(
            IsSet:true,LightForward,LinearRgb,IlluminanceLux,
            CastsShadows,ShadowPriority,ShadowBiasMeters));
    }

    public void RecordAddDirectional(
        Vector3 LightForward,Vector3 LinearRgb,float IlluminanceLux,
        bool CastsShadows,int ShadowPriority,float ShadowBiasMeters)=>
        this.DirectionalLights.Add(new DirectionalCommand(
            IsSet:false,LightForward,LinearRgb,IlluminanceLux,
            CastsShadows,ShadowPriority,ShadowBiasMeters));

    public void RecordAmbient(Vector3 Color){
        this.AmbientLight=Color;
        this.HasAmbientLight=true;
    }

    public void RecordFog(FogParams Fog){
        this.Fog=Fog;
        this.HasFog=true;
    }

    public void RecordPoint(
        Vector3 Position,Vector3 LinearRgb,float LuminousFluxLumens,
        float RangeMeters,bool CastsShadows,int ShadowPriority,float ShadowBiasMeters)=>
        this.LocalLights.Add(LocalLightCommand.Point(
            Position,LinearRgb,LuminousFluxLumens,RangeMeters,
            CastsShadows,ShadowPriority,ShadowBiasMeters));

    public void RecordSpot(
        Vector3 Position,Vector3 LightForward,Vector3 LinearRgb,
        float LuminousFluxLumens,float RangeMeters,
        float InnerConeFullAngleDegrees,float OuterConeFullAngleDegrees,
        bool CastsShadows,int ShadowPriority,float ShadowBiasMeters)=>
        this.LocalLights.Add(LocalLightCommand.Spot(
            Position,LightForward,LinearRgb,LuminousFluxLumens,RangeMeters,
            InnerConeFullAngleDegrees,OuterConeFullAngleDegrees,
            CastsShadows,ShadowPriority,ShadowBiasMeters));

    public void RecordArea(
        Vector3 Position,Vector3 LightForward,Vector3 LinearRgb,
        float LuminanceNits,float WidthMeters,float HeightMeters,float RangeMeters,
        bool CastsShadows,int ShadowPriority,float ShadowBiasMeters)=>
        this.LocalLights.Add(LocalLightCommand.Area(
            Position,LightForward,LinearRgb,LuminanceNits,WidthMeters,HeightMeters,
            RangeMeters,CastsShadows,ShadowPriority,ShadowBiasMeters));

    public void RecordEmissive(
        IReadOnlyList<EmissivePatch>Patches,float RangeMeters,
        bool CastsShadows,int ShadowPriority,float ShadowBiasMeters){
        foreach(EmissivePatch Patch in Patches){
            this.LocalLights.Add(LocalLightCommand.Emissive(
                Patch,RangeMeters,CastsShadows,ShadowPriority,ShadowBiasMeters));
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
        foreach(DirectionalCommand Command in this.DirectionalLights){
            if(Command.IsSet){
                Renderer.SetDirectionalLight(
                    Command.LightForward,Command.LinearRgb,Command.IlluminanceLux,
                    Command.CastsShadows,Command.ShadowPriority,Command.ShadowBiasMeters);
            } else {
                Renderer.AddDirectionalLight(
                    Command.LightForward,Command.LinearRgb,Command.IlluminanceLux,
                    Command.CastsShadows,Command.ShadowPriority,Command.ShadowBiasMeters);
            }
        }
        foreach(LocalLightCommand Command in this.LocalLights){
            Command.Replay(Renderer);
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
        this.LocalLights.Clear();
        foreach(int ObjectIndex in this.TouchedObjectIndices){
            this.ObjectTransformSet[ObjectIndex]=false;
        }
        this.TouchedObjectIndices.Clear();
        foreach(int ObjectIndex in this.TouchedSelectionIndices){
            this.ObjectSelectionWeightSet[ObjectIndex]=false;
        }
        this.TouchedSelectionIndices.Clear();
    }

    private readonly record struct DirectionalCommand(
        bool IsSet,Vector3 LightForward,Vector3 LinearRgb,float IlluminanceLux,
        bool CastsShadows,int ShadowPriority,float ShadowBiasMeters);

    private enum LocalLightKind:byte {
        Point,
        Spot,
        Area,
        Emissive,
    }

    private readonly record struct LocalLightCommand(
        LocalLightKind Kind,Vector3 Position,Vector3 LightForward,Vector3 LinearRgb,
        EmissivePatch EmissivePatch,float Intensity,float RangeMeters,
        float ShapeA,float ShapeB,bool CastsShadows,int ShadowPriority,
        float ShadowBiasMeters){
        public static LocalLightCommand Point(
            Vector3 Position,Vector3 LinearRgb,float LuminousFluxLumens,
            float RangeMeters,bool CastsShadows,int ShadowPriority,float ShadowBiasMeters)=>
            new(LocalLightKind.Point,Position,default,LinearRgb,default,
                LuminousFluxLumens,RangeMeters,0f,0f,
                CastsShadows,ShadowPriority,ShadowBiasMeters);

        public static LocalLightCommand Spot(
            Vector3 Position,Vector3 LightForward,Vector3 LinearRgb,
            float LuminousFluxLumens,float RangeMeters,
            float InnerConeFullAngleDegrees,float OuterConeFullAngleDegrees,
            bool CastsShadows,int ShadowPriority,float ShadowBiasMeters)=>
            new(LocalLightKind.Spot,Position,LightForward,LinearRgb,default,
                LuminousFluxLumens,RangeMeters,
                InnerConeFullAngleDegrees,OuterConeFullAngleDegrees,
                CastsShadows,ShadowPriority,ShadowBiasMeters);

        public static LocalLightCommand Area(
            Vector3 Position,Vector3 LightForward,Vector3 LinearRgb,
            float LuminanceNits,float WidthMeters,float HeightMeters,float RangeMeters,
            bool CastsShadows,int ShadowPriority,float ShadowBiasMeters)=>
            new(LocalLightKind.Area,Position,LightForward,LinearRgb,default,
                LuminanceNits,RangeMeters,WidthMeters,HeightMeters,
                CastsShadows,ShadowPriority,ShadowBiasMeters);

        public static LocalLightCommand Emissive(
            EmissivePatch Patch,float RangeMeters,
            bool CastsShadows,int ShadowPriority,float ShadowBiasMeters)=>
            new(LocalLightKind.Emissive,default,default,default,Patch,
                0f,RangeMeters,0f,0f,
                CastsShadows,ShadowPriority,ShadowBiasMeters);

        public void Replay(StageRenderer Renderer){
            switch(this.Kind){
                case LocalLightKind.Point:
                    Renderer.AddPointLight(
                        this.Position,this.LinearRgb,this.Intensity,this.RangeMeters,
                        this.CastsShadows,this.ShadowPriority,this.ShadowBiasMeters);
                    break;
                case LocalLightKind.Spot:
                    Renderer.AddSpotLight(
                        this.Position,this.LightForward,this.LinearRgb,
                        this.Intensity,this.RangeMeters,this.ShapeA,this.ShapeB,
                        this.CastsShadows,this.ShadowPriority,this.ShadowBiasMeters);
                    break;
                case LocalLightKind.Area:
                    Renderer.AddAreaLight(
                        this.Position,this.LightForward,this.LinearRgb,
                        this.Intensity,this.ShapeA,this.ShapeB,this.RangeMeters,
                        this.CastsShadows,this.ShadowPriority,this.ShadowBiasMeters);
                    break;
                case LocalLightKind.Emissive:
                    Renderer.AddAreaLight(
                        this.EmissivePatch.Center,this.EmissivePatch.Forward,
                        this.EmissivePatch.Right,this.EmissivePatch.Up,
                        this.EmissivePatch.LinearRgb,this.EmissivePatch.LuminanceNits,
                        this.EmissivePatch.WidthMeters,this.EmissivePatch.HeightMeters,
                        this.RangeMeters,this.ShadowPriority,this.CastsShadows,
                        this.ShadowBiasMeters,PhysicalLightKind.Emissive);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unknown local light command {this.Kind}.");
            }
        }
    }
}
