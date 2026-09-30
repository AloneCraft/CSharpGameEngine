using CSharpGameEngine.Runtime.Lighting;
using CSharpGameEngine.Lighting;
using CSharpGameEngine.Rendering;
using System.Numerics;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Platform.Windows;
using RenderCamera=CSharpGameEngine.Graphics.D3D12.Camera;
using Camera=CSharpGameEngine.Runtime.Windows.Camera;
using EcsWorld=CSharpGameEngine.Entities.World;

namespace CSharpGameEngine.Runtime.Windows;

[SystemOrder(1)]
file sealed class Order;

/// <summary>Transformの位置とローカル+Zを、ライト用のScale非依存なワールド入力へ写像する。</summary>
internal static class LightTransform {
    internal static FixedTransformStatus TryGetRectanglePose(Entity Entity,EcsWorld World,EngineHost Host,
        out Vector3 Position,out Vector3 Forward,out Vector3 Up,out FixedTransformResolveStatus ResolveStatus){
        var Status=RuntimeWorldTransform.TryGetCameraRelativeMatrix(Entity,World,Host,out var Matrix,out ResolveStatus);
        Position=default;Forward=default;Up=default;
        if(Status!=FixedTransformStatus.Success){return Status;}
        var Z=Vector3.TransformNormal(Vector3.UnitZ,Matrix);
        var Y=Vector3.TransformNormal(Vector3.UnitY,Matrix);
        if(!float.IsFinite(Z.LengthSquared())||!float.IsFinite(Y.LengthSquared())||Z.LengthSquared()<=1e-12f||Y.LengthSquared()<=1e-12f){return FixedTransformStatus.NonInvertible;}
        Forward=Vector3.Normalize(Z);
        var Right=Vector3.Cross(Vector3.Normalize(Y),Forward);
        if(Right.LengthSquared()<=1e-12f){return FixedTransformStatus.NonInvertible;}
        Up=Vector3.Normalize(Vector3.Cross(Forward,Vector3.Normalize(Right)));
        Position=Matrix.Translation;
        return FixedTransformStatus.Success;
    }
    public static FixedTransformStatus TryGetCameraRelativePose(
        Entity Entity,EcsWorld EcsWorld,EngineHost Host,
        out Vector3 Position,
        out Vector3 Forward,
        out FixedTransformResolveStatus ResolveStatus)=>
        RuntimeWorldTransform.TryGetCameraRelativePose(
            Entity,EcsWorld,Host,out Position,out Forward,out ResolveStatus);
}

/// <summary>
/// Applies the fixed hierarchy's local +Z/+Y camera basis at the final renderer boundary.
/// </summary>
internal static class CameraTransform {
    public static FixedTransformStatus TryApplyWorldOrientation(
        in FixedAffineTransform CameraWorld,
        ref RenderCamera Camera,
        out bool UsesTransformOrientation){
        FixedTransformStatus Status=CameraWorld.TryGetCameraBasis(
            out Vector3 Forward,out Vector3 Up);
        if(Status!=FixedTransformStatus.Success){
            UsesTransformOrientation=false;
            return Status;
        }

        UsesTransformOrientation=!RenderCamera.IsIdentityWorldOrientation(Forward,Up);
        if(!UsesTransformOrientation||Camera.TrySetWorldOrientation(Forward,Up)){
            return FixedTransformStatus.Success;
        }

        return FixedTransformStatus.NonInvertible;
    }

}

[SystemPhase(SystemPhase.FrameInput)]
internal struct Window:ISystem {
    private Entity HostEntity;
    private bool HasHostEntity;

    public bool Update(Entity Entity,ref AppHost App,ref AppHostRuntime Runtime,EngineHost Host,ref State State){
        if(!this.HasHostEntity){this.HostEntity=Entity;this.HasHostEntity=true;}
        else if(!this.HostEntity.Equals(Entity)){
            throw new InvalidOperationException("Runtime requires exactly one AppHost.");
        }
        if(Host.ExternalSurface is not null){
            // ADR-0070 B1: the editor owns the surface and its input.
            return true;
        }
        if(Host.Window is null){
            Host.EnsureWindow(App);
            return true;
        }
        Win32Window Window=Host.Window;
        Window.PumpEvents();
        Host.Automation?.ApplyInput(Window);
        Host.Input.ProcessEvents(Window.DrainInputEvents());
        if(Window.IsClosed){
            State.Enabled=false;
            return false;
        }
        return true;
    }
}

