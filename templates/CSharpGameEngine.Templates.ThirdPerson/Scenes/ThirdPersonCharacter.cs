using CSharpGameEngine.Entities;
using CSharpGameEngine.Runtime.Windows;
namespace CgeThirdPerson.Scenes;
/// <summary>BP_ThirdPersonCharacterの移植先(仮)。Phase 3でキャラクター移動とSpringArmカメラに置き換える。</summary>
[StartupScene(Order=2)]
internal partial class ThirdPersonCharacter:IScene {
    public Node Root=>new(){
        Name="BP_ThirdPersonCharacter",
        Components=[
            new Transform { Position=new LogicalVector3(-4f,3.105049f,0f),Rotation=new LogicalQuaternion(new LogicalVector3(0f,1.5707964f,0f)) },
            new Camera { Active=true,FovYRadians=1.0268f,FarZ=1000f },
        ],
    };
}
