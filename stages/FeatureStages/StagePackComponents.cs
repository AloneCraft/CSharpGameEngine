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
/// <summary>`show` が、シーンに入った <see cref="StageVisual"/> を窓へ渡すための受け皿。値の読み出しは生成された System 経由に限られるため、System が集める。</summary>
internal static class StageVisualSink {
    internal static readonly List<StageVisual>Items=[];
}
[ExecuteInEditor]
internal struct StageVisualCollectSystem:ISystem {
    public void Update(ref StageVisual Visual){ StageVisualSink.Items.Add(Visual); }
}
[ExecuteInEditor]
internal struct StageLifecycleSystem:ISystem {
    public void Update(ref StageScenario Scenario,ref State State){ }
}