internal static class LightValidation {
    internal static int Count(Entity Entity,EcsWorld World)=>
        (World.TryGetComponent(Entity,out DirectionalLight _)?1:0)
        +(World.TryGetComponent(Entity,out PointLight _)?1:0)
        +(World.TryGetComponent(Entity,out SpotLight _)?1:0)
        +(World.TryGetComponent(Entity,out RectAreaLight _)?1:0);
    internal static string? SceneFailure(Entity Entity,EcsWorld World){
        if(Count(Entity,World)!=1){return "LightMultiplicity";}
        if(!World.TryGetComponent(Entity,out Transform _)){return "Transform";}
        if(World.TryGetComponent(Entity,out LightShadow Shadow)&&!Shadow.Bias.IsValid){return "LightShadow.Bias";}
        return null;
    }
    internal static LightShadowSettings? Shadow(Entity Entity,EcsWorld World)=>
        World.TryGetComponent(Entity,out LightShadow Value)?new LightShadowSettings(Value.Priority,Value.Bias):null;
}
[SystemPhase(SystemPhase.Presentation)]
internal struct LightShadowValidation:ISystem {
    public bool Update(Entity Entity,ref LightShadow Shadow,ref LightShadowRuntime Runtime,ref State State){
        Runtime.InvalidProperty=LightValidation.SceneFailure(Entity,State.World);
        return true;
    }
}

/// <summary>
/// Scene DSLが生成した個別メッシュと環境Componentを一巡収集してから、次フレームで
/// 固定ロースター型StageRendererを構築する。Scene由来の子Archetypeは実System由来の
/// {AppHost,Level}より後ろへ登録されるため、同一フレームで確定してはならない。
/// </summary>
[SystemPhase(SystemPhase.Presentation)]
internal struct LevelLoad:ISystem {
    private Entity LevelEntity;
    private bool HasLevelEntity;
    private List<(Entity Entity,Transform Transform,StaticMesh Mesh)>?PendingStaticMeshes;
    private List<(Entity Entity,Transform Transform,PrimitiveMesh Mesh)>?PendingPrimitives;
    private List<(Entity Entity,Transform Transform,SkeletalMesh Mesh)>?PendingSkeletalMeshes;
    private Entity PendingCameraEntity;
    private Camera PendingCamera;
    private int ActiveCameraCount;
    private bool RosterCaptured;

    public bool Update(Entity Entity,ref Transform Transform,ref StaticMesh Mesh,ref State State){
        if(!this.RosterCaptured)(this.PendingStaticMeshes??=[]).Add((Entity,Transform,Mesh));
        return true;
    }

    public bool Update(Entity Entity,ref Transform Transform,ref PrimitiveMesh Mesh,ref State State){
        if(!this.RosterCaptured)(this.PendingPrimitives??=[]).Add((Entity,Transform,Mesh));
        return true;
    }

    public bool Update(Entity Entity,ref Transform Transform,ref SkeletalMesh Mesh,ref State State){
        if(!this.RosterCaptured)(this.PendingSkeletalMeshes??=[]).Add((Entity,Transform,Mesh));
        return true;
    }

