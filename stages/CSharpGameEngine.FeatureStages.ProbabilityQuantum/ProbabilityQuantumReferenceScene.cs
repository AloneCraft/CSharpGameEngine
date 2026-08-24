using CSharpGameEngine.Entities;
namespace CSharpGameEngine.FeatureStages.ProbabilityQuantum;
internal partial class ProbabilityQuantumReferenceScene:IScene {
    public Node Root=>new(){
        Name="ProbabilityQuantum_Root",
        Components=[new StageScenario { Domain=8,Case=1 }],
        Children=[
            new Node { Name="State_A",Components=[new StageVisual { Primitive=2,X=-2,ScaleX=0.8f,ScaleY=0.8f,ScaleZ=0.8f,Color=0xff7e57c2 },new StageLink { Source=0,Target=1,Kind=3 }] },
            new Node { Name="State_B",Components=[new StageVisual { Primitive=2,X=2,ScaleX=0.8f,ScaleY=0.8f,ScaleZ=0.8f,Color=0xff26c6da },new StageLink { Source=1,Target=0,Kind=3 }] },
            new Node { Name="WaveGrid",Components=[new StageVisual { Primitive=4,Y=-2,ScaleX=6,ScaleY=0.1f,ScaleZ=4,Color=0x608e24aa }] },
        ],
    };
}
