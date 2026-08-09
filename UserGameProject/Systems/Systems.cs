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
//   SceneSpawnSystem   … 起動時に1回だけステージ・ライト・Playerを生成する(以降は何もしない)
//   InputSystem        … Win32Windowを読み、InputStateを書く(入力の生産者)
//   PlayerControlSystem … InputStateを読み、Position/Rotation/CameraRig等を書く
//   CameraSystem       … Position/CameraRigを読み、Cameraを書く(PlayerControlの後でなければ
//                         カメラがちょうど1フレーム遅れる)
//   MovingPropSystem   … Rotationを書く(CameraSystemとは独立、RenderSyncより前であればよい)
//   RenderSyncSystem   … 全ての結果をStageRendererへ流し込む(最後)
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
internal struct SceneSpawnSystem : ISystem
{
    public bool Update(ref SceneSpawnRequest request, ref State state)
    {
        if (request.Done || request.Scene is not { } scene)
        {
            return true;
        }

        request.Done = true;

        foreach (LoadedSceneSpawn spawn in scene.Spawns)
        {
            Position position = new() { X = spawn.Position.X, Y = spawn.Position.Y, Z = spawn.Position.Z };
            Rotation rotation = new() { X = spawn.Rotation.X, Y = spawn.Rotation.Y, Z = spawn.Rotation.Z, W = spawn.Rotation.W };
            Scale scale = new() { X = spawn.Scale.X, Y = spawn.Scale.Y, Z = spawn.Scale.Z };
            RenderObjectRef renderObjectRef = new() { Index = spawn.ObjectIndex };

            if (spawn.Mobility == PropMobility.Movable)
            {
                state.World.Create(position, rotation, scale, renderObjectRef, new MovableTag());
            }
            else
            {
                state.World.Create(position, rotation, scale, renderObjectRef);
            }
        }

        if (scene.Sun is { } sun)
        {
            state.World.Create(sun);
        }

        if (scene.Ambient is { } ambient)
        {
            state.World.Create(ambient);
        }

        if (scene.Fog is { } fog)
        {
            state.World.Create(fog);
        }

        Position playerStart = new() { X = scene.PlayerStartPosition.X, Y = scene.PlayerStartPosition.Y, Z = scene.PlayerStartPosition.Z };

        state.World.Create(
            playerStart,
            new Rotation { X = 0f, Y = 0f, Z = 0f, W = 1f },
            new Health { Value = 100 },
            new VerticalMotion(),
            new CameraRig { Yaw = scene.PlayerStartYawRadians, Pitch = 0f },
            new InputState(),
            new PlayerTag(),
            Camera.CreateFirstPerson(new Vector3(playerStart.X, playerStart.Y + CameraSystem.EyeHeight, playerStart.Z)));

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
internal struct InputSystem : ISystem
{
    private const int VkSpace = 0x20;

    public bool Update(ref InputState state, ref PlayerTag _, Win32Window window, ref State gameState)
    {
        bool jumpKeyDown = window.IsKeyDown(VkSpace);
        state.JumpPressed = jumpKeyDown && !state.WasJumpKeyDown;
        state.WasJumpKeyDown = jumpKeyDown;

        state.MoveForward = window.IsKeyDown('W');
        state.MoveBack = window.IsKeyDown('S');
        state.MoveLeft = window.IsKeyDown('A');
        state.MoveRight = window.IsKeyDown('D');

        (float deltaX, float deltaY) = window.ConsumeMouseDelta();
        state.LookDeltaX = deltaX;
        state.LookDeltaY = deltaY;

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
internal struct PlayerControlSystem : ISystem
{
    private const float MouseYawSensitivity = 0.0025f;
    private const float MousePitchSensitivity = 0.0025f;

    public bool Update(
        ref Position position,
        ref Rotation rotation,
        ref VerticalMotion verticalMotion,
        ref CameraRig cameraRig,
        ref InputState inputState,
        ref PlayerTag _,
        in FrameContext frame,
        ref State gameState)
    {
        cameraRig.Yaw += inputState.LookDeltaX * MouseYawSensitivity;
        cameraRig.Pitch = Math.Clamp(
            cameraRig.Pitch - (inputState.LookDeltaY * MousePitchSensitivity),
            Camera.MinPitchRadiansFirstPerson,
            Camera.MaxPitchRadiansFirstPerson);

        // RUF boundary (ADR-0028): yaw-only facing, flat forward/right, and the resulting body
        // rotation are derived in RUF space (mirrors Camera.GetFlatForward/GetFlatRight, ADR-0026)
        // before converting back to the Vector3/Quaternion types CharacterMotion and the ECS
        // Rotation component still use.
        RufQuaternion facingRuf = RufQuaternion.FromAxisAngle(RufVector.Up, cameraRig.Yaw);
        RufVector flatForwardRuf = RufQuaternion.RotateVector(facingRuf, RufVector.Forward).Normalize();
        RufVector flatRightRuf = Spatial<UnityCoordinateConvention>.RightFromUpForward(RufVector.Up, flatForwardRuf);

        Vector3 flatForward = (Vector3)Spatial<UnityCoordinateConvention>.ToXyz(flatForwardRuf);
        Vector3 flatRight = (Vector3)Spatial<UnityCoordinateConvention>.ToXyz(flatRightRuf);

        CharacterMotionResult motion = CharacterMotion.Compute(new CharacterMotionInput
        {
            FlatForward = flatForward,
            FlatRight = flatRight,
            MoveForward = inputState.MoveForward,
            MoveBack = inputState.MoveBack,
            MoveLeft = inputState.MoveLeft,
            MoveRight = inputState.MoveRight,
            JumpPressed = inputState.JumpPressed,
            CurrentY = position.Y,
            VerticalVelocity = verticalMotion.Velocity,
            DeltaTime = frame.DeltaTime,
        });

        verticalMotion = new VerticalMotion { Velocity = motion.NewVerticalVelocity, IsGrounded = motion.IsGrounded };
        position = new Position
        {
            X = position.X + (motion.HorizontalVelocity.X * frame.DeltaTime),
            Y = motion.NewY,
            Z = position.Z + (motion.HorizontalVelocity.Z * frame.DeltaTime),
        };

        rotation = new Rotation { X = facingRuf.R, Y = facingRuf.U, Z = facingRuf.F, W = facingRuf.W };

        return true;
    }
}

/// <summary>
/// Drives the render <see cref="Camera"/> from the player's eye position + <see cref="CameraRig"/>
/// each frame. <see cref="Camera"/> lives as a component directly on the player entity (there is
/// only ever one) rather than a separate singleton entity, so this stays a single-archetype,
/// single-entity loop instead of needing a cross-entity join the model does not support.
/// First-person: Camera.Target doubles as the eye position itself (see
/// <see cref="Camera.CreateFirstPerson"/>).
/// </summary>
internal struct CameraSystem : ISystem
{
    public const float EyeHeight = 1.6f;

    public bool Update(ref Camera camera, ref Position position, ref CameraRig cameraRig, ref PlayerTag _, ref State gameState)
    {
        // EyeHeight is added along RUF Up (ADR-0028), matching Camera/PlayerControlSystem's shared
        // RUF-space convention rather than assuming which raw component is "up".
        RufVector positionRuf = new(position.X, position.Y, position.Z);
        RufVector eyeRuf = positionRuf + (RufVector.Up * EyeHeight);
        camera.Target = (Vector3)Spatial<UnityCoordinateConvention>.ToXyz(eyeRuf);
        camera.Yaw = cameraRig.Yaw;
        camera.Pitch = cameraRig.Pitch;

        return true;
    }
}

/// <summary>
/// Demonstrates the "movable" mobility archetype: a continuous spin, recomputed fresh from
/// elapsed time each frame (not accumulated frame-to-frame) so there is no drift and no need to
/// remember each prop's original position. There is no gameplay reason for the motion beyond
/// visibly proving Static vs Movable props are independently spawnable and updatable.
/// </summary>
internal struct MovingPropSystem : ISystem
{
    private const float SpinSpeedRadiansPerSecond = 0.8f;

    public bool Update(ref Rotation rotation, ref MovableTag _, in FrameContext frame, ref State gameState)
    {
        Quaternion spin = Quaternion.CreateFromAxisAngle(Vector3.UnitY, frame.ElapsedSeconds * SpinSpeedRadiansPerSecond);
        rotation = new Rotation { X = spin.X, Y = spin.Y, Z = spin.Z, W = spin.W };

        return true;
    }
}

/// <summary>
/// Pushes ECS transform/camera/light/fog state into <see cref="StageRenderer"/> once per frame -
/// the only system that consumes <see cref="RenderObjectRef"/> indices or writes to the renderer.
/// <see cref="Update(ref Position, ref Rotation, ref Scale, ref RenderObjectRef, StageRenderer, ref State)"/>
/// requires exactly Position+Rotation+Scale+RenderObjectRef, which both the static and movable
/// stage-prop archetypes satisfy (the movable one is a strict superset), so the generator's
/// subset dispatch naturally covers both with one method - the same property the old Query-based
/// design had, and the reason the pre-migration AoS-era code needed two near-identical overloads.
/// </summary>
internal struct RenderSyncSystem : ISystem
{
    public bool Update(ref Position position, ref Rotation rotation, ref Scale scale, ref RenderObjectRef renderObjectRef, StageRenderer renderer, ref State gameState)
    {
        renderer.SetObjectTransform(renderObjectRef.Index, ComposeTransform(position, rotation, scale));
        return true;
    }

    public bool Update(ref Camera camera, ref PlayerTag _, StageRenderer renderer, ref State gameState)
    {
        renderer.SetCamera(camera);
        return true;
    }

    /// <summary>Applies the single sun singleton, if any (0 matching entities = "not configured, leave the renderer's current/default value alone").</summary>
    public bool Update(ref DirectionalLightData light, StageRenderer renderer, ref State gameState)
    {
        renderer.SetDirectionalLight(
            new Vector3(light.DirectionTowardLightX, light.DirectionTowardLightY, light.DirectionTowardLightZ),
            new Vector3(light.ColorR, light.ColorG, light.ColorB),
            light.Intensity);

        return true;
    }

    public bool Update(ref AmbientLightData ambient, StageRenderer renderer, ref State gameState)
    {
        renderer.SetAmbientLight(new Vector3(ambient.ColorR, ambient.ColorG, ambient.ColorB));
        return true;
    }

    public bool Update(ref FogData fog, StageRenderer renderer, ref State gameState)
    {
        renderer.SetFog(new FogParams(fog.Density, fog.HeightFalloff, new Vector3(fog.ColorR, fog.ColorG, fog.ColorB)));
        return true;
    }

    private static Matrix4x4 ComposeTransform(in Position position, in Rotation rotation, in Scale scale)
    {
        Quaternion rotationQuaternion = new(rotation.X, rotation.Y, rotation.Z, rotation.W);
        return Matrix4x4.CreateScale(scale.X, scale.Y, scale.Z)
            * Matrix4x4.CreateFromQuaternion(rotationQuaternion)
            * Matrix4x4.CreateTranslation(position.X, position.Y, position.Z);
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
internal struct PlayerSpawn : ISystem
{
    public void Spawn(
        ref Position position,
        ref Rotation rotation,
        ref Health health,
        ref VerticalMotion verticalMotion,
        ref CameraRig cameraRig,
        ref InputState inputState,
        ref PlayerTag playerTag,
        ref Camera camera)
    {
    }
}

/// <summary>
/// 可動ステージプロップの構成。<see cref="RenderSyncSystem"/>(Position/Rotation/Scale/RenderObjectRef)と
/// <see cref="MovingPropSystem"/>(Rotation/MovableTag)の両方に部分集合一致で拾われるが、その和集合
/// そのものはどの<c>Update</c>にも現れないため、ここで宣言する。
/// </summary>
internal struct MovableStagePropSpawn : ISystem
{
    public void Spawn(
        ref Position position,
        ref Rotation rotation,
        ref Scale scale,
        ref RenderObjectRef renderObjectRef,
        ref MovableTag movableTag)
    {
    }
}
