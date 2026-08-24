using CSharpGameEngine.Entities;
namespace CSharpGameEngine.FeatureStages;
public struct StageScenario {
    public int Domain;
    public int Case;
    public int Flags;
}
public struct StageVisual {
    public int Primitive;
    public float X;
    public float Y;
    public float Z;
    public float ScaleX;
    public float ScaleY;
    public float ScaleZ;
    public uint Color;
}
public struct StageLink {
    public int Source;
    public int Target;
    public int Kind;
}
[ExecuteInEditor]
internal struct StageLifecycleSystem:ISystem {
    public bool Update(ref StageScenario Scenario,ref State State)=>true;
}