    public bool Update(Entity Entity,ref Transform Transform,ref Camera Camera,ref State State){
        if(!this.RosterCaptured&&Camera.Active){
            this.ActiveCameraCount++;
            if(this.ActiveCameraCount==1){
                this.PendingCameraEntity=Entity;
                this.PendingCamera=Camera;
            }
        }
        return true;
    }
    public bool Update(Entity Entity,ref AppHost App,ref AppHostRuntime AppRuntime,ref Level Level,ref LevelRuntime LevelRuntime,EngineHost Host,ref State State){
        if(!this.HasLevelEntity){this.LevelEntity=Entity;this.HasLevelEntity=true;}
        else if(!this.LevelEntity.Equals(Entity)){
            throw new InvalidOperationException("Runtime requires exactly one Level.");
        }
        if(LevelRuntime.Loaded){
            FixedTransformResolveStatus CameraResolveStatus=ResolveCamera(
                Host,this.PendingCameraEntity,State.World,out FixedAffineTransform CameraWorld,out var CameraFrame);
            LevelRuntime.LastCameraWorldResolveStatus=CameraResolveStatus;
            if(CameraResolveStatus==FixedTransformResolveStatus.Success){
                RenderCamera ActiveRenderCamera=Host.RenderCamera;
                FixedTransformStatus CameraStatus=CameraTransform.TryApplyWorldOrientation(
                    CameraWorld,ref ActiveRenderCamera,out _);
                LevelRuntime.LastCameraTransformStatus=CameraStatus;
                if(Host.EditorCamera is { }Editor){
                    ActiveRenderCamera.FovYRadians=Editor.FovYRadians;
                    ActiveRenderCamera.NearZ=Editor.NearZ;
                    ActiveRenderCamera.FarZ=Editor.FarZ;
                }
                if(CameraStatus==FixedTransformStatus.Success){
                    // Presentation runs system-major. Refresh before later Renderable/light
                    // archetype executes so every GPU submission in this frame uses one camera origin.
                    Host.RenderCamera=ActiveRenderCamera;
                    Host.SetCameraPose(CameraWorld,CameraFrame);
                    LevelRuntime.HasLastCameraWorldPose=true;
                }
            } else {
                LevelRuntime.LastCameraTransformStatus=RuntimeWorldTransform.ToTransformStatus(
                    CameraResolveStatus);
            }
            Host.Renderer?.SetCamera(Host.RenderCamera);
            return true;
        }
        Win32Window?Window=Host.Window;
        if(Host.ExternalSurface is null&&Window is null){
            // Windowが未生成のフレームで集めたロースターは次フレームと二重集計されるため破棄する。
            this.PendingStaticMeshes=null;
            this.PendingPrimitives=null;
            this.PendingSkeletalMeshes=null;
            this.PendingCameraEntity=default;
            this.PendingCamera=default;
            this.ActiveCameraCount=0;
            return true;
        }
        this.RosterCaptured=true;
        if(this.ActiveCameraCount==0&&Host.EditorCamera is null){
            throw new InvalidOperationException("Runtime requires exactly one active Camera, but none was found.");
        }
        if(this.ActiveCameraCount>1){
            throw new InvalidOperationException($"Runtime requires exactly one active Camera, but {this.ActiveCameraCount} were found.");
        }

        var Scene=StageSceneDataBuilder.Build(
            this.PendingStaticMeshes??=[],
            this.PendingPrimitives??=[],
            this.PendingSkeletalMeshes??=[]);
        var Renderer=Host.ExternalSurface is { }Surface
            ?RenderServer.Create(
                                  Surface.Handle,Surface.Width,Surface.Height,Surface.DeviceOptions,
                                  ContentRoot.Resolve(Level.ShaderArtifactPath),Scene.Meshes,Scene.Materials,Scene.Objects)
            :RenderServer.Create(
                                  Window!.Handle,Window.ClientWidth,Window.ClientHeight,GraphicsDeviceOptionsResolver.FromEnvironment(),
                                  ContentRoot.Resolve(Level.ShaderArtifactPath),Scene.Meshes,Scene.Materials,Scene.Objects);
        Host.OwnRenderer(Renderer);
        if(Window is not null){
            Window.Resized+=(Width,Height)=>Renderer.Resize(Width,Height);
        }

        var Camera=RenderCamera.CreateFirstPerson(Vector3.Zero);
        FixedTransformResolveStatus InitialCameraResolveStatus=ResolveCamera(
            Host,this.PendingCameraEntity,State.World,out FixedAffineTransform InitialCameraWorld,out var InitialCameraFrame);
        LevelRuntime.LastCameraWorldResolveStatus=InitialCameraResolveStatus;
        if(InitialCameraResolveStatus!=FixedTransformResolveStatus.Success){
            throw new InvalidOperationException(
                $"The active camera hierarchy cannot be resolved in fixed precision: {InitialCameraResolveStatus}.");
        }

        FixedTransformStatus InitialCameraStatus=CameraTransform.TryApplyWorldOrientation(
            InitialCameraWorld,ref Camera,out _);
        LevelRuntime.LastCameraTransformStatus=InitialCameraStatus;
        if(InitialCameraStatus!=FixedTransformStatus.Success){
            throw new InvalidOperationException(
                $"The active camera Transform basis cannot produce a renderer view: {InitialCameraStatus}.");
        }

        if(Host.EditorCamera is { }EditorCamera){
            Camera.FovYRadians=EditorCamera.FovYRadians;
            Camera.NearZ=EditorCamera.NearZ;
            Camera.FarZ=EditorCamera.FarZ;
        } else {
            Camera.FovYRadians= this.PendingCamera.FovYRadians;
            Camera.NearZ= this.PendingCamera.NearZ;
            Camera.FarZ= this.PendingCamera.FarZ;
        }
        // Exposure is the scene's in the editor too: the view must look like the game.
        Camera.ExposureEv100=this.ActiveCameraCount>0?this.PendingCamera.ExposureEv100:new Camera().ExposureEv100;
        Host.AttachRenderer(Renderer,Camera,InitialCameraWorld,InitialCameraFrame);
        LevelRuntime.HasLastCameraWorldPose=true;
        LevelRuntime.Loaded=true;
        LevelRuntime.ShaderSemanticHash=Renderer.ShaderSemanticHash;
        LevelRuntime.ShaderArtifactLoaded=Renderer.ShaderArtifactLoaded;
        LevelRuntime.ShaderArtifactDiagnosticCount=Renderer.ShaderArtifactDiagnostics.Count;
        Renderer.SetCamera(Camera);


        for(var ObjectIndex=0;ObjectIndex<Scene.Objects.Length;ObjectIndex++){
            FixedTransformStatus Status=RuntimeWorldTransform.TryGetCameraRelativeMatrix(
                Scene.SourceEntities[ObjectIndex],State.World,Host,
                out Matrix4x4 CameraRelativeWorld,
                out FixedTransformResolveStatus ResolveStatus);
            if(Status!=FixedTransformStatus.Success){
                throw new InvalidOperationException(
                    $"The initial render pose cannot be represented relative to the active camera: {Status}.");
            }
            Renderer.SetObjectTransform(
                ObjectIndex,
                Scene.ObjectLocalTransforms[ObjectIndex]*CameraRelativeWorld);
            Host.RegisterRenderObject(Scene.SourceEntities[ObjectIndex],ObjectIndex);
            State.World.CreateDeferred(
                Scene.ObjectTransforms[ObjectIndex],
                new Renderable {
                    ObjectIndex=ObjectIndex,
                    SourceEntity=Scene.SourceEntities[ObjectIndex],
                    HasSourceEntity=true,
                    MeshLocalTransform=Scene.ObjectLocalTransforms[ObjectIndex],
                    HasMeshLocalTransform=true,
                    LastCameraRelativeWorld=CameraRelativeWorld,
                    HasLastCameraRelativeWorld=true,
                    LastTransformStatus=FixedTransformStatus.Success,
                    LastWorldResolveStatus=ResolveStatus,
                });
            StageMaterialCreateInfo Material=Scene.Materials[
                Scene.Objects[ObjectIndex].MaterialIndex];
            Vector3 EmissiveNits=Material.EmissiveNits!=Vector3.Zero
                ?Material.EmissiveNits
                :Material.EmissiveFactor*100f;
            if(EmissiveNits.X>0f||EmissiveNits.Y>0f||EmissiveNits.Z>0f){
                State.World.CreateDeferred(
                    Scene.ObjectTransforms[ObjectIndex],
                    new EmissiveMeshLighting {
                        Mesh=Scene.Meshes[Scene.Objects[ObjectIndex].MeshIndex],
                        EmissiveNits=EmissiveNits,
                        EmissiveTextureLuminanceScale=Material.EmissiveTextureLuminanceScale,
                        SourceEntity=Scene.SourceEntities[ObjectIndex],
                        HasSourceEntity=true,
                        MeshLocalTransform=Scene.ObjectLocalTransforms[ObjectIndex],
                        LastCameraRelativeWorld=CameraRelativeWorld,
                        RangeMeters=20f,
                        CastsShadows=true,
                        ShadowPriority=0,
                        ShadowBiasMeters=0.005f,
                        HasLastCameraRelativeWorld=true,
                        LastTransformStatus=FixedTransformStatus.Success,
                        LastWorldResolveStatus=ResolveStatus,
                    });
            }
        }

        foreach(var (SkinnedEntity,MeshIndices) in Scene.SkinnedMeshes){
            Host.RegisterSkinnedMeshes(SkinnedEntity,MeshIndices);
        }

        this.RosterCaptured=true;
        this.PendingStaticMeshes=null;
        this.PendingPrimitives=null;
        this.PendingSkeletalMeshes=null;
        return true;
    }

