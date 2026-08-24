using System.Numerics;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Platform.Windows;
using RenderCamera=CSharpGameEngine.Graphics.D3D12.Camera;
using Camera=CSharpGameEngine.Runtime.Windows.Camera;

namespace CSharpGameEngine.Runtime.Windows;

[SystemOrder(1)]
file sealed class Order;

/// <summary>LevelLoadが確定したRendererを、Scene由来Runtime Componentへ遅延で接続する。</summary>
internal static class RuntimeRendererBinding {
    private static StageRenderer?ActiveRenderer;
    private static FixedAffineTransform CameraWorldOrigin;
    private static bool HasCameraWorldOrigin;

    public static void SetActive(StageRenderer Renderer,in FixedAffineTransform CameraOrigin){
        ActiveRenderer=Renderer;
        SetCameraWorldOrigin(CameraOrigin);
    }

    public static void SetCameraWorldOrigin(in FixedAffineTransform CameraOrigin){
        CameraWorldOrigin=CameraOrigin;
        HasCameraWorldOrigin=true;
    }

    public static void Clear(StageRenderer Renderer){
        if(ReferenceEquals(ActiveRenderer,Renderer)){
            ActiveRenderer=null;
            CameraWorldOrigin=default;
            HasCameraWorldOrigin=false;
        }
    }

    public static StageRenderer?Resolve(ref StageRenderer? RuntimeRenderer){
        if(RuntimeRenderer is { IsDisposed:true }){
            RuntimeRenderer=null;
        }

        if(ActiveRenderer is { IsDisposed:true }){
            ActiveRenderer=null;
            CameraWorldOrigin=default;
            HasCameraWorldOrigin=false;
        }

        return RuntimeRenderer??=ActiveRenderer;
    }

    public static bool TryGetCameraWorldOrigin(out FixedAffineTransform CameraOrigin){
        CameraOrigin=CameraWorldOrigin;
        return HasCameraWorldOrigin;
    }
}

/// <summary>Transformの位置とローカル+Zを、ライト用のScale非依存なワールド入力へ写像する。</summary>
internal static class LightTransform {
    public static FixedTransformStatus TryGetCameraRelativePose(
        Entity Entity,World EcsWorld,
        out Vector3 Position,
        out Vector3 Forward,
        out FixedTransformResolveStatus ResolveStatus)=>
        RuntimeWorldTransform.TryGetCameraRelativePose(
            Entity,EcsWorld,out Position,out Forward,out ResolveStatus);
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

internal struct Window:ISystem {
    private Entity HostEntity;
    private bool HasHostEntity;

    public bool Update(Entity Entity,ref AppHost App,ref AppHostRuntime Runtime,ref State State){
        if(!this.HasHostEntity){this.HostEntity=Entity;this.HasHostEntity=true;}
        else if(!this.HostEntity.Equals(Entity)){
            throw new InvalidOperationException("Runtime requires exactly one AppHost.");
        }
        if(Runtime.Window is not{ }Window){
            Runtime.Window=new Win32Window(new Win32WindowOptions { Title=App.Title,Width=App.Width,Height=App.Height,Visible=true });
            return true;
        }
        Window.PumpEvents();
        if(Window.IsClosed){
            State.Enabled=false;
            return false;
        }
        return true;
    }
}

/// <summary>
/// Scene DSLが生成した個別メッシュと環境Componentを一巡収集してから、次フレームで
/// 固定ロースター型StageRendererを構築する。Scene由来の子Archetypeは実System由来の
/// {AppHost,Level}より後ろへ登録されるため、同一フレームで確定してはならない。
/// </summary>
internal struct LevelLoad:ISystem {
    private Entity LevelEntity;
    private bool HasLevelEntity;
    private List<(Entity Entity,Transform Transform,StaticMesh Mesh)>?PendingStaticMeshes;
    private List<(Entity Entity,Transform Transform,PrimitiveMesh Mesh)>?PendingPrimitives;
    private Entity PendingCameraEntity;
    private Camera PendingCamera;
    private int ActiveCameraCount;
    private bool CollectionPassStarted;
    private bool RosterCaptured;

    public bool Update(Entity Entity,ref Transform Transform,ref StaticMesh Mesh,ref State State){
        if(!this.RosterCaptured)(this.PendingStaticMeshes??=[]).Add((Entity,Transform,Mesh));
        return true;
    }

