using CSharpGameEngine.Entities;
using CSharpGameEngine.Runtime.Character;
namespace CgeFirstPerson;
/// <summary>
/// BP_FirstPersonCharacterのDoMove/DoLook/DoJump相当。操作を視点・移動・ジャンプへ翻訳するだけで、実際の移動はエンジンの
/// CharacterMotionが行う。ThirdPersonと同じコードで、一人称との違いはシーン(カメラが目の位置にあり、体が移動方向を向かない)だけ。
/// </summary>
internal struct PlayerControls:ISystem {
    public void Update(in PlayerActions Input,ref ViewAngles View,ref MoveIntent Intent){
        View.Turn(Input.Look);
        Intent.Direction=View.Planar(Input.Move);
        if(Input.Jump.Pressed){
            Intent.Jump();
        }
    }
}
