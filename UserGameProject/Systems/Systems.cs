using System.Numerics;
using CSharpGameEngine.Entities;
using CSharpGameEngine.Graphics.D3D12;
using CSharpGameEngine.Platform.Windows;
using CSharpGameEngine.Spatial.Abstractions;
using CSharpGameEngine.Spatial.Unity;

namespace UserGameProject.Systems;

// ============================================================================
// 実行順序 = このファイル内の記述順(ADR-0033決定事項1)
//
// 閉集合Archetype方式ではComponent引数がすべて`ref`になり、旧方式の`in`=Read/`ref`=Writeという
// 区別が消えるため、Write→Read依存からの自動トポロジカルソートが原理的に成立しない。代わりに
// 「ソース上の記述順をそのまま実行順にする」という規則を採用した。Roslynがファイルを渡す順序は
// 保証されないため、順序が意味を持つSystemは必ずこの1ファイルへ集約すること。
//
// 現在の順序は実際のデータ依存に一致している:
//   SceneSpawn   … 起動時に1回だけステージ・ライト・Playerを生成する(以降は何もしない)
//   Input        … Win32Windowを読み、InputStateを書く(入力の生産者)
//   PlayerControl … InputStateを読み、Position/Rotation/CameraRig等を書く
//   Camera       … Position/CameraRigを読み、Cameraを書く(PlayerControlの後でなければ
//                  カメラがちょうど1フレーム遅れる)
//   RotationMotion / OrbitMotionSystem … 回転・軌道を更新(Cameraとは独立、RenderSyncより前であればよい)
//   RenderSync   … 全ての結果をStageRendererへ流し込む(最後)
//
// Component引数の注意: タグComponent(PlayerTag/MovableTag)も`ref`で受ける必要がある。`in`にすると
// Contextパラメータとして分類され、Archetype一致に参加しなくなる(サイレント障害)。
// ============================================================================

