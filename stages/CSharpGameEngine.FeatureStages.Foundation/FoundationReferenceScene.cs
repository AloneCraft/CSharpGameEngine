using CSharpGameEngine.Entities;
namespace CSharpGameEngine.FeatureStages.Foundation;
internal partial class FoundationReferenceScene:IScene {
    public Node Root=>new(){
        Name="Foundation_Root",
        Components=[new StageScenario { Domain=1,Case=1 },new StageVisual { Primitive=1,ScaleX=1,ScaleY=1,ScaleZ=1,Color=0xff4f81ff }],
        Children=[new Node { Name="Foundation_Child",Components=[new StageVisual { Primitive=2,X=2,ScaleX=0.5f,ScaleY=0.5f,ScaleZ=0.5f,Color=0xff81c784 }] }],
    };
}
