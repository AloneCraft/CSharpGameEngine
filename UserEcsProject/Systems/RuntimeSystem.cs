using System.Numerics;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Platform.Windows;
using CSharpGameEngine.Runtime.Windows;
using RenderCamera=CSharpGameEngine.Graphics.D3D12.Camera;
using SceneCamera=CSharpGameEngine.Runtime.Windows.Camera;

namespace UserEcsProject.Systems;

[SystemOrder(1)]
file sealed class Order;

internal struct Window:ISystem {
    public bool Update(ref AppHost App,ref AppHostRuntime Runtime,ref State State){
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
    private List<(Transform Transform,StaticMesh Mesh)>?PendingStaticMeshes;
    private List<(Transform Transform,PrimitiveMesh Mesh)>?PendingPrimitives;
    private AmbientLight PendingAmbient;
    private HeightFog PendingFog;
    private UserEcsProject.DirectionalLight PendingSun;
    private Transform PendingPlayerTransform;
    private PlayerStart PendingPlayerStart;
    private Transform PendingCameraTransform;
    private SceneCamera PendingCamera;
    private bool HasEnvironment;
    private bool HasSun;
    private bool HasPlayerStart;
    private bool HasActiveCamera;
    private bool CollectionPassStarted;
    private bool RosterCaptured;

    public bool Update(ref Transform Transform,ref StaticMesh Mesh,ref State State){
        if(!this.RosterCaptured)(this.PendingStaticMeshes??=[]).Add((Transform,Mesh));
        return true;
    }

    public bool Update(ref Transform Transform,ref PrimitiveMesh Mesh,ref State State){
        if(!this.RosterCaptured)(this.PendingPrimitives??=[]).Add((Transform,Mesh));
        return true;
    }

    public bool Update(ref AmbientLight Ambient,ref HeightFog Fog,ref State State){
        if(!this.RosterCaptured){
            this.PendingAmbient=Ambient;
            this.PendingFog=Fog;
            this.HasEnvironment=true; }
        return true;
    }

    public bool Update(ref UserEcsProject.DirectionalLight Sun,ref DirectionalLightRuntime Runtime,ref State State){
        if(!this.RosterCaptured&&Runtime.Renderer is null){
            this.PendingSun=Sun;
            this.HasSun=true; }
        return true;
    }

    public bool Update(ref Transform Transform,ref PlayerStart PlayerStart,ref State State){
        if(!this.RosterCaptured){
            this.PendingPlayerTransform=Transform;
            this.PendingPlayerStart=PlayerStart;
            this.HasPlayerStart=true; }
        return true;
    }

    public bool Update(ref Transform Transform,ref SceneCamera Camera,ref State State){
        if(!this.RosterCaptured&&Camera.Active){
            this.PendingCameraTransform=Transform;
            this.PendingCamera=Camera;
            this.HasActiveCamera=true; }
        return true;
    }

    public bool Update(ref AppHost App,ref AppHostRuntime AppRuntime,ref Level Level,ref LevelRuntime LevelRuntime,ref State State){
        if(LevelRuntime.Loaded){ LevelRuntime.Renderer?.SetCamera(LevelRuntime.RenderCamera);return true; }
        if(AppRuntime.Window is not{ }Window)return true;
        if(!this.CollectionPassStarted){
            this.CollectionPassStarted=true;return true; }
        if(!this.HasEnvironment||!this.HasSun||!this.HasPlayerStart||!this.HasActiveCamera){
            throw new InvalidOperationException("FirstPersonStageSceneの環境光、Fog、DirectionalLight、PlayerStartが揃っていません。");
        }

        var Scene=StaticMeshSceneData.Build(this.PendingStaticMeshes??=[], this.PendingPrimitives??=[]);
        var Renderer=StageRenderer.Create(
                                          Window.Handle,Window.ClientWidth,Window.ClientHeight,new GraphicsDeviceOptions(),
                                          Path.Combine(AppContext.BaseDirectory,Level.ShaderPath),Scene.Meshes,Scene.Materials,Scene.Objects);
        Window.Resized+=(Width,Height)=>Renderer.Resize(Width,Height);

        var Camera=RenderCamera.CreateFirstPerson(new Vector3(this.PendingCameraTransform.PositionX, this.PendingCameraTransform.PositionY, this.PendingCameraTransform.PositionZ));
        Camera.Yaw= this.PendingPlayerStart.YawRadians;
        Camera.Pitch= this.PendingPlayerStart.PitchRadians;
        Camera.FovYRadians= this.PendingCamera.FovYRadians;
        Camera.NearZ= this.PendingCamera.NearZ;
        Camera.FarZ= this.PendingCamera.FarZ;
        LevelRuntime.RenderCamera=Camera;
        LevelRuntime.Renderer=Renderer;
        LevelRuntime.Loaded=true;
        Renderer.SetCamera(Camera);

        State.World.Create(this.PendingSun,new DirectionalLightRuntime { Renderer=Renderer });
        State.World.Create(this.PendingAmbient,new AmbientLightRuntime { Renderer=Renderer });
        State.World.Create(this.PendingFog,new HeightFogRuntime { Renderer=Renderer });

        for(var ObjectIndex=0;ObjectIndex<Scene.Objects.Length;ObjectIndex++){
            Matrix4x4.Decompose(Scene.Transforms[ObjectIndex],out var Scale,out var Rotation,out var Translation);
            State.World.Create(
                new Transform {
                    PositionX=Translation.X,PositionY=Translation.Y,PositionZ=Translation.Z,
                    RotationX=Rotation.X,RotationY=Rotation.Y,RotationZ=Rotation.Z,RotationW=Rotation.W,
                    ScaleX=Scale.X,ScaleY=Scale.Y,ScaleZ=Scale.Z,
                },
                new Renderable { Renderer=Renderer,ObjectIndex=ObjectIndex });
        }

        this.RosterCaptured=true;
        this.PendingStaticMeshes=null;
        this.PendingPrimitives=null;
        State.World.Create(new FramePresent { Window=Window,Renderer=Renderer,AudioEngine=null });
        return true;
    }
}

internal struct DirectionalLightSync:ISystem {
    public bool Update(ref UserEcsProject.DirectionalLight Light,ref DirectionalLightRuntime Runtime,ref State State){
        Runtime.Renderer?.SetDirectionalLight(
            new Vector3(Light.DirectionTowardLightX,Light.DirectionTowardLightY,Light.DirectionTowardLightZ),
            new Vector3(Light.ColorR,Light.ColorG,Light.ColorB),Light.Intensity);
        return true;
    }
}

internal struct AmbientLightSync:ISystem {
    public bool Update(ref AmbientLight Light,ref AmbientLightRuntime Runtime,ref State State){
        Runtime.Renderer?.SetAmbientLight(new Vector3(Light.ColorR,Light.ColorG,Light.ColorB)*Light.Intensity);
        return true;
    }
}

internal struct HeightFogSync:ISystem {
    public bool Update(ref HeightFog Fog,ref HeightFogRuntime Runtime,ref State State){
        Runtime.Renderer?.SetFog(new FogParams(Fog.Density,Fog.HeightFalloff,new Vector3(Fog.ColorR,Fog.ColorG,Fog.ColorB)));
        return true;
    }
}

internal struct PointLightSync:ISystem {
    public bool Update(ref UserEcsProject.PointLight Light,ref PointLightRuntime Runtime,ref State State){
        Runtime.Renderer?.AddPointLight(new Vector3(Light.PositionX,Light.PositionY,Light.PositionZ),new Vector3(Light.ColorR,Light.ColorG,Light.ColorB),Light.Intensity,Light.Range);
        return true;
    }
}

internal struct SpotLightSync:ISystem {
    public bool Update(ref UserEcsProject.SpotLight Light,ref SpotLightRuntime Runtime,ref State State){
        Runtime.Renderer?.AddSpotLight(new Vector3(Light.PositionX,Light.PositionY,Light.PositionZ),new Vector3(Light.DirectionX,Light.DirectionY,Light.DirectionZ),new Vector3(Light.ColorR,Light.ColorG,Light.ColorB),Light.Intensity,Light.Range,Light.InnerConeAngleRadians,Light.OuterConeAngleRadians);
        return true;
    }
}

internal struct TransformSync:ISystem {
    public bool Update(ref Transform Transform,ref Renderable Renderable,ref State State){
        if(Renderable.Renderer is{ }Renderer){
            var World=Matrix4x4.CreateScale(Transform.ScaleX,Transform.ScaleY,Transform.ScaleZ)
                     *Matrix4x4.CreateFromQuaternion(new Quaternion(Transform.RotationX,Transform.RotationY,Transform.RotationZ,Transform.RotationW))
                     *Matrix4x4.CreateTranslation(Transform.PositionX,Transform.PositionY,Transform.PositionZ);
            Renderer.SetObjectTransform(Renderable.ObjectIndex,World);
        }
        return true;
    }
}

internal struct AudioListenerSpawn:ISystem { public void Spawn(ref AudioListener Listener){} }

internal struct AudioPlayback:ISystem {
    public bool Update(ref AudioSource Source,ref AudioSourceRuntime Runtime,ref State State){
        if(Runtime.Voice is not{ }Voice)return true;
        if(!State.Enabled){ GameAudio.Stop(Voice);GameAudio.DestroyVoice(Voice);return true; }
        GameAudio.SetVolume(Voice,Source.Volume);
        return true;
    }
}

internal struct Present:ISystem {
    public bool Update(ref FramePresent Present,ref State State){
        if(Present.Window is not{ }Window||Present.Renderer is not{ }Renderer)return true;
        if(!State.Enabled){
            Renderer.Dispose();Window.Dispose();
            if(Present.AudioEngine is{ }AudioEngine)GameAudio.DestroyEngine(AudioEngine);
            return true;
        }
        if(!Window.IsMinimized)Renderer.RenderFrame();
        return true;
    }
}