/// <summary>
/// Turns the loaded stage into entities, once, from inside the ECS. Program.cs only creates a
/// single <see cref="SceneSpawnRequest"/> entity; every prop/light/player entity is created here
/// through <c>State.World.Create(...)</c>, whose overloads are resolved purely by the component
/// types passed - so no generated Archetype identifier ever appears in hand-written code
/// (ADR-0033). This doubles as the reference example of how to spawn from a System.
/// </summary>
internal struct SceneSpawn:ISystem {
    public bool Update(ref SceneSpawnRequest Request,ref State state){
        if(Request.Done||Request.Scene is not { } Scene){
            return true;
        }

        Request.Done=true;
        Entity parentEntity=default;
        var parentCreated=false;
        var childCreated=false;

        foreach(LoadedSceneSpawn Spawn in Scene.Spawns){
            Position position=new(){ X=Spawn.Position.X,Y=Spawn.Position.Y,Z=Spawn.Position.Z };
            Rotation rotation=new(){ X=Spawn.Rotation.X,Y=Spawn.Rotation.Y,Z=Spawn.Rotation.Z,W=Spawn.Rotation.W };
            Scale scale=new(){ X=Spawn.Scale.X,Y=Spawn.Scale.Y,Z=Spawn.Scale.Z };
            WorldTransform worldTransform=new(){ Value=Matrix4x4.Identity };
            MeshRenderer meshRenderer=new(){ Mesh=Spawn.Mesh,Material=Spawn.Material,ObjectIndex=Spawn.ObjectIndex };

            if(Spawn.Mobility==PropMobility.Movable){
                state.World.Create(
                    position,
                    rotation,
                    scale,
                    worldTransform,
                    meshRenderer,
                    new RotateMotion { AxisX=0f,AxisY=1f,AxisZ=0f,RadiansPerSecond=0.8f });
            } else {
                if(!parentCreated){
                    parentEntity=state.World.Create(position,rotation,scale,worldTransform,meshRenderer);
                    parentCreated=true;
                } else if(!childCreated){
                    state.World.Create(position,rotation,scale,worldTransform,meshRenderer,new Parent { Value=parentEntity });
                    childCreated=true;
                } else {
                    state.World.Create(position,rotation,scale,worldTransform,meshRenderer);
                }
            }
        }

        if(Scene.Sun is { } Sun){
            state.World.Create(Sun);
        }

        if(Scene.Ambient is { } Ambient){
            state.World.Create(Ambient);
        }

        if(Scene.Fog is { } Fog){
            state.World.Create(Fog);
        }

        // Ordinary data entities, not fixed renderer fields: the imported stage plus these
        // practical lights exercise the same general multi-light path used by future games.
        state.World.Create(
            new Position { X=-4f,Y=2.5f,Z=-3f },
            new PointLightData { ColorR=0.15f,ColorG=0.55f,ColorB=1f,Intensity=28f,Range=8f });
        state.World.Create(
            new Position { X=4f,Y=2.2f,Z=-2f },
            new PointLightData { ColorR=1f,ColorG=0.18f,ColorB=0.08f,Intensity=24f,Range=7f });
        state.World.Create(
            new Position { X=-3f,Y=1.7f,Z=4f },
            new PointLightData { ColorR=0.18f,ColorG=1f,ColorB=0.55f,Intensity=18f,Range=6f });
        state.World.Create(
            new Position { X=0f,Y=2.8f,Z=0f },
            new PointLightData { ColorR=0.9f,ColorG=0.2f,ColorB=1f,Intensity=20f,Range=6f },
            new OrbitMotion { CenterX=0f,CenterY=2.8f,CenterZ=0f,Radius=3.5f,AngularSpeedRadiansPerSecond=0.55f });
        state.World.Create(
            new Position { X=-5f,Y=4f,Z=-5f },
            new SpotLightData {
                DirectionX=0.6f,DirectionY=-0.7f,DirectionZ=0.4f,
                ColorR=0.55f,ColorG=0.75f,ColorB=1f,Intensity=55f,Range=14f,
                InnerConeAngleRadians=0.20f,OuterConeAngleRadians=0.42f,
            });
        state.World.Create(
            new Position { X=5f,Y=3.5f,Z=3f },
            new SpotLightData {
                DirectionX=-0.6f,DirectionY=-0.65f,DirectionZ=-0.35f,
                ColorR=1f,ColorG=0.35f,ColorB=0.12f,Intensity=50f,Range=13f,
                InnerConeAngleRadians=0.18f,OuterConeAngleRadians=0.38f,
            });

        // Two sources share one clip resource; the third follows a reusable orbit.
        state.World.Create(
            new Position { X=-2f,Y=1.2f,Z=1f },
            new AudioSourceData {
                Clip=new AudioClipHandle(0),Volume=0.28f,MinDistance=1.5f,MaxDistance=18f,
                Loop=true,Playing=true,
            });
        state.World.Create(
            new Position { X=3f,Y=1.4f,Z=-3f },
            new AudioSourceData {
                Clip=new AudioClipHandle(0),Volume=0.22f,MinDistance=1.5f,MaxDistance=18f,
                Loop=true,Playing=true,
            });
        state.World.Create(
            new Position { X=0f,Y=1.5f,Z=2f },
            new AudioSourceData {
                Clip=new AudioClipHandle(1),Volume=0.18f,MinDistance=1f,MaxDistance=14f,
                Loop=true,Playing=true,
            },
            new OrbitMotion {
                CenterX=0f,CenterY=1.5f,CenterZ=2f,Radius=4f,AngularSpeedRadiansPerSecond=0.35f,
            });

        Position PlayerStart=new(){ X=Scene.PlayerStartPosition.X,Y=Scene.PlayerStartPosition.Y,Z=Scene.PlayerStartPosition.Z };

        state.World.Create(
            PlayerStart,
            new Rotation { X=0f,Y=0f,Z=0f,W=1f },
            new Health { Value=100 },
            new VerticalMotion(),
            new CameraRig { Yaw=Scene.PlayerStartYawRadians,Pitch=0f },
            new InputState(),
            new PlayerTag(),
            new AudioListenerData(),
            CSharpGameEngine.Graphics.D3D12.Camera.CreateFirstPerson(new Vector3(PlayerStart.X,PlayerStart.Y+Camera.EyeHeight,PlayerStart.Z)));

        return true;
    }
}

