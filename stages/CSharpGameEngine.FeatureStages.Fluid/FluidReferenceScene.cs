using CSharpGameEngine.Entities;
namespace CSharpGameEngine.FeatureStages.Fluid;
internal partial class FluidReferenceScene:IScene {
    public Node Root=>new(){
        Name="Fluid_Tank",
        Components=[new StageScenario { Domain=5,Case=1 },new StageVisual { Primitive=1,Y=1,ScaleX=8,ScaleY=3,ScaleZ=5,Color=0x3042a5f5 }],
        Children=[
            new Node { Name="ParticleEmitter",Components=[new StageVisual { Primitive=2,X=-2,Y=4,ScaleX=0.5f,ScaleY=0.5f,ScaleZ=0.5f,Color=0xff29b6f6 }] },
            new Node { Name="GridVolume",Components=[new StageVisual { Primitive=1,X=1,Y=1.5f,ScaleX=4,ScaleY=2.5f,ScaleZ=3,Color=0x405c6bc0 }] },
        ],
    };
}
