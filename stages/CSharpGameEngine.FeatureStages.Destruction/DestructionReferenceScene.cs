using CSharpGameEngine.Entities;
namespace CSharpGameEngine.FeatureStages.Destruction;
internal partial class DestructionReferenceScene:IScene {
    public Node Root=>new(){
        Name="Destruction_Wall",
        Components=[new StageScenario { Domain=6,Case=1 },new StageVisual { Primitive=1,X=2,Y=2,ScaleX=0.5f,ScaleY=4,ScaleZ=6,Color=0xff8d6e63 }],
        Children=[
            new Node { Name="CutVolume",Components=[new StageVisual { Primitive=2,X=2,Y=2,ScaleX=1.5f,ScaleY=1.5f,ScaleZ=1.5f,Color=0x60ef5350 }] },
            new Node { Name="Impact",Components=[new StageVisual { Primitive=2,X=-4,Y=2,ScaleX=0.4f,ScaleY=0.4f,ScaleZ=0.4f,Color=0xffffca28 }] },
        ],
    };
}