/// <summary>
/// The ECS input abstraction called for by the FirstPerson-port requirements: this is the only
/// place in the game loop that touches <see cref="Win32Window"/> or a virtual-key code. Every
/// other system reads player intent exclusively through the <see cref="InputState"/> component
/// this writes, so they stay testable with a hand-built <see cref="InputState"/> and would need no
/// changes at all if the input backend were ever swapped out.
/// </summary>
internal struct Input:ISystem {
    private const int VkSpace=0x20;

    public bool Update(ref InputState state,ref PlayerTag _,Win32Window Window,ref State GameState){
        var JumpKeyDown=Window.IsKeyDown(VkSpace);
        state.JumpPressed=JumpKeyDown&&!state.WasJumpKeyDown;
        state.WasJumpKeyDown=JumpKeyDown;

        state.MoveForward=Window.IsKeyDown('W');
        state.MoveBack=Window.IsKeyDown('S');
        state.MoveLeft=Window.IsKeyDown('A');
        state.MoveRight=Window.IsKeyDown('D');

        (var DeltaX,var DeltaY)=Window.ConsumeMouseDelta();
        state.LookDeltaX=DeltaX;
        state.LookDeltaY=DeltaY;

        return true;
    }
}

/// <summary>
/// Bridges <see cref="InputState"/> + <see cref="CameraRig"/> into the existing pure character
/// motion math (<see cref="CharacterMotion"/>, unchanged from the third-person slice) and writes
/// the result back onto the player's Position/VerticalMotion/Rotation. First-person: the player
/// always faces the camera's yaw directly (there is no third-person "turn body toward movement"
/// animation concern), so the movement basis comes from CameraRig.Yaw rather than from the
/// previous frame's velocity direction.
/// </summary>
internal struct PlayerControl:ISystem {
    private const float MouseYawSensitivity=0.0025f;
    private const float MousePitchSensitivity=0.0025f;

    public bool Update(
        ref Position position,
        ref Rotation rotation,
        ref VerticalMotion verticalMotion,
        ref CameraRig cameraRig,
        ref InputState inputState,
        ref PlayerTag _,
        in FrameContext Frame,
        ref State GameState){
        cameraRig.Yaw+=inputState.LookDeltaX*MouseYawSensitivity;
        cameraRig.Pitch=Math.Clamp(
            cameraRig.Pitch-(inputState.LookDeltaY*MousePitchSensitivity),
            CSharpGameEngine.Graphics.D3D12.Camera.MinPitchRadiansFirstPerson,
            CSharpGameEngine.Graphics.D3D12.Camera.MaxPitchRadiansFirstPerson);

        // RUF boundary (ADR-0028): yaw-only facing, flat forward/right, and the resulting body
        // rotation are derived in RUF space (mirrors Camera.GetFlatForward/GetFlatRight, ADR-0026)
        // before converting back to the Vector3/Quaternion types CharacterMotion and the ECS
        // Rotation component still use.
        RufQuaternion FacingRuf=RufQuaternion.FromAxisAngle(RufVector.Up,cameraRig.Yaw);
        RufVector FlatForwardRuf=RufQuaternion.RotateVector(FacingRuf,RufVector.Forward).Normalize();
        RufVector FlatRightRuf=Spatial<UnityCoordinateConvention>.RightFromUpForward(RufVector.Up,FlatForwardRuf);

        Vector3 FlatForward=(Vector3)Spatial<UnityCoordinateConvention>.ToXyz(FlatForwardRuf);
        Vector3 FlatRight=(Vector3)Spatial<UnityCoordinateConvention>.ToXyz(FlatRightRuf);

        CharacterMotionResult Motion=CharacterMotion.Compute(new CharacterMotionInput {
            FlatForward=FlatForward,
            FlatRight=FlatRight,
            MoveForward=inputState.MoveForward,
            MoveBack=inputState.MoveBack,
            MoveLeft=inputState.MoveLeft,
            MoveRight=inputState.MoveRight,
            JumpPressed=inputState.JumpPressed,
            CurrentY=position.Y,
            VerticalVelocity=verticalMotion.Velocity,
            DeltaTime=Frame.DeltaTime,
        });

        verticalMotion=new VerticalMotion { Velocity=Motion.NewVerticalVelocity,IsGrounded=Motion.IsGrounded };
        position=new Position {
            X=position.X+(Motion.HorizontalVelocity.X*Frame.DeltaTime),
            Y=Motion.NewY,
            Z=position.Z+(Motion.HorizontalVelocity.Z*Frame.DeltaTime),
        };

        rotation=new Rotation { X=FacingRuf.R,Y=FacingRuf.U,Z=FacingRuf.F,W=FacingRuf.W };

        return true;
    }
}