    /// <summary>
    /// The camera the frame is seen from: the editor camera when the Scene Designer supplies one
    /// (ADR-0070 B1), otherwise the scene's active camera.
    /// </summary>
    private static FixedTransformResolveStatus ResolveCamera(
        EngineHost Host,Entity SceneCamera,EcsWorld World,out FixedAffineTransform CameraWorld,out CSharpGameEngine.Contracts.ReferenceFrameId Frame){
        if(Host.EditorCamera is not { }Editor){
            return RuntimeWorldTransform.TryResolveWorld(SceneCamera,World,out CameraWorld,out Frame);
        }
        Frame=CSharpGameEngine.Contracts.ReferenceFrameId.Root;
        return FixedAffineTransform.TryFromTransform(Editor.Pose,out CameraWorld) switch {
            FixedTransformStatus.Success=>FixedTransformResolveStatus.Success,
            FixedTransformStatus.NumericOverflow=>FixedTransformResolveStatus.NumericOverflow,
            _=>FixedTransformResolveStatus.NonInvertible,
        };
    }
}

/// <summary>Hands each SkeletalMesh's palette to the renderer after the animators ran (ADR-0066). Submitted every frame so device recovery replays the current pose.</summary>
[SystemPhase(SystemPhase.Presentation)]
[SystemOrder(13)]
internal struct SkinnedMeshSync:ISystem {
    public bool Update(Entity Entity,ref SkeletalMeshRuntime Runtime,EngineHost Host,ref State State){
        if(Runtime.Palette is not{ }Palette||Host.Renderer is not{ }Renderer||!Host.TryGetSkinnedMeshes(Entity,out var MeshIndices)){
            return true;
        }
        foreach(var MeshIndex in MeshIndices){
            Renderer.SetSkinPalette(MeshIndex,Palette);
        }
        return true;
    }
}

