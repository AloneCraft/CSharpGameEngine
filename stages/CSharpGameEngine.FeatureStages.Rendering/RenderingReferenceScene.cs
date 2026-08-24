using CSharpGameEngine.Entities;
namespace CSharpGameEngine.FeatureStages.Rendering;
internal partial class RenderingReferenceScene:IScene {
    public Node Root=>new(){
        Name="Rendering_Root",
        Components=[new StageScenario { Domain=2,Case=1 }],
        Children=[
            new Node { Name="Camera",Components=[new StageVisual { Primitive=0,Z=-6,ScaleX=0.25f,ScaleY=0.25f,ScaleZ=0.25f,Color=0xffffffff }] },
            new Node { Name="Triangle",Components=[new StageVisual { Primitive=3,ScaleX=2,ScaleY=2,ScaleZ=2,Color=0xffff8040 }] },
            new Node { Name="KeyLight",Components=[new StageVisual { Primitive=2,X=3,Y=4,Z=-2,ScaleX=0.35f,ScaleY=0.35f,ScaleZ=0.35f,Color=0xffffffc0 }] },
        ],
    };
}
