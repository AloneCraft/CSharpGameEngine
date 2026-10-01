using CSharpGameEngine.Entities;
namespace CSharpGameEngine.FeatureStages;
[SceneComponentOrder(SceneComponentCategory.Gameplay,0)]
public struct StageScenario {
    public int Domain;
    public int Case;
    public int Flags;
}
[SceneComponentOrder(SceneComponentCategory.Gameplay,0)]
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
[SceneComponentOrder(SceneComponentCategory.Gameplay,0)]
public struct StageLink {
    public int Source;
    public int Target;
    public int Kind;
}
[ExecuteInEditor]
internal struct StageLifecycleSystem:ISystem {
    public void Update(ref StageScenario Scenario,ref State State){ }
}