[SystemPhase(SystemPhase.Presentation)]
internal struct CameraOriginSync:ISystem {
    public bool Update(Entity Entity,ref Transform Transform,ref Camera Camera,EngineHost Host,ref State State){
        if(Camera.Active
            &&RuntimeWorldTransform.TryResolveWorld(Entity,State.World,out FixedAffineTransform CameraWorld,out var CameraFrame)
                ==FixedTransformResolveStatus.Success
            &&CameraWorld.TryGetCameraBasis(out Vector3 Forward,out Vector3 Up)
                ==FixedTransformStatus.Success
            &&RenderCamera.TryNormalizeOrientation(Forward,Up,
                out _,out _)){
            Host.SetCameraPose(CameraWorld,CameraFrame);
        }
        return true;
    }
}

[SystemPhase(SystemPhase.Presentation)]
internal struct DirectionalLightSync:ISystem {
    public bool Update(Entity Entity,ref DirectionalLight Light,ref DirectionalLightRuntime Runtime,EngineHost Host,ref State State){
        Runtime.InvalidProperty=LightValidation.SceneFailure(Entity,State.World);
        if(Runtime.InvalidProperty is not null){return true;}
        if(!Light.Color.IsValid){Runtime.InvalidProperty="Color";return true;}
        if(!Light.Illuminance.IsValid){Runtime.InvalidProperty="Illuminance";return true;}
        var Shadow=LightValidation.Shadow(Entity,State.World);
        if(Host.Renderer is { }Renderer){
            FixedTransformStatus Status=LightTransform.TryGetCameraRelativePose(
                Entity,State.World,Host,out _,out Vector3 Forward,out FixedTransformResolveStatus ResolveStatus);
            Runtime.LastTransformStatus=Status;
            Runtime.LastWorldResolveStatus=ResolveStatus;
            if(Status==FixedTransformStatus.Success){
                Runtime.LastWorldForward=Forward;
                Runtime.HasLastWorldForward=true;
            } else if(!Runtime.HasLastWorldForward){
                return true;
            }
            var Submission=new DirectionalLightSubmission(Runtime.LastWorldForward,Light.Color,Light.Illuminance,Shadow);
            if(!Submission.IsValid){Runtime.InvalidProperty="Submission";return true;}
            try{ Renderer.AddDirectionalLight(Submission); }
            catch(ArgumentException Error){ Runtime.InvalidProperty=Error is ArgumentOutOfRangeException RangeError?RangeError.ParamName??"Submission":"Submission"; }
        }
        return true;
    }
}

[SystemPhase(SystemPhase.Presentation)]
internal struct AmbientLightSync:ISystem {
    public bool Update(ref AmbientLight Light,EngineHost Host,ref State State){
        Host.Renderer?.SetAmbientLight(
            new Vector3(Light.ColorR,Light.ColorG,Light.ColorB)*Light.Intensity);
        Host.Environment.AmbientSubmitted=true;
        return true;
    }
}