/// <summary>
/// Drives the render <see cref="CSharpGameEngine.Graphics.D3D12.Camera"/> from the player's eye
/// position + <see cref="CameraRig"/> each frame.
/// <see cref="CSharpGameEngine.Graphics.D3D12.Camera"/> lives as a component directly on the player
/// entity (there is only ever one) rather than a separate singleton entity, so this stays a
/// single-archetype, single-entity loop instead of needing a cross-entity join the model does not
/// support. First-person: Camera.Target doubles as the eye position itself (see
/// <see cref="CSharpGameEngine.Graphics.D3D12.Camera.CreateFirstPerson"/>).
/// </summary>
internal struct Camera:ISystem {
    public const float EyeHeight=1.6f;

    public bool Update(ref CSharpGameEngine.Graphics.D3D12.Camera camera,ref Position position,ref CameraRig cameraRig,ref PlayerTag _,ref State GameState){
        // EyeHeight is added along RUF Up (ADR-0028), matching Camera/PlayerControl's shared
        // RUF-space convention rather than assuming which raw component is "up".
        RufVector PositionRuf=new(position.X,position.Y,position.Z);
        RufVector EyeRuf=PositionRuf+(RufVector.Up*EyeHeight);
        camera.Target=(Vector3)Spatial<UnityCoordinateConvention>.ToXyz(EyeRuf);
        camera.Yaw=cameraRig.Yaw;
        camera.Pitch=cameraRig.Pitch;

        return true;
    }
}

/// <summary>Resolves local Position/Rotation/Scale into a cached world matrix for root entities.</summary>
internal struct LocalToWorld:ISystem {
    public bool Update(Entity entity,ref Position position,ref Rotation rotation,ref Scale scale,ref WorldTransform world,TransformHierarchyRuntime hierarchy,ref State state){
        world.Value=ComposeTransform(position,rotation,scale);
        hierarchy.Set(entity.Id,world.Value);
        return true;
    }

    private static Matrix4x4 ComposeTransform(in Position position,in Rotation rotation,in Scale scale){
        Quaternion q=new(rotation.X,rotation.Y,rotation.Z,rotation.W);
        return Matrix4x4.CreateScale(scale.X,scale.Y,scale.Z)*Matrix4x4.CreateFromQuaternion(q)*Matrix4x4.CreateTranslation(position.X,position.Y,position.Z);
    }
}

/// <summary>Resolves a child after its parent root has been cached by LocalToWorld.</summary>
internal struct ParentToWorld:ISystem {
    public bool Update(Entity entity,ref Position position,ref Rotation rotation,ref Scale scale,ref WorldTransform world,ref Parent parent,TransformHierarchyRuntime hierarchy,ref State state){
        Matrix4x4 local=LocalToWorldMatrix(position,rotation,scale);
        world.Value=hierarchy.TryGet(parent.Value.Id,out Matrix4x4 parentWorld)?local*parentWorld:local;
        hierarchy.Set(entity.Id,world.Value);
        return true;
    }

    private static Matrix4x4 LocalToWorldMatrix(in Position position,in Rotation rotation,in Scale scale){
        Quaternion q=new(rotation.X,rotation.Y,rotation.Z,rotation.W);
        return Matrix4x4.CreateScale(scale.X,scale.Y,scale.Z)*Matrix4x4.CreateFromQuaternion(q)*Matrix4x4.CreateTranslation(position.X,position.Y,position.Z);
    }
}