    public bool Update(Entity Entity,ref Transform Transform,ref PrimitiveMesh Mesh,ref State State){
        if(!this.RosterCaptured)(this.PendingPrimitives??=[]).Add((Entity,Transform,Mesh));
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
    public bool Update(Entity Entity,ref AppHost App,ref AppHostRuntime AppRuntime,ref Level Level,ref LevelRuntime LevelRuntime,ref State State){
        if(!this.HasLevelEntity){this.LevelEntity=Entity;this.HasLevelEntity=true;}
        else if(!this.LevelEntity.Equals(Entity)){
            throw new InvalidOperationException("Runtime requires exactly one Level.");
        }
        if(LevelRuntime.Loaded){
            FixedTransformResolveStatus CameraResolveStatus=RuntimeWorldTransform.TryResolveWorld(
                this.PendingCameraEntity,State.World,out FixedAffineTransform CameraWorld);
            LevelRuntime.LastCameraWorldResolveStatus=CameraResolveStatus;
            if(CameraResolveStatus==FixedTransformResolveStatus.Success){
                FixedTransformStatus CameraStatus=CameraTransform.TryApplyWorldOrientation(
                    CameraWorld,ref LevelRuntime.RenderCamera,out _);
                LevelRuntime.LastCameraTransformStatus=CameraStatus;
                if(CameraStatus==FixedTransformStatus.Success){
                    // The generated loop is archetype-major. Refresh before any Renderable/light
                    // archetype executes so every GPU submission in this frame uses one camera origin.
                    RuntimeRendererBinding.SetCameraWorldOrigin(CameraWorld);
                    LevelRuntime.HasLastCameraWorldPose=true;
                }
            } else {
                LevelRuntime.LastCameraTransformStatus=RuntimeWorldTransform.ToTransformStatus(
                    CameraResolveStatus);
            }
            LevelRuntime.Renderer?.SetCamera(LevelRuntime.RenderCamera);
            return true;
        }
        if(AppRuntime.Window is not{ }Window)return true;
        if(!this.CollectionPassStarted){
            this.CollectionPassStarted=true;return true; }
        if(this.ActiveCameraCount==0){
            throw new InvalidOperationException("Runtime requires exactly one active Camera, but none was found.");
        }
        if(this.ActiveCameraCount>1){
            throw new InvalidOperationException($"Runtime requires exactly one active Camera, but {this.ActiveCameraCount} were found.");
        }

        var Scene=StageSceneDataBuilder.Build(
            this.PendingStaticMeshes??=[],
            this.PendingPrimitives??=[]);
        var Renderer=StageRenderer.Create(
                                          Window.Handle,Window.ClientWidth,Window.ClientHeight,new GraphicsDeviceOptions(),
                                          Path.Combine(AppContext.BaseDirectory,Level.ShaderPath),Scene.Meshes,Scene.Materials,Scene.Objects);
        Window.Resized+=(Width,Height)=>Renderer.Resize(Width,Height);

        var Camera=RenderCamera.CreateFirstPerson(Vector3.Zero);
        FixedTransformResolveStatus InitialCameraResolveStatus=RuntimeWorldTransform.TryResolveWorld(
            this.PendingCameraEntity,State.World,out FixedAffineTransform InitialCameraWorld);
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

        Camera.FovYRadians= this.PendingCamera.FovYRadians;
        Camera.NearZ= this.PendingCamera.NearZ;
        Camera.FarZ= this.PendingCamera.FarZ;
        Camera.ExposureEv100=this.PendingCamera.ExposureEv100;
        LevelRuntime.RenderCamera=Camera;
        LevelRuntime.Renderer=Renderer;
        RuntimeRendererBinding.SetActive(Renderer,InitialCameraWorld);
        LevelRuntime.HasLastCameraWorldPose=true;
        LevelRuntime.Loaded=true;
        Renderer.SetCamera(Camera);


        for(var ObjectIndex=0;ObjectIndex<Scene.Objects.Length;ObjectIndex++){
            FixedTransformStatus Status=RuntimeWorldTransform.TryGetCameraRelativeMatrix(
                Scene.SourceEntities[ObjectIndex],State.World,
                out Matrix4x4 CameraRelativeWorld,
                out FixedTransformResolveStatus ResolveStatus);
            if(Status!=FixedTransformStatus.Success){
                throw new InvalidOperationException(
                    $"The initial render pose cannot be represented relative to the active camera: {Status}.");
            }
            Renderer.SetObjectTransform(
                ObjectIndex,
                Scene.ObjectLocalTransforms[ObjectIndex]*CameraRelativeWorld);
            State.World.Create(
                Scene.ObjectTransforms[ObjectIndex],
                new Renderable {
                    Renderer=Renderer,
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
                State.World.Create(
                    Scene.ObjectTransforms[ObjectIndex],
                    new EmissiveMeshLighting {
                        Renderer=Renderer,
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

        this.RosterCaptured=true;
        this.PendingStaticMeshes=null;
        this.PendingPrimitives=null;
        State.World.Create(new FramePresent { Window=Window,Renderer=Renderer,AudioEngine=null });
        return true;
    }
}

internal struct CameraOriginSync:ISystem {
    public bool Update(Entity Entity,ref Transform Transform,ref Camera Camera,ref State State){
        if(Camera.Active
            &&RuntimeWorldTransform.TryResolveWorld(Entity,State.World,out FixedAffineTransform CameraWorld)
                ==FixedTransformResolveStatus.Success
            &&CameraWorld.TryGetCameraBasis(out Vector3 Forward,out Vector3 Up)
                ==FixedTransformStatus.Success
            &&RenderCamera.TryNormalizeOrientation(Forward,Up,
                out _,out _)){
            RuntimeRendererBinding.SetCameraWorldOrigin(CameraWorld);
        }
        return true;
    }
}

internal struct DirectionalLightSync:ISystem {
    public bool Update(Entity Entity,ref Transform Transform,ref DirectionalLight Light,ref DirectionalLightRuntime Runtime,ref State State){
        if(RuntimeRendererBinding.Resolve(ref Runtime.Renderer) is { }Renderer){
            FixedTransformStatus Status=LightTransform.TryGetCameraRelativePose(
                Entity,State.World,out _,out Vector3 Forward,out FixedTransformResolveStatus ResolveStatus);
            Runtime.LastTransformStatus=Status;
            Runtime.LastWorldResolveStatus=ResolveStatus;
            if(Status==FixedTransformStatus.Success){
                Runtime.LastWorldForward=Forward;
                Runtime.HasLastWorldForward=true;
            } else if(!Runtime.HasLastWorldForward){
                return true;
            }
            Renderer.AddDirectionalLight(
                Runtime.LastWorldForward,new Vector3(Light.ColorR,Light.ColorG,Light.ColorB),
                Light.IlluminanceLux,Light.CastsShadows,Light.ShadowPriority,Light.ShadowBiasMeters);
        }
        return true;
    }
}

internal struct AmbientLightSync:ISystem {
    public bool Update(ref AmbientLight Light,ref AmbientLightRuntime Runtime,ref State State){
        RuntimeRendererBinding.Resolve(ref Runtime.Renderer)?.SetAmbientLight(
            new Vector3(Light.ColorR,Light.ColorG,Light.ColorB)*Light.Intensity);
        return true;
    }
}

internal struct HeightFogSync:ISystem {
    public bool Update(ref HeightFog Fog,ref HeightFogRuntime Runtime,ref State State){
        RuntimeRendererBinding.Resolve(ref Runtime.Renderer)?.SetFog(
            new FogParams(Fog.Density,Fog.HeightFalloff,new Vector3(Fog.ColorR,Fog.ColorG,Fog.ColorB)));
        return true;
    }
}

internal struct PointLightSync:ISystem {
    public bool Update(Entity Entity,ref Transform Transform,ref PointLight Light,ref PointLightRuntime Runtime,ref State State){
        if(RuntimeRendererBinding.Resolve(ref Runtime.Renderer) is { }Renderer){
            FixedTransformStatus Status=LightTransform.TryGetCameraRelativePose(
                Entity,State.World,out Vector3 Position,out _,out FixedTransformResolveStatus ResolveStatus);
            Runtime.LastTransformStatus=Status;
            Runtime.LastWorldResolveStatus=ResolveStatus;
            if(Status==FixedTransformStatus.Success){
                Runtime.LastCameraRelativePosition=Position;
                Runtime.HasLastCameraRelativePosition=true;
            } else if(!Runtime.HasLastCameraRelativePosition){
                return true;
            }
            Renderer.AddPointLight(
                Runtime.LastCameraRelativePosition,new Vector3(Light.ColorR,Light.ColorG,Light.ColorB),
                Light.LuminousFluxLumens,Light.RangeMeters,
                Light.CastsShadows,Light.ShadowPriority,Light.ShadowBiasMeters);
        }
        return true;
    }
}

internal struct SpotLightSync:ISystem {
    public bool Update(Entity Entity,ref Transform Transform,ref SpotLight Light,ref SpotLightRuntime Runtime,ref State State){
        if(RuntimeRendererBinding.Resolve(ref Runtime.Renderer) is { }Renderer){
            FixedTransformStatus Status=LightTransform.TryGetCameraRelativePose(
                Entity,State.World,out Vector3 Position,out Vector3 Forward,out FixedTransformResolveStatus ResolveStatus);
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
            Renderer.AddSpotLight(
                Runtime.LastCameraRelativePosition,Runtime.LastWorldForward,new Vector3(Light.ColorR,Light.ColorG,Light.ColorB),
                Light.LuminousFluxLumens,Light.RangeMeters,Light.InnerConeFullAngleDegrees,Light.OuterConeFullAngleDegrees,
                Light.CastsShadows,Light.ShadowPriority,Light.ShadowBiasMeters);
        }
        return true;
    }
}

internal struct AreaLightSync:ISystem {
    public bool Update(Entity Entity,ref Transform Transform,ref AreaLight Light,ref AreaLightRuntime Runtime,ref State State){
        if(RuntimeRendererBinding.Resolve(ref Runtime.Renderer) is { }Renderer){
            FixedTransformStatus Status=LightTransform.TryGetCameraRelativePose(
                Entity,State.World,out Vector3 Position,out Vector3 Forward,out FixedTransformResolveStatus ResolveStatus);
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
            Renderer.AddAreaLight(
                Runtime.LastCameraRelativePosition,Runtime.LastWorldForward,new Vector3(Light.ColorR,Light.ColorG,Light.ColorB),
                Light.LuminanceNits,Light.WidthMeters,Light.HeightMeters,Light.RangeMeters,
                Light.CastsShadows,Light.ShadowPriority,Light.ShadowBiasMeters);
        }
        return true;
    }
}

internal struct TransformSync:ISystem {
    public bool Update(Entity Entity,ref Transform Transform,ref Renderable Renderable,ref State State){
        if(Renderable.Renderer is{ }Renderer){
            Entity SourceEntity=Renderable.HasSourceEntity?Renderable.SourceEntity:Entity;
            FixedTransformStatus Status=RuntimeWorldTransform.TryGetCameraRelativeMatrix(
                SourceEntity,State.World,out Matrix4x4 CameraRelativeWorld,
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

internal struct EmissiveMeshLightSync:ISystem {
    public bool Update(
        Entity Entity,
        ref Transform Transform,
        ref EmissiveMeshLighting Lighting,
        ref State State){
        if(RuntimeRendererBinding.Resolve(ref Lighting.Renderer) is { }Renderer){
            Entity SourceEntity=Lighting.HasSourceEntity?Lighting.SourceEntity:Entity;
            FixedTransformStatus Status=RuntimeWorldTransform.TryGetCameraRelativeMatrix(
                SourceEntity,State.World,out Matrix4x4 CameraRelativeWorld,
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

internal struct AudioListenerSpawn:ISystem { public void Spawn(ref AudioListener Listener){} }
/// <summary>
/// Registers Parent with the generated World lookup surface even for a Scene that contains only
/// roots today. RuntimeWorldTransform resolves arbitrary future reparenting through this API.
/// </summary>
internal struct HierarchyQueryRegistration:ISystem { public void Spawn(ref Parent Parent){} }


internal struct AudioPlayback:ISystem {
    public bool Update(ref AudioSource Source,ref AudioSourceRuntime Runtime,ref State State){
        if(Runtime.Voice is not{ }Voice)return true;
        if(!State.Enabled){ WindowsAudio.Stop(Voice);WindowsAudio.DestroyVoice(Voice);return true; }
        WindowsAudio.SetVolume(Voice,Source.Volume);
        return true;
    }
}

internal struct Present:ISystem {
    public bool Update(ref FramePresent Present,ref State State){
        if(!State.Enabled||Present.Window is not{ }Window||Present.Renderer is not{ }Renderer)return true;
        if(!Window.IsMinimized)Renderer.RenderFrame();
        return true;
    }

    public void Shutdown(ref FramePresent Present,ref State State){
        if(Present.Renderer is{ }Renderer){
            RuntimeRendererBinding.Clear(Renderer);
            Renderer.Dispose();
            Present.Renderer=null;
        }

        if(Present.Window is{ }Window){
            Window.Dispose();
            Present.Window=null;
        }

        if(Present.AudioEngine is{ }AudioEngine){
            WindowsAudio.DestroyEngine(AudioEngine);
            Present.AudioEngine=null;
        }
    }
}