/// <summary>Collects the environment Components (ADR-0069); EngineHost submits them once before rendering.</summary>
[SystemPhase(SystemPhase.Presentation)]
internal struct EnvironmentCollect:ISystem {
    public bool Update(ref SkyAtmosphere Value,EngineHost Host,ref State State){
        Host.Environment.Atmosphere=Value;
        return true;
    }
    public bool Update(ref SkyLight Value,EngineHost Host,ref State State){
        Host.Environment.SkyLight=Value;
        return true;
    }
    public bool Update(ref PostProcessVolume Value,EngineHost Host,ref State State){
        Host.Environment.PostProcess=Value;
        return true;
    }
    public bool Update(ref ExponentialHeightFog Value,EngineHost Host,ref State State){
        Host.Environment.Fog=Value;
        return true;
    }
    public bool Update(Entity Entity,ref GlobalIlluminationVolume Value,EngineHost Host,ref State State){
        // The probe grid is placed in the renderer's camera-relative space like every object, so the
        // absolute volume position never crosses a float boundary.
        if(RuntimeWorldTransform.TryGetCameraRelativeMatrix(Entity,State.World,Host,out var Matrix,out _)!=FixedTransformStatus.Success){
            Host.Environment.InvalidReason="GlobalIlluminationVolume.Transform";
            return true;
        }
        Host.Environment.GlobalIllumination=Value;
        Host.Environment.GlobalIlluminationCenter=Matrix.Translation;
        return true;
    }
}

[SystemPhase(SystemPhase.Presentation)]
internal struct HeightFogSync:ISystem {
    public bool Update(ref HeightFog Fog,EngineHost Host,ref State State){
        Host.Renderer?.SetFog(
            new FogParams(Fog.Density,Fog.HeightFalloff,new Vector3(Fog.ColorR,Fog.ColorG,Fog.ColorB)));
        return true;
    }
}

[SystemPhase(SystemPhase.Presentation)]
internal struct PointLightSync:ISystem {
    public bool Update(Entity Entity,ref PointLight Light,ref PointLightRuntime Runtime,EngineHost Host,ref State State){
        Runtime.InvalidProperty=LightValidation.SceneFailure(Entity,State.World);
        if(Runtime.InvalidProperty is not null){return true;}
        if(!Light.Color.IsValid){Runtime.InvalidProperty="Color";return true;}
        if(!Light.Flux.IsValid){Runtime.InvalidProperty="Flux";return true;}
        if(!Light.Range.IsValid){Runtime.InvalidProperty="Range";return true;}
        var Shadow=LightValidation.Shadow(Entity,State.World);
        if(Host.Renderer is { }Renderer){
            FixedTransformStatus Status=LightTransform.TryGetCameraRelativePose(
                Entity,State.World,Host,out Vector3 Position,out _,out FixedTransformResolveStatus ResolveStatus);
            Runtime.LastTransformStatus=Status;
            Runtime.LastWorldResolveStatus=ResolveStatus;
            if(Status==FixedTransformStatus.Success){
                Runtime.LastCameraRelativePosition=Position;
                Runtime.HasLastCameraRelativePosition=true;
            } else if(!Runtime.HasLastCameraRelativePosition){
                return true;
            }
            var Submission=new PointLightSubmission(Runtime.LastCameraRelativePosition,Light.Color,Light.Flux,Light.Range,Shadow);
            if(!Submission.IsValid){Runtime.InvalidProperty="Submission";return true;}
            try{ Renderer.AddPointLight(Submission); }
            catch(ArgumentException Error){ Runtime.InvalidProperty=Error is ArgumentOutOfRangeException RangeError?RangeError.ParamName??"Submission":"Submission"; }
        }
        return true;
    }
}