/// <summary>
/// Demonstrates the "movable" mobility archetype: a continuous spin, recomputed fresh from
/// elapsed time each frame (not accumulated frame-to-frame) so there is no drift and no need to
/// remember each prop's original position. There is no gameplay reason for the motion beyond
/// visibly proving Static vs Movable props are independently spawnable and updatable.
/// </summary>
internal struct RotationMotion:ISystem {
    public bool Update(ref Rotation rotation,ref RotateMotion motion,in FrameContext Frame,ref State GameState){
        Vector3 axis=Vector3.Normalize(new Vector3(motion.AxisX,motion.AxisY,motion.AxisZ));
        Quaternion Spin=Quaternion.CreateFromAxisAngle(axis,motion.PhaseRadians+(Frame.ElapsedSeconds*motion.RadiansPerSecond));
        rotation=new Rotation { X=Spin.X,Y=Spin.Y,Z=Spin.Z,W=Spin.W };

        return true;
    }
}

/// <summary>Moves an entity around a horizontal circle without retaining per-frame mutable state.</summary>
internal struct OrbitMotionSystem:ISystem {
    public bool Update(ref Position position,ref OrbitMotion motion,in FrameContext Frame,ref State GameState){
        var angle=motion.PhaseRadians+(Frame.ElapsedSeconds*motion.AngularSpeedRadiansPerSecond);
        position.X=motion.CenterX+(MathF.Cos(angle)*motion.Radius);
        position.Y=motion.CenterY;
        position.Z=motion.CenterZ+(MathF.Sin(angle)*motion.Radius);
        return true;
    }
}

/// <summary>Copies the player Position into the shared audio listener resource each frame.</summary>
internal struct AudioListenerSync:ISystem {
    public bool Update(ref Position position,ref AudioListenerData _,AudioRuntime Audio,ref State GameState){
        Audio.SetListenerPosition(new Vector3(position.X,position.Y,position.Z));
        return true;
    }
}

/// <summary>Creates/updates one backend voice per source entity and applies distance attenuation.</summary>
internal struct AudioSourcePlayback:ISystem {
    public bool Update(Entity entity,ref Position position,ref AudioSourceData source,AudioRuntime Audio,ref State GameState){
        if(!source.Playing){
            Audio.RemoveSource(entity);
            return true;
        }

        Audio.UpdateSource(entity,new Vector3(position.X,position.Y,position.Z),source);
        return true;
    }
}

/// <summary>
/// Pushes ECS transform/camera/light/fog state into <see cref="StageRenderer"/> once per frame -
/// the only system that consumes <see cref="MeshRenderer"/> object indices or writes to the renderer.
/// Its renderable overload requires WorldTransform+MeshRenderer, which both the static and movable
/// stage-prop archetypes satisfy (the movable one is a strict superset), so the generator's
/// subset dispatch naturally covers both with one method - the same property the old Query-based
/// design had, and the reason the pre-migration AoS-era code needed two near-identical overloads.
/// </summary>
internal struct RenderSync:ISystem {
    public bool Update(ref WorldTransform worldTransform,ref MeshRenderer meshRenderer,StageRenderer Renderer,ref State GameState){
        Renderer.SetObjectTransform(meshRenderer.ObjectIndex,worldTransform.Value);
        return true;
    }

    public bool Update(ref CSharpGameEngine.Graphics.D3D12.Camera camera,ref PlayerTag _,StageRenderer Renderer,ref State GameState){
        Renderer.SetCamera(camera);
        return true;
    }

    /// <summary>Applies the single sun singleton, if any (0 matching entities = "not configured, leave the renderer's current/default value alone").</summary>
    public bool Update(ref DirectionalLightData Light,StageRenderer Renderer,ref State GameState){
        Renderer.SetDirectionalLight(
            new Vector3(Light.DirectionTowardLightX,Light.DirectionTowardLightY,Light.DirectionTowardLightZ),
            new Vector3(Light.ColorR,Light.ColorG,Light.ColorB),
            Light.Intensity);

        return true;
    }

    public bool Update(ref AmbientLightData Ambient,StageRenderer Renderer,ref State GameState){
        Renderer.SetAmbientLight(new Vector3(Ambient.ColorR,Ambient.ColorG,Ambient.ColorB));
        return true;
    }

