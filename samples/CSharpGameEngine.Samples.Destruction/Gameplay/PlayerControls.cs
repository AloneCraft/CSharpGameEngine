using CSharpGameEngine.Entities;
using CSharpGameEngine.Runtime.Character;
namespace CgeDestructionSample;
/// <summary>
/// ThirdPersonテンプレートのBP_ThirdPersonCharacterにあるDoMove/DoLook/DoJumpに相当する。操作入力を視点方向・歩行・ジャンプに変換し、移動はエンジンの
/// CharacterMotionが行う。
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
