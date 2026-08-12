namespace UserGameProject;

public struct Position {
    public float X;
    public float Y;
    public float Z;
}

public struct Rotation {
    public float X;
    public float Y;
    public float Z;
    public float W;
}

public struct Velocity {
    public float X;
    public float Y;
    public float Z;
}

public struct Health {
    public int Value;
}

public struct Damage {
    public int Value;
}

/// <summary>Marks the single player entity. Like <see cref="MovableTag"/>, a tag component must still be received by <c>ref</c> (ADR-0033).</summary>
public struct PlayerTag {
}

/// <summary>
/// Holds the loaded stage until <c>SceneSpawn</c> has turned it into entities. Program.cs
/// creates exactly one entity carrying this component and nothing else; everything the stage needs
/// (props, lights, the player) is then spawned from inside a System, so the whole startup path is
/// expressed as Components + ISystem rather than as hand-written World calls (ADR-0033).
/// <see cref="Done"/> makes the spawn one-shot: the system runs every frame like any other, and
/// simply does nothing once it has consumed the scene.
/// </summary>
public struct SceneSpawnRequest {
    public LoadedScene?Scene;
    public bool Done;
}

public struct Scale {
    public float X;
    public float Y;
    public float Z;
}

/// <summary>Computed world matrix. Local Position/Rotation/Scale remain the authored entity state.</summary>
public struct WorldTransform {
    public System.Numerics.Matrix4x4 Value;
}

/// <summary>Lightweight index into the mesh collection owned by the scene resource.</summary>
public readonly record struct MeshHandle(int Value);

/// <summary>Lightweight index into the material collection owned by the scene resource.</summary>
public readonly record struct MaterialHandle(int Value);

/// <summary>
/// A reusable renderable component. Mesh and material are immutable scene-resource handles; the
/// renderer object index identifies this entity's independently transformable instance. GPU
/// resources remain owned once by the scene resource and StageRenderer, not once per entity.
/// </summary>
public struct MeshRenderer {
    public MeshHandle Mesh;
    public MaterialHandle Material;
    public int ObjectIndex;
}

/// <summary>Gravity/jump state carried across frames for <see cref="CSharpGameEngine.Graphics.D3D12"/>-independent character motion (see Systems/Systems.cs, PlayerControl, which feeds this into PlayerController.CharacterMotion).</summary>
public struct VerticalMotion {
    public float Velocity;
    public bool IsGrounded;
}

/// <summary>
/// The player's abstracted input for one frame, written only by Systems/Systems.cs's Input (which is
/// the sole place that touches Win32Window) and read by every other system - this component *is*
/// the input abstraction required by the FirstPerson port: downstream systems never see a window
/// or a virtual key code, only this data, so they are testable with a hand-built InputState and
/// portable to a different input backend without changing them.
/// </summary>
public struct InputState {
    public bool MoveForward;
    public bool MoveBack;
    public bool MoveLeft;
    public bool MoveRight;

    /// <summary>Edge-detected (true only on the frame the jump key was first pressed) - Input does the edge detection, matching CharacterMotionInput.JumpPressed's existing contract.</summary>
    public bool JumpPressed;

    public float LookDeltaX;
    public float LookDeltaY;

    /// <summary>Previous frame's raw (non-edge-detected) jump key state, carried on the component itself since [GameSystem] methods are independent execution units with no other place to persist frame-to-frame scratch state.</summary>
    public bool WasJumpKeyDown;
}

/// <summary>
/// Marks the "movable" mobility archetype (Systems/Systems.cs): disambiguates a spinning stage
/// prop from any other entity that happens to also carry Rotation (e.g. the player). A zero-field
/// struct is a tag component - it still has to be received by <c>ref</c> like any other component,
/// because <c>in</c>/by-value parameters are classified as Context instead (ADR-0033).
/// </summary>
public struct MovableTag {
}

/// <summary>Generic rotation motion evaluated from absolute elapsed time.</summary>
public struct RotateMotion {
    public float AxisX;
    public float AxisY;
    public float AxisZ;
    public float RadiansPerSecond;
    public float PhaseRadians;
}

/// <summary>Generic circular motion for a position; useful for lights and gameplay props alike.</summary>
public struct OrbitMotion {
    public float CenterX;
    public float CenterY;
    public float CenterZ;
    public float Radius;
    public float AngularSpeedRadiansPerSecond;
    public float PhaseRadians;
}

/// <summary>First-person view orientation, independent of Position (the eye location). Yaw/Pitch follow the same convention as <see cref="CSharpGameEngine.Graphics.D3D12.Camera"/>.</summary>
public struct CameraRig {
    public float Yaw;
    public float Pitch;
}

/// <summary>A single data-driven directional light (the "sun"). DirectionTowardLight need not be normalized by callers - RenderSync normalizes it before handing it to StageRenderer.</summary>
public struct DirectionalLightData {
    public float DirectionTowardLightX;
    public float DirectionTowardLightY;
    public float DirectionTowardLightZ;
    public float ColorR;
    public float ColorG;
    public float ColorB;
    public float Intensity;
}

/// <summary>Omnidirectional punctual light. Its world position is supplied by <see cref=Position/>.</summary>
public struct PointLightData {
    public float ColorR;
    public float ColorG;
    public float ColorB;
    public float Intensity;
    public float Range;
}

/// <summary>Conical punctual light. Its world position is supplied by <see cref=Position/>.</summary>
public struct SpotLightData {
    public float DirectionX;
    public float DirectionY;
    public float DirectionZ;
    public float ColorR;
    public float ColorG;
    public float ColorB;
    public float Intensity;
    public float Range;
    public float InnerConeAngleRadians;
    public float OuterConeAngleRadians;
}

/// <summary>Stable index into the audio resource registry owned by <see cref=AudioRuntime/>.</summary>
public readonly record struct AudioClipHandle(int Value);

/// <summary>
/// Data-only 3D audio source. The voice/backend handle intentionally lives in AudioRuntime, not in
/// this component, so entities remain portable and the same clip samples can be shared by many
/// sources.
/// </summary>
public struct AudioSourceData {
    public AudioClipHandle Clip;
    public float Volume;
    public float MinDistance;
    public float MaxDistance;
    public bool Loop;
    public bool Playing;
}

/// <summary>Tag attached to the player whose Position becomes the 3D audio listener position.</summary>
public struct AudioListenerData {
}

/// <summary>Flat ambient term approximating sky/indirect light (see ADR-0017's SkyLight approximation).</summary>
public struct AmbientLightData {
    public float ColorR;
    public float ColorG;
    public float ColorB;
}

/// <summary>Simplified exponential height fog parameters (see ADR-0018); mirrors <see cref="CSharpGameEngine.Graphics.D3D12.FogParams"/> as plain ECS-friendly fields.</summary>
public struct FogData {
    public float Density;
    public float HeightFalloff;
    public float ColorR;
    public float ColorG;
    public float ColorB;
}

/// <summary>
/// Per-frame data every <c>[GameSystem]</c> method needs but no single Entity owns - the [Context]
/// counterpart to InputState (which *is* per-entity, since only the player has one). ElapsedSeconds
/// is the running total since startup (Systems/Systems.cs, RotationMotion recomputes its spin fresh from
/// this each frame rather than accumulating, so there is no drift).
/// </summary>
public readonly struct FrameContext {
    public FrameContext(float DeltaTime,float ElapsedSeconds){
        this.DeltaTime=DeltaTime;
        this.ElapsedSeconds=ElapsedSeconds;
    }

    public float DeltaTime { get;}

    public float ElapsedSeconds { get;}
}
