using System.Numerics;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Platform.Windows;
using CSharpGameEngine.Runtime.Windows;

namespace UserEcsProject.Systems;

[SystemOrder(1)]
file sealed class Order;

internal struct Window:ISystem {
    public bool Update(ref AppHost App,ref State State){
        if(App.Window is not{ }Window){
            App.Window=new Win32Window(new Win32WindowOptions { Title=App.Title,Width=App.Width,Height=App.Height,Visible=true });
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
    private bool HasEnvironment;
    private bool HasSun;
    private bool HasPlayerStart;
    private bool CollectionPassStarted;
    private bool RosterCaptured;

    public bool Update(ref Transform Transform,ref StaticMesh Mesh,ref State State){
        if(!RosterCaptured)(PendingStaticMeshes??=[]).Add((Transform,Mesh));
        return true;
    }

    public bool Update(ref Transform Transform,ref PrimitiveMesh Mesh,ref State State){
        if(!RosterCaptured)(PendingPrimitives??=[]).Add((Transform,Mesh));
        return true;
    }

    public bool Update(ref AmbientLight Ambient,ref HeightFog Fog,ref State State){
        if(!RosterCaptured){ PendingAmbient=Ambient;PendingFog=Fog;HasEnvironment=true; }
        return true;
    }

    public bool Update(ref UserEcsProject.DirectionalLight Sun,ref State State){
        if(!RosterCaptured&&Sun.Renderer is null){ PendingSun=Sun;HasSun=true; }
        return true;
    }

    public bool Update(ref Transform Transform,ref PlayerStart PlayerStart,ref State State){
        if(!RosterCaptured){ PendingPlayerTransform=Transform;PendingPlayerStart=PlayerStart;HasPlayerStart=true; }
        return true;
    }

    public bool Update(ref AppHost App,ref Level Level,ref State State){
        if(Level.Loaded){ Level.Renderer?.SetCamera(Level.Camera);return true; }
        if(App.Window is not{ }Window)return true;
        if(!CollectionPassStarted){ CollectionPassStarted=true;return true; }
        if(!HasEnvironment||!HasSun||!HasPlayerStart){
            throw new InvalidOperationException("FirstPersonStageSceneの環境光、Fog、DirectionalLight、PlayerStartが揃っていません。");
        }

        StaticMeshSceneData Scene=StaticMeshSceneData.Build(PendingStaticMeshes??=[],PendingPrimitives??=[]);
        StageRenderer Renderer=StageRenderer.Create(
            Window.Handle,Window.ClientWidth,Window.ClientHeight,new GraphicsDeviceOptions(),
            Path.Combine(AppContext.BaseDirectory,Level.ShaderPath),Scene.Meshes,Scene.Materials,Scene.Objects);
        Window.Resized+=(Width,Height)=>Renderer.Resize(Width,Height);

        Camera Camera=Camera.CreateFirstPerson(new Vector3(PendingPlayerTransform.PositionX,PendingPlayerTransform.PositionY,PendingPlayerTransform.PositionZ));
        Camera.Yaw=PendingPlayerStart.YawRadians;
        Camera.Pitch=PendingPlayerStart.PitchRadians;
        Level.Camera=Camera;
        Level.Renderer=Renderer;
        Level.Loaded=true;
        Renderer.SetCamera(Camera);

        UserEcsProject.DirectionalLight Sun=PendingSun;Sun.Renderer=Renderer;State.World.Create(Sun);
        AmbientLight Ambient=PendingAmbient;Ambient.Renderer=Renderer;State.World.Create(Ambient);
        HeightFog Fog=PendingFog;Fog.Renderer=Renderer;State.World.Create(Fog);

        for(var ObjectIndex=0;ObjectIndex<Scene.Objects.Length;ObjectIndex++){
            Matrix4x4.Decompose(Scene.Transforms[ObjectIndex],out Vector3 Scale,out Quaternion Rotation,out Vector3 Translation);
            State.World.Create(
                new Transform {
                    PositionX=Translation.X,PositionY=Translation.Y,PositionZ=Translation.Z,
                    RotationX=Rotation.X,RotationY=Rotation.Y,RotationZ=Rotation.Z,RotationW=Rotation.W,
                    ScaleX=Scale.X,ScaleY=Scale.Y,ScaleZ=Scale.Z,
                },
                new Renderable { Renderer=Renderer,ObjectIndex=ObjectIndex });
        }

        RosterCaptured=true;
        PendingStaticMeshes=null;
        PendingPrimitives=null;
        State.World.Create(new FramePresent { Window=Window,Renderer=Renderer,AudioEngine=null });
        return true;
    }
}

internal struct DirectionalLightSync:ISystem {
    public bool Update(ref UserEcsProject.DirectionalLight Light,ref State State){
        Light.Renderer?.SetDirectionalLight(
            new Vector3(Light.DirectionTowardLightX,Light.DirectionTowardLightY,Light.DirectionTowardLightZ),
            new Vector3(Light.ColorR,Light.ColorG,Light.ColorB),Light.Intensity);
        return true;
    }
}

internal struct AmbientLightSync:ISystem {
    public bool Update(ref AmbientLight Light,ref State State){
        Light.Renderer?.SetAmbientLight(new Vector3(Light.ColorR,Light.ColorG,Light.ColorB)*Light.Intensity);
        return true;
    }
}

internal struct HeightFogSync:ISystem {
    public bool Update(ref HeightFog Fog,ref State State){
        Fog.Renderer?.SetFog(new FogParams(Fog.Density,Fog.HeightFalloff,new Vector3(Fog.ColorR,Fog.ColorG,Fog.ColorB)));
        return true;
    }
}

internal struct PointLightSync:ISystem {
    public bool Update(ref UserEcsProject.PointLight Light,ref State State){
        Light.Renderer?.AddPointLight(new Vector3(Light.PositionX,Light.PositionY,Light.PositionZ),new Vector3(Light.ColorR,Light.ColorG,Light.ColorB),Light.Intensity,Light.Range);
        return true;
    }
}

internal struct SpotLightSync:ISystem {
    public bool Update(ref UserEcsProject.SpotLight Light,ref State State){
        Light.Renderer?.AddSpotLight(new Vector3(Light.PositionX,Light.PositionY,Light.PositionZ),new Vector3(Light.DirectionX,Light.DirectionY,Light.DirectionZ),new Vector3(Light.ColorR,Light.ColorG,Light.ColorB),Light.Intensity,Light.Range,Light.InnerConeAngleRadians,Light.OuterConeAngleRadians);
        return true;
    }
}

internal struct TransformSync:ISystem {
    public bool Update(ref Transform Transform,ref Renderable Renderable,ref State State){
        if(Renderable.Renderer is{ }Renderer){
            Matrix4x4 World=Matrix4x4.CreateScale(Transform.ScaleX,Transform.ScaleY,Transform.ScaleZ)
                *Matrix4x4.CreateFromQuaternion(new Quaternion(Transform.RotationX,Transform.RotationY,Transform.RotationZ,Transform.RotationW))
                *Matrix4x4.CreateTranslation(Transform.PositionX,Transform.PositionY,Transform.PositionZ);
            Renderer.SetObjectTransform(Renderable.ObjectIndex,World);
        }
        return true;
    }
}

internal struct AudioListenerSpawn:ISystem { public void Spawn(ref AudioListener Listener){} }

internal struct AudioPlayback:ISystem {
    public bool Update(ref AudioSource Source,ref State State){
        if(Source.Voice is not{ }Voice)return true;
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
