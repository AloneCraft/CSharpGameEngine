using CSharpGameEngine.Entities;
namespace CSharpGameEngine.FeatureStages.Rigid;
internal partial class RigidReferenceScene:IScene {
    public Node Root=>new(){
        Name="Rigid_Ground",
        Components=[new StageScenario { Domain=3,Case=1 },new StageVisual { Primitive=1,Y=-0.5f,ScaleX=12,ScaleY=0.5f,ScaleZ=12,Color=0xff455a64 }],
        Children=[
            new Node { Name="Stack_0",Components=[new StageVisual { Primitive=1,Y=1,ScaleX=1,ScaleY=1,ScaleZ=1,Color=0xffef5350 },new StageLink { Source=0,Target=1,Kind=1 }] },
            new Node { Name="Stack_1",Components=[new StageVisual { Primitive=1,Y=2.1f,ScaleX=1,ScaleY=1,ScaleZ=1,Color=0xffffca28 },new StageLink { Source=1,Target=2,Kind=1 }] },
            new Node { Name="Stack_2",Components=[new StageVisual { Primitive=1,Y=3.2f,ScaleX=1,ScaleY=1,ScaleZ=1,Color=0xff66bb6a }] },
            new Node { Name="Projectile",Components=[new StageVisual { Primitive=2,X=-6,Y=1.5f,ScaleX=0.5f,ScaleY=0.5f,ScaleZ=0.5f,Color=0xff42a5f5 }] },
        ],
    };
}
