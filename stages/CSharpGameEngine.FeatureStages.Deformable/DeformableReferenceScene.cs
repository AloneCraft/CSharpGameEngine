using CSharpGameEngine.Entities;
namespace CSharpGameEngine.FeatureStages.Deformable;
internal partial class DeformableReferenceScene:IScene {
    public Node Root=>new(){
        Name="Deformable_Root",
        Components=[new StageScenario { Domain=4,Case=1 }],
        Children=[
            new Node { Name="Cloth",Components=[new StageVisual { Primitive=4,X=-3,Y=2,ScaleX=3,ScaleY=0.1f,ScaleZ=3,Color=0xffab47bc }] },
            new Node { Name="Rope",Components=[new StageVisual { Primitive=5,Y=3,ScaleX=0.15f,ScaleY=5,ScaleZ=0.15f,Color=0xffff7043 }] },
            new Node { Name="SoftBody",Components=[new StageVisual { Primitive=2,X=3,Y=1.5f,ScaleX=2,ScaleY=2,ScaleZ=2,Color=0xff26a69a }] },
        ],
    };
}
