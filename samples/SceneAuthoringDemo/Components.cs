namespace SceneAuthoringDemo;

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

public struct Scale
{
    public float X;
    public float Y;
    public float Z;
}

public struct Camera
{
    public float FieldOfView;
}

public readonly struct PlayerTag
{
}

public struct Health
{
    public float Value;
}
