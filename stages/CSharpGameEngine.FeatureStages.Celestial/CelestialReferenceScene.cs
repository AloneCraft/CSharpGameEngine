using CSharpGameEngine.Entities;
namespace CSharpGameEngine.FeatureStages.Celestial;
internal partial class CelestialReferenceScene:IScene {
    public Node Root=>new(){
        Name="Celestial_Star",
        Components=[new StageScenario { Domain=7,Case=1 },new StageVisual { Primitive=2,ScaleX=2,ScaleY=2,ScaleZ=2,Color=0xffffca28 }],
        Children=[
            new Node { Name="Planet",Components=[new StageVisual { Primitive=2,X=8,ScaleX=1,ScaleY=1,ScaleZ=1,Color=0xff42a5f5 },new StageLink { Source=1,Target=0,Kind=2 }] },
            new Node { Name="Moon",Components=[new StageVisual { Primitive=2,X=10,ScaleX=0.35f,ScaleY=0.35f,ScaleZ=0.35f,Color=0xffb0bec5 },new StageLink { Source=2,Target=1,Kind=2 }] },
        ],
    };
}
