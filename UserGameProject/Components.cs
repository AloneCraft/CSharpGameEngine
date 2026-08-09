namespace UserGameProject;

public struct Position
{
    public float X;
    public float Y;
    public float Z;
}

public struct Rotation
{
    public float X;
    public float Y;
    public float Z;
    public float W;
}

public struct Velocity
{
    public float X;
    public float Y;
    public float Z;
}

public struct Health
{
    public int Value;
}

public struct Damage
{
    public int Value;
}

/// <summary>Marks the single player entity. Like <see cref="MovableTag"/>, a tag component must still be received by <c>ref</c> (ADR-0033).</summary>
public struct PlayerTag
{
}

/// <summary>
/// Holds the loaded stage until <c>SceneSpawnSystem</c> has turned it into entities. Program.cs
/// creates exactly one entity carrying this component and nothing else; everything the stage needs
/// (props, lights, the player) is then spawned from inside a System, so the whole startup path is
/// expressed as Components + ISystem rather than as hand-written World calls (ADR-0033).
/// <see cref="Done"/> makes the spawn one-shot: the system runs every frame like any other, and
/// simply does nothing once it has consumed the scene.
/// </summary>
public struct SceneSpawnRequest
{
    public LoadedScene? Scene;
    public bool Done;
}

public struct Scale
{
    public float X;
    public float Y;
    public float Z;
}

/// <summary>Index into the StageRenderer object array this entity's transform is written to each frame (see Systems/RenderSyncSystem.cs).</summary>
public struct RenderObjectRef
{
    public int Index;
}

/// <summary>Gravity/jump state carried across frames for <see cref="CSharpGameEngine.Graphics.D3D12"/>-independent character motion (see Systems/PlayerControlSystem.cs, which feeds this into PlayerController.CharacterMotion).</summary>
public struct VerticalMotion
{
    public float Velocity;
    public bool IsGrounded;
}

/// <summary>
/// The player's abstracted input for one frame, written only by Systems/InputSystem.cs (which is
/// the sole place that touches Win32Window) and read by every other system - this component *is*
/// the input abstraction required by the FirstPerson port: downstream systems never see a window
/// or a virtual key code, only this data, so they are testable with a hand-built InputState and
/// portable to a different input backend without changing them.
/// </summary>
public struct InputState
{
    public bool MoveForward;
    public bool MoveBack;
    public bool MoveLeft;
    public bool MoveRight;

    /// <summary>Edge-detected (true only on the frame the jump key was first pressed) - InputSystem does the edge detection, matching CharacterMotionInput.JumpPressed's existing contract.</summary>
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
public struct MovableTag
{
}

/// <summary>First-person view orientation, independent of Position (the eye location). Yaw/Pitch follow the same convention as <see cref="CSharpGameEngine.Graphics.D3D12.Camera"/>.</summary>
public struct CameraRig
{
    public float Yaw;
    public float Pitch;
}

/// <summary>A single data-driven directional light (the "sun"). DirectionTowardLight need not be normalized by callers - RenderSyncSystem normalizes it before handing it to StageRenderer.</summary>
public struct DirectionalLightData
{
    public float DirectionTowardLightX;
    public float DirectionTowardLightY;
    public float DirectionTowardLightZ;
    public float ColorR;
    public float ColorG;
    public float ColorB;
    public float Intensity;
}

/// <summary>Flat ambient term approximating sky/indirect light (see ADR-0017's SkyLight approximation).</summary>
public struct AmbientLightData
{
    public float ColorR;
    public float ColorG;
    public float ColorB;
}

/// <summary>Simplified exponential height fog parameters (see ADR-0018); mirrors <see cref="CSharpGameEngine.Graphics.D3D12.FogParams"/> as plain ECS-friendly fields.</summary>
public struct FogData
{
    public float Density;
    public float HeightFalloff;
    public float ColorR;
    public float ColorG;
    public float ColorB;
}

/// <summary>
/// Per-frame data every <c>[GameSystem]</c> method needs but no single Entity owns - the [Context]
/// counterpart to InputState (which *is* per-entity, since only the player has one). ElapsedSeconds
/// is the running total since startup (Systems/MovingPropSystem.cs recomputes its spin fresh from
/// this each frame rather than accumulating, so there is no drift).
/// </summary>
public readonly struct FrameContext
{
    public FrameContext(float deltaTime, float elapsedSeconds)
    {
        this.DeltaTime = deltaTime;
        this.ElapsedSeconds = elapsedSeconds;
    }

    public float DeltaTime { get; }

    public float ElapsedSeconds { get; }
}