    public bool Update(ref FogData Fog,StageRenderer Renderer,ref State GameState){
        Renderer.SetFog(new FogParams(Fog.Density,Fog.HeightFalloff,new Vector3(Fog.ColorR,Fog.ColorG,Fog.ColorB)));
        return true;
    }

    public bool Update(ref Position position,ref PointLightData light,StageRenderer Renderer,ref State GameState){
        Renderer.AddPointLight(
            new Vector3(position.X,position.Y,position.Z),
            new Vector3(light.ColorR,light.ColorG,light.ColorB),
            light.Intensity,
            light.Range);
        return true;
    }

    public bool Update(ref Position position,ref SpotLightData light,StageRenderer Renderer,ref State GameState){
        Renderer.AddSpotLight(
            new Vector3(position.X,position.Y,position.Z),
            new Vector3(light.DirectionX,light.DirectionY,light.DirectionZ),
            new Vector3(light.ColorR,light.ColorG,light.ColorB),
            light.Intensity,
            light.Range,
            light.InnerConeAngleRadians,
            light.OuterConeAngleRadians);
        return true;
    }

}

// ============================================================================
// Archetype形状の宣言専用System(ADR-0033決定事項2)
//
// Archetypeは`Update`/`Spawn`の引数からのみ導出される(`World.Create`の呼び出し側からは推論しない)。
// どの`Update`オーバーロードとも一致しない構成は、ここで`Spawn`として宣言しないと
// Archetypeも`World.Create`オーバーロードも生成されない。`Spawn`はメインループから呼ばれない。
// ============================================================================

/// <summary>
/// Playerの8Component構成。どの<c>Update</c>とも一致しない(特に<see cref="Health"/>はどのSystemからも
/// 参照されていない)ため、この宣言が無いとPlayerを生成できない。
/// </summary>
internal struct PlayerSpawn:ISystem {
    public void Spawn(
        ref Position position,
        ref Rotation rotation,
        ref Health health,
        ref VerticalMotion verticalMotion,
        ref CameraRig cameraRig,
        ref InputState inputState,
        ref PlayerTag playerTag,
        ref AudioListenerData audioListener,
        ref CSharpGameEngine.Graphics.D3D12.Camera camera){
    }
}

/// <summary>
/// 可動ステージプロップの構成。<see cref="RenderSync"/>(WorldTransform/MeshRenderer)と
/// <see cref="RotationMotion"/>(Rotation/RotateMotion)の両方に部分集合一致で拾われるが、その和集合
/// そのものはどの<c>Update</c>にも現れないため、ここで宣言する。
/// </summary>
internal struct MovableStagePropSpawn:ISystem {
    public void Spawn(
        ref Position position,
        ref Rotation rotation,
        ref Scale scale,
        ref WorldTransform worldTransform,
        ref MeshRenderer meshRenderer,
        ref RotateMotion rotateMotion){
    }
}

internal struct StaticStagePropSpawn:ISystem {
    public void Spawn(ref Position position,ref Rotation rotation,ref Scale scale,ref WorldTransform worldTransform,ref MeshRenderer meshRenderer){
    }
}

internal struct ChildStagePropSpawn:ISystem {
    public void Spawn(ref Position position,ref Rotation rotation,ref Scale scale,ref WorldTransform worldTransform,ref MeshRenderer meshRenderer,ref Parent parent){
    }
}

internal struct PointLightSpawn:ISystem {
    public void Spawn(ref Position position,ref PointLightData pointLight){
    }
}

internal struct OrbitingPointLightSpawn:ISystem {
    public void Spawn(ref Position position,ref PointLightData pointLight,ref OrbitMotion orbitMotion){
    }
}

internal struct AudioSourceSpawn:ISystem {
    public void Spawn(ref Position position,ref AudioSourceData audioSource){
    }
}

internal struct OrbitingAudioSourceSpawn:ISystem {
    public void Spawn(ref Position position,ref AudioSourceData audioSource,ref OrbitMotion orbitMotion){
    }
}

internal struct SpotLightSpawn:ISystem {
    public void Spawn(ref Position position,ref SpotLightData spotLight){
    }
}