[SystemPhase(SystemPhase.Presentation)]
internal struct SpotLightSync:ISystem {
    public bool Update(Entity Entity,ref SpotLight Light,ref SpotLightRuntime Runtime,EngineHost Host,ref State State){
        Runtime.InvalidProperty=LightValidation.SceneFailure(Entity,State.World);
        if(Runtime.InvalidProperty is not null){return true;}
        if(!Light.Color.IsValid){Runtime.InvalidProperty="Color";return true;}
        if(!Light.Flux.IsValid){Runtime.InvalidProperty="Flux";return true;}
        if(!Light.Range.IsValid){Runtime.InvalidProperty="Range";return true;}
        if(!Light.Cone.IsValid){Runtime.InvalidProperty="Cone";return true;}
        var Shadow=LightValidation.Shadow(Entity,State.World);
        if(Host.Renderer is { }Renderer){
            FixedTransformStatus Status=LightTransform.TryGetCameraRelativePose(
                Entity,State.World,Host,out Vector3 Position,out Vector3 Forward,out FixedTransformResolveStatus ResolveStatus);
            Runtime.LastTransformStatus=Status;
            Runtime.LastWorldResolveStatus=ResolveStatus;
            if(Status==FixedTransformStatus.Success){
                Runtime.LastCameraRelativePosition=Position;
                Runtime.LastWorldForward=Forward;
                Runtime.HasLastCameraRelativePosition=true;
                Runtime.HasLastWorldForward=true;
            } else if(!Runtime.HasLastCameraRelativePosition||!Runtime.HasLastWorldForward){
                return true;
            }
            var Submission=new SpotLightSubmission(Runtime.LastCameraRelativePosition,Runtime.LastWorldForward,Light.Color,Light.Flux,Light.Range,Light.Cone,Shadow);
            if(!Submission.IsValid){Runtime.InvalidProperty="Submission";return true;}
            try{ Renderer.AddSpotLight(Submission); }
            catch(ArgumentException Error){ Runtime.InvalidProperty=Error is ArgumentOutOfRangeException RangeError?RangeError.ParamName??"Submission":"Submission"; }
        }
        return true;
    }
}

[SystemPhase(SystemPhase.Presentation)]
internal struct RectAreaLightSync:ISystem {
    public bool Update(Entity Entity,ref RectAreaLight Light,ref RectAreaLightRuntime Runtime,EngineHost Host,ref State State){
        Runtime.InvalidProperty=LightValidation.SceneFailure(Entity,State.World);
        if(Runtime.InvalidProperty is not null){return true;}
        if(!Light.Color.IsValid){Runtime.InvalidProperty="Color";return true;}
        if(!Light.Luminance.IsValid){Runtime.InvalidProperty="Luminance";return true;}
        if(!Light.Size.IsValid){Runtime.InvalidProperty="Size";return true;}
        if(!Light.Range.IsValid){Runtime.InvalidProperty="Range";return true;}
        var Shadow=LightValidation.Shadow(Entity,State.World);
        if(Host.Renderer is { }Renderer){
            FixedTransformStatus Status=LightTransform.TryGetRectanglePose(
                Entity,State.World,Host,out Vector3 Position,out Vector3 Forward,out Vector3 Up,out FixedTransformResolveStatus ResolveStatus);
            Runtime.LastTransformStatus=Status;
            Runtime.LastWorldResolveStatus=ResolveStatus;
            if(Status==FixedTransformStatus.Success){
                Runtime.LastCameraRelativePosition=Position;
                Runtime.LastWorldForward=Forward;
                Runtime.LastWorldUp=Up;
                Runtime.HasLastCameraRelativePosition=true;
                Runtime.HasLastWorldForward=true;
            } else if(!Runtime.HasLastCameraRelativePosition||!Runtime.HasLastWorldForward){
                return true;
            }
            var Submission=new RectAreaLightSubmission(Runtime.LastCameraRelativePosition,Runtime.LastWorldForward,Runtime.LastWorldUp,Light.Color,Light.Luminance,Light.Size,Light.Range,Shadow);
            if(!Submission.IsValid){Runtime.InvalidProperty="Submission";return true;}
            try{ Renderer.AddRectAreaLight(Submission); }
            catch(ArgumentException Error){ Runtime.InvalidProperty=Error is ArgumentOutOfRangeException RangeError?RangeError.ParamName??"Submission":"Submission"; }
        }
        return true;
    }
}

[SystemPhase(SystemPhase.Presentation)]
internal struct TransformSync:ISystem {
    public bool Update(Entity Entity,ref Transform Transform,ref Renderable Renderable,EngineHost Host,ref State State){
        if(Host.Renderer is{ }Renderer){
            Entity SourceEntity=Renderable.HasSourceEntity?Renderable.SourceEntity:Entity;
            FixedTransformStatus Status=RuntimeWorldTransform.TryGetCameraRelativeMatrix(
                SourceEntity,State.World,Host,out Matrix4x4 CameraRelativeWorld,
                out FixedTransformResolveStatus ResolveStatus);
            Renderable.LastTransformStatus=Status;
            Renderable.LastWorldResolveStatus=ResolveStatus;
            if(Status==FixedTransformStatus.Success){
                Renderable.LastCameraRelativeWorld=CameraRelativeWorld;
                Renderable.HasLastCameraRelativeWorld=true;
            } else if(!Renderable.HasLastCameraRelativeWorld){
                return true;
            }

            Matrix4x4 Local=Renderable.HasMeshLocalTransform
                ?Renderable.MeshLocalTransform
                :Matrix4x4.Identity;
            Renderer.SetObjectTransform(
                Renderable.ObjectIndex,Local*Renderable.LastCameraRelativeWorld);
        }
        return true;
    }
}

