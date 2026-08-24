using CSharpGameEngine.Entities;
namespace CSharpGameEngine.FeatureStages.Vsix;
internal partial class VsixReferenceScene:IScene {
    public Node Root=>new(){
        Name="VSIX_SceneEditor",
        Components=[new StageScenario { Domain=10,Case=1 }],
        Children=[
            new Node { Name="SelectedNode",Components=[new StageVisual { Primitive=1,ScaleX=1,ScaleY=1,ScaleZ=1,Color=0xffffa726 }] },
            new Node { Name="GizmoX",Components=[new StageVisual { Primitive=5,X=2,ScaleX=2,ScaleY=0.1f,ScaleZ=0.1f,Color=0xffef5350 }] },
            new Node { Name="GizmoY",Components=[new StageVisual { Primitive=5,Y=2,ScaleX=0.1f,ScaleY=2,ScaleZ=0.1f,Color=0xff66bb6a }] },
            new Node { Name="GizmoZ",Components=[new StageVisual { Primitive=5,Z=2,ScaleX=0.1f,ScaleY=0.1f,ScaleZ=2,Color=0xff42a5f5 }] },
        ],
    };
}
