using CSharpGameEngine.Entities;
namespace CSharpGameEngine.FeatureStages.Coupling;
internal partial class CouplingReferenceScene:IScene {
    public Node Root=>new(){
        Name="Coupling_Root",
        Components=[new StageScenario { Domain=9,Case=1 }],
        Children=[
            new Node { Name="RigidDomain",Components=[new StageVisual { Primitive=1,X=-3,ScaleX=2,ScaleY=2,ScaleZ=2,Color=0xffef5350 },new StageLink { Source=0,Target=1,Kind=4 }] },
            new Node { Name="FluidDomain",Components=[new StageVisual { Primitive=1,ScaleX=2,ScaleY=2,ScaleZ=2,Color=0xff42a5f5 },new StageLink { Source=1,Target=2,Kind=4 }] },
            new Node { Name="ThermalDomain",Components=[new StageVisual { Primitive=1,X=3,ScaleX=2,ScaleY=2,ScaleZ=2,Color=0xffff7043 },new StageLink { Source=2,Target=0,Kind=4 }] },
        ],
    };
}