[SystemPhase(SystemPhase.Presentation)]
internal struct EmissiveMeshLightSync:ISystem {
    public bool Update(
        Entity Entity,
        ref Transform Transform,
        ref EmissiveMeshLighting Lighting,
        EngineHost Host,
        ref State State){
        if(Host.Renderer is { }Renderer){
            Entity SourceEntity=Lighting.HasSourceEntity?Lighting.SourceEntity:Entity;
            FixedTransformStatus Status=RuntimeWorldTransform.TryGetCameraRelativeMatrix(
                SourceEntity,State.World,Host,out Matrix4x4 CameraRelativeWorld,
                out FixedTransformResolveStatus ResolveStatus);
            Lighting.LastTransformStatus=Status;
            Lighting.LastWorldResolveStatus=ResolveStatus;
            if(Status==FixedTransformStatus.Success){
                Lighting.LastCameraRelativeWorld=CameraRelativeWorld;
                Lighting.HasLastCameraRelativeWorld=true;
            } else if(!Lighting.HasLastCameraRelativeWorld){
                return true;
            }

            IReadOnlyList<EmissivePatch> Patches=EmissivePatchBuilder.Build(
                new EmissivePatchBuildInput {
                    Positions=Lighting.Mesh.Positions,
                    Indices=Lighting.Mesh.Indices,
                    ObjectWorld=Lighting.MeshLocalTransform*Lighting.LastCameraRelativeWorld,
                    EmissiveNits=Lighting.EmissiveNits,
                    EmissiveTextureLuminanceScale=Lighting.EmissiveTextureLuminanceScale,
                });
            Renderer.SubmitDirectLighting(
                Patches,
                Lighting.RangeMeters,
                Lighting.CastsShadows,
                Lighting.ShadowPriority,
                Lighting.ShadowBiasMeters);
        }
        return true;
    }
}

/// <summary>
/// Registers Parent with the generated World lookup surface even for a Scene that contains only
/// roots today. RuntimeWorldTransform resolves arbitrary future reparenting through this API.
/// </summary>
internal struct HierarchyQueryRegistration:ISystem { public void Spawn(ref Parent Parent){} }

[SystemPhase(SystemPhase.Presentation)]
[SystemOrder(20_000)]
internal struct AudioFrameBegin:ISystem {
    public bool Update(ref AppHostRuntime Runtime,EngineHost Host,ref State State){
        Host.BeginAudioFrame();
        return true;
    }
}

[SystemPhase(SystemPhase.Presentation)]
[SystemOrder(20_001)]
internal struct AudioListenerSync:ISystem {
    public bool Update(Entity Entity,ref Transform Transform,ref AudioListener Listener,EngineHost Host,ref State State){
        FixedTransformResolveStatus Status=RuntimeWorldTransform.TryResolveWorld(
            Entity,State.World,out FixedAffineTransform Pose,out var Frame);
        if(Status==FixedTransformResolveStatus.Success){ Host.SetAudioListener(Entity,Pose,Frame); }
        return true;
    }
}

[SystemPhase(SystemPhase.Presentation)]
[SystemOrder(20_002)]
internal struct AudioSourceSync:ISystem {
    public bool Update(Entity Entity,ref Transform Transform,ref AudioSource Source,EngineHost Host,ref State State){
        FixedTransformResolveStatus Status=RuntimeWorldTransform.TryResolveWorld(
            Entity,State.World,out FixedAffineTransform Pose,out var Frame);
        if(Status==FixedTransformResolveStatus.Success){
            Host.TrySubmitAudioSource(
                Entity,Pose,Frame,Source.Volume,Source.MinimumDistance,Source.MaximumDistance,Source.Loop);
        }
        return true;
    }
}


[SystemPhase(SystemPhase.Presentation)]
[SystemOrder(30_000)]
internal struct Present:ISystem {
    public bool Update(ref LevelRuntime Runtime,EngineHost Host,ref State State){
        if(State.Enabled&&!Host.Present()){ State.Enabled=false; }
        return true;
    }
}
